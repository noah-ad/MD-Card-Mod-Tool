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
			// The transparent drop-shadow area around a frame is a separate alpha
			// island. Taking one bounding box around every transparent pixel mixed
			// that exterior island with the illustration opening and effectively
			// returned almost the whole 704×1024 card. Find connected alpha islands
			// and prefer the largest one containing the card centre.
			int pixelCount = source.Width * source.Height;
			byte[] visited = new byte[pixelCount];
			int[] queue = new int[pixelCount];
			(int Area, int Left, int Top, int Right, int Bottom) best = default;
			int centerX = source.Width / 2;
			int centerY = source.Height / 2;
			bool IsTransparent(int x, int y)
			{
				int row = data.Stride >= 0 ? y * stride : (source.Height - 1 - y) * stride;
				return bytes[row + x * 4 + 3] <= 128;
			}
			(int Area, int Left, int Top, int Right, int Bottom) FloodComponent(int startX, int startY)
			{
				int head = 0;
				int tail = 0;
				int start = startY * source.Width + startX;
				queue[tail++] = start;
				visited[start] = 1;
				int area = 0;
				int left = startX;
				int top = startY;
				int right = startX;
				int bottom = startY;
				while (head < tail)
				{
					int index = queue[head++];
					int y = index / source.Width;
					int x = index - y * source.Width;
					area++;
					left = Math.Min(left, x);
					top = Math.Min(top, y);
					right = Math.Max(right, x);
					bottom = Math.Max(bottom, y);
					TryVisit(x - 1, y);
					TryVisit(x + 1, y);
					TryVisit(x, y - 1);
					TryVisit(x, y + 1);
					void TryVisit(int nextX, int nextY)
					{
						if ((uint)nextX >= (uint)source.Width || (uint)nextY >= (uint)source.Height) return;
						int next = nextY * source.Width + nextX;
						if (visited[next] != 0 || !IsTransparent(nextX, nextY)) return;
						visited[next] = 1;
						queue[tail++] = next;
					}
				}
				return (area, left, top, right, bottom);
			}

			// Every official card illustration window contains the card centre. This
			// fast path avoids scanning unrelated transparent islands when switching
			// frames in the editor.
			if (IsTransparent(centerX, centerY))
			{
				best = FloodComponent(centerX, centerY);
				return RectangleF.FromLTRB(best.Left, best.Top, best.Right + 1, best.Bottom + 1);
			}

			for (int startY = 0; startY < source.Height; startY++)
			{
				for (int startX = 0; startX < source.Width; startX++)
				{
					int start = startY * source.Width + startX;
					if (visited[start] != 0 || !IsTransparent(startX, startY)) continue;
					var component = FloodComponent(startX, startY);
					if (component.Area > best.Area)
					{
						best = component;
					}
				}
			}
			if (best.Area == 0)
			{
				throw new InvalidDataException("卡框中没有找到透明插图区。");
			}
			return RectangleF.FromLTRB(best.Left, best.Top, best.Right + 1, best.Bottom + 1);
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
		// Pendulum Texture2D uses the complete tall canvas. Cropping the first
		// 596 rows discarded real image data and made the cropper disagree with
		// the in-game UV mapping. Draw the complete source into the card window.
		Rectangle source = new Rectangle(0, 0, storedArt.Width, storedArt.Height);
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
