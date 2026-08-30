using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MdCardModTool;

public static class PortableIndexService
{
	public const string BundledFileName = "prebuilt-index-v1.json.br";

	public static string BundledPath => AppPaths.ResolveFile("prebuilt-index-v1.json.br");

	public static bool TryLoadBundled(string gameRoot, out GameIndex index, out string buildId)
	{
		index = new GameIndex();
		buildId = "";
		if (!File.Exists(BundledPath))
		{
			return false;
		}
		PortableGameIndex portable = Read(BundledPath);
		if (portable.FormatVersion != 1 || portable.Textures.Count < 1000)
		{
			throw new InvalidDataException("随包预绑定索引格式错误或内容不完整。");
		}
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string streamingRoot = IndexService.StreamingRoot(gameRoot);
		index = new GameIndex
		{
			AlternateArtIndexVersion = portable.AlternateArtIndexVersion,
			Textures = portable.Textures.Select((PortableTextureEntry x) => new TexRef
			{
				BundlePath = ResolveInside(SourceRoot(gameRoot, localRoot, streamingRoot, x.SourceKind), x.RelativeBundlePath),
				RelativeBundlePath = x.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar),
				PathId = x.PathId,
				AssetFileName = x.AssetFileName,
				Name = x.Name,
				Width = x.Width,
				Height = x.Height,
				Category = x.Category,
				IsAlternateArt = x.IsAlternateArt,
				IsTokenOrMisc = x.IsTokenOrMisc,
				SourceKind = x.SourceKind,
				CardKey = x.CardKey
			}).ToList()
		};
		buildId = portable.GameBuildId;
		return true;
	}

	public static bool TryRepairFromBundled(string gameRoot, GameIndex? existing, out GameIndex repaired, out string buildId, out int retainedExtras)
	{
		retainedExtras = 0;
		if (!TryLoadBundled(gameRoot, out repaired, out buildId))
		{
			return false;
		}
		if (existing == null)
		{
			return true;
		}
		HashSet<string> known = repaired.Textures.Select(TextureLogicalIdentity).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TexRef texture in existing.Textures)
		{
			if (File.Exists(texture.BundlePath) && known.Add(TextureLogicalIdentity(texture)))
			{
				repaired.Textures.Add(texture);
				retainedExtras++;
			}
		}
		HashSet<string> checkedPaths = repaired.CheckedLocalBundlePaths.ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string path in existing.CheckedLocalBundlePaths)
		{
			if (checkedPaths.Add(path))
			{
				repaired.CheckedLocalBundlePaths.Add(path);
			}
		}
		repaired.Textures.Sort((TexRef a, TexRef b) => string.Compare($"{a.SourceKind}\0{a.Category}\0{a.Name}\0{a.Width:D8}", $"{b.SourceKind}\0{b.Category}\0{b.Name}\0{b.Width:D8}", StringComparison.Ordinal));
		return true;
	}

	public static void Export(string gameRoot, GameIndex index, string outputPath)
	{
		List<PortableTextureEntry> entries = index.Textures.Select(ToPortable).ToList();
		NormalizeBackedUpBundles(gameRoot, index.Textures, entries);
		PortableGameIndex portable = new PortableGameIndex
		{
			GameBuildId = GetGameBuildId(gameRoot),
			AlternateArtIndexVersion = index.AlternateArtIndexVersion,
			Textures = entries
		};
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
		using FileStream file = File.Create(outputPath);
		using BrotliStream brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
		JsonSerializer.Serialize(brotli, portable);
	}

	public static PortableGameIndex Read(string path)
	{
		using FileStream file = File.OpenRead(path);
		using BrotliStream brotli = new BrotliStream(file, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<PortableGameIndex>(brotli) ?? throw new InvalidDataException("无法读取随包预绑定索引。");
	}

	private static PortableTextureEntry ToPortable(TexRef x)
	{
		return new PortableTextureEntry
		{
			RelativeBundlePath = x.RelativeBundlePath.Replace('\\', '/'),
			PathId = x.PathId,
			AssetFileName = x.AssetFileName,
			Name = x.Name,
			Width = x.Width,
			Height = x.Height,
			Category = x.Category,
			IsAlternateArt = x.IsAlternateArt,
			IsTokenOrMisc = x.IsTokenOrMisc,
			SourceKind = x.SourceKind,
			CardKey = x.CardKey
		};
	}

	private static string TextureLogicalIdentity(TexRef texture)
	{
		return $"{texture.SourceKind}\0{texture.RelativeBundlePath.Replace('\\', '/')}\0{texture.CardKey}\0{texture.Name}";
	}

	private static void NormalizeBackedUpBundles(string gameRoot, IReadOnlyList<TexRef> textures, List<PortableTextureEntry> entries)
	{
		ModEngine engine = new ModEngine();
		for (int i = 0; i < textures.Count; i++)
		{
			TexRef texture = textures[i];
			string backupRoot = Path.Combine(gameRoot, "_MD卡图备份", texture.SourceKind);
			string backup = Path.Combine(backupRoot, texture.RelativeBundlePath);
			if (!File.Exists(backup))
			{
				continue;
			}
			try
			{
				TexRef original = engine.ScanBundle(backup, backupRoot, texture.SourceKind, includeDependencies: false).Textures.FirstOrDefault((TexRef x) => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName);
				if (original == null)
				{
					continue;
				}
				string category = original.Category;
				if (texture.SourceKind == "本地卡图")
				{
					if (original.Width == 512 && original.Height == 1024)
					{
						category = "灵摆卡图";
					}
					else if (original.Width == 512 && original.Height == 512)
					{
						category = (texture.IsAlternateArt ? "异画卡图" : (texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图"));
					}
				}
				entries[i] = new PortableTextureEntry
				{
					RelativeBundlePath = entries[i].RelativeBundlePath,
					PathId = entries[i].PathId,
					AssetFileName = entries[i].AssetFileName,
					Name = entries[i].Name,
					Width = original.Width,
					Height = original.Height,
					Category = category,
					IsAlternateArt = entries[i].IsAlternateArt,
					IsTokenOrMisc = entries[i].IsTokenOrMisc,
					SourceKind = entries[i].SourceKind,
					CardKey = entries[i].CardKey
				};
			}
			catch
			{
			}
		}
	}

	private static string SourceRoot(string gameRoot, string localRoot, string streamingRoot, string sourceKind)
	{
		return sourceKind switch
		{
			"本地卡图" => localRoot,
			"视觉资源" => localRoot,
			"游戏内图片" => streamingRoot,
			"卡框资源" => gameRoot,
			"基础视觉资源" => gameRoot,
			_ => throw new InvalidDataException("预绑定索引包含未知来源：" + sourceKind),
		};
	}

	private static string ResolveInside(string root, string relative)
	{
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("预绑定索引包含无效路径。");
		}
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("预绑定索引路径越出了游戏目录。");
		}
		return fullPath;
	}

	public static string GetGameBuildId(string gameRoot)
	{
		try
		{
			string manifest = Path.GetFullPath(Path.Combine(gameRoot, "..", "..", "appmanifest_1449850.acf"));
			if (!File.Exists(manifest))
			{
				return "";
			}
			Match match = Regex.Match(File.ReadAllText(manifest), "\\\"buildid\\\"\\s+\\\"(?<id>\\d+)\\\"", RegexOptions.IgnoreCase);
			return match.Success ? match.Groups["id"].Value : "";
		}
		catch
		{
			return "";
		}
	}
}
