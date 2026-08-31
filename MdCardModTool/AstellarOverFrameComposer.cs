using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed record AstellarOverFrameComposition(
	byte[] GamePng,
	byte[] PreviewPng,
	int TransparentEdgePixels);

/// <summary>
/// Independent implementation of Astellar's six-layer over-frame composition.
/// The output deliberately keeps RGB values below zero-alpha edge pixels; this
/// is data used by Master Duel's card shader and must not be flattened by GDI+.
/// </summary>
public static class AstellarOverFrameComposer
{
	private static readonly string[] BottomToTop =
	[
		"BackGround", "EffBox", "ArtFrame", "EffFrame", "NameBox", "PeriFrame"
	];

	private static readonly HashSet<string> TransparentEdgeLayers =
	[
		"PeriFrame", "ArtFrame", "EffFrame"
	];

	public static AstellarOverFrameComposition Compose(byte[] transparentArtPng,
		AstellarOverFrameTemplate template, byte[]? backgroundPng = null)
	{
		using Image<Rgba32> art = Image.Load<Rgba32>(transparentArtPng);
		Validate(art, "透明高图");
		int pixelCount = FrameComposer.Width * FrameComposer.Height;
		Rgba32[] artPixels = new Rgba32[pixelCount];
		art.CopyPixelDataTo(artPixels);
		// A true over-frame stack is background -> frame chrome -> transparent subject.
		// Keeping the subject last is what lets opaque subject pixels cross the frame.
		Rgba32[] output = CreateUnderlay(pixelCount, backgroundPng);
		Dictionary<string, bool[]> layerMasks = new(StringComparer.Ordinal);

		foreach (string name in BottomToTop)
		{
			if (!template.Layers.TryGetValue(name, out byte[]? bytes))
			{
				throw new InvalidDataException($"透明边缘模板 {template.Key} 缺少 {name} 图层。");
			}
			using Image<Rgba32> layer = Image.Load<Rgba32>(bytes);
			Validate(layer, name);
			Rgba32[] layerPixels = new Rgba32[pixelCount];
			layer.CopyPixelDataTo(layerPixels);
			if (TransparentEdgeLayers.Contains(name) || name == "EffBox")
			{
				bool[] layerMask = new bool[pixelCount];
				CollectAlphaMask(layerPixels, layerMask);
				layerMasks[name] = layerMask;
			}
			AlphaComposite(output, layerPixels);
		}
		AlphaComposite(output, artPixels);

		bool[] edgeMask = CombineTransparentEdgeMasks(layerMasks);
		int transparentPixels = ClearAlphaPreservingRgb(output, edgeMask);
		byte[] gamePng = Encode(output);

		// The default preview must show the real Alpha result. Hidden RGB remains
		// available through CreateVisibleRgbPreview as an explicit diagnostic only.
		return new AstellarOverFrameComposition(gamePng, (byte[])gamePng.Clone(),
			transparentPixels);
	}

	/// <summary>
	/// Uses Floowan's iridescent RGB together with Astellar's more open Alpha
	/// geometry. This is the standalone frame shown in the transparent-iridescent
	/// frame category.
	/// </summary>
	public static byte[] CreateTransparentGradientFrame(byte[] gradientFramePng,
		byte[] transparentFramePng)
	{
		using Image<Rgba32> gradient = Image.Load<Rgba32>(gradientFramePng);
		using Image<Rgba32> transparent = Image.Load<Rgba32>(transparentFramePng);
		Validate(gradient, "炫彩卡框");
		Validate(transparent, "透明卡框");
		int pixelCount = FrameComposer.Width * FrameComposer.Height;
		Rgba32[] gradientPixels = new Rgba32[pixelCount];
		Rgba32[] transparentPixels = new Rgba32[pixelCount];
		gradient.CopyPixelDataTo(gradientPixels);
		transparent.CopyPixelDataTo(transparentPixels);
		for (int index = 0; index < pixelCount; index++)
		{
			Rgba32 color = gradientPixels[index];
			color.A = Math.Min(color.A, transparentPixels[index].A);
			gradientPixels[index] = color;
		}
		// This PNG is a read-only UI resource generated on demand. Favor responsive
		// category switching; final game output still uses BestCompression below.
		return Encode(gradientPixels, PngCompressionLevel.BestSpeed);
	}

