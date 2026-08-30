using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed class MonsterAnimationTierBuildResult : IDisposable
{
	public required string Tier { get; init; }

	public required Image<Rgba32> AtlasImage { get; init; }

	public required string AtlasText { get; init; }

	public required byte[] SkeletonJson { get; init; }

	public int AtlasWidth => AtlasImage.Width;

	public int AtlasHeight => AtlasImage.Height;

	public void Dispose()
	{
		AtlasImage.Dispose();
	}
}

public sealed class MonsterAnimationBuildResult : IDisposable
{
	public required MonsterAnimationTierBuildResult Hd { get; init; }

	public required MonsterAnimationTierBuildResult Sd { get; init; }

	// Kept as aliases for export diagnostics and older command-line tests.
	public Image<Rgba32> AtlasImage => Hd.AtlasImage;

	public string AtlasText => Hd.AtlasText;

	public byte[] SkeletonJson => Hd.SkeletonJson;

	public required int FrameCount { get; init; }

	public required int FramesPerSecond { get; init; }

	public required double DisplayWidth { get; init; }

	public required double DisplayHeight { get; init; }

	public int AtlasWidth => Hd.AtlasWidth;

	public int AtlasHeight => Hd.AtlasHeight;

	public MonsterAnimationTierBuildResult ForTier(string tier) =>
		tier.Equals("SD", StringComparison.OrdinalIgnoreCase) ? Sd : Hd;

	public void Dispose()
	{
		Hd.Dispose();
		Sd.Dispose();
	}
}
