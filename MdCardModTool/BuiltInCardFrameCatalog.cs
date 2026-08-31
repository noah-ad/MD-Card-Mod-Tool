using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

/// <summary>
/// Read-only 704x1024 card-frame library. Four visually and technically
/// different resources are deliberately exposed instead of relabelling one PNG:
/// normal Floowan frames, Astellar transparent-RGB frames, Floowan's iridescent
/// OfGradient frames, and a derived transparent-iridescent combination.
/// </summary>
public static class BuiltInCardFrameCatalog
{
	public const string SourceKind = "卡框资源";

	public const string NormalCategory = "普通卡框";

	public const string TransparentCategory = "透明卡框";

	public const string TransparentGradientCategory = "透明炫彩卡框";

	public const string GradientCategory = "炫彩卡框";

	private sealed record FrameDefinition(string Key, string SolidFile, string GradientFile);

	private static readonly object TransparentGradientCacheLock = new();

	private static readonly Dictionary<string, byte[]> TransparentGradientCache =
		new(StringComparer.OrdinalIgnoreCase);

	private static readonly FrameDefinition[] Definitions =
	[
		new("card_frame00", "Normal.png", "OfGradientNormal.png"),
		new("card_frame01", "Effect.png", "OfGradientEffect.png"),
		new("card_frame02", "Ritual.png", "OfGradientRitual.png"),
		new("card_frame03", "Fusion.png", "OfGradientFusion.png"),
		new("card_frame07", "Spell.png", "OfGradientSpell.png"),
		new("card_frame08", "Trap.png", "OfGradientTrap.png"),
		new("card_frame09", "Token.png", "OfGradientToken.png"),
		new("card_frame10", "Synchro.png", "OfGradientSynchro.png"),
		new("card_frame12", "Xyz.png", "OfGradientXyz.png"),
		new("card_frame13", "PendulumNormal.png", "OfGradientPendulumNormal.png"),
		new("card_frame14", "PendulumEffect.png", "OfGradientPendulumEffect.png"),
		new("card_frame15", "PendulumXyz.png", "OfGradientPendulumXyz.png"),
		new("card_frame16", "PendulumSynchro.png", "OfGradientPendulumSynchro.png"),
		new("card_frame17", "PendulumFusion.png", "OfGradientPendulumFusion.png"),
		new("card_frame18", "Link.png", "OfGradientLink.png"),
		new("card_frame19", "PendulumRitual.png", "OfGradientPendulumRitual.png")
	];

	public static IReadOnlyList<TexRef> Load()
	{
		string? astellarDirectory = CandidateAstellarDirectories().FirstOrDefault(Directory.Exists);
		string? floowanDirectory = CandidateFloowanDirectories().FirstOrDefault(Directory.Exists);
		List<TexRef> frames = [];
		foreach (FrameDefinition definition in Definitions)
		{
			AddIfPresent(frames, floowanDirectory, definition.SolidFile, definition.Key,
				NormalCategory, Path.Combine("Resources", "CardFrames", "Floowan", definition.SolidFile));
			AddIfPresent(frames, astellarDirectory, definition.Key + ".png", "transparent_" + definition.Key,
				TransparentCategory, Path.Combine("Resources", "CardFrames", definition.Key + ".png"));
			AddTransparentGradientIfPresent(frames, astellarDirectory, floowanDirectory, definition);
			AddIfPresent(frames, floowanDirectory, definition.GradientFile, "gradient_" + definition.Key,
				GradientCategory, Path.Combine("Resources", "CardFrames", "Floowan", definition.GradientFile));
		}
		return frames;
	}

	public static bool IsPackagedFrame(TexRef texture) => texture.SourceKind == SourceKind
		&& texture.PathId == 0
		&& string.Equals(Path.GetExtension(texture.ActiveBundlePath), ".png", StringComparison.OrdinalIgnoreCase);

