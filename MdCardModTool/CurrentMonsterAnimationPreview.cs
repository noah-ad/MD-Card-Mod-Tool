using System;
using System.Collections.Generic;
using System.Drawing;

namespace MdCardModTool;

public sealed class CurrentMonsterAnimationPreview : IDisposable
{
	public required List<Bitmap> Frames { get; init; }

	public required int FramesPerSecond { get; init; }

	public required string AnimationName { get; init; }

	public required int ScalePercent { get; init; }

	public void Dispose()
	{
		foreach (Bitmap frame in Frames)
		{
			frame.Dispose();
		}
		Frames.Clear();
	}
}
