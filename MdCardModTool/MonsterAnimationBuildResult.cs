using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed class MonsterAnimationBuildResult : IDisposable
{
	public required MonsterAnimationTierBuildResult Hd { get; init; }

	public required MonsterAnimationTierBuildResult Sd { get; init; }

	public Image<Rgba32> AtlasImage => Hd.AtlasImage;

	public string AtlasText => Hd.AtlasText;

	public byte[] SkeletonJson => Hd.SkeletonJson;

	public required int FrameCount { get; init; }

	public required int FramesPerSecond { get; init; }

	public required double DisplayWidth { get; init; }

	public required double DisplayHeight { get; init; }

	public int AtlasWidth => Hd.AtlasWidth;

	public int AtlasHeight => Hd.AtlasHeight;

	public MonsterAnimationTierBuildResult ForTier(string tier)
	{
		if (!tier.Equals("SD", StringComparison.OrdinalIgnoreCase))
		{
			return Hd;
		}
		return Sd;
	}

	public void Dispose()
	{
		Hd.Dispose();
		Sd.Dispose();
	}
}