	/// <summary>
	/// Composes an iridescent frame and then applies the same Astellar Dirty Alpha
	/// geometry used by the transparent frame mode. The result is genuinely
	/// transparent while retaining the gradient RGB below zero Alpha.
	/// </summary>
	public static AstellarOverFrameComposition ComposeTransparentFlatFrame(byte[] artPng,
		byte[] framePng, AstellarOverFrameTemplate template, byte[]? backgroundPng = null)
	{
		byte[] flatPng = ComposeFlatFrame(artPng, framePng, backgroundPng);
		using Image<Rgba32> flat = Image.Load<Rgba32>(flatPng);
		Rgba32[] pixels = new Rgba32[FrameComposer.Width * FrameComposer.Height];
		flat.CopyPixelDataTo(pixels);
		int transparentPixels = ClearAlphaPreservingRgb(pixels,
			CreateTransparentEdgeMask(template));
		byte[] gamePng = Encode(pixels);
		return new AstellarOverFrameComposition(gamePng, (byte[])gamePng.Clone(),
			transparentPixels);
	}

	/// <summary>
	/// Composes the shared background -> frame -> transparent subject pipeline used by
	/// Floowan gradient frames and complete ordinary card frames in the unified OF editor.
	/// </summary>
	public static byte[] ComposeFlatFrame(byte[] artPng, byte[] framePng,
		byte[]? backgroundPng = null)
	{
		using Image<Rgba32> art = Image.Load<Rgba32>(artPng);
		using Image<Rgba32> frame = Image.Load<Rgba32>(framePng);
		Validate(art, "卡图");
		Validate(frame, "卡框");
		int pixelCount = FrameComposer.Width * FrameComposer.Height;
		Rgba32[] artPixels = new Rgba32[pixelCount];
		Rgba32[] framePixels = new Rgba32[pixelCount];
		art.CopyPixelDataTo(artPixels);
		frame.CopyPixelDataTo(framePixels);
		Rgba32[] output = CreateUnderlay(pixelCount, backgroundPng);
		AlphaComposite(output, framePixels);
		AlphaComposite(output, artPixels);
		return Encode(output);
	}

	public static byte[] CreateVisibleFramePreview(byte[] framePng)
	{
		using Image<Rgba32> frame = Image.Load<Rgba32>(framePng);
		Validate(frame, "卡框");
		Rgba32[] pixels = new Rgba32[FrameComposer.Width * FrameComposer.Height];
		frame.CopyPixelDataTo(pixels);
		return Encode(CreateVisibleRgbProjection(pixels));
	}

