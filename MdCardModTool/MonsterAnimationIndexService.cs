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

	private static readonly string[] DeterministicScales = new string[10] { "1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4" }.Concat(from value in Enumerable.Range(1, 2000)
		select ((double)value / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Distinct<string>(StringComparer.Ordinal).ToArray();

	public static string BundledPath => AppPaths.ResolveFile("prebuilt-animation-index-v1.json.br");

	public static MonsterAnimationSet Find(string gameRoot, string cardId)
	{
		if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0)
		{
			throw new ArgumentException("卡号必须是纯数字。", "cardId");
		}
		List<MonsterAnimationAssetRef> list = FindDeterministicCandidates(gameRoot, cardId);
		list.AddRange(FindPrefabDependencies(gameRoot, cardId));
		try
		{
			list.AddRange(from x in LoadBestAvailable(gameRoot, out string _)
				where x.CardId == cardId && File.Exists(x.BundlePath)
				select x);
		}
		catch
		{
		}
		list.AddRange(FindCompanionPaths(gameRoot, cardId, list));
		list = (from x in list.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{Path.GetFullPath(x.BundlePath)}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
			select x.First() into x
			orderby x.Kind
			select x).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ToList();
		return new MonsterAnimationSet
		{
			CardId = cardId,
			Assets = (ResourceSource.IsMobile(gameRoot) ? list.Select((MonsterAnimationAssetRef a) => new MonsterAnimationAssetRef
			{
				BundlePath = a.BundlePath,
				RelativeBundlePath = a.RelativeBundlePath,
				AssetFileName = a.AssetFileName,
				PathId = a.PathId,
				Name = a.Name,
				CardId = a.CardId,
				Kind = a.Kind,
				StorageKind = "Mobile"
			}).ToList() : list)
		};
	}

	internal static List<MonsterAnimationAssetRef> FindCompanionPaths(string gameRoot, string cardId, IEnumerable<MonsterAnimationAssetRef> assets)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		ModEngine modEngine = new ModEngine();
		foreach (MonsterAnimationAssetRef item in assets.Where((MonsterAnimationAssetRef a) => a.Kind != MonsterAnimationAssetKind.Skeleton))
		{
			foreach (string item2 in modEngine.ReadAssetBundleContainerPaths(item.BundlePath))
			{
				Match match = Regex.Match(item2.Replace('\\', '/'), "/monstercutin/(?<region>tcg|ocg)/p" + cardId + "/(?:highend_hd|sd)/(?<folder>[^/]+)/[^/]+$", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					string[] array = new string[2] { "HighEnd_HD", "SD" };
					foreach (string value in array)
					{
						hashSet.Add(IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{match.Groups["region"].Value.ToLowerInvariant()}/P{cardId}/{value}/{match.Groups["folder"].Value}/{item.Name}"));
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

	internal static List<MonsterAnimationAssetRef> FindPrefabDependencies(string gameRoot, string cardId)
	{
		string[] source = AnimationRoots(gameRoot);
		Queue<string> queue = new Queue<string>();
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string value in array)
		{
			string[] array2 = new string[2] { "HighEnd_HD", "SD" };
			foreach (string value2 in array2)
			{
				queue.Enqueue(IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{value}/P{cardId}/{value2}/P{cardId}"));
			}
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<MonsterAnimationAssetRef> list = new List<MonsterAnimationAssetRef>();
		ModEngine modEngine = new ModEngine();
		while (queue.Count > 0 && hashSet.Count < 256)
		{
			string relative = queue.Dequeue().Replace('\\', '/');
			if (!Regex.IsMatch(relative, "^[0-9a-fA-F]{2}/[0-9a-fA-F]{8}$") || !hashSet.Add(relative))
			{
				continue;
			}
			string text = source.FirstOrDefault((string r) => File.Exists(Path.Combine(r, relative)));
			if (text == null)
			{
				continue;
			}
			string bundlePath = Path.Combine(text, relative);
			try
			{
				if (!modEngine.ReadAssetBundleContainerPaths(bundlePath).Any((string p) => p.Replace('\\', '/').Contains("/monstercutin/tcg/p" + cardId + "/", StringComparison.OrdinalIgnoreCase) || p.Replace('\\', '/').Contains("/monstercutin/ocg/p" + cardId + "/", StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}
				foreach (MonsterAnimationAssetRef item in modEngine.ScanAnimationAssetsFast(bundlePath, text, cardId))
				{
					list.Add(new MonsterAnimationAssetRef
					{
						BundlePath = bundlePath,
						RelativeBundlePath = item.RelativeBundlePath,
						AssetFileName = item.AssetFileName,
						PathId = item.PathId,
						Name = item.Name,
						CardId = cardId,
						Kind = item.Kind,
						StorageKind = ((text == IndexService.StreamingRoot(gameRoot)) ? "StreamingAssets" : "LocalData")
					});
				}
				foreach (string item2 in modEngine.ReadAssetBundleContainerPaths(bundlePath, dependencies: true))
				{
					queue.Enqueue(item2);
				}
			}
			catch (IOException)
			{
			}
		}
		return list;
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
		string[] source = AnimationRoots(gameRoot);
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string region in array)
		{
			(string High, string Sd) paths = SkeletonRelativePaths(cardId, region);
			bool num;
			if (!ResourceSource.IsMobile(gameRoot))
			{
				if (!source.Any((string root) => File.Exists(Path.Combine(root, paths.High))))
				{
					continue;
				}
				num = source.Any((string root) => File.Exists(Path.Combine(root, paths.Sd)));
			}
			else
			{
				num = source.Any((string root) => File.Exists(Path.Combine(root, paths.High)) || File.Exists(Path.Combine(root, paths.Sd)));
			}
			if (num)
			{
				return true;
			}
		}
		return false;
	}

	public static IReadOnlyList<string> FindInstalledCardIds(string gameRoot)
	{
		string[] source = AnimationRoots(gameRoot);
		HashSet<string> files = source.SelectMany((string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)).Select(Path.GetFullPath).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet = LoadBundledCardIds();
		hashSet.UnionWith(from entry in CardCatalogService.LoadBestAvailable().Entries
			where entry.IsMonster
			select entry.AnimationId.ToString(CultureInfo.InvariantCulture));
		try
		{
			hashSet.UnionWith(from asset in LoadBestAvailable(gameRoot, out string _)
				select asset.CardId);
		}
		catch
		{
		}
		List<string> list = new List<string>();
		int result;
		foreach (string item in from id in hashSet
			where id.Length > 0 && id.All(char.IsAsciiDigit)
			orderby (!int.TryParse(id, out result)) ? int.MaxValue : result
			select id)
		{
			string[] array = new string[2] { "tcg", "ocg" };
			foreach (string region in array)
			{
				(string High, string Sd) paths = SkeletonRelativePaths(item, region);
				bool num2;
				if (!ResourceSource.IsMobile(gameRoot))
				{
					if (!source.Any((string root) => files.Contains(Path.GetFullPath(Path.Combine(root, paths.High)))))
					{
						continue;
					}
					num2 = source.Any((string root) => files.Contains(Path.GetFullPath(Path.Combine(root, paths.Sd))));
				}
				else
				{
					num2 = source.Any((string root) => files.Contains(Path.GetFullPath(Path.Combine(root, paths.High))) || files.Contains(Path.GetFullPath(Path.Combine(root, paths.Sd))));
				}
				if (num2)
				{
					list.Add(item);
					break;
				}
			}
		}
		return list;
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
					foreach (MonsterAnimationAssetRef item4 in from x in modEngine.ScanAnimationAssetsFast(text3, tuple.Item1)
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
		return (from @group in list.SelectMany((PortableMonsterAnimationIndex index) => index.Assets).GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
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

	public static PortableMonsterAnimationIndex Rebuild(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<string> cardIds = FindInstalledCardIds(gameRoot);
		ConcurrentBag<MonsterAnimationAssetRef> found = new ConcurrentBag<MonsterAnimationAssetRef>();
		try
		{
			string buildId;
			foreach (MonsterAnimationAssetRef item in from asset in LoadBestAvailable(gameRoot, out buildId)
				where File.Exists(asset.BundlePath)
				select asset)
			{
				found.Add(item);
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
		}, delegate(string cardId)
		{
			try
			{
				List<MonsterAnimationAssetRef> list = FindDeterministicCandidates(gameRoot, cardId).Concat(FindPrefabDependencies(gameRoot, cardId)).ToList();
				list.AddRange(FindCompanionPaths(gameRoot, cardId, list));
				foreach (MonsterAnimationAssetRef item2 in ResourceSource.IsMobile(gameRoot) ? Find(gameRoot, cardId).Assets : list)
				{
					found.Add(item2);
				}
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
		PortableMonsterAnimationIndex portableMonsterAnimationIndex = new PortableMonsterAnimationIndex
		{
			GameBuildId = PortableIndexService.GetGameBuildId(gameRoot),
			Assets = (from x in found.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => $"{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
				select WithoutAbsolutePath(x.First()) into x
				orderby int.TryParse(x.CardId, out result2) ? result2 : int.MaxValue, x.Kind
				select x).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ToList()
		};
		Write(CachePath(gameRoot), portableMonsterAnimationIndex);
		return portableMonsterAnimationIndex;
	}

	public static PortableMonsterAnimationIndex EnsureCurrentIndex(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
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
					Write(path, portableMonsterAnimationIndex2);
					return RefreshMissingLinks(gameRoot, portableMonsterAnimationIndex2, progress, cancellationToken);
				}
			}
			catch
			{
			}
		}
		return Rebuild(gameRoot, progress, cancellationToken);
	}

	public static PortableMonsterAnimationIndex RefreshMissingLinks(string gameRoot, PortableMonsterAnimationIndex index, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动账号。");
		List<MonsterAnimationAssetRef> list = index.Assets.Where((MonsterAnimationAssetRef asset) => File.Exists(Path.Combine((asset.StorageKind == "StreamingAssets") ? IndexService.StreamingRoot(gameRoot) : local, asset.RelativeBundlePath))).ToList();
		HashSet<string> complete = CompleteCardIds(list);
		string[] array = (from id in FindInstalledCardIds(gameRoot)
			where !complete.Contains(id)
			select id).ToArray();
		for (int num = 0; num < array.Length; num++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			List<MonsterAnimationAssetRef> list2 = FindDeterministicCandidates(gameRoot, array[num]).Concat(FindPrefabDependencies(gameRoot, array[num])).ToList();
			list2.AddRange(FindCompanionPaths(gameRoot, array[num], list2));
			list.AddRange((ResourceSource.IsMobile(gameRoot) ? ((IEnumerable<MonsterAnimationAssetRef>)Find(gameRoot, array[num]).Assets) : ((IEnumerable<MonsterAnimationAssetRef>)list2)).Select(WithoutAbsolutePath));
			progress?.Invoke(num + 1, array.Length, list.Count);
		}
		PortableMonsterAnimationIndex portableMonsterAnimationIndex = new PortableMonsterAnimationIndex
		{
			GameBuildId = index.GameBuildId,
			Assets = list.DistinctBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef asset) => $"{asset.StorageKind}|{asset.RelativeBundlePath.Replace('\\', '/')}|{asset.AssetFileName}|{asset.PathId}", StringComparer.OrdinalIgnoreCase).ToList()
		};
		cancellationToken.ThrowIfCancellationRequested();
		Write(CachePath(gameRoot), portableMonsterAnimationIndex);
		return portableMonsterAnimationIndex;
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
