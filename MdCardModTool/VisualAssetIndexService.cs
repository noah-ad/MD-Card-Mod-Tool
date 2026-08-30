using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		List<CatalogEntry> catalog = ReadCatalog();
		IGrouping<string, CatalogEntry>[] groups = catalog.GroupBy((CatalogEntry x) => x.Hash, StringComparer.OrdinalIgnoreCase).ToArray();
		int total = groups.Length + 1;
		int done = 0;
		int installed = 0;
		ConcurrentBag<TexRef> found = new ConcurrentBag<TexRef>();
		Parallel.ForEach(groups, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(2, Math.Min(8, Environment.ProcessorCount)),
			CancellationToken = cancellationToken
		}, delegate(IGrouping<string, CatalogEntry> group)
		{
			try
			{
				string path = ResolveBundlePath(localRoot, group.Key);
				if (File.Exists(path))
				{
					Interlocked.Increment(ref installed);
					Dictionary<string, string> expected = group.GroupBy((CatalogEntry x) => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, CatalogEntry> x) => x.Key, (IGrouping<string, CatalogEntry> x) => x.First().Category, StringComparer.OrdinalIgnoreCase);
					foreach (TexRef texture in new ModEngine().ScanBundle(path, localRoot, LocalSourceKind, includeDependencies: false).Textures)
					{
						if (expected.TryGetValue(texture.Name, out string? category))
						{
							texture.Category = category;
							found.Add(texture);
						}
					}
				}
			}
			finally
			{
				int current = Interlocked.Increment(ref done);
				if (current % 25 == 0 || current == total)
				{
					progress?.Invoke(current, total, found.Count);
				}
			}
		});
		string builtIn = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
		if (File.Exists(builtIn))
		{
			Interlocked.Increment(ref installed);
			foreach (TexRef texture in new ModEngine().ScanBundle(builtIn, gameRoot, BuiltInSourceKind, includeDependencies: false).Textures)
			{
				string? category = VisualAssetClassifier.CategoryFor(texture.Name);
				if (category == "大厅背景" || category == "决斗场地")
				{
					texture.Category = category;
					found.Add(texture);
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
			CatalogEntries = catalog.Count,
			CandidateBundles = groups.Length + 1,
			InstalledBundles = installed
		};
	}

	private static string ResolveBundlePath(string localRoot, string hash)
	{
		string nested = Path.Combine(localRoot, hash.Substring(0, 2), hash);
		return File.Exists(nested) ? nested : Path.Combine(localRoot, hash);
	}

	private static List<CatalogEntry> ReadCatalog()
	{
		string external = AppPaths.ResolveFile("visual-assets-v1.tsv");
		using Stream stream = File.Exists(external) ? File.OpenRead(external) : (Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedCatalogName) ?? throw new FileNotFoundException("程序缺少 Astellar 视觉资源目录。", external));
		using StreamReader reader = new StreamReader(stream);
		List<CatalogEntry> result = new List<CatalogEntry>();
		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}
			string[] parts = line.Split('\t');
			if (parts.Length == 3 && parts[0].Length == 8)
			{
				result.Add(new CatalogEntry(parts[0], parts[1], parts[2]));
			}
		}
		if (result.Count < 500)
		{
			throw new InvalidDataException("Astellar 视觉资源目录内容不完整。");
		}
		return result;
	}
}
