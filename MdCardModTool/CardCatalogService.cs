using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MdCardModTool;

public sealed class CardCatalogService
{
	public const string BundledFileName = "card-catalog-v1.json.br";

	private readonly List<CardCatalogEntry> _entries;

	private readonly Dictionary<int, CardCatalogEntry> _byId;

	private readonly Dictionary<int, CardCatalogEntry> _byMrk;

	private readonly Dictionary<int, string[]> _normalizedNames;

	public int Count => _entries.Count;

	public IReadOnlyList<CardCatalogEntry> Entries => _entries;

	public static string BundledPath => AppPaths.ResolveFile("card-catalog-v1.json.br");

	public CardCatalogService(IEnumerable<CardCatalogEntry> entries)
	{
		_entries = entries.OrderBy((CardCatalogEntry x) => x.CardId).ToList();
		_byId = _entries.ToDictionary((CardCatalogEntry x) => x.CardId);
		_byMrk = (from x in _entries
			where x.Mrk > 0
			group x by x.Mrk).ToDictionary((IGrouping<int, CardCatalogEntry> group) => group.Key, (IGrouping<int, CardCatalogEntry> group) => (from x in @group
			orderby HasName(x) descending, x.CardId
			select x).First());
		_normalizedNames = _entries.ToDictionary((CardCatalogEntry x) => x.CardId, (CardCatalogEntry x) => new string[4]
		{
			Normalize(x.SimplifiedChineseName),
			Normalize(x.TraditionalChineseName),
			Normalize(x.JapaneseName),
			Normalize(x.EnglishName)
		});
	}

	public static CardCatalogService LoadBestAvailable()
	{
		List<CardCatalogEntry> list = new List<CardCatalogEntry>();
		foreach (string item in BundledCandidates())
		{
			if (!File.Exists(item))
			{
				continue;
			}
			try
			{
				list = Read(item);
				if (list.Count > 0)
				{
					break;
				}
			}
			catch
			{
			}
		}
		string extraCatalogPath = GameCardCatalogUpdater.ExtraCatalogPath;
		if (File.Exists(extraCatalogPath))
		{
			try
			{
				Dictionary<int, CardCatalogEntry> dictionary = list.ToDictionary((CardCatalogEntry x) => x.CardId);
				foreach (CardCatalogEntry item2 in Read(extraCatalogPath))
				{
					dictionary[item2.CardId] = MergeGameData(dictionary.GetValueOrDefault(item2.CardId), item2);
				}
				list = dictionary.Values.ToList();
			}
			catch
			{
			}
		}
		return new CardCatalogService(list);
	}

	private static IEnumerable<string> BundledCandidates()
	{
		yield return BundledPath;
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", "card-catalog-v1.json.br");
	}

	public CardCatalogEntry? Find(int cardId)
	{
		return _byId.GetValueOrDefault(cardId);
	}

	public CardCatalogEntry? FindByMrk(int mrk)
	{
		return _byMrk.GetValueOrDefault(mrk);
	}

	public CardCatalogEntry? FindCardOrMrk(int value)
	{
		return Find(value);
	}

