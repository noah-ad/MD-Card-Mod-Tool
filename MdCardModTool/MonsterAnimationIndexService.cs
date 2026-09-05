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
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class MonsterAnimationIndexService
{
	public const string BundledFileName = "prebuilt-animation-index-v1.json.br";
	private static readonly string[] DeterministicScales = new string[10]
	{
		"1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4"
	}.Concat(Enumerable.Range(1, 2000).Select(value => ((double)value / 1000.0)
		.ToString("0.###", CultureInfo.InvariantCulture))).Distinct(StringComparer.Ordinal).ToArray();

	public static string BundledPath => AppPaths.ResolveFile("prebuilt-animation-index-v1.json.br");

	public static MonsterAnimationSet Find(string gameRoot, string cardId)
	{
		if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0)
		{
			throw new ArgumentException("卡号必须是纯数字。", "cardId");
		}
		List<MonsterAnimationAssetRef> assets = FindDeterministicCandidates(gameRoot, cardId);
		try
		{
			assets.AddRange(from x in LoadBestAvailable(gameRoot, out string _)
				where x.CardId == cardId && File.Exists(x.BundlePath)
				select x);
		}
		catch
		{
		}
		assets = (from x in assets.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			select x.First() into x
			orderby x.Kind
			select x).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ToList();
		return new MonsterAnimationSet
		{
			CardId = cardId,
			Assets = assets
		};
	}

	public static HashSet<string> LoadBundledCardIds()
	{
		string path = AppPaths.ResolveFile("monster-animation-cards-v1.txt");
		if (!File.Exists(path))
		{
			return new HashSet<string>();
		}
		return (from x in File.ReadLines(path)
			select x.Trim() into x
			where x.Length > 0 && x.All(char.IsAsciiDigit)
			select x).ToHashSet<string>(StringComparer.Ordinal);
	}

	public static bool HasInstalledAnimation(string gameRoot, string cardId)
	{
		if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0)
		{
			return false;
		}
		string[] roots = AnimationRoots(gameRoot);
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string region in array)
		{
			(string High, string Sd) paths = SkeletonRelativePaths(cardId, region);
			if (roots.Any((string root) => File.Exists(Path.Combine(root, paths.High))) && roots.Any((string root) => File.Exists(Path.Combine(root, paths.Sd))))
			{
				return true;
			}
		}
		return false;
	}

	public static IReadOnlyList<string> FindInstalledCardIds(string gameRoot)
	{
		string[] roots = AnimationRoots(gameRoot);
		HashSet<string> files = roots.SelectMany((string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Select(Path.GetFullPath).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> candidates = LoadBundledCardIds();
		candidates.UnionWith(CardCatalogService.LoadBestAvailable().Entries
			.Where(entry => entry.IsMonster)
			.Select(entry => entry.AnimationId.ToString(CultureInfo.InvariantCulture)));
		try
		{
			candidates.UnionWith(LoadBestAvailable(gameRoot, out _).Select(asset => asset.CardId));
		}
		catch
		{
		}
		List<string> result = new List<string>();
		foreach (string cardId in candidates.Where(id => id.Length > 0 && id.All(char.IsAsciiDigit))
			.OrderBy(id => int.TryParse(id, out int value) ? value : int.MaxValue))
		{
			string[] array = new string[2] { "tcg", "ocg" };
			foreach (string region in array)
			{
				(string High, string Sd) paths = SkeletonRelativePaths(cardId, region);
				if (roots.Any((string root) => files.Contains(Path.GetFullPath(Path.Combine(root, paths.High)))) && roots.Any((string root) => files.Contains(Path.GetFullPath(Path.Combine(root, paths.Sd)))))
				{
					result.Add(cardId);
					break;
				}
			}
		}
		return result;
	}

	private static string[] AnimationRoots(string gameRoot)
	{
		List<string> roots = new List<string>();
		string local = IndexService.FindLocalRoot(gameRoot);
		if (local != null && Directory.Exists(local))
		{
			roots.Add(local);
		}
		string streaming = IndexService.StreamingRoot(gameRoot);
		if (Directory.Exists(streaming))
		{
			roots.Add(streaming);
		}
		return roots.ToArray();
	}

	private static (string High, string Sd) SkeletonRelativePaths(string cardId, string region)
	{
		string basePath = "Duel/Timeline/Duel/MonsterCutIn/" + region + "/P" + cardId;
		return (High: IndexService.ResourceBundleRelativePath(basePath + "/HighEnd_HD/P" + cardId + "JS"), Sd: IndexService.ResourceBundleRelativePath(basePath + "/SD/P" + cardId + "JS"));
	}

	private static List<MonsterAnimationAssetRef> FindDeterministicCandidates(string gameRoot, string cardId, IEnumerable<string>? discoveredScales = null)
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string streamingRoot = IndexService.StreamingRoot(gameRoot);
		(string, string)[] roots = new(string, string)[2]
		{
			(localRoot, "LocalData"),
			(streamingRoot, "StreamingAssets")
		};
		List<string> logicalPaths = new List<string>();
		string[] scales = discoveredScales?.Distinct(StringComparer.Ordinal).ToArray() ?? DeterministicScales;
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string region in array)
		{
			string basePath = "Duel/Timeline/Duel/MonsterCutIn/" + region + "/P" + cardId;
			logicalPaths.Add(basePath + "/HighEnd_HD/P" + cardId + "JS");
			logicalPaths.Add(basePath + "/SD/P" + cardId + "JS");
			string[] array2 = new string[2] { "HighEnd_HD", "SD" };
			foreach (string tier in array2)
			{
				string[] array3 = scales;
				foreach (string scale in array3)
				{
					logicalPaths.Add($"{basePath}/{tier}/{scale}/P{cardId}");
					logicalPaths.Add($"{basePath}/{tier}/{scale}/P{cardId}.atlas");
				}
			}
		}
		List<MonsterAnimationAssetRef> result = new List<MonsterAnimationAssetRef>();
		ModEngine engine = new ModEngine();
		(string, string)[] array4 = roots;
		for (int num = 0; num < array4.Length; num++)
		{
			(string, string) root = array4[num];
			if (!Directory.Exists(root.Item1))
			{
				continue;
			}
			foreach (string logicalPath in logicalPaths)
			{
				string path = Path.Combine(root.Item1, IndexService.ResourceBundleRelativePath(logicalPath));
				if (!File.Exists(path))
				{
					continue;
				}
				try
				{
					foreach (MonsterAnimationAssetRef asset in from x in engine.ScanAnimationAssetsFast(path, root.Item1)
						where x.CardId == cardId
						select x)
					{
						result.Add(new MonsterAnimationAssetRef
						{
							BundlePath = asset.BundlePath,
							RelativeBundlePath = asset.RelativeBundlePath,
							AssetFileName = asset.AssetFileName,
							PathId = asset.PathId,
							Name = asset.Name,
							CardId = asset.CardId,
							Kind = asset.Kind,
							StorageKind = root.Item2
						});
					}
				}
				catch
				{
				}
			}
		}
		if (discoveredScales == null)
		{
			// Official SD folders may use four or more decimal digits (e.g. half
			// of an HD scale). Derive companion scales from actual containers,
			// not a fixed precision or a special list of card numbers.
			HashSet<string> companionScales = new(StringComparer.Ordinal);
			foreach (var asset in result.Where(x => x.Kind != MonsterAnimationAssetKind.Skeleton))
			{
				foreach (string container in engine.ReadAssetBundleContainerPaths(asset.BundlePath))
				{
					string[] parts = container.Replace('\\', '/').Split('/');
					if (parts.Length < 3 || !decimal.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal scale)) continue;
					decimal companion = parts[^3].Equals("sd", StringComparison.OrdinalIgnoreCase) ? scale * 2m : scale / 2m;
					if (companion > 0) companionScales.Add(companion.ToString("0.################", CultureInfo.InvariantCulture));
				}
			}
			companionScales.ExceptWith(DeterministicScales);
			if (companionScales.Count > 0) result.AddRange(FindDeterministicCandidates(gameRoot, cardId, companionScales));
		}
		return result;
	}

	public static List<MonsterAnimationAssetRef> LoadBestAvailable(string gameRoot, out string buildId)
	{
		string cache = CachePath(gameRoot);
		List<PortableMonsterAnimationIndex> indexes = [];
		foreach (string source in new[] { cache, BundledPath }.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (!File.Exists(source)) continue;
			try
			{
				PortableMonsterAnimationIndex candidate = Read(source);
				if (candidate.FormatVersion == 1) indexes.Add(candidate);
			}
			catch
			{
			}
		}
		if (indexes.Count == 0)
		{
			buildId = "";
			return new List<MonsterAnimationAssetRef>();
		}
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		Dictionary<string, string> roots = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["LocalData"] = Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
			["StreamingAssets"] = Path.GetFullPath(IndexService.StreamingRoot(gameRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
		};
		string currentBuild = PortableIndexService.GetGameBuildId(gameRoot);
		buildId = indexes.FirstOrDefault(index => string.Equals(index.GameBuildId, currentBuild, StringComparison.Ordinal))?.GameBuildId
			?? indexes[0].GameBuildId;
		return indexes.SelectMany(index => index.Assets)
			.GroupBy(x => $"{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.Select((MonsterAnimationAssetRef x) => new MonsterAnimationAssetRef
		{
			BundlePath = ResolveInside(roots.GetValueOrDefault(x.StorageKind, roots["LocalData"]), x.RelativeBundlePath),
			RelativeBundlePath = x.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar),
			AssetFileName = x.AssetFileName,
			PathId = x.PathId,
			Name = x.Name,
			CardId = x.CardId,
			Kind = x.Kind,
			StorageKind = x.StorageKind
		}).ToList();
	}

	public static MonsterAnimationSet? FindEquivalentPreview(string gameRoot, CardCatalogEntry card,
		CardCatalogService catalog)
	{
		HashSet<string> candidates = LoadBundledCardIds();
		try
		{
			candidates.UnionWith(CompleteCardIds(LoadBestAvailable(gameRoot, out _)));
		}
		catch
		{
		}
		foreach (CardCatalogEntry equivalent in catalog.FindEquivalentCards(card))
		{
			string id = equivalent.CardId.ToString(CultureInfo.InvariantCulture);
			if (equivalent.CardId == card.CardId || !candidates.Contains(id)) continue;
			try
			{
				MonsterAnimationSet set = Find(gameRoot, id);
				if (set.IsComplete) return set;
			}
			catch
			{
			}
		}
		return null;
	}

	public static PortableMonsterAnimationIndex Rebuild(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		// Discover installed cards from the current multilingual card catalog and
		// deterministic MonsterCutIn skeleton paths first. This keeps new cards
		// discoverable without parsing every unrelated Bundle (some valid game
		// Bundles are extremely expensive for AssetsTools to materialize).
		IReadOnlyList<string> cardIds = FindInstalledCardIds(gameRoot);
		ConcurrentBag<MonsterAnimationAssetRef> found = new ConcurrentBag<MonsterAnimationAssetRef>();
		// Preserve the shipped full scan. Deterministic probing discovers newly
		// added cards, but some official scale/path layouts cannot be inferred.
		try
		{
			foreach (MonsterAnimationAssetRef asset in LoadBestAvailable(gameRoot, out _).Where(asset => File.Exists(asset.BundlePath)))
			{
				found.Add(asset);
			}
		}
		catch
		{
		}
		int done = 0;
		Parallel.ForEach(cardIds, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
			CancellationToken = cancellationToken
		}, cardId =>
		{
			try
			{
				foreach (MonsterAnimationAssetRef current in FindDeterministicCandidates(gameRoot, cardId)) found.Add(current);
			}
			catch
			{
			}
			int num = Interlocked.Increment(ref done);
			if (num % 10 == 0 || num == cardIds.Count)
			{
				progress?.Invoke(num, cardIds.Count, found.Count);
			}
		});
		int result2;
		PortableMonsterAnimationIndex result = new PortableMonsterAnimationIndex
		{
			GameBuildId = PortableIndexService.GetGameBuildId(gameRoot),
			Assets = (from x in found.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
				select WithoutAbsolutePath(x.First()) into x
				orderby (!int.TryParse(x.CardId, out result2)) ? int.MaxValue : result2, x.Kind
				select x).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ToList()
		};
		Write(CachePath(gameRoot), result);
		return result;
	}

	public static PortableMonsterAnimationIndex EnsureCurrentIndex(string gameRoot,
		Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		string currentBuild = PortableIndexService.GetGameBuildId(gameRoot);
		string cache = CachePath(gameRoot);
		if (File.Exists(cache))
		{
			try
			{
				PortableMonsterAnimationIndex cached = Read(cache);
				if (cached.FormatVersion == 1 && string.Equals(cached.GameBuildId, currentBuild, StringComparison.Ordinal))
				{
					return RefreshMissingLinks(gameRoot, cached, progress, cancellationToken);
				}
			}
			catch
			{
			}
		}
		if (File.Exists(BundledPath))
		{
			try
			{
				PortableMonsterAnimationIndex bundled = Read(BundledPath);
				if (bundled.FormatVersion == 1 && string.Equals(bundled.GameBuildId, currentBuild, StringComparison.Ordinal))
				{
					Write(cache, bundled);
					return RefreshMissingLinks(gameRoot, bundled, progress, cancellationToken);
				}
			}
			catch
			{
			}
		}
		return Rebuild(gameRoot, progress, cancellationToken);
	}

	public static PortableMonsterAnimationIndex RefreshMissingLinks(string gameRoot, PortableMonsterAnimationIndex index,
		Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动账号。");
		List<MonsterAnimationAssetRef> assets = index.Assets.Where(asset => File.Exists(Path.Combine(
			asset.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(gameRoot) : local, asset.RelativeBundlePath))).ToList();
		HashSet<string> complete = CompleteCardIds(assets);
		string[] missing = FindInstalledCardIds(gameRoot).Where(id => !complete.Contains(id)).ToArray();
		for (int i = 0; i < missing.Length; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			assets.AddRange(FindDeterministicCandidates(gameRoot, missing[i]).Select(WithoutAbsolutePath));
			progress?.Invoke(i + 1, missing.Length, assets.Count);
		}
		var refreshed = new PortableMonsterAnimationIndex
		{
			GameBuildId = index.GameBuildId,
			Assets = assets.DistinctBy(asset => $"{asset.StorageKind}|{asset.RelativeBundlePath.Replace('\\', '/')}|{asset.AssetFileName}|{asset.PathId}", StringComparer.OrdinalIgnoreCase).ToList()
		};
		cancellationToken.ThrowIfCancellationRequested();
		Write(CachePath(gameRoot), refreshed);
		return refreshed;
	}

	public static HashSet<string> CompleteCardIds(PortableMonsterAnimationIndex index)
	{
		return CompleteCardIds(index.Assets);
	}

	public static HashSet<string> CompleteCardIds(IEnumerable<MonsterAnimationAssetRef> assets)
	{
		return assets.GroupBy(asset => asset.CardId, StringComparer.Ordinal)
			.Where(group => group.Count(asset => asset.Kind == MonsterAnimationAssetKind.Texture) >= 2
				&& group.Count(asset => asset.Kind == MonsterAnimationAssetKind.Atlas) >= 2
				&& group.Count(asset => asset.Kind == MonsterAnimationAssetKind.Skeleton) >= 2)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.Ordinal);
	}

	public static void Export(string gameRoot, PortableMonsterAnimationIndex index, string outputPath)
	{
		Write(outputPath, index);
	}

	public static PortableMonsterAnimationIndex Read(string path)
	{
		using FileStream file = File.OpenRead(path);
		using BrotliStream brotli = new BrotliStream(file, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<PortableMonsterAnimationIndex>(brotli) ?? throw new InvalidDataException("动画预绑定索引无法读取。");
	}

	public static void Write(string path, PortableMonsterAnimationIndex index)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream file = File.Create(temporary))
			using (BrotliStream brotli = new(file, CompressionLevel.SmallestSize)) JsonSerializer.Serialize(brotli, index);
			File.Move(temporary, path, overwrite: true);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}

	public static string CachePath(string gameRoot)
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localRoot + "|animation-v1"))).Substring(0, 12);
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
		Directory.CreateDirectory(text);
		return Path.Combine(text, "animation_index_" + id + ".json.br");
	}

	private static MonsterAnimationAssetRef WithoutAbsolutePath(MonsterAnimationAssetRef x)
	{
		return new MonsterAnimationAssetRef
		{
			RelativeBundlePath = x.RelativeBundlePath.Replace('\\', '/'),
			AssetFileName = x.AssetFileName,
			PathId = x.PathId,
			Name = x.Name,
			CardId = x.CardId,
			Kind = x.Kind,
			StorageKind = x.StorageKind
		};
	}

	private static string ResolveInside(string fullRoot, string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("动画索引包含无效路径。");
		}
		string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("动画索引路径越出了 LocalData。");
		}
		return fullPath;
	}
}
