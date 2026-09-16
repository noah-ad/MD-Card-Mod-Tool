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
