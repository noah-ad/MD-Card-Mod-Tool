using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class VisualAssetIndexService
{
	private sealed record CatalogEntry(string Hash, string Name, string Category);

	private const string EmbeddedCatalogName = "MdCardModTool.visual-assets-v1.tsv";

	public const string LocalSourceKind = "视觉资源";

	public const string BuiltInSourceKind = "基础视觉资源";

	public static VisualAssetScanResult Scan(string gameRoot, Action<int, int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (ResourceSource.IsMobile(gameRoot))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string path = IndexService.CachePath(gameRoot, IndexService.StreamingRoot(gameRoot));
			GameIndex gameIndex = (File.Exists(path) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path)) : null);
			if (gameIndex == null)
			{
				gameIndex = ResourceSource.BuildMobile(gameRoot, progress);
			}
			return new VisualAssetScanResult
			{
				Textures = gameIndex.Textures.Where((TexRef t) => t.SourceKind == "视觉资源").ToList()
			};
		}
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		List<CatalogEntry> list = ReadCatalog();
		IGrouping<string, CatalogEntry>[] array = list.GroupBy<CatalogEntry, string>((CatalogEntry x) => x.Hash, StringComparer.OrdinalIgnoreCase).ToArray();
		int total = array.Length + 1;
		int done = 0;
		int installed = 0;
		ConcurrentBag<TexRef> found = new ConcurrentBag<TexRef>();
		Parallel.ForEach(array, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(2, Math.Min(8, Environment.ProcessorCount)),
			CancellationToken = cancellationToken
		}, delegate(IGrouping<string, CatalogEntry> group)
		{
			try
			{
				string text3 = ResolveBundlePath(localRoot, group.Key);
				if (File.Exists(text3))
				{
					Interlocked.Increment(ref installed);
					Dictionary<string, string> dictionary = group.GroupBy<CatalogEntry, string>((CatalogEntry x) => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary<IGrouping<string, CatalogEntry>, string, string>((IGrouping<string, CatalogEntry> x) => x.Key, (IGrouping<string, CatalogEntry> x) => x.First().Category, StringComparer.OrdinalIgnoreCase);
					{
						foreach (TexRef texture in new ModEngine().ScanBundle(text3, localRoot, "视觉资源", includeDependencies: false).Textures)
						{
							if (dictionary.TryGetValue(texture.Name, out var value))
							{
								texture.Category = value;
								found.Add(texture);
							}
						}
						return;
					}
				}
			}
			finally
			{
				int num = Interlocked.Increment(ref done);
				if (num % 25 == 0 || num == total)
				{
					progress?.Invoke(num, total, found.Count);
				}
			}
		});
		string text = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
		if (File.Exists(text))
		{
			Interlocked.Increment(ref installed);
			foreach (TexRef texture2 in new ModEngine().ScanBundle(text, gameRoot, "基础视觉资源", includeDependencies: false).Textures)
			{
				string text2 = VisualAssetClassifier.CategoryFor(texture2.Name);
				if (text2 == "大厅背景" || text2 == "决斗场地")
				{
					texture2.Category = text2;
					found.Add(texture2);
				}
			}
		}
		done = total;
		progress?.Invoke(done, total, found.Count);
		List<TexRef> textures = (from texture in found
			group texture by $"{texture.BundlePath}\0{texture.AssetFileName}\0{texture.PathId}" into unique
			select unique.First() into texture
			orderby texture.SourceKind, texture.Category, texture.Name
			select texture).ToList();
		return new VisualAssetScanResult
		{
			Textures = textures,
			CatalogEntries = list.Count,
			CandidateBundles = array.Length + 1,
			InstalledBundles = installed
		};
	}

	private static string ResolveBundlePath(string localRoot, string hash)
	{
		string text = Path.Combine(localRoot, hash.Substring(0, 2), hash);
		if (!File.Exists(text))
		{
			return Path.Combine(localRoot, hash);
		}
		return text;
	}

	private static List<CatalogEntry> ReadCatalog()
	{
		string text = AppPaths.ResolveFile("visual-assets-v1.tsv");
		using Stream stream = (File.Exists(text) ? File.OpenRead(text) : (Assembly.GetExecutingAssembly().GetManifestResourceStream("MdCardModTool.visual-assets-v1.tsv") ?? throw new FileNotFoundException("程序缺少 Astellar 视觉资源目录。", text)));
		using StreamReader streamReader = new StreamReader(stream);
		List<CatalogEntry> list = new List<CatalogEntry>();
		string text2;
		while ((text2 = streamReader.ReadLine()) != null)
		{
			if (text2.Length != 0 && text2[0] != '#')
			{
				string[] array = text2.Split('\t');
				if (array.Length == 3 && array[0].Length == 8)
				{
					list.Add(new CatalogEntry(array[0], array[1], array[2]));
				}
			}
		}
		if (list.Count < 500)
		{
			throw new InvalidDataException("Astellar 视觉资源目录内容不完整。");
		}
		return list;
	}
}
