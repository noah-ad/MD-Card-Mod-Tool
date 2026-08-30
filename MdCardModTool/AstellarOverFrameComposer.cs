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

		// Match Astellar's geometry sanitization: the effect-text box is not part of
		// the transparent edge even where ArtFrame/EffFrame overlap it.
		bool[] edgeMask = new bool[pixelCount];
		layerMasks.TryGetValue("EffBox", out bool[]? effectBoxMask);
		foreach (string name in TransparentEdgeLayers)
		{
			if (!layerMasks.TryGetValue(name, out bool[]? layerMask)) continue;
			for (int index = 0; index < pixelCount; index++)
			{
				if (layerMask[index] && (name == "PeriFrame" || effectBoxMask == null || !effectBoxMask[index]))
				{
					edgeMask[index] = true;
				}
			}
		}

		Rgba32[] preview = (Rgba32[])output.Clone();
		int transparentPixels = 0;
		for (int index = 0; index < output.Length; index++)
		{
			if (!edgeMask[index]) continue;
			Rgba32 pixel = output[index];
			bool carriesRgb = pixel.R != 0 || pixel.G != 0 || pixel.B != 0;
			pixel.A = 0;
			output[index] = pixel;
			Rgba32 visible = preview[index];
			visible.A = 255;
			preview[index] = visible;
			// Fully black edge pixels still need alpha cleared, but they carry no
			// hidden RGB shader data and therefore must not inflate this diagnostic.
			if (carriesRgb) transparentPixels++;
		}

		return new AstellarOverFrameComposition(
			Encode(output),
			Encode(preview),
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
		for (int index = 0; index < pixels.Length; index++)
		{
			Rgba32 pixel = pixels[index];
			if (pixel.A == 0 && (pixel.R != 0 || pixel.G != 0 || pixel.B != 0))
			{
				pixel.A = 255;
				pixels[index] = pixel;
			}
		}
		return Encode(pixels);
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
		using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(pixels, FrameComposer.Width, FrameComposer.Height);
		using MemoryStream stream = new();
		image.Save(stream, new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			// Transparent RGB is shader data in Master Duel, not disposable color.
			// Keep this explicit even though current ImageSharp versions default to
			// Preserve; a future package/default change must not silently clear it.
			TransparentColorMode = PngTransparentColorMode.Preserve,
			CompressionLevel = PngCompressionLevel.BestCompression
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
