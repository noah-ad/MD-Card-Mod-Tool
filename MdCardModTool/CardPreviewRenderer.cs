using System;
using System.Drawing;

namespace MdCardModTool;

/// <summary>
/// Optional card-frame composition must never make a successfully decoded
/// Texture2D disappear. When enhancement fails, the original RGBA texture is
/// returned with a non-blocking diagnostic.
/// </summary>
public static class CardPreviewRenderer
{
	public static Bitmap RenderRaw(byte[] texturePng)
	{
		return RgbaBitmap.FromPng(texturePng);
	}

	public static Bitmap Render(byte[] texturePng, byte[]? framePng, bool fullArt,
		out string? enhancementWarning)
	{
		enhancementWarning = null;
		try
		{
			if (fullArt)
			{
				return FrameComposer.PreviewBitmap(texturePng);
			}
			if (framePng != null)
			{
				return FrameComposer.BitmapFrom(CardFrameRenderer.ComposeStoredArtPreview(texturePng, framePng));
			}
		}
		catch (Exception error)
		{
			enhancementWarning = Compact(error.Message);
		}

		return RgbaBitmap.FromPng(texturePng);
	}

	private static string Compact(string value)
	{
		string compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
		return compact.Length <= 120 ? compact : compact[..117] + "…";
	}
}
