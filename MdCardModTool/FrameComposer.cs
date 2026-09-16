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
		using Bitmap image = BitmapFrom(artPng);
		using Bitmap image2 = BitmapFrom(framePng);
		Validate(image, "透明高图");
		Validate(image2, "卡框");
		using Bitmap bitmap = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.DrawImageUnscaled(image2, 0, 0);
			graphics.CompositingMode = CompositingMode.SourceOver;
			graphics.DrawImageUnscaled(image, 0, 0);
		}
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static byte[] ComposeNormalFrame(byte[] artPng, byte[] framePng)
	{
		using Bitmap image = BitmapFrom(artPng);
		using Bitmap image2 = BitmapFrom(framePng);
		Validate(image, "卡图");
		Validate(image2, "普通完整卡框");
		using Bitmap bitmap = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.DrawImageUnscaled(image, 0, 0);
			graphics.CompositingMode = CompositingMode.SourceOver;
			graphics.DrawImageUnscaled(image2, 0, 0);
		}
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static Bitmap BitmapFrom(byte[] data)
	{
		using MemoryStream stream = new MemoryStream(data);
		using Image original = Image.FromStream(stream);
		return new Bitmap(original);
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
