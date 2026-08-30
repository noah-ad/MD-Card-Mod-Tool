using System;
using System.Drawing;
using System.Drawing.Imaging;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public static class RgbaBitmap
{
	public static Bitmap FromPng(ReadOnlySpan<byte> png)
	{
		using SixLabors.ImageSharp.Image<Rgba32> source = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
		BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		try
		{
			unsafe
			{
				byte* destination = (byte*)locked.Scan0;
				source.ProcessPixelRows(accessor =>
				{
					for (int y = 0; y < accessor.Height; y++)
					{
						Span<Rgba32> row = accessor.GetRowSpan(y);
						byte* output = destination + y * locked.Stride;
						for (int x = 0; x < row.Length; x++)
						{
							Rgba32 pixel = row[x];
							output[x * 4] = pixel.B;
							output[x * 4 + 1] = pixel.G;
							output[x * 4 + 2] = pixel.R;
							output[x * 4 + 3] = pixel.A;
						}
					}
				});
			}
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