	/// <summary>
	/// Creates a diagnostic RGB projection used to inspect Master Duel's hidden
	/// transparent color data:
	/// zero-alpha pixels that still carry RGB are made visible for display only.
	/// The returned PNG is never a default preview and is never written back to
	/// Texture2D; the original alpha remains authoritative.
	/// </summary>
	public static byte[] CreateVisibleRgbPreview(byte[] texturePng)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(texturePng);
		Rgba32[] pixels = new Rgba32[image.Width * image.Height];
		image.CopyPixelDataTo(pixels);
		return Encode(CreateVisibleRgbProjection(pixels), image.Width, image.Height);
	}

	private static Rgba32[] CreateVisibleRgbProjection(Rgba32[] source)
	{
		Rgba32[] preview = (Rgba32[])source.Clone();
		for (int index = 0; index < preview.Length; index++)
		{
			Rgba32 pixel = preview[index];
			if (pixel.A != 0 || (pixel.R == 0 && pixel.G == 0 && pixel.B == 0)) continue;
			pixel.A = 255;
			preview[index] = pixel;
		}
		return preview;
	}

	private static bool[] CreateTransparentEdgeMask(AstellarOverFrameTemplate template)
	{
		Dictionary<string, bool[]> layerMasks = new(StringComparer.Ordinal);
		foreach (string name in TransparentEdgeLayers)
		{
			if (!template.Layers.TryGetValue(name, out byte[]? bytes))
			{
				throw new InvalidDataException($"透明边缘模板 {template.Key} 缺少 {name} 图层。");
			}
			using Image<Rgba32> layer = Image.Load<Rgba32>(bytes);
			Validate(layer, name);
			Rgba32[] pixels = new Rgba32[FrameComposer.Width * FrameComposer.Height];
			layer.CopyPixelDataTo(pixels);
			bool[] mask = new bool[pixels.Length];
			CollectAlphaMask(pixels, mask);
			layerMasks[name] = mask;
		}
		if (!template.Layers.TryGetValue("EffBox", out byte[]? effectBoxBytes))
		{
			throw new InvalidDataException($"透明边缘模板 {template.Key} 缺少 EffBox 图层。");
		}
		using (Image<Rgba32> effectBox = Image.Load<Rgba32>(effectBoxBytes))
		{
			Validate(effectBox, "EffBox");
			Rgba32[] pixels = new Rgba32[FrameComposer.Width * FrameComposer.Height];
			effectBox.CopyPixelDataTo(pixels);
			bool[] mask = new bool[pixels.Length];
			CollectAlphaMask(pixels, mask);
			layerMasks["EffBox"] = mask;
		}
		return CombineTransparentEdgeMasks(layerMasks);
	}

	private static bool[] CombineTransparentEdgeMasks(
		IReadOnlyDictionary<string, bool[]> layerMasks)
	{
		int pixelCount = FrameComposer.Width * FrameComposer.Height;
		bool[] edgeMask = new bool[pixelCount];
		layerMasks.TryGetValue("EffBox", out bool[]? effectBoxMask);
		foreach (string name in TransparentEdgeLayers)
		{
			if (!layerMasks.TryGetValue(name, out bool[]? layerMask)) continue;
			for (int index = 0; index < pixelCount; index++)
			{
				if (layerMask[index]
					&& (name == "PeriFrame" || effectBoxMask == null || !effectBoxMask[index]))
				{
					edgeMask[index] = true;
				}
			}
		}
		return edgeMask;
	}

	private static int ClearAlphaPreservingRgb(Rgba32[] pixels, bool[] edgeMask)
	{
		int transparentPixels = 0;
		for (int index = 0; index < pixels.Length; index++)
		{
			if (!edgeMask[index]) continue;
			Rgba32 pixel = pixels[index];
			bool carriesRgb = pixel.R != 0 || pixel.G != 0 || pixel.B != 0;
			pixel.A = 0;
			pixels[index] = pixel;
			// Fully black edge pixels still need alpha cleared, but they carry no
			// hidden RGB shader data and therefore do not inflate this diagnostic.
			if (carriesRgb) transparentPixels++;
		}
		return transparentPixels;
	}

	private static Rgba32[] CreateUnderlay(int pixelCount, byte[]? backgroundPng)
	{
		Rgba32[] output = new Rgba32[pixelCount];
		if (backgroundPng == null) return output;
		using Image<Rgba32> background = Image.Load<Rgba32>(backgroundPng);
		Validate(background, "叠底背景");
		background.CopyPixelDataTo(output);
		return output;
	}

	private static void AlphaComposite(Rgba32[] destination, Rgba32[] source)
	{
		if (destination.Length != source.Length) throw new ArgumentException("图层像素数量不一致。", nameof(source));
		for (int index = 0; index < destination.Length; index++)
		{
			Rgba32 over = source[index];
			if (over.A == 0) continue;
			if (over.A == 255)
			{
				destination[index] = over;
				continue;
			}
			Rgba32 under = destination[index];
			int sourceAlpha = over.A;
			int inverse = 255 - sourceAlpha;
			int outAlpha = sourceAlpha + (under.A * inverse + 127) / 255;
			if (outAlpha == 0)
			{
				destination[index] = over;
				continue;
			}
			int denominator = outAlpha * 255;
			byte Blend(byte top, byte bottom) => (byte)Math.Clamp(
				(top * sourceAlpha * 255 + bottom * under.A * inverse + denominator / 2) / denominator,
				0, 255);
			destination[index] = new Rgba32(
				Blend(over.R, under.R),
				Blend(over.G, under.G),
				Blend(over.B, under.B),
				(byte)outAlpha);
		}
	}

	private static void CollectAlphaMask(Rgba32[] pixels, bool[] mask)
	{
		for (int index = 0; index < pixels.Length; index++)
		{
			if (pixels[index].A > 0) mask[index] = true;
		}
	}

	private static byte[] Encode(Rgba32[] pixels)
	{
		return Encode(pixels, FrameComposer.Width, FrameComposer.Height);
	}

	private static byte[] Encode(Rgba32[] pixels, PngCompressionLevel compressionLevel)
	{
		return Encode(pixels, FrameComposer.Width, FrameComposer.Height, compressionLevel);
	}

	private static byte[] Encode(Rgba32[] pixels, int width, int height,
		PngCompressionLevel compressionLevel = PngCompressionLevel.BestCompression)
	{
		using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(pixels, width, height);
		using MemoryStream stream = new();
		image.Save(stream, new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			// Transparent RGB is shader data in Master Duel, not disposable color.
			// Keep this explicit even though current ImageSharp versions default to
			// Preserve; a future package/default change must not silently clear it.
			TransparentColorMode = PngTransparentColorMode.Preserve,
			CompressionLevel = compressionLevel
		});
		return stream.ToArray();
	}

	private static void Validate(Image image, string label)
	{
		if (image.Width != FrameComposer.Width || image.Height != FrameComposer.Height)
		{
			throw new InvalidDataException($"{label}必须严格为 {FrameComposer.Width}×{FrameComposer.Height}；当前为 {image.Width}×{image.Height}。");
		}
	}
}
