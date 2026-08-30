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

	public CardCatalogService(IEnumerable<CardCatalogEntry> entries)
	{
		_entries = entries.OrderBy(x => x.CardId).ToList();
		_byId = _entries.ToDictionary(x => x.CardId);
		_byMrk = _entries.Where(x => x.Mrk > 0)
			.GroupBy(x => x.Mrk)
			.ToDictionary(group => group.Key, group => group.OrderByDescending(x => HasName(x)).ThenBy(x => x.CardId).First());
		_normalizedNames = _entries.ToDictionary(x => x.CardId, x => new[]
		{
			Normalize(x.SimplifiedChineseName),
			Normalize(x.TraditionalChineseName),
			Normalize(x.JapaneseName),
			Normalize(x.EnglishName)
		});
	}

	public int Count => _entries.Count;

	public IReadOnlyList<CardCatalogEntry> Entries => _entries;

	public static string BundledPath => AppPaths.ResolveFile(BundledFileName);

	public static CardCatalogService LoadBestAvailable()
	{
		List<CardCatalogEntry> entries = [];
		foreach (string candidate in BundledCandidates())
		{
			if (!File.Exists(candidate))
			{
				continue;
			}
			try
			{
				entries = Read(candidate);
				if (entries.Count > 0)
				{
					break;
				}
			}
			catch
			{
				// A partial or quarantined copy at one publish location must not
				// hide the valid fallback copy at the other location.
			}
		}
		string extra = GameCardCatalogUpdater.ExtraCatalogPath;
		if (File.Exists(extra))
		{
			try
			{
				Dictionary<int, CardCatalogEntry> merged = entries.ToDictionary(x => x.CardId);
				foreach (CardCatalogEntry entry in Read(extra))
				{
					merged[entry.CardId] = MergeGameData(merged.GetValueOrDefault(entry.CardId), entry);
				}
				entries = merged.Values.ToList();
			}
			catch
			{
			}
		}
		return new CardCatalogService(entries);
	}

	private static IEnumerable<string> BundledCandidates()
	{
		yield return BundledPath;
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", BundledFileName);
	}

	public CardCatalogEntry? Find(int cardId) => _byId.GetValueOrDefault(cardId);

	public CardCatalogEntry? FindByMrk(int mrk) => _byMrk.GetValueOrDefault(mrk);

	public CardCatalogEntry? FindCardOrMrk(int value)
	{
		// Plain numeric input is always a public card number. CARD_Indx positions
		// can collide with real card IDs and therefore must never silently redirect
		// a user's selection to an unrelated monster.
		return Find(value);
	}

	public CardCatalogEntry? Find(string cardId)
	{
		return int.TryParse(cardId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? Find(id) : null;
	}

	public IReadOnlyList<CardCatalogEntry> FindEquivalentCards(CardCatalogEntry card)
	{
		if (!_normalizedNames.TryGetValue(card.CardId, out string[]? source)) return [];
		HashSet<string> names = source.Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);
		if (names.Count == 0) return [];
		return _entries.Where(candidate => _normalizedNames[candidate.CardId].Any(names.Contains))
			.OrderBy(candidate => candidate.CardId == card.CardId ? 0 : 1)
			.ThenBy(candidate => candidate.CardId)
			.ToArray();
	}

	public IReadOnlyList<CardCatalogEntry> Search(string query, int limit = 50)
	{
		string normalized = Normalize(query);
		if (normalized.Length == 0)
		{
			return [];
		}
		bool numeric = int.TryParse(query.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int requestedId);
		return _entries.Select(entry => (Entry: entry, Score: Score(entry, normalized, numeric, requestedId)))
			.Where(x => x.Score > 0)
			.OrderByDescending(x => x.Score)
			.ThenBy(x => x.Entry.CardId)
			.Take(Math.Clamp(limit, 1, 250))
			.Select(x => x.Entry)
			.ToArray();
	}

	public static int GenerateFromAstellarCsv(string csvPath, string outputPath)
	{
		using StreamReader reader = new(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		using IEnumerator<string[]> rows = ReadCsv(reader).GetEnumerator();
		if (!rows.MoveNext())
		{
			throw new InvalidDataException("Astellar CSV 为空。");
		}
		Dictionary<string, int> columns = rows.Current.Select((name, index) => (name, index))
			.ToDictionary(x => x.name.Trim(), x => x.index, StringComparer.OrdinalIgnoreCase);
		int itemId = Required(columns, "ITEM ID");
		int type = Required(columns, "Type");
		int subType = Required(columns, "SubType");
		int zhTw = Required(columns, "zh-tw(Name)");
		int zhCn = Required(columns, "zh-cn(Name)");
		int ja = Required(columns, "ja-jp(Name)");
		int en = Required(columns, "en-us(Name)");
		Dictionary<int, CardCatalogEntry> entries = new();
		while (rows.MoveNext())
		{
			string[] row = rows.Current;
			if (!TryCell(row, itemId, out string idText)
				|| !int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
				|| id <= 0)
			{
				continue;
			}
			CardCatalogEntry incoming = new()
			{
				CardId = id,
				Type = Cell(row, type),
				SubType = Cell(row, subType),
				TraditionalChineseName = Cell(row, zhTw),
				SimplifiedChineseName = Cell(row, zhCn),
				JapaneseName = Cell(row, ja),
				EnglishName = Cell(row, en)
			};
			if (HasName(incoming))
			{
				entries[id] = Merge(entries.GetValueOrDefault(id), incoming);
			}
		}
		Write(outputPath, entries.Values.OrderBy(x => x.CardId));
		return entries.Count;
	}

	public static List<CardCatalogEntry> Read(string path)
	{
		using FileStream file = File.OpenRead(path);
		using BrotliStream brotli = new(file, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<List<CardCatalogEntry>>(brotli) ?? [];
	}

	public static void Write(string path, IEnumerable<CardCatalogEntry> entries)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		using (FileStream file = File.Create(temporary))
		using (BrotliStream brotli = new(file, CompressionLevel.SmallestSize))
		{
			JsonSerializer.Serialize(brotli, entries);
		}
		File.Move(temporary, path, overwrite: true);
	}

	private int Score(CardCatalogEntry entry, string query, bool numeric, int requestedId)
	{
		if (numeric && entry.CardId == requestedId)
		{
			return 10000;
		}
		int score = 0;
		string id = entry.CardId.ToString(CultureInfo.InvariantCulture);
		if (id.StartsWith(query, StringComparison.Ordinal))
		{
			score = 6000 - id.Length;
		}
		foreach (string name in _normalizedNames[entry.CardId])
		{
			if (name.Length == 0)
			{
				continue;
			}
			if (name == query)
			{
				score = Math.Max(score, 9000);
			}
			else if (name.StartsWith(query, StringComparison.Ordinal))
			{
				score = Math.Max(score, 7000 - name.Length);
			}
			else
			{
				int index = name.IndexOf(query, StringComparison.Ordinal);
				if (index >= 0)
				{
					score = Math.Max(score, 4000 - index - name.Length / 8);
				}
			}
		}
		return score;
	}

	private static CardCatalogEntry Merge(CardCatalogEntry? current, CardCatalogEntry incoming)
	{
		if (current == null)
		{
			return incoming;
		}
		return current with
		{
			Mrk = current.Mrk > 0 ? current.Mrk : incoming.Mrk,
			Type = Pick(current.Type, incoming.Type),
			SubType = Pick(current.SubType, incoming.SubType),
			TraditionalChineseName = Pick(current.TraditionalChineseName, incoming.TraditionalChineseName),
			SimplifiedChineseName = Pick(current.SimplifiedChineseName, incoming.SimplifiedChineseName),
			JapaneseName = Pick(current.JapaneseName, incoming.JapaneseName),
			EnglishName = Pick(current.EnglishName, incoming.EnglishName)
		};
	}

	internal static IReadOnlyList<CardCatalogEntry> MergeGameCatalogs(IEnumerable<CardCatalogEntry> bundled,
		IEnumerable<CardCatalogEntry> game)
	{
		Dictionary<int, CardCatalogEntry> merged = bundled.ToDictionary(entry => entry.CardId);
		foreach (CardCatalogEntry entry in game)
		{
			merged[entry.CardId] = MergeGameData(merged.GetValueOrDefault(entry.CardId), entry);
		}
		return merged.Values.OrderBy(entry => entry.CardId).ToArray();
	}

	private static CardCatalogEntry MergeGameData(CardCatalogEntry? current, CardCatalogEntry incoming)
	{
		if (current == null)
		{
			return incoming;
		}
		return current with
		{
			// The compact Astellar catalog has richer type/frame metadata. Game data is
			// authoritative for an installed language, but must not erase the other
			// three translations when only one language pack is present.
			Type = Pick(current.Type, incoming.Type),
			SubType = Pick(current.SubType, incoming.SubType),
			Mrk = incoming.Mrk > 0 ? incoming.Mrk : current.Mrk,
			TraditionalChineseName = PreferIncoming(current.TraditionalChineseName, incoming.TraditionalChineseName),
			SimplifiedChineseName = PreferIncoming(current.SimplifiedChineseName, incoming.SimplifiedChineseName),
			JapaneseName = PreferIncoming(current.JapaneseName, incoming.JapaneseName),
			EnglishName = PreferIncoming(current.EnglishName, incoming.EnglishName)
		};
	}

	private static bool HasName(CardCatalogEntry entry) =>
		!string.IsNullOrWhiteSpace(entry.SimplifiedChineseName)
		|| !string.IsNullOrWhiteSpace(entry.TraditionalChineseName)
		|| !string.IsNullOrWhiteSpace(entry.JapaneseName)
		|| !string.IsNullOrWhiteSpace(entry.EnglishName);

	private static string Pick(string current, string incoming) => string.IsNullOrWhiteSpace(current) ? incoming.Trim() : current;

	private static string PreferIncoming(string current, string incoming) =>
		string.IsNullOrWhiteSpace(incoming) ? current : incoming.Trim();

	private static int Required(Dictionary<string, int> columns, string name) =>
		columns.TryGetValue(name, out int index) ? index : throw new InvalidDataException("Astellar CSV 缺少字段：" + name);

	private static bool TryCell(string[] row, int index, out string value)
	{
		value = Cell(row, index);
		return value.Length > 0;
	}

	private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : "";

	private static string Normalize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		return new string(value.Normalize(NormalizationForm.FormKC)
			.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant)
			.ToArray());
	}

	private static IEnumerable<string[]> ReadCsv(TextReader reader)
	{
		List<string> row = [];
		StringBuilder field = new();
		bool quoted = false;
		while (true)
		{
			int next = reader.Read();
			if (next < 0)
			{
				if (field.Length > 0 || row.Count > 0)
				{
					row.Add(field.ToString());
					yield return [.. row];
				}
				yield break;
			}
			char c = (char)next;
			if (quoted)
			{
				if (c == '"')
				{
					if (reader.Peek() == '"')
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
			}
			else if (c == ',')
			{
				row.Add(field.ToString());
				field.Clear();
			}
			else if (c == '\n')
			{
				row.Add(field.ToString().TrimEnd('\r'));
				field.Clear();
				yield return [.. row];
				row.Clear();
			}
			else
			{
				field.Append(c);
			}
		}
	}
}