	public static bool IsNormalFrame(TexRef texture) => IsPackagedFrame(texture)
		&& texture.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase);

	public static bool IsTransparentFrame(TexRef texture) => IsPackagedFrame(texture)
		&& texture.Name.StartsWith("transparent_card_frame", StringComparison.OrdinalIgnoreCase);

	public static bool IsTransparentGradientFrame(TexRef texture) => IsPackagedFrame(texture)
		&& texture.Name.StartsWith("transparent_gradient_card_frame", StringComparison.OrdinalIgnoreCase);

	public static bool IsGradientFrame(TexRef texture) => IsPackagedFrame(texture)
		&& texture.Name.StartsWith("gradient_card_frame", StringComparison.OrdinalIgnoreCase);

	public static byte[] DecodeTransparentGradientFrame(TexRef texture)
	{
		if (!IsTransparentGradientFrame(texture))
		{
			throw new ArgumentException("资源不是透明炫彩卡框。", nameof(texture));
		}
		string baseKey = CardFrameCatalog.BaseKey(texture.Name);
		string? astellarDirectory = CandidateAstellarDirectories().FirstOrDefault(Directory.Exists);
		string transparentPath = astellarDirectory == null
			? ""
			: Path.Combine(astellarDirectory, baseKey + ".png");
		if (!File.Exists(transparentPath))
		{
			throw new FileNotFoundException($"透明炫彩卡框缺少 {baseKey} 的 Astellar Alpha 模板。",
				transparentPath);
		}
		string gradientPath = texture.ActiveBundlePath;
		string cacheKey = string.Join("|", gradientPath, File.GetLastWriteTimeUtc(gradientPath).Ticks,
			transparentPath, File.GetLastWriteTimeUtc(transparentPath).Ticks);
		lock (TransparentGradientCacheLock)
		{
			if (TransparentGradientCache.TryGetValue(cacheKey, out byte[]? cached)) return cached;
		}
		byte[] generated = AstellarOverFrameComposer.CreateTransparentGradientFrame(
			File.ReadAllBytes(gradientPath), File.ReadAllBytes(transparentPath));
		lock (TransparentGradientCacheLock)
		{
			TransparentGradientCache[cacheKey] = generated;
		}
		return generated;
	}

	private static void AddTransparentGradientIfPresent(List<TexRef> output,
		string? astellarDirectory, string? floowanDirectory, FrameDefinition definition)
	{
		if (astellarDirectory == null || floowanDirectory == null
			|| !File.Exists(Path.Combine(astellarDirectory, definition.Key + ".png")))
		{
			return;
		}
		AddIfPresent(output, floowanDirectory, definition.GradientFile,
			"transparent_gradient_" + definition.Key, TransparentGradientCategory,
			Path.Combine("Resources", "CardFrames", "TransparentGradient", definition.Key + ".png"));
	}

	private static void AddIfPresent(List<TexRef> output, string? directory, string fileName,
		string name, string category, string relativePath)
	{
		if (directory == null) return;
		string path = Path.Combine(directory, fileName);
		if (!File.Exists(path)) return;
		output.Add(new TexRef
		{
			BundlePath = path,
			RelativeBundlePath = relativePath,
			AssetFileName = "",
			PathId = 0,
			Name = name,
			Width = 704,
			Height = 1024,
			Category = category,
			SourceKind = SourceKind
		});
	}

	private static IEnumerable<string> CandidateAstellarDirectories()
	{
		foreach (string candidate in AppPaths.CandidatePaths("CardFrames"))
		{
			yield return candidate;
		}
	}

	private static IEnumerable<string> CandidateFloowanDirectories()
	{
		foreach (string candidate in AppPaths.CandidatePaths("CardFrames", "Floowan"))
		{
			yield return candidate;
		}
		// Development fallback. Published builds receive the same MIT resources via
		// MSBuild content linking and do not depend on this checkout path.
		yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
			"reference", "master-duel-modding", "src", "Floowan.Core", "Resources", "frames"));
	}
}
