using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public sealed record GameTextureDisplayMapping(TextureDisplayMappingKind Kind, int StorageWidth, int StorageHeight, int DisplayWidth, int DisplayHeight)
{
	public bool RequiresMapping
	{
		get
		{
			if (StorageWidth == DisplayWidth)
			{
				return StorageHeight != DisplayHeight;
			}
			return true;
		}
	}

	public string KindLabel => Kind switch
	{
		TextureDisplayMappingKind.CardSleeve => "卡套 UV 映射",
		TextureDisplayMappingKind.PendulumCardArt => "灵摆卡图 UV 映射",
		_ => "原生尺寸",
	};

	public string EditorSummary
	{
		get
		{
			if (RequiresMapping)
			{
				return $"正常预览 {DisplayWidth}×{DisplayHeight} / 游戏存储 {StorageWidth}×{StorageHeight}";
			}
			return $"预览与游戏存储均为 {StorageWidth}×{StorageHeight}";
		}
	}

	public const int CardDisplayWidth = 704;

	public const int CardDisplayHeight = 1024;

	public const int PendulumDisplayWidth = 512;

	public const int PendulumDisplayHeight = 683;

	public const int TallStorageWidth = 512;

	public const int TallStorageHeight = 1024;

	public static GameTextureDisplayMapping Resolve(TexRef texture, string? preferredFrameKey = null)
	{
		ArgumentNullException.ThrowIfNull(texture, "texture");
		return ResolveCanvas(texture, texture.Width, texture.Height, preferredFrameKey);
	}

	public static GameTextureDisplayMapping ResolveCanvas(TexRef texture, int width, int height, string? preferredFrameKey = null)
	{
		ArgumentNullException.ThrowIfNull(texture, "texture");
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException("texture", "Texture2D 尺寸必须大于 0。");
		}
		if (IsTallStorage(width, height) && IsCardSleeve(texture))
		{
			int displayWidth = Math.Max(1, (int)Math.Round((double)height * 0.6875));
			return new GameTextureDisplayMapping(TextureDisplayMappingKind.CardSleeve, width, height, displayWidth, height);
		}
		if (IsTallStorage(width, height) && IsPendulumCardArt(texture, preferredFrameKey))
		{
			int displayHeight = Math.Max(1, (int)Math.Round((double)width * 4.0 / 3.0));
			return new GameTextureDisplayMapping(TextureDisplayMappingKind.PendulumCardArt, width, height, width, displayHeight);
		}
		return new GameTextureDisplayMapping(TextureDisplayMappingKind.Native, width, height, width, height);
	}

	public byte[] DecodeForDisplay(byte[] storedPng)
	{
		return ResizeFullCanvas(storedPng, StorageWidth, StorageHeight, DisplayWidth, DisplayHeight, "游戏存储纹理");
	}

	public byte[] EncodeForStorage(byte[] displayPng)
	{
		return ResizeFullCanvas(displayPng, DisplayWidth, DisplayHeight, StorageWidth, StorageHeight, "正常比例预览图");
	}

	private static byte[] ResizeFullCanvas(byte[] png, int expectedWidth, int expectedHeight, int outputWidth, int outputHeight, string role)
	{
		ArgumentNullException.ThrowIfNull(png, "png");
		using Image<Rgba32> image = Image.Load<Rgba32>(png);
		if (image.Width != expectedWidth || image.Height != expectedHeight)
		{
			throw new InvalidDataException($"{role}尺寸应为 {expectedWidth}×{expectedHeight}，实际为 {image.Width}×{image.Height}。");
		}
		if (image.Width == outputWidth && image.Height == outputHeight)
		{
			return png.ToArray();
		}
		image.Mutate(delegate(IImageProcessingContext context)
		{
			context.Resize(new ResizeOptions
			{
				Size = new Size(outputWidth, outputHeight),
				Mode = ResizeMode.Stretch,
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

	private static bool IsTallStorage(int width, int height)
	{
		if (width <= 0 || height <= width)
		{
			return false;
		}
		return Math.Abs((double)width / (double)height - 0.5) <= 0.02;
	}

	private static bool IsCardSleeve(TexRef texture)
	{
		if (!texture.Category.Contains("卡套", StringComparison.OrdinalIgnoreCase) && !texture.Category.Contains("卡背", StringComparison.OrdinalIgnoreCase) && !texture.Category.Contains("sleeve", StringComparison.OrdinalIgnoreCase))
		{
			return texture.Name.StartsWith("ProtectorIcon", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsPendulumCardArt(TexRef texture, string? preferredFrameKey)
	{
		if (!texture.SourceKind.Equals("本地卡图", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!texture.Category.Contains("灵摆", StringComparison.OrdinalIgnoreCase) && !texture.Category.Contains("靈擺", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(preferredFrameKey))
			{
				return CardFrameCatalog.IsPendulum(preferredFrameKey);
			}
			return false;
		}
		return true;
	}

	[CompilerGenerated]
	private GameTextureDisplayMapping(GameTextureDisplayMapping original)
	{
		Kind = original.Kind;
		StorageWidth = original.StorageWidth;
		StorageHeight = original.StorageHeight;
		DisplayWidth = original.DisplayWidth;
		DisplayHeight = original.DisplayHeight;
	}
}
