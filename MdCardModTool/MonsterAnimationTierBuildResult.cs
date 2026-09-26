using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed class MonsterAnimationTierBuildResult : IDisposable
{
	public required string Tier { get; init; }

	public required Image<Rgba32> AtlasImage { get; init; }

	public required string AtlasText { get; init; }

	public required byte[] SkeletonJson { get; init; }
	public System.Collections.Generic.IReadOnlyList<Image<Rgba32>> ExtraPages { get; init; } = Array.Empty<Image<Rgba32>>();
	public System.Collections.Generic.IEnumerable<Image<Rgba32>> Pages => new[] { AtlasImage }.Concat(ExtraPages);

	public int AtlasWidth => AtlasImage.Width;

	public int AtlasHeight => AtlasImage.Height;

	public void Dispose()
	{
		AtlasImage.Dispose();
		foreach (var page in ExtraPages) page.Dispose();
	}
}
