using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace MdCardModTool;

public sealed class ExtractedAnimation : IDisposable
{
	public required string SourcePath { get; init; }

	public required string WorkingDirectory { get; init; }

	public required List<string> FramePaths { get; init; }

	public required int FramesPerSecond { get; init; }

	public bool GreenScreenRemoved { get; init; }

	public double DurationSeconds => (double)FramePaths.Count / (double)FramesPerSecond;

	public Bitmap LoadFrame(int index, int maxPreviewEdge = 0)
	{
		if (index < 0 || index >= FramePaths.Count)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		using FileStream stream = new FileStream(FramePaths[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using Image image = Image.FromStream(stream);
		if (maxPreviewEdge > 0 && Math.Max(image.Width, image.Height) > maxPreviewEdge)
		{
			double ratio = (double)maxPreviewEdge / (double)Math.Max(image.Width, image.Height);
			return new Bitmap(image, Math.Max(1, (int)Math.Round((double)image.Width * ratio)), Math.Max(1, (int)Math.Round((double)image.Height * ratio)));
		}
		return new Bitmap(image);
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(WorkingDirectory))
			{
				Directory.Delete(WorkingDirectory, recursive: true);
			}
		}
		catch
		{
		}
	}
}
