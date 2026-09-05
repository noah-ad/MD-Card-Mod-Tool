using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

	private static readonly string[] Languages = ["zh-cn", "zh-tw", "ja-jp", "en-us"];

	public static string ExtraCatalogPath => Path.Combine(AppSettingsStore.AppDataRoot, "card-catalog-extra-v2.json.br");

	public static bool NeedsUpdate(string gameRoot) => NeedsUpdate(gameRoot, AppSettingsStore.AppDataRoot);

	internal static bool NeedsUpdate(string gameRoot, string cacheDirectory)
	{
		string extraPath = Path.Combine(cacheDirectory, "card-catalog-extra-v2.json.br");
		string statePath = Path.Combine(cacheDirectory, "card-catalog-game-state-v2.json");
		try
		{
			if (!File.Exists(extraPath) || !File.Exists(statePath)) return true;
			UpdateState? state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(statePath));
			string localRoot = IndexService.FindLocalRoot(gameRoot) ?? "";
			return state == null || state.FormatVersion != 3
				|| !string.Equals(state.BuildId, PortableIndexService.GetGameBuildId(gameRoot), StringComparison.Ordinal)
				|| !string.Equals(Path.GetFullPath(state.LocalDataRoot), Path.GetFullPath(localRoot), StringComparison.OrdinalIgnoreCase)
				|| state.ResourceFingerprint != CaptureResourceFingerprint(localRoot, IndexService.StreamingRoot(gameRoot));
		}
		catch
		{
			return true;
		}
	}

	public static IReadOnlyList<CardCatalogEntry> UpdateIfNeeded(string gameRoot,
		Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
		=> UpdateIfNeeded(gameRoot, AppSettingsStore.AppDataRoot, progress, cancellationToken);

	internal static IReadOnlyList<CardCatalogEntry> UpdateIfNeeded(string gameRoot, string cacheDirectory,
		Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		string extraPath = Path.Combine(cacheDirectory, "card-catalog-extra-v2.json.br");
		string statePath = Path.Combine(cacheDirectory, "card-catalog-game-state-v2.json");
		if (!NeedsUpdate(gameRoot, cacheDirectory))
		{
			return File.Exists(extraPath) ? CardCatalogService.Read(extraPath) : [];
		}
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? "";
		string fingerprint = CaptureResourceFingerprint(localRoot, IndexService.StreamingRoot(gameRoot));
		IReadOnlyList<CardCatalogEntry> entries = Extract(gameRoot, progress, cancellationToken);
		if (entries.Count == 0)
		{
			throw new InvalidDataException("没有从当前游戏 Build 解析出卡片目录；保留原目录不变。" );
		}
		cancellationToken.ThrowIfCancellationRequested();
		if (fingerprint != CaptureResourceFingerprint(localRoot, IndexService.StreamingRoot(gameRoot)))
			throw new IOException("卡片资源在读取期间发生变化，请等待游戏下载结束后重新加载目录；原目录未改动。");
		if (File.Exists(extraPath))
		{
			try { entries = CardCatalogService.MergeGameCatalogs(CardCatalogService.Read(extraPath), entries); }
			catch (InvalidDataException) { }
			catch (JsonException) { }
		}
		CardCatalogService.Write(extraPath, entries);
		UpdateState state = new()
		{
			BuildId = PortableIndexService.GetGameBuildId(gameRoot),
			LocalDataRoot = IndexService.FindLocalRoot(gameRoot) ?? "",
			CardCount = entries.Count,
			ResourceFingerprint = fingerprint
		};
		Directory.CreateDirectory(cacheDirectory);
		string temporary = statePath + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
		File.Move(temporary, statePath, overwrite: true);
		return entries;
	}

	// Steam build IDs do not change for in-game data downloads. Hash cheap file
	// metadata, not Bundle contents; paths also detect same-count substitutions.
	internal static string CaptureResourceFingerprint(params string[] roots)
	{
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (string root in roots)
		{
			if (!Directory.Exists(root)) continue;
			foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories)
				.Where(file => file.Name.Length == 8 && file.Name.All(char.IsAsciiHexDigit))
				.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase))
			{
				string item = $"{file.FullName.ToUpperInvariant()}\0{file.Length}\0{file.LastWriteTimeUtc.Ticks}\n";
				hash.AppendData(Encoding.UTF8.GetBytes(item));
			}
		}
		return Convert.ToHexString(hash.GetHashAndReset());
	}

	public static IReadOnlyList<CardCatalogEntry> Extract(string gameRoot,
		Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot)
			?? throw new DirectoryNotFoundException("未找到活动 LocalData 账号。" );
		string streamingRoot = IndexService.StreamingRoot(gameRoot);
		List<string> files = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
			.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
		if (Directory.Exists(streamingRoot))
		{
			files.AddRange(Directory.EnumerateFiles(streamingRoot, "*", SearchOption.AllDirectories)
				.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.OrdinalIgnoreCase));
		}
		ConcurrentDictionary<string, LocatedAsset> located = new(StringComparer.OrdinalIgnoreCase);
		int done = 0;
		bool[] completed = new bool[files.Count];
		int completedPrefix = 0;
		object completionGate = new();
		Dictionary<string, LocatedAsset>? completeFamily = null;
		int familyPriority = int.MaxValue;
		Parallel.ForEach(Enumerable.Range(0, files.Count), new ParallelOptions
		{
			// AssetsTools parsing is allocation and storage heavy. Leaving CPU capacity
			// for WinForms prevents Windows from marking the app unresponsive while a
			// first-run catalog refresh scans tens of thousands of Bundles.
			MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 2, 4),
			CancellationToken = cancellationToken
		}, (priority, loop) =>
		{
			string file = files[priority];
			void Locate(string key, string name)
			{
				System.Diagnostics.Trace.WriteLine($"Card dictionary {key}: {file}");
				LocatedAsset incoming = new(file, name, priority);
				located.AddOrUpdate(key, incoming, (_, current) => priority < current.Priority ? incoming : current);
			}
			try
			{
				if (!IsUnityBundle(file)) return;
				ModEngine engine = new();
				foreach (string container in engine.ReadAssetBundleContainerPaths(file))
				{
					string normalized = container.Replace('\\', '/').ToLowerInvariant();
					string name = Path.GetFileName(normalized);
					if (name is "card_prop.bytes" or "card_name.bytes" or "card_indx.bytes")
					{
						lock (completionGate)
						{
							if (priority < familyPriority)
							{
								var family = TryLocateDictionaryFamily(file, normalized, localRoot, streamingRoot);
								if (family != null) { completeFamily = family; familyPriority = priority; }
							}
						}
					}
					if (name == "card_prop.bytes")
					{
						Locate("prop", name);
					}
					else if (name is "card_name.bytes" or "card_indx.bytes")
					{
						foreach (string language in Languages)
						{
							if (normalized.Contains("/" + language + "/", StringComparison.Ordinal)
								|| normalized.Contains("_" + language, StringComparison.Ordinal))
							{
								Locate(language + "|" + (name.StartsWith("card_name", StringComparison.Ordinal) ? "name" : "index"), name);
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
				int current = Interlocked.Increment(ref done);
				if (current % 100 == 0 || current == files.Count)
				{
					progress?.Invoke(current, files.Count, located.Count);
				}
				lock (completionGate)
				{
					completed[priority] = true;
					while (completedPrefix < completed.Length && completed[completedPrefix]) completedPrefix++;
					// Stop only once every higher-priority candidate was checked. Plain
					// first-wins is nondeterministic; scanning the entire texture library
					// after all nine best dictionary assets are found is unnecessary.
					if (located.Count == Languages.Length * 2 + 1
						&& completedPrefix > located.Values.Max(asset => asset.Priority)) loop.Stop();
					if (completeFamily != null && completedPrefix > familyPriority) loop.Stop();
				}
			}
		});
		if (completeFamily != null)
			located = new ConcurrentDictionary<string, LocatedAsset>(completeFamily, StringComparer.OrdinalIgnoreCase);

		if (!located.TryGetValue("prop", out LocatedAsset? propertyLocation))
		{
			throw new InvalidDataException("当前游戏资源中没有定位到 card_prop.bytes。" );
		}
		byte[] properties = ReadDecodedTextAsset(propertyLocation);
		Dictionary<int, CardCatalogEntry> result = [];
		foreach (string language in Languages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!located.TryGetValue(language + "|name", out LocatedAsset? nameLocation)
				|| !located.TryGetValue(language + "|index", out LocatedAsset? indexLocation))
			{
				continue;
			}
			byte[] names = ReadDecodedTextAsset(nameLocation);
			byte[] indexes = ReadDecodedTextAsset(indexLocation);
			MergeLanguage(result, language, names, indexes, properties);
		}
		return result.Values.Where(HasAnyName).OrderBy(entry => entry.CardId).ToArray();
	}

	private static Dictionary<string, LocatedAsset>? TryLocateDictionaryFamily(string observedFile,
		string container, string localRoot, string streamingRoot)
	{
		// The version folder is discovered from the actual Bundle, never hard-coded.
		// Confirm its CRC mapping before using it to probe other language packs.
		Match match = Regex.Match(container,
			@"^assets/resourcesassetbundle/card/data/([^/]+)/(zh-cn|zh-tw|ja-jp|en-us)/(card_name|card_indx|card_prop)\.bytes$");
		if (!match.Success) return null;
		string family = match.Groups[1].Value;
		string stem = "CARD_" + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups[3].Value[5..]);
		string language = CultureInfo.GetCultureInfo(match.Groups[2].Value).Name;
		string relative = IndexService.ResourceBundleRelativePath($"Card/Data/{family}/{language}/{stem}");
		if (!Path.GetFileName(relative).Equals(Path.GetFileName(observedFile), StringComparison.OrdinalIgnoreCase)) return null;
		Dictionary<string, LocatedAsset> found = new(StringComparer.OrdinalIgnoreCase);
		foreach (string code in Languages)
		{
			language = CultureInfo.GetCultureInfo(code).Name;
			foreach (string assetName in new[] { "CARD_Name", "CARD_Indx", "CARD_Prop" })
			{
				relative = IndexService.ResourceBundleRelativePath($"Card/Data/{family}/{language}/{assetName}");
				string expected = $"assets/resourcesassetbundle/card/data/{family}/{code}/{assetName.ToLowerInvariant()}.bytes";
				foreach (string root in new[] { localRoot, streamingRoot })
				{
					string path = Path.Combine(root, relative);
					if (!File.Exists(path)) continue;
					if (!new ModEngine().ReadAssetBundleContainerPaths(path).Any(p => p.Replace('\\', '/').Equals(expected, StringComparison.OrdinalIgnoreCase))) continue;
					string key = assetName == "CARD_Prop" ? "prop" : code + (assetName == "CARD_Name" ? "|name" : "|index");
					found.TryAdd(key, new(path, assetName.ToLowerInvariant() + ".bytes", 0));
					break;
				}
			}
		}
		return found.ContainsKey("prop") && Languages.Any(code => found.ContainsKey(code + "|name") && found.ContainsKey(code + "|index"))
			? found : null;
	}

	private static void MergeLanguage(Dictionary<int, CardCatalogEntry> result, string language,
		byte[] names, byte[] indexes, byte[] properties)
	{
		int count = Math.Min(indexes.Length / 8, properties.Length / 8);
		if (count < 2) return;
		int[] offsets = new int[count + 1];
		for (int i = 0; i < count; i++)
		{
			offsets[i] = BitConverter.ToInt32(indexes, i * 8);
		}
		offsets[count] = names.Length;
		for (int i = 1; i < count; i++)
		{
			int propertyOffset = i * 8;
			int cardId = BitConverter.ToUInt16(properties, propertyOffset);
			int start = Math.Clamp(offsets[i], 0, names.Length);
			int end = Math.Clamp(offsets[i + 1], start, names.Length);
			int zero = Array.IndexOf(names, (byte)0, start, end - start);
			if (zero >= 0) end = zero;
			string name = Encoding.UTF8.GetString(names, start, end - start).Trim();
			if (cardId <= 0 || name.Length == 0) continue;
			byte typeCode = (byte)(properties[propertyOffset + 2] & 0x3F);
			string type = typeCode switch { 0x0D => "Spell", 0x0E => "Trap", _ => "Monster" };
			CardCatalogEntry current = result.GetValueOrDefault(cardId) ?? new CardCatalogEntry
			{
				CardId = cardId,
				Mrk = i,
				Type = type,
				SubType = "0x" + typeCode.ToString("X2", CultureInfo.InvariantCulture)
			};
			result[cardId] = language switch
			{
				"zh-cn" => current with { SimplifiedChineseName = name, Type = type, Mrk = i },
				"zh-tw" => current with { TraditionalChineseName = name, Type = type, Mrk = i },
				"ja-jp" => current with { JapaneseName = name, Type = type, Mrk = i },
				"en-us" => current with { EnglishName = name, Type = type, Mrk = i },
				_ => current
			};
		}
	}

	private static byte[] ReadDecodedTextAsset(LocatedAsset location)
	{
		ModEngine engine = new();
		TextAssetRef[] assets = engine.ReadTextAssets(location.BundlePath).ToArray();
		string stem = Path.GetFileNameWithoutExtension(location.FileName);
		TextAssetRef? selected = assets.FirstOrDefault(asset => asset.Name.Contains(stem, StringComparison.OrdinalIgnoreCase))
			?? (assets.Length == 1 ? assets[0] : null);
		if (selected == null)
		{
			throw new InvalidDataException("字典 Bundle 中找不到 " + location.FileName + " 的 TextAsset。" );
		}
		byte[] raw = selected.Data;
		int ydlz = Find(raw, "YDLZ"u8);
		if (ydlz >= 0 && TryInflate(raw.AsSpan(ydlz + 8).ToArray(), out byte[]? ydlzData)) return ydlzData;
		foreach (int key in CandidateKeys())
		{
			byte[] decrypted = new byte[raw.Length];
			for (int i = 0; i < raw.Length; i++)
			{
				decrypted[i] = (byte)(raw[i] ^ (((i + key + 0x23D) * key ^ (i % 7)) & 0xFF));
			}
			if (TryInflate(decrypted, out byte[]? decoded)) return decoded;
		}
		return raw;
	}

	private static IEnumerable<int> CandidateKeys()
	{
		yield return 61;
		for (int key = 0; key < 1024; key++) if (key != 61) yield return key;
	}

	private static bool TryInflate(byte[] input, out byte[] data)
	{
		if (TryStream(input, zlib: true, out data)) return true;
		if (input.Length > 2 && TryStream(input.AsSpan(2).ToArray(), zlib: false, out data)) return true;
		data = [];
		return false;
	}

	private static bool TryStream(byte[] input, bool zlib, out byte[] data)
	{
		try
		{
			using MemoryStream source = new(input, writable: false);
			using Stream decoder = zlib ? new ZLibStream(source, CompressionMode.Decompress) : new DeflateStream(source, CompressionMode.Decompress);
			using MemoryStream output = new();
			decoder.CopyTo(output);
			data = output.ToArray();
			return data.Length > 0;
		}
		catch
		{
			data = [];
			return false;
		}
	}

	private static int Find(byte[] source, ReadOnlySpan<byte> needle)
	{
		for (int i = 0; i <= source.Length - needle.Length; i++)
		{
			if (source.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
		}
		return -1;
	}

	private static bool HasAnyName(CardCatalogEntry entry) => entry.SimplifiedChineseName.Length > 0
		|| entry.TraditionalChineseName.Length > 0 || entry.JapaneseName.Length > 0 || entry.EnglishName.Length > 0;

	private static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			Span<byte> header = stackalloc byte[7];
			return stream.Read(header) == 7 && header.SequenceEqual("UnityFS"u8);
		}
		catch
		{
			return false;
		}
	}
}
