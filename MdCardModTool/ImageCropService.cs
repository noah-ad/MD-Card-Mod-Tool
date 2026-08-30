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
		using MemoryStream stream = new MemoryStream();
		image.Save(stream, new PngEncoder());
		stream.Position = 0L;
		using System.Drawing.Image drawingImage = System.Drawing.Image.FromStream(stream);
		return new Bitmap(drawingImage);
	}

	public static byte[] RenderToTarget(string sourcePath, ImageRenderSpec spec, int targetWidth, int targetHeight, int? visibleTargetHeight = null)
	{
		if (spec.VisualWidth <= 0f || spec.VisualHeight <= 0f || spec.ImageScale <= 0f)
		{
			throw new ArgumentException("裁剪布局无效。", "spec");
		}
		int mappedHeight = visibleTargetHeight ?? targetHeight;
		if (mappedHeight <= 0 || mappedHeight > targetHeight)
		{
			throw new ArgumentOutOfRangeException("visibleTargetHeight", "显示区高度必须位于输出纹理范围内。");
		}
		using Bitmap source = LoadPreview(sourcePath);
		return RenderToTarget(source, spec, targetWidth, targetHeight, visibleTargetHeight);
	}

	public static byte[] RenderToTarget(Bitmap source, ImageRenderSpec spec, int targetWidth, int targetHeight, int? visibleTargetHeight = null)
	{
		ArgumentNullException.ThrowIfNull(source);
		if (spec.VisualWidth <= 0f || spec.VisualHeight <= 0f || spec.ImageScale <= 0f)
		{
			throw new ArgumentException("裁剪布局无效。", nameof(spec));
		}
		int mappedHeight = visibleTargetHeight ?? targetHeight;
		if (mappedHeight <= 0 || mappedHeight > targetHeight)
		{
			throw new ArgumentOutOfRangeException(nameof(visibleTargetHeight), "显示区高度必须位于输出纹理范围内。");
		}
		using Bitmap output = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(output))
		{
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.Clear(System.Drawing.Color.Transparent);
			graphics.CompositingMode = CompositingMode.SourceOver;
			CardFrameRenderer.Configure(graphics);
			float scaleX = (float)targetWidth / spec.VisualWidth;
			float scaleY = (float)mappedHeight / spec.VisualHeight;
			float visualWidth = (float)source.Width * spec.ImageScale;
			float visualHeight = (float)source.Height * spec.ImageScale;
			float left = spec.VisualWidth / 2f + spec.OffsetX - visualWidth / 2f;
			float top = spec.VisualHeight / 2f + spec.OffsetY - visualHeight / 2f;
			System.Drawing.RectangleF destination = new System.Drawing.RectangleF(left * scaleX, top * scaleY, visualWidth * scaleX, visualHeight * scaleY);
			graphics.DrawImage(source, destination);
		}
		using MemoryStream stream = new MemoryStream();
		output.Save(stream, ImageFormat.Png);
		return stream.ToArray();
	}

	public static byte[] CropAndResize(string sourcePath, System.Drawing.RectangleF sourceCrop, int targetWidth, int targetHeight)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(sourcePath);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.AutoOrient();
		});
		double aspect = (double)targetWidth / (double)targetHeight;
		int cropWidth = Math.Clamp((int)Math.Round(sourceCrop.Width), 1, image.Width);
		int cropHeight = Math.Max(1, (int)Math.Round((double)cropWidth / aspect));
		if (cropHeight > image.Height)
		{
			cropHeight = image.Height;
			cropWidth = Math.Clamp((int)Math.Round((double)cropHeight * aspect), 1, image.Width);
		}
		float centerX = Math.Clamp(sourceCrop.Left + sourceCrop.Width / 2f, 0f, image.Width);
		float centerY = Math.Clamp(sourceCrop.Top + sourceCrop.Height / 2f, 0f, image.Height);
		int x = Math.Clamp((int)Math.Round(centerX - (float)cropWidth / 2f), 0, image.Width - cropWidth);
		int y = Math.Clamp((int)Math.Round(centerY - (float)cropHeight / 2f), 0, image.Height - cropHeight);
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.Crop(new SixLabors.ImageSharp.Rectangle(x, y, cropWidth, cropHeight)).Resize(new ResizeOptions
			{
				Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight),
				Mode = ResizeMode.Stretch,
				Sampler = KnownResamplers.Lanczos3
			});
		});
		using MemoryStream output = new MemoryStream();
		image.Save(output, new PngEncoder());
		return output.ToArray();
	}

	public static byte[] RenderCoverToTarget(string sourcePath, int targetWidth, int targetHeight)
	{
		if (targetWidth <= 0 || targetHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(targetWidth), "目标尺寸必须大于 0。");
		}
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(sourcePath);
		image.Mutate(context => context.AutoOrient().Resize(new ResizeOptions
		{
			Size = new SixLabors.ImageSharp.Size(targetWidth, targetHeight),
			Mode = ResizeMode.Crop,
			Position = AnchorPositionMode.Center,
			Sampler = KnownResamplers.Lanczos3
		}));
		using MemoryStream output = new();
		image.Save(output, new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve
		});
		return output.ToArray();
	}
}
