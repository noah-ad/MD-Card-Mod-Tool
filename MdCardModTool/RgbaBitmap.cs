using System;
using System.Drawing;
using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public static class RgbaBitmap
{
	public unsafe static Bitmap FromPng(ReadOnlySpan<byte> png)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
		BitmapData locked = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte* destination = (byte*)locked.Scan0;
			image.ProcessPixelRows(delegate(PixelAccessor<Rgba32> accessor)
			{
				for (int i = 0; i < accessor.Height; i++)
				{
					Span<Rgba32> rowSpan = accessor.GetRowSpan(i);
					byte* ptr = destination + i * locked.Stride;
					for (int j = 0; j < rowSpan.Length; j++)
					{
						Rgba32 rgba = rowSpan[j];
						ptr[j * 4] = rgba.B;
						ptr[j * 4 + 1] = rgba.G;
						ptr[j * 4 + 2] = rgba.R;
						ptr[j * 4 + 3] = rgba.A;
					}
				}
			});
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}
		finally
		{
			bitmap.UnlockBits(locked);
		}
		return bitmap;
	}
}
