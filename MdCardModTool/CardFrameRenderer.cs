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
		using Bitmap bitmap = ((frame.PixelFormat == PixelFormat.Format32bppArgb) ? null : new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb));
		Bitmap source = bitmap ?? frame;
		if (bitmap != null)
		{
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.DrawImageUnscaled(frame, 0, 0);
		}
		BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			int stride = Math.Abs(data.Stride);
			byte[] bytes = new byte[stride * source.Height];
			Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
			int num = source.Width * source.Height;
			byte[] visited = new byte[num];
			int[] queue = new int[num];
			(int, int, int, int, int) tuple = default((int, int, int, int, int));
			int num2 = source.Width / 2;
			int num3 = source.Height / 2;
			if (IsTransparent(num2, num3))
			{
				tuple = FloodComponent(num2, num3);
				return RectangleF.FromLTRB(tuple.Item2, tuple.Item3, tuple.Item4 + 1, tuple.Item5 + 1);
			}
			for (int i = 0; i < source.Height; i++)
			{
				for (int j = 0; j < source.Width; j++)
				{
					int num4 = i * source.Width + j;
					if (visited[num4] == 0 && IsTransparent(j, i))
					{
						(int, int, int, int, int) tuple2 = FloodComponent(j, i);
						if (tuple2.Item1 > tuple.Item1)
						{
							tuple = tuple2;
						}
					}
				}
			}
			if (tuple.Item1 == 0)
			{
				throw new InvalidDataException("卡框中没有找到透明插图区。");
			}
			return RectangleF.FromLTRB(tuple.Item2, tuple.Item3, tuple.Item4 + 1, tuple.Item5 + 1);
			(int Area, int Left, int Top, int Right, int Bottom) FloodComponent(int startX, int startY)
			{
				int num5 = 0;
				int tail = 0;
				int num6 = startY * source.Width + startX;
				queue[tail++] = num6;
				visited[num6] = 1;
				int num7 = 0;
				int num8 = startX;
				int num9 = startY;
				int num10 = startX;
				int num11 = startY;
				while (num5 < tail)
				{
					int num12 = queue[num5++];
					int num13 = num12 / source.Width;
					int num14 = num12 - num13 * source.Width;
					num7++;
					num8 = Math.Min(num8, num14);
					num9 = Math.Min(num9, num13);
					num10 = Math.Max(num10, num14);
					num11 = Math.Max(num11, num13);
					TryVisit(num14 - 1, num13);
					TryVisit(num14 + 1, num13);
					TryVisit(num14, num13 - 1);
					TryVisit(num14, num13 + 1);
				}
				return (Area: num7, Left: num8, Top: num9, Right: num10, Bottom: num11);
				void TryVisit(int nextX, int nextY)
				{
					if ((uint)nextX < (uint)source.Width && (uint)nextY < (uint)source.Height)
					{
						int num15 = nextY * source.Width + nextX;
						if (visited[num15] == 0 && IsTransparent(nextX, nextY))
						{
							visited[num15] = 1;
							queue[tail++] = num15;
						}
					}
				}
			}
			bool IsTransparent(int x, int y)
			{
				int num5 = ((data.Stride >= 0) ? (y * stride) : ((source.Height - 1 - y) * stride));
				return bytes[num5 + x * 4 + 3] <= 128;
			}
		}
		finally
		{
			source.UnlockBits(data);
		}
	}

	public static byte[] ComposeStoredArtPreview(byte[] storedArtPng, byte[] framePng)
	{
		using Bitmap storedArt = FrameComposer.BitmapFrom(storedArtPng);
		using Bitmap frame = FrameComposer.BitmapFrom(framePng);
		using Bitmap bitmap = ComposeStoredArtPreview(storedArt, frame);
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static Bitmap ComposeStoredArtPreview(Bitmap storedArt, Bitmap frame)
	{
		RectangleF destRect = FindArtWindow(frame);
		Bitmap bitmap = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		Configure(graphics);
		graphics.Clear(Color.White);
		Rectangle rectangle = new Rectangle(0, 0, storedArt.Width, storedArt.Height);
		graphics.DrawImage(storedArt, destRect, rectangle, GraphicsUnit.Pixel);
		graphics.DrawImageUnscaled(frame, 0, 0);
		return bitmap;
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
