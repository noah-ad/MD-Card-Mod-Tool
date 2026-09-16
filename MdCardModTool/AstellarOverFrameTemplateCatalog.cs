using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

public static class AstellarOverFrameTemplateCatalog
{
	public static readonly string[] LayerNames = new string[6] { "PeriFrame", "NameBox", "ArtFrame", "EffFrame", "EffBox", "BackGround" };

	public static bool IsAvailable(string key)
	{
		return FindDirectory(key) != null;
	}

	public static AstellarOverFrameTemplate Load(string key)
	{
		string path = FindDirectory(key) ?? throw new DirectoryNotFoundException("找不到 Astellar 透明边缘模板：" + key + "。");
		Dictionary<string, byte[]> dictionary = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		string[] layerNames = LayerNames;
		foreach (string text in layerNames)
		{
			string text2 = Path.Combine(path, text + ".png");
			if (!File.Exists(text2))
			{
				throw new FileNotFoundException($"透明边缘模板 {key} 缺少 {text} 图层。", text2);
			}
			dictionary[text] = File.ReadAllBytes(text2);
		}
		return new AstellarOverFrameTemplate(key, dictionary);
	}

	private static string? FindDirectory(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			return null;
		}
		return (from root in CandidateRoots()
			select Path.Combine(root, key)).FirstOrDefault(Directory.Exists);
	}

	private static IEnumerable<string> CandidateRoots()
	{
		foreach (string item in AppPaths.CandidatePaths("OverFrameTemplates"))
		{
			yield return item;
		}
	}
}
