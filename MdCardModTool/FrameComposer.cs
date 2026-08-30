using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MdCardModTool;

public static class FrameComposer
{
	public const int Width = 704;

	public const int Height = 1024;

	public static byte[] Compose(byte[] artPng, byte[] framePng)
	{
		using Bitmap art = BitmapFrom(artPng);
		using Bitmap frame = BitmapFrom(framePng);
		Validate(art, "透明高图");
		Validate(frame, "卡框");
		using Bitmap output = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(output))
		{
			graphics.Clear(Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.DrawImageUnscaled(frame, 0, 0);
			graphics.CompositingMode = CompositingMode.SourceOver;
			graphics.DrawImageUnscaled(art, 0, 0);
		}
		using MemoryStream stream = new MemoryStream();
		output.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	/// <summary>
	/// Conventional card composition: artwork below a complete flat frame.  This
	/// is deliberately separate from transparent-edge over-frame composition.
	/// </summary>
	public static byte[] ComposeNormalFrame(byte[] artPng, byte[] framePng)
	{
		using Bitmap art = BitmapFrom(artPng);
		using Bitmap frame = BitmapFrom(framePng);
		Validate(art, "卡图");
		Validate(frame, "普通完整卡框");
		using Bitmap output = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(output))
		{
			graphics.Clear(Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.DrawImageUnscaled(art, 0, 0);
			graphics.CompositingMode = CompositingMode.SourceOver;
			graphics.DrawImageUnscaled(frame, 0, 0);
		}
		using MemoryStream stream = new MemoryStream();
		output.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	public static Bitmap BitmapFrom(byte[] data)
	{
		using MemoryStream stream = new MemoryStream(data);
		using Image image = Image.FromStream(stream);
		return new Bitmap(image);
	}

	public static Bitmap PreviewBitmap(byte[] data)
	{
		return RgbaBitmap.FromPng(data);
	}

	private static void Validate(Image image, string label)
	{
		if (image.Width != 704 || image.Height != 1024)
		{
			throw new InvalidDataException($"{label}必须严格为 {704}×{1024}；当前为 {image.Width}×{image.Height}。");
		}
	}
}
