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

	private static readonly ConcurrentDictionary<string, string> PreferredLocalRoots = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
		string fullPath = Path.GetFullPath(gameRoot);
		if (ResourceSource.IsMobile(fullPath))
		{
			return fullPath;
		}
		string text = Path.Combine(fullPath, "LocalData");
		if (!Directory.Exists(text))
		{
			return null;
		}
		if (PreferredLocalRoots.TryGetValue(fullPath, out string value) && Directory.Exists(value) && IsInsideLocalData(value, text))
		{
			return value;
		}
		return (from x in Directory.GetDirectories(text)
			select Path.Combine(x, "0000")).Where(Directory.Exists).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
	}

	public static void SetPreferredLocalRoot(string gameRoot, string? localRoot)
	{
		string fullPath = Path.GetFullPath(gameRoot);
		if (string.IsNullOrWhiteSpace(localRoot))
		{
			PreferredLocalRoots.TryRemove(fullPath, out string _);
			return;
		}
		string fullPath2 = Path.GetFullPath(localRoot);
		if (!ResourceSource.IsMobile(fullPath) || !fullPath.Equals(fullPath2, StringComparison.OrdinalIgnoreCase))
		{
			string localData = Path.Combine(fullPath, "LocalData");
			if (!Directory.Exists(fullPath2) || !IsInsideLocalData(fullPath2, localData) || !string.Equals(Path.GetFileName(fullPath2), "0000", StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("所选账号目录必须是当前游戏 LocalData/<账号>/0000。", "localRoot");
			}
			PreferredLocalRoots[fullPath] = fullPath2;
		}
	}

	private static bool IsInsideLocalData(string path, string localData)
	{
		string relativePath = Path.GetRelativePath(Path.GetFullPath(localData), Path.GetFullPath(path));
		if (relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			return !Path.IsPathRooted(relativePath);
		}
		return false;
	}

	public static string StreamingRoot(string gameRoot)
	{
		return Path.Combine(gameRoot, "masterduel_Data", "StreamingAssets", "AssetBundle");
	}

	public static string CachePath(string localRoot, string streamingRoot)
	{
		string text = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{localRoot}|{streamingRoot}|{"v6"}"))).Substring(0, 12);
		string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
		Directory.CreateDirectory(text2);
		return Path.Combine(text2, "index_" + text + ".json");
	}

	public static GameIndex Build(string gameRoot, Action<int, int, int>? progress = null)
	{
		if (ResourceSource.IsMobile(gameRoot))
		{
			return ResourceSource.BuildMobile(gameRoot, progress);
		}
		string localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
		string streamingRoot = StreamingRoot(gameRoot);
		FileInfo[] array = (from x in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
			select new FileInfo(x)).ToArray();
		HashSet<string> hashSet = new HashSet<string>(from x in array
			where x.Length >= 200000 && x.Length < 300000 && IsUnityBundle(x.FullName)
			select x.FullName, StringComparer.OrdinalIgnoreCase);
		foreach (string item in EnumerateDownloadedCardIllustrationBundles(localRoot, (IReadOnlyCollection<FileInfo>)(object)array))
		{
			hashSet.Add(item);
		}
		string backedLocal = Path.Combine(gameRoot, "_MD卡图备份", "本地卡图");
		if (Directory.Exists(backedLocal))
		{
			foreach (string item2 in Directory.EnumerateFiles(backedLocal, "*", SearchOption.AllDirectories))
			{
				string text = Path.Combine(localRoot, Path.GetRelativePath(backedLocal, item2));
				if (File.Exists(text) && IsUnityBundle(text))
				{
					hashSet.Add(text);
				}
			}
		}
		List<(string Path, string Root, string Kind)> files = hashSet.Select((string x) => (Path: x, Root: localRoot, Kind: "本地卡图")).ToList();
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
				foreach (TexRef item3 in engine.ScanBundle(file.Path, file.Root, file.Kind, includeDependencies: false).Textures.Where((TexRef x) => file.Kind != "本地卡图" || IsDirectCardIllustration(x) || File.Exists(Path.Combine(backedLocal, x.RelativeBundlePath))))
				{
					bag.Add(item3);
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
		foreach (TexRef cardFrame in GetCardFrames(gameRoot))
		{
			bag.Add(cardFrame);
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
		GameIndex value = Build(gameRoot, progress);
		Save(gameRoot, value);
	}

	public static void Save(string gameRoot, IEnumerable<TexRef> textures)
	{
		string path = CachePath(FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000"), StreamingRoot(gameRoot));
		GameIndex gameIndex = (File.Exists(path) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path)) : null);
		Save(gameRoot, new GameIndex
		{
			Textures = textures.ToList(),
			AlternateArtIndexVersion = (gameIndex?.AlternateArtIndexVersion ?? 0),
			CheckedLocalBundlePaths = (gameIndex?.CheckedLocalBundlePaths ?? new List<string>())
		});
	}

	public static void Save(string gameRoot, GameIndex index)
	{
		if (index.Textures.Any(t => !IndexWorkspaceGuard.Belongs(gameRoot, t)))
			throw new InvalidDataException("索引包含其他目录或账号的资源，已拒绝覆盖当前账号索引。");
		string path = CachePath(FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000"), StreamingRoot(gameRoot));
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try { File.WriteAllText(temporary, JsonSerializer.Serialize(index)); File.Move(temporary, path, true); }
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}

	public static void AddCardFramesAndSave(string gameRoot)
	{
		string text = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
		string text2 = StreamingRoot(gameRoot);
		string path = CachePath(text, text2);
		GameIndex gameIndex;
		if (File.Exists(path))
		{
			gameIndex = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path)) ?? new GameIndex();
		}
		else
		{
			string text3 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + "|" + text2 + "|v4"))).Substring(0, 12);
			string path2 = Path.Combine(Path.GetDirectoryName(path), "index_" + text3 + ".json");
			gameIndex = (File.Exists(path2) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path2)) ?? new GameIndex()) : Build(gameRoot));
		}
		gameIndex.Textures.RemoveAll((TexRef x) => x.SourceKind == "卡框资源");
		gameIndex.Textures.AddRange(GetCardFrames(gameRoot));
		gameIndex.Textures.Sort((TexRef a, TexRef b) => string.Compare($"{a.SourceKind}\0{a.Category}\0{a.Name}\0{a.Width:D8}", $"{b.SourceKind}\0{b.Category}\0{b.Name}\0{b.Width:D8}", StringComparison.Ordinal));
		Save(gameRoot, gameIndex);
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
				foreach (TexRef item in engine.ScanBundle(file.Path, file.Root, "本地卡图", includeDependencies: false).Textures.Where(IsDirectCardIllustration))
				{
					added.Add(item);
					if (item.CardKey == cardKey)
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
		List<TexRef> textures = (from x in added.GroupBy<TexRef, string>((TexRef x) => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			select x.First()).ToList();
		return new MissingCardScanResult
		{
			Textures = textures,
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
		string text = Crc32(logicalPath).ToString("x8");
		return Path.Combine(text.Substring(0, 2), text);
	}

	public static IEnumerable<string> CardIllustrationBundleCandidates(string localRoot, string cardKey)
	{
		string[] array = new string[2] { "tcg", "ocg" };
		string[] array2 = array;
		foreach (string illustrationType in array2)
		{
			string text = Path.Combine(localRoot, CardIllustrationRelativePath(cardKey, illustrationType));
			if (File.Exists(text) && IsUnityBundle(text))
			{
				yield return text;
			}
		}
	}

	private static IEnumerable<string> EnumerateDownloadedCardIllustrationBundles(string localRoot, IReadOnlyCollection<FileInfo>? localFiles = null)
	{
		if (localFiles == null)
		{
			localFiles = (IReadOnlyCollection<FileInfo>)(object)(from x in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
				select new FileInfo(x)).ToArray();
		}
		Dictionary<string, string> available = localFiles.ToDictionary<FileInfo, string, string>((FileInfo x) => Path.GetRelativePath(localRoot, x.FullName), (FileInfo x) => x.FullName, StringComparer.OrdinalIgnoreCase);
		foreach (int cardId in from id in CardCatalogService.LoadBestAvailable().Entries.Select((CardCatalogEntry entry) => entry.CardId).Distinct()
			orderby id
			select id)
		{
			string[] array = new string[2] { "tcg", "ocg" };
			string[] array2 = array;
			foreach (string illustrationType in array2)
			{
				string key = CardIllustrationRelativePath(cardId.ToString(), illustrationType);
				if (available.TryGetValue(key, out string value) && IsUnityBundle(value))
				{
					yield return value;
				}
			}
		}
	}

	private static uint Crc32(string value)
	{
		uint num = uint.MaxValue;
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		foreach (byte b in bytes)
		{
			num = Crc32Table[(num ^ b) & 0xFF] ^ (num >> 8);
		}
		return num ^ 0xFFFFFFFFu;
	}

	public static int RemoveSpineAtlasParts(GameIndex index)
	{
		return index.Textures.RemoveAll((TexRef x) => x.SourceKind == "本地卡图" && x.Name.Length > 1 && x.Name[0] == 'P' && x.Name.AsSpan(1).ToString().All(char.IsAsciiDigit));
	}

	public static int RemoveNonCardLocalTextures(GameIndex index)
	{
		return index.Textures.RemoveAll((TexRef x) => x.SourceKind == "本地卡图" && x.CardKey.Length == 0);
	}

	internal static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			byte[] array = new byte[7];
			return fileStream.Read(array) == 7 && Encoding.ASCII.GetString(array) == "UnityFS";
		}
		catch
		{
			return false;
		}
	}
}