	public CardCatalogEntry? Find(string cardId)
	{
		if (!int.TryParse(cardId, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return Find(result);
	}

	public IReadOnlyList<CardCatalogEntry> FindEquivalentCards(CardCatalogEntry card)
	{
		if (!_normalizedNames.TryGetValue(card.CardId, out string[] value))
		{
			return Array.Empty<CardCatalogEntry>();
		}
		HashSet<string> names = value.Where((string name) => name.Length > 0).ToHashSet<string>(StringComparer.Ordinal);
		if (names.Count == 0)
		{
			return Array.Empty<CardCatalogEntry>();
		}
		return (from candidate in _entries
			where _normalizedNames[candidate.CardId].Any(names.Contains)
			orderby (candidate.CardId != card.CardId) ? 1 : 0, candidate.CardId
			select candidate).ToArray();
	}

	public IReadOnlyList<CardCatalogEntry> Search(string query, int limit = 50)
	{
		string normalized = Normalize(query);
		if (normalized.Length == 0)
		{
			return Array.Empty<CardCatalogEntry>();
		}
		int requestedId;
		bool numeric = int.TryParse(query.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out requestedId);
		return (from x in (from entry in _entries
				select (Entry: entry, Score: Score(entry, normalized, numeric, requestedId)) into x
				where x.Score > 0
				orderby x.Score descending, x.Entry.CardId
				select x).Take(Math.Clamp(limit, 1, 250))
			select x.Entry).ToArray();
	}

	public static int GenerateFromAstellarCsv(string csvPath, string outputPath)
	{
		using StreamReader reader = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		using IEnumerator<string[]> enumerator = ReadCsv(reader).GetEnumerator();
		if (!enumerator.MoveNext())
		{
			throw new InvalidDataException("Astellar CSV 为空。");
		}
		Dictionary<string, int> columns = enumerator.Current.Select((string name, int item) => (name: name, index: item)).ToDictionary<(string, int), string, int>(((string name, int index) x) => x.name.Trim(), ((string name, int index) x) => x.index, StringComparer.OrdinalIgnoreCase);
		int index = Required(columns, "ITEM ID");
		int index2 = Required(columns, "Type");
		int index3 = Required(columns, "SubType");
		int index4 = Required(columns, "zh-tw(Name)");
		int index5 = Required(columns, "zh-cn(Name)");
		int index6 = Required(columns, "ja-jp(Name)");
		int index7 = Required(columns, "en-us(Name)");
		Dictionary<int, CardCatalogEntry> dictionary = new Dictionary<int, CardCatalogEntry>();
		while (enumerator.MoveNext())
		{
			string[] current = enumerator.Current;
			if (TryCell(current, index, out string value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result > 0)
			{
				CardCatalogEntry cardCatalogEntry = new CardCatalogEntry
				{
					CardId = result,
					Type = Cell(current, index2),
					SubType = Cell(current, index3),
					TraditionalChineseName = Cell(current, index4),
					SimplifiedChineseName = Cell(current, index5),
					JapaneseName = Cell(current, index6),
					EnglishName = Cell(current, index7)
				};
				if (HasName(cardCatalogEntry))
				{
					dictionary[result] = Merge(dictionary.GetValueOrDefault(result), cardCatalogEntry);
				}
			}
		}
		Write(outputPath, dictionary.Values.OrderBy((CardCatalogEntry x) => x.CardId));
		return dictionary.Count;
	}

	public static List<CardCatalogEntry> Read(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using BrotliStream utf8Json = new BrotliStream(stream, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<List<CardCatalogEntry>>(utf8Json) ?? new List<CardCatalogEntry>();
	}

	public static void Write(string path, IEnumerable<CardCatalogEntry> entries)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		string text = path + ".tmp";
		using (FileStream stream = File.Create(text))
		{
			using BrotliStream utf8Json = new BrotliStream(stream, CompressionLevel.SmallestSize);
			JsonSerializer.Serialize(utf8Json, entries);
		}
		File.Move(text, path, overwrite: true);
	}

	private int Score(CardCatalogEntry entry, string query, bool numeric, int requestedId)
	{
		if (numeric && entry.CardId == requestedId)
		{
			return 10000;
		}
		int num = 0;
		string text = entry.CardId.ToString(CultureInfo.InvariantCulture);
		if (text.StartsWith(query, StringComparison.Ordinal))
		{
			num = 6000 - text.Length;
		}
		string[] array = _normalizedNames[entry.CardId];
		foreach (string text2 in array)
		{
			if (text2.Length == 0)
			{
				continue;
			}
			if (text2 == query)
			{
				num = Math.Max(num, 9000);
				continue;
			}
			if (text2.StartsWith(query, StringComparison.Ordinal))
			{
				num = Math.Max(num, 7000 - text2.Length);
				continue;
			}
			int num2 = text2.IndexOf(query, StringComparison.Ordinal);
			if (num2 >= 0)
			{
				num = Math.Max(num, 4000 - num2 - text2.Length / 8);
			}
		}
		return num;
	}

	private static CardCatalogEntry Merge(CardCatalogEntry? current, CardCatalogEntry incoming)
	{
		if (current == null)
		{
			return incoming;
		}
		return current with
		{
			Mrk = ((current.Mrk > 0) ? current.Mrk : incoming.Mrk),
			Type = Pick(current.Type, incoming.Type),
			SubType = Pick(current.SubType, incoming.SubType),
			TraditionalChineseName = Pick(current.TraditionalChineseName, incoming.TraditionalChineseName),
			SimplifiedChineseName = Pick(current.SimplifiedChineseName, incoming.SimplifiedChineseName),
			JapaneseName = Pick(current.JapaneseName, incoming.JapaneseName),
			EnglishName = Pick(current.EnglishName, incoming.EnglishName)
		};
	}

	internal static IReadOnlyList<CardCatalogEntry> MergeGameCatalogs(IEnumerable<CardCatalogEntry> bundled, IEnumerable<CardCatalogEntry> game)
	{
		Dictionary<int, CardCatalogEntry> dictionary = bundled.ToDictionary((CardCatalogEntry entry) => entry.CardId);
		foreach (CardCatalogEntry item in game)
		{
			dictionary[item.CardId] = MergeGameData(dictionary.GetValueOrDefault(item.CardId), item);
		}
		return dictionary.Values.OrderBy((CardCatalogEntry entry) => entry.CardId).ToArray();
	}

	private static CardCatalogEntry MergeGameData(CardCatalogEntry? current, CardCatalogEntry incoming)
	{
		if (current == null)
		{
			return incoming;
		}
		return current with
		{
			Type = Pick(current.Type, incoming.Type),
			SubType = Pick(current.SubType, incoming.SubType),
			Mrk = ((incoming.Mrk > 0) ? incoming.Mrk : current.Mrk),
			TraditionalChineseName = PreferIncoming(current.TraditionalChineseName, incoming.TraditionalChineseName),
			SimplifiedChineseName = PreferIncoming(current.SimplifiedChineseName, incoming.SimplifiedChineseName),
			JapaneseName = PreferIncoming(current.JapaneseName, incoming.JapaneseName),
			EnglishName = PreferIncoming(current.EnglishName, incoming.EnglishName)
		};
	}

	private static bool HasName(CardCatalogEntry entry)
	{
		if (string.IsNullOrWhiteSpace(entry.SimplifiedChineseName) && string.IsNullOrWhiteSpace(entry.TraditionalChineseName) && string.IsNullOrWhiteSpace(entry.JapaneseName))
		{
			return !string.IsNullOrWhiteSpace(entry.EnglishName);
		}
		return true;
	}

	private static string Pick(string current, string incoming)
	{
		if (!string.IsNullOrWhiteSpace(current))
		{
			return current;
		}
		return incoming.Trim();
	}

	private static string PreferIncoming(string current, string incoming)
	{
		if (!string.IsNullOrWhiteSpace(incoming))
		{
			return incoming.Trim();
		}
		return current;
	}

	private static int Required(Dictionary<string, int> columns, string name)
	{
		if (!columns.TryGetValue(name, out var value))
		{
			throw new InvalidDataException("Astellar CSV 缺少字段：" + name);
		}
		return value;
	}

	private static bool TryCell(string[] row, int index, out string value)
	{
		value = Cell(row, index);
		return value.Length > 0;
	}

	private static string Cell(string[] row, int index)
	{
		if (index < 0 || index >= row.Length)
		{
			return "";
		}
		return row[index].Trim();
	}

	private static string Normalize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		return new string(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)
			.ToArray());
	}

	private static IEnumerable<string[]> ReadCsv(TextReader reader)
	{
		List<string> row = new List<string>();
		StringBuilder field = new StringBuilder();
		bool quoted = false;
		while (true)
		{
			int num = reader.Read();
			if (num < 0)
			{
				break;
			}
			char c = (char)num;
			if (quoted)
			{
				if (c == '"')
				{
					if (reader.Peek() == 34)
					{
						reader.Read();
						field.Append('"');
					}
					else
					{
						quoted = false;
					}
				}
				else
				{
					field.Append(c);
				}
				continue;
			}
			if (c == '"' && field.Length == 0)
			{
				quoted = true;
				continue;
			}
			switch (c)
			{
			case ',':
				row.Add(field.ToString());
				field.Clear();
				break;
			case '\n':
				row.Add(field.ToString().TrimEnd('\r'));
				field.Clear();
				yield return row.ToArray();
				row.Clear();
				break;
			default:
				field.Append(c);
				break;
			}
		}
		if (field.Length > 0 || row.Count > 0)
		{
			row.Add(field.ToString());
			yield return row.ToArray();
		}
	}
}
