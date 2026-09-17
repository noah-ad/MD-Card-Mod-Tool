using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public static class AstellarOverFrameComposer
{
	private static readonly string[] BottomToTop = new string[6] { "BackGround", "EffBox", "ArtFrame", "EffFrame", "NameBox", "PeriFrame" };

	private static readonly HashSet<string> TransparentEdgeLayers = new HashSet<string> { "PeriFrame", "ArtFrame", "EffFrame" };

	// Experimental per-art foil coverage, not ordinary image transparency.
	// Do not touch RGB, the outer frame, or pixels outside the two inner-frame layers.
	public static byte[] RemoveFoilInnerFrame(byte[] png, AstellarOverFrameTemplate template)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(png);
		Validate(image, "闪面遮罩");
		var pixels = new Rgba32[image.Width * image.Height];
		image.CopyPixelDataTo(pixels);
		using Image<Rgba32> effectBox = Image.Load<Rgba32>(template.Layers["EffBox"]);
		Validate(effectBox, "EffBox");
		var box = new Rgba32[pixels.Length];
		effectBox.CopyPixelDataTo(box);
		foreach (string key in new[] { "ArtFrame", "EffFrame" })
		{
			if (!template.Layers.TryGetValue(key, out var bytes)) throw new InvalidDataException("缺少内框遮罩：" + key);
			using Image<Rgba32> layer = Image.Load<Rgba32>(bytes);
			Validate(layer, key);
			var mask = new Rgba32[pixels.Length];
			layer.CopyPixelDataTo(mask);
			for (int i = 0; i < pixels.Length; i++)
				if (mask[i].A > 0 && box[i].A == 0) pixels[i].A = 4;
		}
		return Encode(pixels);
	}

	public static AstellarOverFrameComposition Compose(byte[] transparentArtPng, AstellarOverFrameTemplate template, byte[]? backgroundPng = null)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(transparentArtPng);
		Validate(image, "透明高图");
		int num = 720896;
		Rgba32[] array = new Rgba32[num];
		image.CopyPixelDataTo(array);
		Rgba32[] array2 = CreateUnderlay(num, backgroundPng);
		Dictionary<string, bool[]> dictionary = new Dictionary<string, bool[]>(StringComparer.Ordinal);
		string[] bottomToTop = BottomToTop;
		foreach (string text in bottomToTop)
		{
			if (!template.Layers.TryGetValue(text, out byte[] value))
			{
				throw new InvalidDataException($"透明边缘模板 {template.Key} 缺少 {text} 图层。");
			}
			using Image<Rgba32> image2 = Image.Load<Rgba32>(value);
			Validate(image2, text);
			Rgba32[] array3 = new Rgba32[num];
			image2.CopyPixelDataTo(array3);
			if (TransparentEdgeLayers.Contains(text) || text == "EffBox")
			{
				bool[] array4 = new bool[num];
				CollectAlphaMask(array3, array4);
				dictionary[text] = array4;
			}
			AlphaComposite(array2, array3);
		}
		AlphaComposite(array2, array);
		bool[] edgeMask = CombineTransparentEdgeMasks(dictionary);
		int transparentEdgePixels = ClearAlphaPreservingRgb(array2, edgeMask);
		byte[] array5 = Encode(array2);
		return new AstellarOverFrameComposition(array5, (byte[])array5.Clone(), transparentEdgePixels);
	}

	public static byte[] CreateTransparentGradientFrame(byte[] gradientFramePng, byte[] transparentFramePng)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(gradientFramePng);
		using Image<Rgba32> image2 = Image.Load<Rgba32>(transparentFramePng);
		Validate(image, "炫彩卡框");
		Validate(image2, "透明卡框");
		int num = 720896;
		Rgba32[] array = new Rgba32[num];
		Rgba32[] array2 = new Rgba32[num];
		image.CopyPixelDataTo(array);
		image2.CopyPixelDataTo(array2);
		for (int i = 0; i < num; i++)
		{
			Rgba32 rgba = array[i];
			rgba.A = Math.Min(rgba.A, array2[i].A);
			array[i] = rgba;
		}
		return Encode(array, PngCompressionLevel.Level1);
	}

	public static AstellarOverFrameComposition ComposeTransparentFlatFrame(byte[] artPng, byte[] framePng, AstellarOverFrameTemplate template, byte[]? backgroundPng = null)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(ComposeFlatFrame(artPng, framePng, backgroundPng));
		Rgba32[] array = new Rgba32[720896];
		image.CopyPixelDataTo(array);
		int transparentEdgePixels = ClearAlphaPreservingRgb(array, CreateTransparentEdgeMask(template));
		byte[] array2 = Encode(array);
		return new AstellarOverFrameComposition(array2, (byte[])array2.Clone(), transparentEdgePixels);
	}

	public static byte[] ComposeFlatFrame(byte[] artPng, byte[] framePng, byte[]? backgroundPng = null)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(artPng);
		using Image<Rgba32> image2 = Image.Load<Rgba32>(framePng);
		Validate(image, "卡图");
		Validate(image2, "卡框");
		Rgba32[] array = new Rgba32[720896];
		Rgba32[] array2 = new Rgba32[720896];
		image.CopyPixelDataTo(array);
		image2.CopyPixelDataTo(array2);
		Rgba32[] array3 = CreateUnderlay(720896, backgroundPng);
		AlphaComposite(array3, array2);
		AlphaComposite(array3, array);
		return Encode(array3);
	}

	public static byte[] CreateVisibleFramePreview(byte[] framePng)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(framePng);
		Validate(image, "卡框");
		Rgba32[] array = new Rgba32[720896];
		image.CopyPixelDataTo(array);
		return Encode(CreateVisibleRgbProjection(array));
	}

	public static byte[] CreateVisibleRgbPreview(byte[] texturePng)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(texturePng);
		Rgba32[] array = new Rgba32[image.Width * image.Height];
		image.CopyPixelDataTo(array);
		return Encode(CreateVisibleRgbProjection(array), image.Width, image.Height);
	}

	private static Rgba32[] CreateVisibleRgbProjection(Rgba32[] source)
	{
		Rgba32[] array = (Rgba32[])source.Clone();
		for (int i = 0; i < array.Length; i++)
		{
			Rgba32 rgba = array[i];
			if (rgba.A == 0 && (rgba.R != 0 || rgba.G != 0 || rgba.B != 0))
			{
				rgba.A = byte.MaxValue;
				array[i] = rgba;
			}
		}
		return array;
	}

	private static bool[] CreateTransparentEdgeMask(AstellarOverFrameTemplate template)
	{
		Dictionary<string, bool[]> dictionary = new Dictionary<string, bool[]>(StringComparer.Ordinal);
		foreach (string transparentEdgeLayer in TransparentEdgeLayers)
		{
			if (!template.Layers.TryGetValue(transparentEdgeLayer, out byte[] value))
			{
				throw new InvalidDataException($"透明边缘模板 {template.Key} 缺少 {transparentEdgeLayer} 图层。");
			}
			using Image<Rgba32> image = Image.Load<Rgba32>(value);
			Validate(image, transparentEdgeLayer);
			Rgba32[] array = new Rgba32[720896];
			image.CopyPixelDataTo(array);
			bool[] array2 = new bool[array.Length];
			CollectAlphaMask(array, array2);
			dictionary[transparentEdgeLayer] = array2;
		}
		if (!template.Layers.TryGetValue("EffBox", out byte[] value2))
		{
			throw new InvalidDataException("透明边缘模板 " + template.Key + " 缺少 EffBox 图层。");
		}
		using (Image<Rgba32> image2 = Image.Load<Rgba32>(value2))
		{
			Validate(image2, "EffBox");
			Rgba32[] array3 = new Rgba32[720896];
			image2.CopyPixelDataTo(array3);
			bool[] array4 = new bool[array3.Length];
			CollectAlphaMask(array3, array4);
			dictionary["EffBox"] = array4;
		}
		return CombineTransparentEdgeMasks(dictionary);
	}

	private static bool[] CombineTransparentEdgeMasks(IReadOnlyDictionary<string, bool[]> layerMasks)
	{
		int num = 720896;
		bool[] array = new bool[num];
		layerMasks.TryGetValue("EffBox", out bool[] value);
		foreach (string transparentEdgeLayer in TransparentEdgeLayers)
		{
			if (!layerMasks.TryGetValue(transparentEdgeLayer, out bool[] value2))
			{
				continue;
			}
			for (int i = 0; i < num; i++)
			{
				if (value2[i] && (transparentEdgeLayer == "PeriFrame" || value == null || !value[i]))
				{
					array[i] = true;
				}
			}
		}
		return array;
	}

	private static int ClearAlphaPreservingRgb(Rgba32[] pixels, bool[] edgeMask)
	{
		int num = 0;
		for (int i = 0; i < pixels.Length; i++)
		{
			if (edgeMask[i])
			{
				Rgba32 rgba = pixels[i];
				bool num2 = rgba.R != 0 || rgba.G != 0 || rgba.B != 0;
				rgba.A = 0;
				pixels[i] = rgba;
				if (num2)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static Rgba32[] CreateUnderlay(int pixelCount, byte[]? backgroundPng)
	{
		Rgba32[] array = new Rgba32[pixelCount];
		if (backgroundPng == null)
		{
			return array;
		}
		using Image<Rgba32> image = Image.Load<Rgba32>(backgroundPng);
		Validate(image, "叠底背景");
		image.CopyPixelDataTo(array);
		return array;
	}

	private static void AlphaComposite(Rgba32[] destination, Rgba32[] source)
	{
		if (destination.Length != source.Length)
		{
			throw new ArgumentException("图层像素数量不一致。", "source");
		}
		for (int i = 0; i < destination.Length; i++)
		{
			Rgba32 rgba = source[i];
			if (rgba.A == 0)
			{
				continue;
			}
			if (rgba.A == byte.MaxValue)
			{
				destination[i] = rgba;
				continue;
			}
			Rgba32 under = destination[i];
			int sourceAlpha = rgba.A;
			int inverse = 255 - sourceAlpha;
			int num = sourceAlpha + (under.A * inverse + 127) / 255;
			if (num == 0)
			{
				destination[i] = rgba;
				continue;
			}
			int denominator = num * 255;
			destination[i] = new Rgba32(Blend(rgba.R, under.R), Blend(rgba.G, under.G), Blend(rgba.B, under.B), (byte)num);
			byte Blend(byte top, byte bottom)
			{
				return (byte)Math.Clamp((top * sourceAlpha * 255 + bottom * under.A * inverse + denominator / 2) / denominator, 0, 255);
			}
		}
	}

	private static void CollectAlphaMask(Rgba32[] pixels, bool[] mask)
	{
		for (int i = 0; i < pixels.Length; i++)
		{
			if (pixels[i].A > 0)
			{
				mask[i] = true;
			}
		}
	}

	private static byte[] Encode(Rgba32[] pixels)
	{
		return Encode(pixels, 704, 1024);
	}

	private static byte[] Encode(Rgba32[] pixels, PngCompressionLevel compressionLevel)
	{
		return Encode(pixels, 704, 1024, compressionLevel);
	}

	private static byte[] Encode(Rgba32[] pixels, int width, int height, PngCompressionLevel compressionLevel = PngCompressionLevel.Level9)
	{
		using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(pixels, width, height);
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve,
			CompressionLevel = compressionLevel
		});
		return memoryStream.ToArray();
	}

	private static void Validate(Image image, string label)
	{
		if (image.Width != 704 || image.Height != 1024)
		{
			throw new InvalidDataException($"{label}必须严格为 {704}×{1024}；当前为 {image.Width}×{image.Height}。");
		}
	}
}
