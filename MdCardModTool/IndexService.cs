using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class IndexService
{
	private const string CacheVersion = "v6";

	private static readonly ConcurrentDictionary<string, string> PreferredLocalRoots = new(StringComparer.OrdinalIgnoreCase);

	private static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(delegate(int index)
	{
		uint num = (uint)index;
		for (int i = 0; i < 8; i++)
		{
			num = (((num & 1) != 0) ? (0xEDB88320u ^ (num >> 1)) : (num >> 1));
		}
		return num;
	}).ToArray();

	public static string? FindLocalRoot(string gameRoot)
	{
		string fullGameRoot = Path.GetFullPath(gameRoot);
		string local = Path.Combine(fullGameRoot, "LocalData");
		if (!Directory.Exists(local))
		{
			return null;
		}
		if (PreferredLocalRoots.TryGetValue(fullGameRoot, out string? preferred)
			&& Directory.Exists(preferred)
			&& IsInsideLocalData(preferred, local))
		{
			return preferred;
		}
		return (from x in Directory.GetDirectories(local)
			select Path.Combine(x, "0000")).Where(Directory.Exists).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
	}

	public static void SetPreferredLocalRoot(string gameRoot, string? localRoot)
	{
		string fullGameRoot = Path.GetFullPath(gameRoot);
		if (string.IsNullOrWhiteSpace(localRoot))
		{
			PreferredLocalRoots.TryRemove(fullGameRoot, out _);
			return;
		}
		string fullLocalRoot = Path.GetFullPath(localRoot);
		string localData = Path.Combine(fullGameRoot, "LocalData");
		if (!Directory.Exists(fullLocalRoot) || !IsInsideLocalData(fullLocalRoot, localData)
			|| !string.Equals(Path.GetFileName(fullLocalRoot), "0000", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("所选账号目录必须是当前游戏 LocalData/<账号>/0000。", nameof(localRoot));
		}
		PreferredLocalRoots[fullGameRoot] = fullLocalRoot;
	}

	private static bool IsInsideLocalData(string path, string localData)
	{
		string relative = Path.GetRelativePath(Path.GetFullPath(localData), Path.GetFullPath(path));
		return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
			&& !Path.IsPathRooted(relative);
	}

	public static string StreamingRoot(string gameRoot)
	{
		return Path.Combine(gameRoot, "masterduel_Data", "StreamingAssets", "AssetBundle");
	}

	public static string CachePath(string localRoot, string streamingRoot)
	{
		string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{localRoot}|{streamingRoot}|{"v6"}"))).Substring(0, 12);
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
		Directory.CreateDirectory(text);
		return Path.Combine(text, "index_" + id + ".json");
	}

	public static GameIndex Build(string gameRoot, Action<int, int, int>? progress = null)
	{
		string localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
		string streamingRoot = StreamingRoot(gameRoot);
		FileInfo[] allLocalFiles = (from x in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
			select new FileInfo(x)).ToArray();
		HashSet<string> localPaths = new HashSet<string>(from x in allLocalFiles
			where x.Length >= 200000 && x.Length < 300000 && IsUnityBundle(x.FullName)
			select x.FullName, StringComparer.OrdinalIgnoreCase);
		foreach (string path in EnumerateDownloadedCardIllustrationBundles(localRoot, (IReadOnlyCollection<FileInfo>?)(object)allLocalFiles))
		{
			localPaths.Add(path);
		}
		string backedLocal = Path.Combine(gameRoot, "_MD卡图备份", "本地卡图");
		if (Directory.Exists(backedLocal))
		{
			foreach (string backup in Directory.EnumerateFiles(backedLocal, "*", SearchOption.AllDirectories))
			{
				string live = Path.Combine(localRoot, Path.GetRelativePath(backedLocal, backup));
				if (File.Exists(live) && IsUnityBundle(live))
				{
					localPaths.Add(live);
				}
			}
		}
		List<(string Path, string Root, string Kind)> files = localPaths.Select((string x) => (Path: x, Root: localRoot, Kind: "本地卡图")).ToList();
		if (Directory.Exists(streamingRoot))
		{
			files.AddRange(from x in Directory.EnumerateFiles(streamingRoot, "*", SearchOption.AllDirectories).Where(IsUnityBundle)
				select (Path: x, Root: streamingRoot, Kind: "游戏内图片"));
		}
		ModEngine engine = new ModEngine();
		ConcurrentBag<TexRef> bag = new ConcurrentBag<TexRef>();
		int done = 0;
		Parallel.ForEach(files, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount - 1)
		}, delegate((string Path, string Root, string Kind) file)
		{
			try
			{
				foreach (TexRef current in engine.ScanBundle(file.Path, file.Root, file.Kind, includeDependencies: false).Textures.Where((TexRef x) => file.Kind != "本地卡图" || IsDirectCardIllustration(x) || File.Exists(Path.Combine(backedLocal, x.RelativeBundlePath))))
				{
					bag.Add(current);
				}
			}
			catch
			{
			}
			int num = Interlocked.Increment(ref done);
			if (num % 100 == 0 || num == files.Count)
			{
				progress?.Invoke(num, files.Count, bag.Count);
			}
		});
		foreach (TexRef frame in GetCardFrames(gameRoot))
		{
			bag.Add(frame);
		}
		return new GameIndex
		{
			Textures = (from x in bag
				orderby x.SourceKind, x.Category, x.Name
				select x).ToList()
		};
	}

	public static void BuildAndSave(string gameRoot, Action<int, int, int>? progress = null)
	{
		GameIndex index = Build(gameRoot, progress);
		File.WriteAllText(CachePath(FindLocalRoot(gameRoot), StreamingRoot(gameRoot)), JsonSerializer.Serialize(index));
	}

	public static void Save(string gameRoot, IEnumerable<TexRef> textures)
	{
		string path = CachePath(FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000"), StreamingRoot(gameRoot));
		GameIndex existing = (File.Exists(path) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path)) : null);
		Save(gameRoot, new GameIndex
		{
			Textures = textures.ToList(),
			AlternateArtIndexVersion = (existing?.AlternateArtIndexVersion ?? 0),
			CheckedLocalBundlePaths = (existing?.CheckedLocalBundlePaths ?? new List<string>())
		});
	}

	public static void Save(string gameRoot, GameIndex index)
	{
		File.WriteAllText(CachePath(FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000"), StreamingRoot(gameRoot)), JsonSerializer.Serialize(index));
	}

	public static void AddCardFramesAndSave(string gameRoot)
	{
		string localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
		string streamingRoot = StreamingRoot(gameRoot);
		string cache = CachePath(localRoot, streamingRoot);
		GameIndex index;
		if (File.Exists(cache))
		{
			index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex();
		}
		else
		{
			string legacyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localRoot + "|" + streamingRoot + "|v4"))).Substring(0, 12);
			string legacy = Path.Combine(Path.GetDirectoryName(cache), "index_" + legacyId + ".json");
			index = (File.Exists(legacy) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(legacy)) ?? new GameIndex()) : Build(gameRoot));
		}
		index.Textures.RemoveAll((TexRef x) => x.SourceKind == "卡框资源");
		index.Textures.AddRange(GetCardFrames(gameRoot));
		index.Textures.Sort((TexRef a, TexRef b) => string.Compare($"{a.SourceKind}\0{a.Category}\0{a.Name}\0{a.Width:D8}", $"{b.SourceKind}\0{b.Category}\0{b.Name}\0{b.Width:D8}", StringComparison.Ordinal));
		File.WriteAllText(cache, JsonSerializer.Serialize(index));
	}

	private static IEnumerable<TexRef> GetCardFrames(string gameRoot)
	{
		return BuiltInCardFrameCatalog.Load();
	}

	public static MissingCardScanResult ScanMissingLocalCard(string gameRoot, GameIndex index, string cardKey, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(cardKey) || !cardKey.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("补查只接受纯数字卡号。", "cardKey");
		}
		string localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
		string streamingRoot = StreamingRoot(gameRoot);
		HashSet<string> knownBundles = (from x in index.Textures
			where x.SourceKind == "本地卡图" && IsDirectCardIllustration(x)
			select Path.GetFullPath(x.BundlePath)).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<(string Path, string Root)> files = ((cardKey == "0") ? (from path in EnumerateDownloadedCardIllustrationBundles(localRoot)
			where !knownBundles.Contains(Path.GetFullPath(path))
			select (Path: path, Root: localRoot)).ToList() : (from path in CardIllustrationBundleCandidates(localRoot, cardKey)
			where !knownBundles.Contains(Path.GetFullPath(path))
			select (Path: path, Root: localRoot)).ToList());
		// A small set of card illustrations ships with the base game instead of the
		// selected account's LocalData. Resolve those deterministic TCG/OCG paths too.
		if (cardKey != "0" && Directory.Exists(streamingRoot))
		{
			files.AddRange(from path in CardIllustrationBundleCandidates(streamingRoot, cardKey)
				where !knownBundles.Contains(Path.GetFullPath(path))
				select (Path: path, Root: streamingRoot));
		}
		int found = 0;
		int done = 0;
		ConcurrentBag<TexRef> added = new ConcurrentBag<TexRef>();
		ModEngine engine = new ModEngine();
		Parallel.ForEach(files, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
			CancellationToken = cancellationToken
		}, delegate((string Path, string Root) file, ParallelLoopState state)
		{
			try
			{
				foreach (TexRef current in engine.ScanBundle(file.Path, file.Root, "本地卡图", includeDependencies: false).Textures.Where(IsDirectCardIllustration))
				{
					added.Add(current);
					if (current.CardKey == cardKey)
					{
						Interlocked.Exchange(ref found, 1);
						state.Stop();
					}
				}
			}
			catch
			{
			}
			finally
			{
				int num = Interlocked.Increment(ref done);
				if (num % 25 == 0 || num == files.Count || Volatile.Read(in found) != 0)
				{
					progress?.Invoke(num, files.Count, added.Count);
				}
			}
		});
		List<TexRef> unique = (from x in added.GroupBy<TexRef, string>((TexRef x) => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			select x.First()).ToList();
		return new MissingCardScanResult
		{
			Textures = unique,
			ScannedBundles = done,
			TotalBundles = files.Count,
			Found = (Volatile.Read(in found) != 0)
		};
	}

	public static bool IsDirectCardIllustration(TexRef texture)
	{
		if (texture.CardKey.Length > 0 && texture.Width == 512)
		{
			if (texture.Height != 512)
			{
				return texture.Height == 1024;
			}
			return true;
		}
		return false;
	}

	public static void NormalizeLocalCardCategory(TexRef texture)
	{
		if (!(texture.SourceKind != "本地卡图") && texture.CardKey.Length != 0)
		{
			if (texture.Width == 512 && texture.Height == 1024)
			{
				texture.Category = "灵摆卡图";
			}
			else if (texture.Width == 512 && texture.Height == 512)
			{
				texture.Category = (texture.IsAlternateArt ? "异画卡图" : (texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图"));
			}
		}
	}

	public static string CardIllustrationRelativePath(string cardKey, string illustrationType = "tcg")
	{
		if (string.IsNullOrWhiteSpace(cardKey) || !cardKey.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("卡号必须是纯数字。", "cardKey");
		}
		return ResourceBundleRelativePath("Card/Images/Illust/" + illustrationType + "/" + cardKey);
	}

	public static string ResourceBundleRelativePath(string logicalPath)
	{
		if (string.IsNullOrWhiteSpace(logicalPath))
		{
			throw new ArgumentException("资源逻辑路径不能为空。", "logicalPath");
		}
		string hash = Crc32(logicalPath).ToString("x8");
		return Path.Combine(hash.Substring(0, 2), hash);
	}

	public static IEnumerable<string> CardIllustrationBundleCandidates(string localRoot, string cardKey)
	{
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string type in array)
		{
			string path = Path.Combine(localRoot, CardIllustrationRelativePath(cardKey, type));
			if (File.Exists(path) && IsUnityBundle(path))
			{
				yield return path;
			}
		}
	}

	private static IEnumerable<string> EnumerateDownloadedCardIllustrationBundles(string localRoot, IReadOnlyCollection<FileInfo>? localFiles = null)
	{
		if (localFiles == null)
		{
			localFiles = (IReadOnlyCollection<FileInfo>?)(object)(from x in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
				select new FileInfo(x)).ToArray();
		}
		Dictionary<string, string> available = localFiles.ToDictionary<FileInfo, string, string>((FileInfo x) => Path.GetRelativePath(localRoot, x.FullName), (FileInfo x) => x.FullName, StringComparer.OrdinalIgnoreCase);
		foreach (int cardId in CardCatalogService.LoadBestAvailable().Entries.Select(entry => entry.CardId).Distinct().OrderBy(id => id))
		{
			string[] array = new string[2] { "tcg", "ocg" };
			foreach (string type in array)
			{
				string relative = CardIllustrationRelativePath(cardId.ToString(), type);
				if (available.TryGetValue(relative, out var path) && IsUnityBundle(path))
				{
					yield return path;
				}
			}
		}
	}

	private static uint Crc32(string value)
	{
		uint crc = uint.MaxValue;
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		foreach (byte item in bytes)
		{
			crc = Crc32Table[(crc ^ item) & 0xFF] ^ (crc >> 8);
		}
		return crc ^ 0xFFFFFFFFu;
	}

	public static int RemoveSpineAtlasParts(GameIndex index)
	{
		return index.Textures.RemoveAll((TexRef x) => x.SourceKind == "本地卡图" && x.Name.Length > 1 && x.Name[0] == 'P' && x.Name.AsSpan(1).ToString().All(char.IsAsciiDigit));
	}

	public static int RemoveNonCardLocalTextures(GameIndex index)
	{
		return index.Textures.RemoveAll((TexRef x) => x.SourceKind == "本地卡图" && x.CardKey.Length == 0);
	}

	private static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			byte[] bytes = new byte[7];
			return stream.Read(bytes) == 7 && Encoding.ASCII.GetString(bytes) == "UnityFS";
		}
		catch
		{
			return false;
		}
	}
}
