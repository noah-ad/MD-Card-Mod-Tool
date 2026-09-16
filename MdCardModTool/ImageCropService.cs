using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public static class ImageCropService
{
	public static Bitmap LoadPreview(string sourcePath)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(sourcePath);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.AutoOrient();
		});
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder());
		memoryStream.Position = 0L;
		using System.Drawing.Image original = System.Drawing.Image.FromStream(memoryStream);
		return new Bitmap(original);
	}

	public static byte[] RenderToTarget(string sourcePath, ImageRenderSpec spec, int targetWidth, int targetHeight)
	{
		if (spec.VisualWidth <= 0f || spec.VisualHeight <= 0f || spec.ImageScale <= 0f)
		{
			throw new ArgumentException("裁剪布局无效。", "spec");
		}
		using Bitmap source = LoadPreview(sourcePath);
		return RenderToTarget(source, spec, targetWidth, targetHeight);
	}

	public static byte[] RenderToTarget(Bitmap source, ImageRenderSpec spec, int targetWidth, int targetHeight)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		if (spec.VisualWidth <= 0f || spec.VisualHeight <= 0f || spec.ImageScale <= 0f)
		{
			throw new ArgumentException("裁剪布局无效。", "spec");
		}
		using Bitmap bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.Clear(System.Drawing.Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceOver;
			CardFrameRenderer.Configure(graphics);
			float num = (float)targetWidth / spec.VisualWidth;
			float num2 = (float)targetHeight / spec.VisualHeight;
			float num3 = (float)source.Width * spec.ImageScale;
			float num4 = (float)source.Height * spec.ImageScale;
			float num5 = spec.VisualWidth / 2f + spec.OffsetX - num3 / 2f;
			float num6 = spec.VisualHeight / 2f + spec.OffsetY - num4 / 2f;
			System.Drawing.RectangleF rect = new System.Drawing.RectangleF(num5 * num, num6 * num2, num3 * num, num4 * num2);
			graphics.DrawImage(source, rect);
		}
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static byte[] CropAndResize(string sourcePath, System.Drawing.RectangleF sourceCrop, int targetWidth, int targetHeight)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(sourcePath);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.AutoOrient();
		});
		double num = (double)targetWidth / (double)targetHeight;
		int cropWidth = Math.Clamp((int)Math.Round(sourceCrop.Width), 1, image.Width);
		int cropHeight = Math.Max(1, (int)Math.Round((double)cropWidth / num));
		if (cropHeight > image.Height)
		{
			cropHeight = image.Height;
			cropWidth = Math.Clamp((int)Math.Round((double)cropHeight * num), 1, image.Width);
		}
		float num2 = Math.Clamp(sourceCrop.Left + sourceCrop.Width / 2f, 0f, image.Width);
		float num3 = Math.Clamp(sourceCrop.Top + sourceCrop.Height / 2f, 0f, image.Height);
		int x = Math.Clamp((int)Math.Round(num2 - (float)cropWidth / 2f), 0, image.Width - cropWidth);
		int y = Math.Clamp((int)Math.Round(num3 - (float)cropHeight / 2f), 0, image.Height - cropHeight);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.Crop(new SixLabors.ImageSharp.Rectangle(x, y, cropWidth, cropHeight)).Resize(new ResizeOptions
			{
				Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight),
				Mode = ResizeMode.Stretch,
				Sampler = KnownResamplers.Lanczos3
			});
		});
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder());
		return memoryStream.ToArray();
	}

	public static byte[] RenderCoverToTarget(string sourcePath, int targetWidth, int targetHeight)
	{
		if (targetWidth <= 0 || targetHeight <= 0)
		{
			throw new ArgumentOutOfRangeException("targetWidth", "目标尺寸必须大于 0。");
		}
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(sourcePath);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.AutoOrient().Resize(new ResizeOptions
			{
				Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight),
				Mode = ResizeMode.Crop,
				Position = AnchorPositionMode.Center,
				Sampler = KnownResamplers.Lanczos3
			});
		});
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve
		});
		return memoryStream.ToArray();
	}
}
