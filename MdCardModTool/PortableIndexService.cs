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
		if (ResourceSource.IsMobile(gameRoot))
		{
			return false;
		}
		if (!File.Exists(BundledPath))
		{
			return false;
		}
		PortableGameIndex portableGameIndex = Read(BundledPath);
		if (portableGameIndex.FormatVersion != 1 || portableGameIndex.Textures.Count < 1000)
		{
			throw new InvalidDataException("随包预绑定索引格式错误或内容不完整。");
		}
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string streamingRoot = IndexService.StreamingRoot(gameRoot);
		index = new GameIndex
		{
			AlternateArtIndexVersion = portableGameIndex.AlternateArtIndexVersion,
			Textures = portableGameIndex.Textures.Select((PortableTextureEntry x) => new TexRef
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
		buildId = portableGameIndex.GameBuildId;
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
		HashSet<string> hashSet = repaired.Textures.Select(TextureLogicalIdentity).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TexRef texture in existing.Textures)
		{
			if (File.Exists(texture.BundlePath) && hashSet.Add(TextureLogicalIdentity(texture)))
			{
				repaired.Textures.Add(texture);
				retainedExtras++;
			}
		}
		HashSet<string> hashSet2 = repaired.CheckedLocalBundlePaths.ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string checkedLocalBundlePath in existing.CheckedLocalBundlePaths)
		{
			if (hashSet2.Add(checkedLocalBundlePath))
			{
				repaired.CheckedLocalBundlePaths.Add(checkedLocalBundlePath);
			}
		}
		repaired.Textures.Sort((TexRef a, TexRef b) => string.Compare($"{a.SourceKind}\0{a.Category}\0{a.Name}\0{a.Width:D8}", $"{b.SourceKind}\0{b.Category}\0{b.Name}\0{b.Width:D8}", StringComparison.Ordinal));
		return true;
	}

	public static void Export(string gameRoot, GameIndex index, string outputPath)
	{
		List<PortableTextureEntry> list = index.Textures.Select(ToPortable).ToList();
		NormalizeBackedUpBundles(gameRoot, index.Textures, list);
		PortableGameIndex value = new PortableGameIndex
		{
			GameBuildId = GetGameBuildId(gameRoot),
			AlternateArtIndexVersion = index.AlternateArtIndexVersion,
			Textures = list
		};
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
		using FileStream stream = File.Create(outputPath);
		using BrotliStream utf8Json = new BrotliStream(stream, CompressionLevel.SmallestSize);
		JsonSerializer.Serialize(utf8Json, value);
	}

	public static PortableGameIndex Read(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using BrotliStream utf8Json = new BrotliStream(stream, CompressionMode.Decompress);
		return JsonSerializer.Deserialize<PortableGameIndex>(utf8Json) ?? throw new InvalidDataException("无法读取随包预绑定索引。");
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
		ModEngine modEngine = new ModEngine();
		for (int i = 0; i < textures.Count; i++)
		{
			TexRef texture = textures[i];
			string text = Path.Combine(gameRoot, "_MD卡图备份", texture.SourceKind);
			string text2 = Path.Combine(text, texture.RelativeBundlePath);
			if (!File.Exists(text2))
			{
				continue;
			}
			try
			{
				TexRef texRef = modEngine.ScanBundle(text2, text, texture.SourceKind, includeDependencies: false).Textures.FirstOrDefault((TexRef x) => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName);
				if (texRef == null)
				{
					continue;
				}
				string category = texRef.Category;
				if (texture.SourceKind == "本地卡图")
				{
					if (texRef.Width == 512 && texRef.Height == 1024)
					{
						category = "灵摆卡图";
					}
					else if (texRef.Width == 512 && texRef.Height == 512)
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
					Width = texRef.Width,
					Height = texRef.Height,
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
		string text = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("预绑定索引路径越出了游戏目录。");
		}
		return fullPath;
	}

	public static string GetGameBuildId(string gameRoot)
	{
		if (ResourceSource.IsMobile(gameRoot))
		{
			return "mobile";
		}
		try
		{
			string fullPath = Path.GetFullPath(Path.Combine(gameRoot, "..", "..", "appmanifest_1449850.acf"));
			if (!File.Exists(fullPath))
			{
				return "";
			}
			Match match = Regex.Match(File.ReadAllText(fullPath), "\\\"buildid\\\"\\s+\\\"(?<id>\\d+)\\\"", RegexOptions.IgnoreCase);
			return match.Success ? match.Groups["id"].Value : "";
		}
		catch
		{
			return "";
		}
	}
}
