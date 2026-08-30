using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

public sealed record AstellarOverFrameTemplate(
	string Key,
	IReadOnlyDictionary<string, byte[]> Layers);

/// <summary>
/// Six-layer transparent-edge frame templates extracted deterministically from
/// AstellarTool's PSD assets.  These are intentionally separate from the flat,
/// complete card frames in <see cref="BuiltInCardFrameCatalog"/>.
/// </summary>
public static class AstellarOverFrameTemplateCatalog
{
	public static readonly string[] LayerNames =
	[
		"PeriFrame", "NameBox", "ArtFrame", "EffFrame", "EffBox", "BackGround"
	];

	public static bool IsAvailable(string key) => FindDirectory(key) != null;

	public static AstellarOverFrameTemplate Load(string key)
	{
		string directory = FindDirectory(key)
			?? throw new DirectoryNotFoundException($"找不到 Astellar 透明边缘模板：{key}。");
		Dictionary<string, byte[]> layers = new(StringComparer.Ordinal);
		foreach (string name in LayerNames)
		{
			string path = Path.Combine(directory, name + ".png");
			if (!File.Exists(path))
			{
				throw new FileNotFoundException($"透明边缘模板 {key} 缺少 {name} 图层。", path);
			}
			layers[name] = File.ReadAllBytes(path);
		}
		return new AstellarOverFrameTemplate(key, layers);
	}

	private static string? FindDirectory(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			return null;
		}
		return CandidateRoots()
			.Select(root => Path.Combine(root, key))
			.FirstOrDefault(Directory.Exists);
	}

	private static IEnumerable<string> CandidateRoots()
	{
		yield return Path.Combine(AppContext.BaseDirectory, "OverFrameTemplates");
		yield return Path.Combine(AppContext.BaseDirectory, "Resources", "OverFrameTemplates");
	}
}
