using System;
using System.Drawing;

namespace MdCardModTool;

public static class CardPreviewRenderer
{
	public static Bitmap RenderRaw(byte[] texturePng, bool showTransparentRgb = false)
	{
		return RgbaBitmap.FromPng(showTransparentRgb ? AstellarOverFrameComposer.CreateVisibleRgbPreview(texturePng) : texturePng);
	}

	public static Bitmap Render(byte[] texturePng, byte[]? framePng, bool fullArt, out string? enhancementWarning)
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
		catch (Exception ex)
		{
			enhancementWarning = Compact(ex.Message);
		}
		return RgbaBitmap.FromPng(texturePng);
	}

	private static string Compact(string value)
	{
		string text = value.Replace("\r", " ").Replace("\n", " ").Trim();
		if (text.Length > 120)
		{
			return text.Substring(0, 117) + "…";
		}
		return text;
	}
}
