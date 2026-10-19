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

public static class MonsterAnimationIndexService
{
	public const string BundledFileName = "prebuilt-animation-index-v1.json.br";

	public const int CurrentResolverVersion = 1;

	private static readonly string[] DeterministicScales = new string[10] { "1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4" }.Concat(from value in Enumerable.Range(1, 2000)
		select ((double)value / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Distinct<string>(StringComparer.Ordinal).ToArray();

	public static string BundledPath => AppPaths.ResolveFile("prebuilt-animation-index-v1.json.br");

	public static MonsterAnimationSet Find(string gameRoot, string cardId)
	{
		List<MonsterAnimationAssetRef> cached;
		try { cached = LoadBestAvailable(gameRoot, out _); }
		catch { cached = new(); }
		return FindCore(gameRoot, cardId, cached);
	}

	private static MonsterAnimationSet FindCore(string gameRoot, string cardId,
		IEnumerable<MonsterAnimationAssetRef> cached, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(cardId) || !cardId.All(char.IsAsciiDigit))
			throw new ArgumentException("卡号必须是纯数字。", nameof(cardId));

		var list = FindPrefabDependencies(gameRoot, cardId, out var bundles, out var diagnostics, cancellationToken);
		var scanned = bundles.Select(bundle => Path.GetFullPath(bundle.BundlePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var engine = new ModEngine();
		// Old PathIDs and asset names must not survive a reimport/update simply
		// because the bundle filename still exists.
		foreach (var asset in cached.Where(asset => asset.CardId == cardId && File.Exists(asset.BundlePath)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!scanned.Add(Path.GetFullPath(asset.BundlePath))) continue;
			string? storageRoot = AnimationRoots(gameRoot).FirstOrDefault(root =>
				Path.GetFullPath(asset.BundlePath).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
			if (storageRoot == null) continue;
			try
			{
				list.AddRange(engine.ScanAnimationAssetsFast(asset.BundlePath, storageRoot, cardId)
					.Select(value => WithStorage(value, asset.StorageKind)));
			}
			catch (IOException ex) { diagnostics.Add(ex.Message); }
		}
		list.AddRange(FindCompanionPaths(gameRoot, cardId, list));
		bool native = bundles.Any(bundle => bundle.IsPrefab) && bundles.Any(bundle => bundle.IsTimeline)
			&& !bundles.Any(bundle => bundle.HasSpineData) && diagnostics.Count == 0;
		// Prefab dependencies are authoritative and resolve unusual asset names,
		// scale folders and multi-page atlases without tens of thousands of guesses.
		if (!native && !new MonsterAnimationSet { CardId = cardId, Assets = list }.IsComplete)
		{
			cancellationToken.ThrowIfCancellationRequested();
			list.AddRange(FindDeterministicCandidates(gameRoot, cardId));
			list.AddRange(FindCompanionPaths(gameRoot, cardId, list));
		}
		list = list.DistinctBy(asset => $"{asset.StorageKind}|{Path.GetFullPath(asset.BundlePath)}|{asset.AssetFileName}|{asset.PathId}",
			StringComparer.OrdinalIgnoreCase).OrderBy(asset => asset.Kind).ThenBy(asset => asset.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ToList();
		return new MonsterAnimationSet
		{
			CardId = cardId,
			Assets = ResourceSource.IsMobile(gameRoot) ? list.Select(asset => WithStorage(asset, "Mobile")).ToList() : list,
			ResourceBundles = bundles,
			Diagnostics = diagnostics
		};
	}

	private static MonsterAnimationAssetRef WithStorage(MonsterAnimationAssetRef asset, string storageKind) => new()
	{
		BundlePath = asset.BundlePath,
		RelativeBundlePath = asset.RelativeBundlePath,
		AssetFileName = asset.AssetFileName,
		PathId = asset.PathId,
		Name = asset.Name,
		CardId = asset.CardId,
		Kind = asset.Kind,
		StorageKind = storageKind
	};

	internal static List<MonsterAnimationAssetRef> FindCompanionPaths(string gameRoot, string cardId, IEnumerable<MonsterAnimationAssetRef> assets)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		ModEngine modEngine = new ModEngine();
		foreach (MonsterAnimationAssetRef item in assets.Where((MonsterAnimationAssetRef a) => a.Kind != MonsterAnimationAssetKind.Skeleton))
		{
			// Some official prefabs only list the first page. The atlas itself is
			// the authority for later pages, including names such as P73022.
			string[] pages = item.Kind == MonsterAnimationAssetKind.Atlas
				? MonsterAnimationAssetPairing.AtlasPages(modEngine.ReadTextAsset(item).Data) : Array.Empty<string>();
			foreach (string item2 in modEngine.ReadAssetBundleContainerPaths(item.BundlePath))
			{
				Match match = Regex.Match(item2.Replace('\\', '/'), "/monstercutin/(?<region>tcg|ocg)/p" + cardId + "/(?:highend_hd|sd)/(?<folder>[^/]+)/[^/]+$", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					string[] array = new string[2] { "HighEnd_HD", "SD" };
					foreach (string value in array)
					{
						hashSet.Add(IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{match.Groups["region"].Value.ToLowerInvariant()}/P{cardId}/{value}/{match.Groups["folder"].Value}/{item.Name}"));
						foreach (string page in pages)
							hashSet.Add(IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{match.Groups["region"].Value.ToLowerInvariant()}/P{cardId}/{value}/{match.Groups["folder"].Value}/{page[..^4]}"));
					}
				}
			}
		}
		List<MonsterAnimationAssetRef> list = new List<MonsterAnimationAssetRef>();
		foreach (string item3 in hashSet)
		{
			string[] array = AnimationRoots(gameRoot);
			foreach (string text in array)
			{
				string text2 = Path.Combine(text, item3);
				if (!File.Exists(text2))
				{
					continue;
				}
				foreach (MonsterAnimationAssetRef item4 in modEngine.ScanAnimationAssetsFast(text2, text, cardId))
				{
					list.Add(new MonsterAnimationAssetRef
					{
						BundlePath = text2,
						RelativeBundlePath = item4.RelativeBundlePath,
						AssetFileName = item4.AssetFileName,
						PathId = item4.PathId,
						Name = item4.Name,
						CardId = cardId,
						Kind = item4.Kind,
						StorageKind = ((text == IndexService.StreamingRoot(gameRoot)) ? "StreamingAssets" : "LocalData")
					});
				}
			}
		}
		return list;
	}

	internal static List<MonsterAnimationAssetRef> FindPrefabDependencies(string gameRoot, string cardId) =>
		FindPrefabDependencies(gameRoot, cardId, out _, out _);

	private static List<MonsterAnimationAssetRef> FindPrefabDependencies(string gameRoot, string cardId,
		out List<MonsterAnimationResourceBundle> bundles, out List<string> diagnostics, CancellationToken cancellationToken = default)
	{
		bundles = new();
		diagnostics = new();
		var roots = AnimationRoots(gameRoot);
		var queue = new Queue<(string Relative, bool Root)>();
		foreach (string region in new[] { "tcg", "ocg" })
			foreach (string tier in new[] { "HighEnd_HD", "SD" })
				queue.Enqueue((IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}/{tier}/P{cardId}"), true));
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var found = new List<MonsterAnimationAssetRef>();
		var engine = new ModEngine();
		while (queue.Count > 0 && visited.Count < 256)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var entry = queue.Dequeue();
			string relative = entry.Relative.Replace('\\', '/');
			if (!Regex.IsMatch(relative, "^[0-9a-fA-F]{2}/[0-9a-fA-F]{8}$") || !visited.Add(relative)) continue;
			string? root = roots.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, relative)));
			if (root == null)
			{
				if (!entry.Root) diagnostics.Add("Missing dependency: " + relative);
				continue;
			}
			string path = Path.Combine(root, relative);
			try
			{
				var containers = engine.ReadAssetBundleContainerPaths(path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
				if (entry.Root && !containers.Any(container => Regex.IsMatch(container.Replace('\\', '/'),
					"/monstercutin/(?:tcg|ocg)/p" + Regex.Escape(cardId) + "/(?:highend_hd|sd)/[^/]+\\.prefab$", RegexOptions.IgnoreCase))) continue;
				string storage = root == IndexService.StreamingRoot(gameRoot) ? "StreamingAssets" : "LocalData";
				bundles.Add(new(path, relative, storage, containers));
				found.AddRange(engine.ScanAnimationAssetsFast(path, root, cardId).Select(asset => WithStorage(asset, storage)));
				// Bridge/material/timeline bundles need not themselves be in the
				// card folder. Still follow their declared, bounded dependencies.
				foreach (string dependency in engine.ReadAssetBundleContainerPaths(path, dependencies: true))
					queue.Enqueue((dependency, false));
			}
			catch (IOException ex) { diagnostics.Add(relative + ": " + ex.Message); }
		}
		if (queue.Count > 0) diagnostics.Add("Dependency traversal limit (256) reached.");
		return found;
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
		if (string.IsNullOrEmpty(cardId) || !cardId.All(char.IsAsciiDigit)) return false;
		var roots = AnimationRoots(gameRoot);
		foreach (string region in new[] { "tcg", "ocg" })
		{
			var paths = SkeletonRelativePaths(cardId, region);
			if (roots.Any(root => File.Exists(Path.Combine(root, paths.High)) || File.Exists(Path.Combine(root, paths.Sd)))) return true;
			foreach (string tier in new[] { "HighEnd_HD", "SD" })
			{
				string relative = IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}/{tier}/P{cardId}");
				if (roots.Any(root => File.Exists(Path.Combine(root, relative)))) return true;
			}
		}
		return false;
	}

	public static IReadOnlyList<string> FindInstalledCardIds(string gameRoot, bool spineOnly = false)
	{
		var roots = AnimationRoots(gameRoot);
		var files = roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var candidates = LoadBundledCardIds();
		// Missing/old type metadata must not make a downloaded official prefab invisible.
		candidates.UnionWith(CardCatalogService.LoadBestAvailable().Entries.Select(entry => entry.CardId.ToString(CultureInfo.InvariantCulture)));
		var complete = new HashSet<string>(StringComparer.Ordinal);
		try
		{
			var assets = LoadBestAvailable(gameRoot, out _).Where(asset => File.Exists(asset.BundlePath)).ToList();
			candidates.UnionWith(assets.Select(asset => asset.CardId));
			complete.UnionWith(CompleteCardIds(assets));
		}
		catch { }
		bool Exists(string relative) => files.Contains(relative.Replace('\\', '/'));
		return candidates.Where(id => id.Length > 0 && id.All(char.IsAsciiDigit)).Where(id =>
		{
			if (spineOnly && complete.Contains(id)) return true;
			foreach (string region in new[] { "tcg", "ocg" })
			{
				var paths = SkeletonRelativePaths(id, region);
				if (spineOnly)
				{
					if (Exists(paths.High) && Exists(paths.Sd)) return true;
				}
				else
				{
					if (Exists(paths.High) || Exists(paths.Sd)) return true;
					foreach (string tier in new[] { "HighEnd_HD", "SD" })
						if (Exists(IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{region}/P{id}/{tier}/P{id}"))) return true;
				}
			}
			return false;
		}).OrderBy(id => int.TryParse(id, out int value) ? value : int.MaxValue).ToArray();
	}

	public static HashSet<string> KnownCardIds(PortableMonsterAnimationIndex index)
	{
		var ids = CompleteCardIds(index);
		ids.UnionWith(index.OfficialCardIds);
		return ids;
	}

	public static HashSet<string> LoadKnownCardIds(string gameRoot)
	{
		var ids = CompleteCardIds(LoadBestAvailable(gameRoot, out _));
		foreach (string path in new[] { CachePath(gameRoot), BundledPath }.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				if (File.Exists(path))
					ids.UnionWith(Read(path).OfficialCardIds.Where(id => HasInstalledAnimation(gameRoot, id)));
			}
			catch (IOException) { }
			catch (JsonException) { }
		}
		return ids;
	}

	private static string[] AnimationRoots(string gameRoot)
	{
		List<string> list = new List<string>();
		string text = IndexService.FindLocalRoot(gameRoot);
		if (text != null && Directory.Exists(text))
		{
			list.Add(text);
		}
		string text2 = IndexService.StreamingRoot(gameRoot);
		if (Directory.Exists(text2))
		{
			list.Add(text2);
		}
		return list.ToArray();
	}

	private static (string High, string Sd) SkeletonRelativePaths(string cardId, string region)
	{
		string text = "Duel/Timeline/Duel/MonsterCutIn/" + region + "/P" + cardId;
		return (High: IndexService.ResourceBundleRelativePath(text + "/HighEnd_HD/P" + cardId + "JS"), Sd: IndexService.ResourceBundleRelativePath(text + "/SD/P" + cardId + "JS"));
	}

	private static List<MonsterAnimationAssetRef> FindDeterministicCandidates(string gameRoot, string cardId, IEnumerable<string>? discoveredScales = null)
	{
		string item = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string item2 = IndexService.StreamingRoot(gameRoot);
		(string, string)[] array = new(string, string)[2]
		{
			(item, "LocalData"),
			(item2, "StreamingAssets")
		};
		List<string> list = new List<string>();
		string[] array2 = discoveredScales?.Distinct<string>(StringComparer.Ordinal).ToArray() ?? DeterministicScales;
		string[] array3 = new string[2] { "tcg", "ocg" };
		foreach (string text in array3)
		{
			string text2 = "Duel/Timeline/Duel/MonsterCutIn/" + text + "/P" + cardId;
			list.Add(text2 + "/HighEnd_HD/P" + cardId + "JS");
			list.Add(text2 + "/SD/P" + cardId + "JS");
			string[] array4 = new string[2] { "HighEnd_HD", "SD" };
			foreach (string value in array4)
			{
				string[] array5 = array2;
				foreach (string value2 in array5)
				{
					list.Add($"{text2}/{value}/{value2}/P{cardId}");
					list.Add($"{text2}/{value}/{value2}/P{cardId}.atlas");
				}
			}
		}
		List<MonsterAnimationAssetRef> list2 = new List<MonsterAnimationAssetRef>();
		ModEngine modEngine = new ModEngine();
		(string, string)[] array6 = array;
		for (int l = 0; l < array6.Length; l++)
		{
			(string, string) tuple = array6[l];
			if (!Directory.Exists(tuple.Item1))
			{
				continue;
			}
			foreach (string item3 in list)
			{
				string text3 = Path.Combine(tuple.Item1, IndexService.ResourceBundleRelativePath(item3));
				if (!File.Exists(text3))
				{
					continue;
				}
				try
				{
					foreach (MonsterAnimationAssetRef item4 in from x in modEngine.ScanAnimationAssetsFast(text3, tuple.Item1, cardId)
						where x.CardId == cardId
						select x)
					{
						list2.Add(new MonsterAnimationAssetRef
						{
							BundlePath = item4.BundlePath,
							RelativeBundlePath = item4.RelativeBundlePath,
							AssetFileName = item4.AssetFileName,
							PathId = item4.PathId,
							Name = item4.Name,
							CardId = item4.CardId,
							Kind = item4.Kind,
							StorageKind = tuple.Item2
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
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (MonsterAnimationAssetRef item5 in list2.Where((MonsterAnimationAssetRef x) => x.Kind != MonsterAnimationAssetKind.Skeleton))
			{
				foreach (string item6 in modEngine.ReadAssetBundleContainerPaths(item5.BundlePath))
				{
					string[] array7 = item6.Replace('\\', '/').Split('/');
					if (array7.Length >= 3 && decimal.TryParse(array7[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
					{
						decimal num = (array7[^3].Equals("sd", StringComparison.OrdinalIgnoreCase) ? (result * 2m) : (result / 2m));
						if (num > 0m)
						{
							hashSet.Add(num.ToString("0.################", CultureInfo.InvariantCulture));
						}
					}
				}
			}
			hashSet.ExceptWith(DeterministicScales);
			if (hashSet.Count > 0)
			{
				list2.AddRange(FindDeterministicCandidates(gameRoot, cardId, hashSet));
			}
		}
		return list2;
	}

	public static List<MonsterAnimationAssetRef> LoadBestAvailable(string gameRoot, out string buildId)
	{
		string text = CachePath(gameRoot);
		List<PortableMonsterAnimationIndex> list = new List<PortableMonsterAnimationIndex>();
		foreach (string item in ((!ResourceSource.IsMobile(gameRoot)) ? new string[2] { text, BundledPath } : new string[1] { text }).Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (!File.Exists(item))
			{
				continue;
			}
			try
			{
				PortableMonsterAnimationIndex portableMonsterAnimationIndex = Read(item);
				if (portableMonsterAnimationIndex.FormatVersion == 1)
				{
					list.Add(portableMonsterAnimationIndex);
				}
			}
			catch
			{
			}
		}
		if (list.Count == 0)
		{
			buildId = "";
			return new List<MonsterAnimationAssetRef>();
		}
		string path = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		Dictionary<string, string> roots = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["LocalData"] = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
			["StreamingAssets"] = Path.GetFullPath(IndexService.StreamingRoot(gameRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
		};
		string currentBuild = PortableIndexService.GetGameBuildId(gameRoot);
		buildId = list.FirstOrDefault((PortableMonsterAnimationIndex index) => string.Equals(index.GameBuildId, currentBuild, StringComparison.Ordinal))?.GameBuildId ?? list[0].GameBuildId;
		return (from @group in list.SelectMany((PortableMonsterAnimationIndex index) => index.Assets).GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{x.StorageKind}\0{x.CardId}\0{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			select @group.First() into x
			select new MonsterAnimationAssetRef
			{
				BundlePath = ResolveInside(roots.GetValueOrDefault(x.StorageKind, roots["LocalData"]), x.RelativeBundlePath),
				RelativeBundlePath = x.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar),
				AssetFileName = x.AssetFileName,
				PathId = x.PathId,
				Name = x.Name,
				CardId = x.CardId,
				Kind = x.Kind,
				StorageKind = (ResourceSource.IsMobile(gameRoot) ? "Mobile" : x.StorageKind)
			}).ToList();
	}

	public static MonsterAnimationSet? FindEquivalentPreview(string gameRoot, CardCatalogEntry card, CardCatalogService catalog)
	{
		HashSet<string> hashSet = LoadBundledCardIds();
		try
		{
			hashSet.UnionWith(CompleteCardIds(LoadBestAvailable(gameRoot, out string _)));
		}
		catch
		{
		}
		foreach (CardCatalogEntry item in catalog.FindEquivalentCards(card))
		{
			string text = item.CardId.ToString(CultureInfo.InvariantCulture);
			if (item.CardId == card.CardId || !hashSet.Contains(text))
			{
				continue;
			}
			try
			{
				MonsterAnimationSet monsterAnimationSet = Find(gameRoot, text);
				if (monsterAnimationSet.IsComplete)
				{
					return monsterAnimationSet;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	public static PortableMonsterAnimationIndex Rebuild(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		var cardIds = FindInstalledCardIds(gameRoot);
		var found = new ConcurrentBag<MonsterAnimationAssetRef>();
		var official = new ConcurrentBag<string>();
		List<MonsterAnimationAssetRef> cached;
		try { cached = LoadBestAvailable(gameRoot, out _); }
		catch { cached = new(); }
		int done = 0;
		Parallel.ForEach(cardIds, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8),
			CancellationToken = cancellationToken
		}, cardId =>
		{
			var set = FindCore(gameRoot, cardId, cached, cancellationToken);
			foreach (var asset in set.Assets) found.Add(WithoutAbsolutePath(asset));
			if (set.HasOfficialPrefab || set.IsComplete) official.Add(cardId);
			int count = Interlocked.Increment(ref done);
			if (count % 10 == 0 || count == cardIds.Count) progress?.Invoke(count, cardIds.Count, found.Count);
		});
		cancellationToken.ThrowIfCancellationRequested();
		var index = new PortableMonsterAnimationIndex
		{
			GameBuildId = PortableIndexService.GetGameBuildId(gameRoot),
			ResolverVersion = CurrentResolverVersion,
			OfficialCardIds = official.Distinct(StringComparer.Ordinal).OrderBy(id => int.Parse(id)).ToList(),
			Assets = found.DistinctBy(asset => $"{asset.StorageKind}|{asset.RelativeBundlePath}|{asset.AssetFileName}|{asset.PathId}",
				StringComparer.OrdinalIgnoreCase).OrderBy(asset => int.Parse(asset.CardId)).ThenBy(asset => asset.Kind).ToList()
		};
		Write(CachePath(gameRoot), index);
		return index;
	}

	public static PortableMonsterAnimationIndex EnsureCurrentIndex(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		string gameBuildId = PortableIndexService.GetGameBuildId(gameRoot);
		string path = CachePath(gameRoot);
		if (File.Exists(path))
		{
			try
			{
				PortableMonsterAnimationIndex portableMonsterAnimationIndex = Read(path);
				if (portableMonsterAnimationIndex.FormatVersion == 1 && string.Equals(portableMonsterAnimationIndex.GameBuildId, gameBuildId, StringComparison.Ordinal))
				{
					return RefreshMissingLinks(gameRoot, portableMonsterAnimationIndex, progress, cancellationToken);
				}
			}
			catch (OperationCanceledException) { throw; }
			catch
			{
			}
		}
		if (!ResourceSource.IsMobile(gameRoot) && File.Exists(BundledPath))
		{
			try
			{
				PortableMonsterAnimationIndex portableMonsterAnimationIndex2 = Read(BundledPath);
				if (portableMonsterAnimationIndex2.FormatVersion == 1 && string.Equals(portableMonsterAnimationIndex2.GameBuildId, gameBuildId, StringComparison.Ordinal))
				{
					return RefreshMissingLinks(gameRoot, portableMonsterAnimationIndex2, progress, cancellationToken);
				}
			}
			catch (OperationCanceledException) { throw; }
			catch
			{
			}
		}
		return Rebuild(gameRoot, progress, cancellationToken);
	}

	public static PortableMonsterAnimationIndex RefreshMissingLinks(string gameRoot, PortableMonsterAnimationIndex index, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default)
	{
		string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动账号。");
		var current = LoadBestAvailable(gameRoot, out _);
		var complete = CompleteCardIds(current.Where(asset => File.Exists(asset.BundlePath)));
		var installed = FindInstalledCardIds(gameRoot);
		bool upgrade = index.ResolverVersion != CurrentResolverVersion;
		var targets = installed.Where(id => upgrade || !complete.Contains(id)).ToArray();
		var assets = index.Assets.Where(asset => File.Exists(Path.Combine(asset.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(gameRoot) : local,
			asset.RelativeBundlePath))).ToList();
		var official = index.OfficialCardIds.Where(id => HasInstalledAnimation(gameRoot, id)).ToHashSet(StringComparer.Ordinal);
		for (int i = 0; i < targets.Length; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var set = FindCore(gameRoot, targets[i], current, cancellationToken);
			assets.RemoveAll(asset => asset.CardId == targets[i]);
			assets.AddRange(set.Assets.Select(WithoutAbsolutePath));
			if (set.HasOfficialPrefab || set.IsComplete) official.Add(targets[i]);
			else official.Remove(targets[i]);
			progress?.Invoke(i + 1, targets.Length, assets.Count);
		}
		var updated = new PortableMonsterAnimationIndex
		{
			GameBuildId = PortableIndexService.GetGameBuildId(gameRoot),
			ResolverVersion = CurrentResolverVersion,
			OfficialCardIds = official.OrderBy(id => int.Parse(id)).ToList(),
			Assets = assets.DistinctBy(asset => $"{asset.StorageKind}|{asset.RelativeBundlePath.Replace('\\', '/')}|{asset.AssetFileName}|{asset.PathId}",
				StringComparer.OrdinalIgnoreCase).ToList()
		};
		cancellationToken.ThrowIfCancellationRequested();
		Write(CachePath(gameRoot), updated);
		return updated;
	}

	public static HashSet<string> CompleteCardIds(PortableMonsterAnimationIndex index)
	{
		return CompleteCardIds(index.Assets);
	}

	public static HashSet<string> CompleteCardIds(IEnumerable<MonsterAnimationAssetRef> assets)
	{
		return (from @group in assets.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef asset) => asset.CardId, StringComparer.Ordinal)
			where @group.Count((MonsterAnimationAssetRef asset) => asset.Kind == MonsterAnimationAssetKind.Texture) >= (@group.Any((MonsterAnimationAssetRef a) => a.StorageKind == "Mobile") ? 1 : 2) && @group.Count((MonsterAnimationAssetRef asset) => asset.Kind == MonsterAnimationAssetKind.Atlas) >= (@group.Any((MonsterAnimationAssetRef a) => a.StorageKind == "Mobile") ? 1 : 2) && @group.Count((MonsterAnimationAssetRef asset) => asset.Kind == MonsterAnimationAssetKind.Skeleton) >= (@group.Any((MonsterAnimationAssetRef a) => a.StorageKind == "Mobile") ? 1 : 2)
			select @group.Key).ToHashSet<string>(StringComparer.Ordinal);
	}

	public static void Export(string gameRoot, PortableMonsterAnimationIndex index, string outputPath)
	{
		Write(outputPath, index);
	}

	public static PortableMonsterAnimationIndex Read(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using BrotliStream utf8Json = new BrotliStream(stream, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<PortableMonsterAnimationIndex>(utf8Json) ?? throw new InvalidDataException("动画预绑定索引无法读取。");
	}

	public static void Write(string path, PortableMonsterAnimationIndex index)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (FileStream stream = File.Create(text))
			{
				using BrotliStream utf8Json = new BrotliStream(stream, CompressionLevel.SmallestSize);
				JsonSerializer.Serialize(utf8Json, index);
			}
			File.Move(text, path, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static string CachePath(string gameRoot)
	{
		string text = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string text2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + "|animation-v1"))).Substring(0, 12);
		string text3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
		Directory.CreateDirectory(text3);
		return Path.Combine(text3, "animation_index_" + text2 + ".json.br");
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
