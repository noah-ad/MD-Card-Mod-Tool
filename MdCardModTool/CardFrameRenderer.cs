using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MdCardModTool;

public static class CardFrameRenderer
{
	public static RectangleF FindArtWindow(Bitmap frame)
	{
		if (frame.Width != 704 || frame.Height != 1024)
		{
			throw new InvalidDataException($"卡框必须为 {704}×{1024}。当前为 {frame.Width}×{frame.Height}。");
		}
		using Bitmap argb = ((frame.PixelFormat == PixelFormat.Format32bppArgb) ? null : new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb));
		Bitmap source = argb ?? frame;
		if (argb != null)
		{
			using Graphics graphics = Graphics.FromImage(argb);
			graphics.DrawImageUnscaled(frame, 0, 0);
		}
		BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			int stride = Math.Abs(data.Stride);
			byte[] bytes = new byte[stride * source.Height];
			Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
			int left = source.Width;
			int top = source.Height;
			int right = -1;
			int bottom = -1;
			for (int y = 0; y < source.Height; y++)
			{
				int row = ((data.Stride >= 0) ? (y * stride) : ((source.Height - 1 - y) * stride));
				for (int x = 0; x < source.Width; x++)
				{
					if (bytes[row + x * 4 + 3] <= 128)
					{
						left = Math.Min(left, x);
						top = Math.Min(top, y);
						right = Math.Max(right, x);
						bottom = Math.Max(bottom, y);
					}
				}
			}
			if (right < left || bottom < top)
			{
				throw new InvalidDataException("卡框中没有找到透明插图区。");
			}
			return RectangleF.FromLTRB(left, top, right + 1, bottom + 1);
		}
		finally
		{
			source.UnlockBits(data);
		}
	}

	public static byte[] ComposeStoredArtPreview(byte[] storedArtPng, byte[] framePng)
	{
		using Bitmap art = FrameComposer.BitmapFrom(storedArtPng);
		using Bitmap frame = FrameComposer.BitmapFrom(framePng);
		using Bitmap output = ComposeStoredArtPreview(art, frame);
		using MemoryStream stream = new MemoryStream();
		output.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	public static Bitmap ComposeStoredArtPreview(Bitmap storedArt, Bitmap frame)
	{
		RectangleF artWindow = FindArtWindow(frame);
		Bitmap output = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(output);
		Configure(graphics);
		graphics.Clear(Color.White);
		Rectangle source = ((storedArt.Width == 512 && storedArt.Height == 1024) ? new Rectangle(0, 0, storedArt.Width, 596) : new Rectangle(0, 0, storedArt.Width, storedArt.Height));
		graphics.DrawImage(storedArt, artWindow, source, GraphicsUnit.Pixel);
		graphics.DrawImageUnscaled(frame, 0, 0);
		return output;
	}

	internal static void Configure(Graphics graphics)
	{
		graphics.CompositingMode = CompositingMode.SourceOver;
		graphics.CompositingQuality = CompositingQuality.HighQuality;
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
	}
}
