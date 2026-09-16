#define TRACE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class GameCardCatalogUpdater
{
	private sealed record LocatedAsset(string BundlePath, string FileName, int Priority);

	private sealed record UpdateState
	{
		public int FormatVersion { get; init; } = 3;

		public string ResourceFingerprint { get; init; } = "";

		public string BuildId { get; init; } = "";

		public string LocalDataRoot { get; init; } = "";

		public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;

		public int CardCount { get; init; }
	}

	private static readonly string[] Languages = new string[4] { "zh-cn", "zh-tw", "ja-jp", "en-us" };

	public static string ExtraCatalogPath => Path.Combine(AppSettingsStore.AppDataRoot, "card-catalog-extra-v2.json.br");

	public static bool NeedsUpdate(string gameRoot)
	{
		return NeedsUpdate(gameRoot, AppSettingsStore.AppDataRoot);
	}

	internal static bool NeedsUpdate(string gameRoot, string cacheDirectory)
	{
		string path = Path.Combine(cacheDirectory, "card-catalog-extra-v2.json.br");
		string path2 = Path.Combine(cacheDirectory, "card-catalog-game-state-v2.json");
		try
		{
			if (!File.Exists(path) || !File.Exists(path2))
			{
				return true;
			}
			UpdateState updateState = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path2));
			string text = IndexService.FindLocalRoot(gameRoot) ?? "";
			return updateState == null || updateState.FormatVersion != 3 || !string.Equals(updateState.BuildId, PortableIndexService.GetGameBuildId(gameRoot), StringComparison.Ordinal) || !string.Equals(Path.GetFullPath(updateState.LocalDataRoot), Path.GetFullPath(text), StringComparison.OrdinalIgnoreCase) || updateState.ResourceFingerprint != CaptureResourceFingerprint(text, IndexService.StreamingRoot(gameRoot));
		}
		catch
		{
			return true;
		}
	}

	public static IReadOnlyList<CardCatalogEntry> UpdateIfNeeded(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		return UpdateIfNeeded(gameRoot, AppSettingsStore.AppDataRoot, progress, cancellationToken);
	}

	internal static IReadOnlyList<CardCatalogEntry> UpdateIfNeeded(string gameRoot, string cacheDirectory, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		string path = Path.Combine(cacheDirectory, "card-catalog-extra-v2.json.br");
		string text = Path.Combine(cacheDirectory, "card-catalog-game-state-v2.json");
		if (!NeedsUpdate(gameRoot, cacheDirectory))
		{
			if (!File.Exists(path))
			{
				return new List<CardCatalogEntry>();
			}
			return CardCatalogService.Read(path);
		}
		string text2 = IndexService.FindLocalRoot(gameRoot) ?? "";
		string text3 = CaptureResourceFingerprint(text2, IndexService.StreamingRoot(gameRoot));
		IReadOnlyList<CardCatalogEntry> readOnlyList = Extract(gameRoot, progress, cancellationToken);
		if (readOnlyList.Count == 0)
		{
			throw new InvalidDataException("没有从当前游戏 Build 解析出卡片目录；保留原目录不变。");
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (text3 != CaptureResourceFingerprint(text2, IndexService.StreamingRoot(gameRoot)))
		{
			throw new IOException("卡片资源在读取期间发生变化，请等待游戏下载结束后重新加载目录；原目录未改动。");
		}
		if (File.Exists(path))
		{
			try
			{
				readOnlyList = CardCatalogService.MergeGameCatalogs(CardCatalogService.Read(path), readOnlyList);
			}
			catch (InvalidDataException)
			{
			}
			catch (JsonException)
			{
			}
		}
		CardCatalogService.Write(path, readOnlyList);
		UpdateState value = new UpdateState
		{
			BuildId = PortableIndexService.GetGameBuildId(gameRoot),
			LocalDataRoot = (IndexService.FindLocalRoot(gameRoot) ?? ""),
			CardCount = readOnlyList.Count,
			ResourceFingerprint = text3
		};
		Directory.CreateDirectory(cacheDirectory);
		string text4 = text + ".tmp";
		File.WriteAllText(text4, JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		File.Move(text4, text, overwrite: true);
		return readOnlyList;
	}

	internal static string CaptureResourceFingerprint(params string[] roots)
	{
		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (string path in roots)
		{
			if (!Directory.Exists(path))
			{
				continue;
			}
			foreach (FileInfo item in (from file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
				where file.Name.Length == 8 && file.Name.All(char.IsAsciiHexDigit)
				select file).OrderBy<FileInfo, string>((FileInfo file) => file.FullName, StringComparer.OrdinalIgnoreCase))
			{
				string s = $"{item.FullName.ToUpperInvariant()}\0{item.Length}\0{item.LastWriteTimeUtc.Ticks}\n";
				incrementalHash.AppendData(Encoding.UTF8.GetBytes(s));
			}
		}
		return Convert.ToHexString(incrementalHash.GetHashAndReset());
	}

	public static IReadOnlyList<CardCatalogEntry> Extract(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动 LocalData 账号。");
		string streamingRoot = IndexService.StreamingRoot(gameRoot);
		List<string> files = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ThenBy<string, string>((string path) => path, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (Directory.Exists(streamingRoot))
		{
			files.AddRange(Directory.EnumerateFiles(streamingRoot, "*", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ThenBy<string, string>((string path) => path, StringComparer.OrdinalIgnoreCase));
		}
		ConcurrentDictionary<string, LocatedAsset> located = new ConcurrentDictionary<string, LocatedAsset>(StringComparer.OrdinalIgnoreCase);
		int done = 0;
		bool[] completed = new bool[files.Count];
		int completedPrefix = 0;
		object completionGate = new object();
		Dictionary<string, LocatedAsset> completeFamily = null;
		int familyPriority = int.MaxValue;
		Parallel.ForEach(Enumerable.Range(0, files.Count), new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 2, 4),
			CancellationToken = cancellationToken
		}, delegate(int priority, ParallelLoopState loop)
		{
			string file = files[priority];
			try
			{
				if (!IsUnityBundle(file))
				{
					return;
				}
				foreach (string item in new ModEngine().ReadAssetBundleContainerPaths(file))
				{
					string text2 = item.Replace('\\', '/').ToLowerInvariant();
					string fileName = Path.GetFileName(text2);
					bool flag;
					switch (fileName)
					{
					case "card_prop.bytes":
					case "card_name.bytes":
					case "card_indx.bytes":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (flag)
					{
						lock (completionGate)
						{
							if (priority < familyPriority)
							{
								Dictionary<string, LocatedAsset> dictionary2 = TryLocateDictionaryFamily(file, text2, localRoot, streamingRoot);
								if (dictionary2 != null)
								{
									completeFamily = dictionary2;
									familyPriority = priority;
								}
							}
						}
					}
					switch (fileName)
					{
					case "card_prop.bytes":
						Locate("prop", fileName);
						continue;
					case "card_name.bytes":
					case "card_indx.bytes":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (flag)
					{
						string[] languages2 = Languages;
						foreach (string text3 in languages2)
						{
							if (text2.Contains("/" + text3 + "/", StringComparison.Ordinal) || text2.Contains("_" + text3, StringComparison.Ordinal))
							{
								Locate(text3 + "|" + (fileName.StartsWith("card_name", StringComparison.Ordinal) ? "name" : "index"), fileName);
							}
						}
					}
				}
			}
			catch
			{
			}
			finally
			{
				int num2 = Interlocked.Increment(ref done);
				if (num2 % 100 == 0 || num2 == files.Count)
				{
					progress?.Invoke(num2, files.Count, located.Count);
				}
				lock (completionGate)
				{
					completed[priority] = true;
					for (; completedPrefix < completed.Length && completed[completedPrefix]; completedPrefix++)
					{
					}
					if (located.Count == Languages.Length * 2 + 1 && completedPrefix > located.Values.Max((LocatedAsset asset) => asset.Priority))
					{
						loop.Stop();
					}
					if (completeFamily != null && completedPrefix > familyPriority)
					{
						loop.Stop();
					}
				}
			}
			void Locate(string key, string name)
			{
				Trace.WriteLine("Card dictionary " + key + ": " + file);
				LocatedAsset incoming = new LocatedAsset(file, name, priority);
				located.AddOrUpdate(key, incoming, (string _, LocatedAsset current) => (priority >= current.Priority) ? current : incoming);
			}
		});
		if (completeFamily != null)
		{
			located = new ConcurrentDictionary<string, LocatedAsset>(completeFamily, StringComparer.OrdinalIgnoreCase);
		}
		if (!located.TryGetValue("prop", out LocatedAsset value))
		{
			throw new InvalidDataException("当前游戏资源中没有定位到 card_prop.bytes。");
		}
		byte[] properties = ReadDecodedTextAsset(value);
		Dictionary<int, CardCatalogEntry> dictionary = new Dictionary<int, CardCatalogEntry>();
		string[] languages = Languages;
		foreach (string text in languages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (located.TryGetValue(text + "|name", out LocatedAsset value2) && located.TryGetValue(text + "|index", out LocatedAsset value3))
			{
				byte[] names = ReadDecodedTextAsset(value2);
				byte[] indexes = ReadDecodedTextAsset(value3);
				MergeLanguage(dictionary, text, names, indexes, properties);
			}
		}
		return (from entry in dictionary.Values.Where(HasAnyName)
			orderby entry.CardId
			select entry).ToArray();
	}

	private static Dictionary<string, LocatedAsset>? TryLocateDictionaryFamily(string observedFile, string container, string localRoot, string streamingRoot)
	{
		Match match = Regex.Match(container, "^assets/resourcesassetbundle/card/data/([^/]+)/(zh-cn|zh-tw|ja-jp|en-us)/(card_name|card_indx|card_prop)\\.bytes$");
		if (!match.Success)
		{
			return null;
		}
		string value = match.Groups[1].Value;
		TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
		string value2 = match.Groups[3].Value;
		string value3 = "CARD_" + textInfo.ToTitleCase(value2.Substring(5, value2.Length - 5));
		string name = CultureInfo.GetCultureInfo(match.Groups[2].Value).Name;
		string path = IndexService.ResourceBundleRelativePath($"Card/Data/{value}/{name}/{value3}");
		if (!Path.GetFileName(path).Equals(Path.GetFileName(observedFile), StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		Dictionary<string, LocatedAsset> found = new Dictionary<string, LocatedAsset>(StringComparer.OrdinalIgnoreCase);
		string[] languages = Languages;
		foreach (string text in languages)
		{
			name = CultureInfo.GetCultureInfo(text).Name;
			string[] array = new string[3] { "CARD_Name", "CARD_Indx", "CARD_Prop" };
			foreach (string text2 in array)
			{
				path = IndexService.ResourceBundleRelativePath($"Card/Data/{value}/{name}/{text2}");
				string expected = $"assets/resourcesassetbundle/card/data/{value}/{text}/{text2.ToLowerInvariant()}.bytes";
				string[] array2 = new string[2] { localRoot, streamingRoot };
				for (int k = 0; k < array2.Length; k++)
				{
					string text3 = Path.Combine(array2[k], path);
					if (File.Exists(text3) && new ModEngine().ReadAssetBundleContainerPaths(text3).Any((string p) => p.Replace('\\', '/').Equals(expected, StringComparison.OrdinalIgnoreCase)))
					{
						string key = ((text2 == "CARD_Prop") ? "prop" : (text + ((text2 == "CARD_Name") ? "|name" : "|index")));
						found.TryAdd(key, new LocatedAsset(text3, text2.ToLowerInvariant() + ".bytes", 0));
						break;
					}
				}
			}
		}
		if (!found.ContainsKey("prop") || !Languages.Any((string code) => found.ContainsKey(code + "|name") && found.ContainsKey(code + "|index")))
		{
			return null;
		}
		return found;
	}

	private static void MergeLanguage(Dictionary<int, CardCatalogEntry> result, string language, byte[] names, byte[] indexes, byte[] properties)
	{
		int num = Math.Min(indexes.Length / 8, properties.Length / 8);
		if (num < 2)
		{
			return;
		}
		int[] array = new int[num + 1];
		for (int i = 0; i < num; i++)
		{
			array[i] = BitConverter.ToInt32(indexes, i * 8);
		}
		array[num] = names.Length;
		for (int j = 1; j < num; j++)
		{
			int num2 = j * 8;
			int num3 = BitConverter.ToUInt16(properties, num2);
			int num4 = Math.Clamp(array[j], 0, names.Length);
			int num5 = Math.Clamp(array[j + 1], num4, names.Length);
			int num6 = Array.IndexOf(names, (byte)0, num4, num5 - num4);
			if (num6 >= 0)
			{
				num5 = num6;
			}
			string text = Encoding.UTF8.GetString(names, num4, num5 - num4).Trim();
			if (num3 > 0 && text.Length != 0)
			{
				byte b = (byte)(properties[num2 + 2] & 0x3F);
				string type = b switch
				{
					13 => "Spell",
					14 => "Trap",
					_ => "Monster",
				};
				CardCatalogEntry cardCatalogEntry = result.GetValueOrDefault(num3) ?? new CardCatalogEntry
				{
					CardId = num3,
					Mrk = j,
					Type = type,
					SubType = "0x" + b.ToString("X2", CultureInfo.InvariantCulture)
				};
				int key = num3;
				result[key] = language switch
				{
					"zh-cn" => cardCatalogEntry with
					{
						SimplifiedChineseName = text,
						Type = type,
						Mrk = j
					},
					"zh-tw" => cardCatalogEntry with
					{
						TraditionalChineseName = text,
						Type = type,
						Mrk = j
					}, 
					"ja-jp" => cardCatalogEntry with
					{
						JapaneseName = text,
						Type = type,
						Mrk = j
					}, 
					"en-us" => cardCatalogEntry with
					{
						EnglishName = text,
						Type = type,
						Mrk = j
					}, 
					_ => cardCatalogEntry,
				};
			}
		}
	}

	private static byte[] ReadDecodedTextAsset(LocatedAsset location)
	{
		TextAssetRef[] array = new ModEngine().ReadTextAssets(location.BundlePath).ToArray();
		string stem = Path.GetFileNameWithoutExtension(location.FileName);
		TextAssetRef? obj = array.FirstOrDefault((TextAssetRef asset) => asset.Name.Contains(stem, StringComparison.OrdinalIgnoreCase)) ?? ((array.Length == 1) ? array[0] : null);
		if (obj == null)
		{
			throw new InvalidDataException("字典 Bundle 中找不到 " + location.FileName + " 的 TextAsset。");
		}
		byte[] data = obj.Data;
		int num = Find(data, "YDLZ"u8);
		if (num >= 0 && TryInflate(data.AsSpan(num + 8).ToArray(), out byte[] data2))
		{
			return data2;
		}
		foreach (int item in CandidateKeys())
		{
			byte[] array2 = new byte[data.Length];
			for (int num2 = 0; num2 < data.Length; num2++)
			{
				array2[num2] = (byte)(data[num2] ^ ((((num2 + item + 573) * item) ^ (num2 % 7)) & 0xFF));
			}
			if (TryInflate(array2, out byte[] data3))
			{
				return data3;
			}
		}
		return data;
	}

	private static IEnumerable<int> CandidateKeys()
	{
		yield return 61;
		for (int key = 0; key < 1024; key++)
		{
			if (key != 61)
			{
				yield return key;
			}
		}
	}

	private static bool TryInflate(byte[] input, out byte[] data)
	{
		if (TryStream(input, zlib: true, out data))
		{
			return true;
		}
		if (input.Length > 2 && TryStream(input.AsSpan(2).ToArray(), zlib: false, out data))
		{
			return true;
		}
		data = Array.Empty<byte>();
		return false;
	}

	private static bool TryStream(byte[] input, bool zlib, out byte[] data)
	{
		try
		{
			using MemoryStream stream = new MemoryStream(input, writable: false);
			using Stream stream2 = (zlib ? ((Stream)new ZLibStream(stream, CompressionMode.Decompress)) : ((Stream)new DeflateStream(stream, CompressionMode.Decompress)));
			using MemoryStream memoryStream = new MemoryStream();
			stream2.CopyTo(memoryStream);
			data = memoryStream.ToArray();
			return data.Length != 0;
		}
		catch
		{
			data = Array.Empty<byte>();
			return false;
		}
	}

	private static int Find(byte[] source, ReadOnlySpan<byte> needle)
	{
		for (int i = 0; i <= source.Length - needle.Length; i++)
		{
			if (source.AsSpan(i, needle.Length).SequenceEqual(needle))
			{
				return i;
			}
		}
		return -1;
	}

	private static bool HasAnyName(CardCatalogEntry entry)
	{
		if (entry.SimplifiedChineseName.Length <= 0 && entry.TraditionalChineseName.Length <= 0 && entry.JapaneseName.Length <= 0)
		{
			return entry.EnglishName.Length > 0;
		}
		return true;
	}

	private static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			Span<byte> span = stackalloc byte[7];
			return fileStream.Read(span) == 7 && span.SequenceEqual("UnityFS"u8);
		}
		catch
		{
			return false;
		}
	}
}
