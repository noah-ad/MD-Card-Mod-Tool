using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

public static class BuiltInCardFrameCatalog
{
	private sealed record FrameDefinition(string Key, string SolidFile, string GradientFile);

	public const string SourceKind = "卡框资源";

	public const string NormalCategory = "普通卡框";

	public const string TransparentCategory = "透明卡框";

	public const string TransparentGradientCategory = "透明炫彩卡框";

	public const string GradientCategory = "炫彩卡框";

	private static readonly object TransparentGradientCacheLock = new object();

	private static readonly Dictionary<string, byte[]> TransparentGradientCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

	private static readonly FrameDefinition[] Definitions = new FrameDefinition[16]
	{
		new FrameDefinition("card_frame00", "Normal.png", "OfGradientNormal.png"),
		new FrameDefinition("card_frame01", "Effect.png", "OfGradientEffect.png"),
		new FrameDefinition("card_frame02", "Ritual.png", "OfGradientRitual.png"),
		new FrameDefinition("card_frame03", "Fusion.png", "OfGradientFusion.png"),
		new FrameDefinition("card_frame07", "Spell.png", "OfGradientSpell.png"),
		new FrameDefinition("card_frame08", "Trap.png", "OfGradientTrap.png"),
		new FrameDefinition("card_frame09", "Token.png", "OfGradientToken.png"),
		new FrameDefinition("card_frame10", "Synchro.png", "OfGradientSynchro.png"),
		new FrameDefinition("card_frame12", "Xyz.png", "OfGradientXyz.png"),
		new FrameDefinition("card_frame13", "PendulumNormal.png", "OfGradientPendulumNormal.png"),
		new FrameDefinition("card_frame14", "PendulumEffect.png", "OfGradientPendulumEffect.png"),
		new FrameDefinition("card_frame15", "PendulumXyz.png", "OfGradientPendulumXyz.png"),
		new FrameDefinition("card_frame16", "PendulumSynchro.png", "OfGradientPendulumSynchro.png"),
		new FrameDefinition("card_frame17", "PendulumFusion.png", "OfGradientPendulumFusion.png"),
		new FrameDefinition("card_frame18", "Link.png", "OfGradientLink.png"),
		new FrameDefinition("card_frame19", "PendulumRitual.png", "OfGradientPendulumRitual.png")
	};

	public static IReadOnlyList<TexRef> Load()
	{
		string text = CandidateAstellarDirectories().FirstOrDefault(Directory.Exists);
		string text2 = CandidateFloowanDirectories().FirstOrDefault(Directory.Exists);
		List<TexRef> list = new List<TexRef>();
		FrameDefinition[] definitions = Definitions;
		foreach (FrameDefinition frameDefinition in definitions)
		{
			AddIfPresent(list, text2, frameDefinition.SolidFile, frameDefinition.Key, "普通卡框", Path.Combine("Resources", "CardFrames", "Floowan", frameDefinition.SolidFile));
			AddIfPresent(list, text, frameDefinition.Key + ".png", "transparent_" + frameDefinition.Key, "透明卡框", Path.Combine("Resources", "CardFrames", frameDefinition.Key + ".png"));
			AddTransparentGradientIfPresent(list, text, text2, frameDefinition);
			AddIfPresent(list, text2, frameDefinition.GradientFile, "gradient_" + frameDefinition.Key, "炫彩卡框", Path.Combine("Resources", "CardFrames", "Floowan", frameDefinition.GradientFile));
		}
		return list;
	}

	public static bool IsPackagedFrame(TexRef texture)
	{
		if (texture.SourceKind == "卡框资源" && texture.PathId == 0L)
		{
			return string.Equals(Path.GetExtension(texture.ActiveBundlePath), ".png", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool IsNormalFrame(TexRef texture)
	{
		if (IsPackagedFrame(texture))
		{
			return texture.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool IsTransparentFrame(TexRef texture)
	{
		if (IsPackagedFrame(texture))
		{
			return texture.Name.StartsWith("transparent_card_frame", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool IsTransparentGradientFrame(TexRef texture)
	{
		if (IsPackagedFrame(texture))
		{
			return texture.Name.StartsWith("transparent_gradient_card_frame", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool IsGradientFrame(TexRef texture)
	{
		if (IsPackagedFrame(texture))
		{
			return texture.Name.StartsWith("gradient_card_frame", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static byte[] DecodeTransparentGradientFrame(TexRef texture)
	{
		if (!IsTransparentGradientFrame(texture))
		{
			throw new ArgumentException("资源不是透明炫彩卡框。", "texture");
		}
		string text = CardFrameCatalog.BaseKey(texture.Name);
		string text2 = CandidateAstellarDirectories().FirstOrDefault(Directory.Exists);
		string text3 = ((text2 == null) ? "" : Path.Combine(text2, text + ".png"));
		if (!File.Exists(text3))
		{
			throw new FileNotFoundException("透明炫彩卡框缺少 " + text + " 的 Astellar Alpha 模板。", text3);
		}
		string activeBundlePath = texture.ActiveBundlePath;
		string key = string.Join("|", activeBundlePath, File.GetLastWriteTimeUtc(activeBundlePath).Ticks, text3, File.GetLastWriteTimeUtc(text3).Ticks);
		lock (TransparentGradientCacheLock)
		{
			if (TransparentGradientCache.TryGetValue(key, out byte[] value))
			{
				return value;
			}
		}
		byte[] array = AstellarOverFrameComposer.CreateTransparentGradientFrame(File.ReadAllBytes(activeBundlePath), File.ReadAllBytes(text3));
		lock (TransparentGradientCacheLock)
		{
			TransparentGradientCache[key] = array;
			return array;
		}
	}

	private static void AddTransparentGradientIfPresent(List<TexRef> output, string? astellarDirectory, string? floowanDirectory, FrameDefinition definition)
	{
		if (astellarDirectory != null && floowanDirectory != null && File.Exists(Path.Combine(astellarDirectory, definition.Key + ".png")))
		{
			AddIfPresent(output, floowanDirectory, definition.GradientFile, "transparent_gradient_" + definition.Key, "透明炫彩卡框", Path.Combine("Resources", "CardFrames", "TransparentGradient", definition.Key + ".png"));
		}
	}

	private static void AddIfPresent(List<TexRef> output, string? directory, string fileName, string name, string category, string relativePath)
	{
		if (directory != null)
		{
			string text = Path.Combine(directory, fileName);
			if (File.Exists(text))
			{
				output.Add(new TexRef
				{
					BundlePath = text,
					RelativeBundlePath = relativePath,
					AssetFileName = "",
					PathId = 0L,
					Name = name,
					Width = 704,
					Height = 1024,
					Category = category,
					SourceKind = "卡框资源"
				});
			}
		}
	}

	private static IEnumerable<string> CandidateAstellarDirectories()
	{
		foreach (string item in AppPaths.CandidatePaths("CardFrames"))
		{
			yield return item;
		}
	}

	private static IEnumerable<string> CandidateFloowanDirectories()
	{
		foreach (string item in AppPaths.CandidatePaths("CardFrames", "Floowan"))
		{
			yield return item;
		}
		yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "reference", "master-duel-modding", "src", "Floowan.Core", "Resources", "frames"));
	}
}
