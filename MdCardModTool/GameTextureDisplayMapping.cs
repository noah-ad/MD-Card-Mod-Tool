using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public enum TextureDisplayMappingKind
{
	Native,
	CardSleeve,
	PendulumCardArt
}

/// <summary>
/// Separates the dimensions users compose and preview from the dimensions
/// Master Duel stores in Texture2D. The game applies its own UV mapping to
/// sleeves and Pendulum art, so writing a normally proportioned image directly
/// to the native canvas makes the final in-game result look distorted.
/// </summary>
public sealed record GameTextureDisplayMapping(
	TextureDisplayMappingKind Kind,
	int StorageWidth,
	int StorageHeight,
	int DisplayWidth,
	int DisplayHeight)
{
	public const int CardDisplayWidth = 704;
	public const int CardDisplayHeight = 1024;
	public const int PendulumDisplayWidth = 512;
	public const int PendulumDisplayHeight = 683;
	public const int TallStorageWidth = 512;
	public const int TallStorageHeight = 1024;

	public bool RequiresMapping => StorageWidth != DisplayWidth || StorageHeight != DisplayHeight;

	public string KindLabel => Kind switch
	{
		TextureDisplayMappingKind.CardSleeve => "卡套 UV 映射",
		TextureDisplayMappingKind.PendulumCardArt => "灵摆卡图 UV 映射",
		_ => "原生尺寸"
	};

	public string EditorSummary => RequiresMapping
		? $"正常预览 {DisplayWidth}×{DisplayHeight} / 游戏存储 {StorageWidth}×{StorageHeight}"
		: $"预览与游戏存储均为 {StorageWidth}×{StorageHeight}";

	public static GameTextureDisplayMapping Resolve(TexRef texture, string? preferredFrameKey = null)
	{
		ArgumentNullException.ThrowIfNull(texture);
		return ResolveCanvas(texture, texture.Width, texture.Height, preferredFrameKey);
	}

	/// <summary>
	/// Resolves an image canvas using the card/resource metadata from
	/// <paramref name="texture"/>. This is also used for old editable drafts:
	/// after an over-frame mod changes the live Texture2D to 704×1024, its saved
	/// source can still be the former 512×1024 Pendulum storage canvas.
	/// </summary>
	public static GameTextureDisplayMapping ResolveCanvas(TexRef texture, int width, int height,
		string? preferredFrameKey = null)
	{
		ArgumentNullException.ThrowIfNull(texture);
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(texture), "Texture2D 尺寸必须大于 0。");
		}

		if (IsTallStorage(width, height) && IsCardSleeve(texture))
		{
			int displayHeight = height;
			int displayWidth = Math.Max(1, (int)Math.Round(displayHeight
				* (CardDisplayWidth / (double)CardDisplayHeight)));
			return new GameTextureDisplayMapping(TextureDisplayMappingKind.CardSleeve,
				width, height, displayWidth, displayHeight);
		}

		if (IsTallStorage(width, height) && IsPendulumCardArt(texture, preferredFrameKey))
		{
			int displayWidth = width;
			int displayHeight = Math.Max(1, (int)Math.Round(displayWidth * 4d / 3d));
			return new GameTextureDisplayMapping(TextureDisplayMappingKind.PendulumCardArt,
				width, height, displayWidth, displayHeight);
		}

		return new GameTextureDisplayMapping(TextureDisplayMappingKind.Native,
			width, height, width, height);
	}

	/// <summary>
	/// Restores the full native Texture2D canvas to its normal editing/display
	/// proportions. No part of the tall canvas is cropped.
	/// </summary>
	public byte[] DecodeForDisplay(byte[] storedPng)
	{
		return ResizeFullCanvas(storedPng, StorageWidth, StorageHeight,
			DisplayWidth, DisplayHeight, "游戏存储纹理");
	}

	/// <summary>
	/// Applies the inverse distortion expected by the game's UV mapping. The
	/// returned PNG always has the original Texture2D dimensions.
	/// </summary>
	public byte[] EncodeForStorage(byte[] displayPng)
	{
		return ResizeFullCanvas(displayPng, DisplayWidth, DisplayHeight,
			StorageWidth, StorageHeight, "正常比例预览图");
	}

	private static byte[] ResizeFullCanvas(byte[] png, int expectedWidth, int expectedHeight,
		int outputWidth, int outputHeight, string role)
	{
		ArgumentNullException.ThrowIfNull(png);
		using Image<Rgba32> image = Image.Load<Rgba32>(png);
		if (image.Width != expectedWidth || image.Height != expectedHeight)
		{
			throw new InvalidDataException($"{role}尺寸应为 {expectedWidth}×{expectedHeight}，实际为 {image.Width}×{image.Height}。");
		}

		if (image.Width == outputWidth && image.Height == outputHeight)
		{
			return png.ToArray();
		}

		image.Mutate(context => context.Resize(new ResizeOptions
		{
			Size = new Size(outputWidth, outputHeight),
			Mode = ResizeMode.Stretch,
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

	private static bool IsTallStorage(int width, int height)
	{
		if (width <= 0 || height <= width)
		{
			return false;
		}
		double aspect = width / (double)height;
		return Math.Abs(aspect - 0.5d) <= 0.02d;
	}

	private static bool IsCardSleeve(TexRef texture)
	{
		return texture.Category.Contains("卡套", StringComparison.OrdinalIgnoreCase)
			|| texture.Category.Contains("卡背", StringComparison.OrdinalIgnoreCase)
			|| texture.Category.Contains("sleeve", StringComparison.OrdinalIgnoreCase)
			|| texture.Name.StartsWith("ProtectorIcon", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPendulumCardArt(TexRef texture, string? preferredFrameKey)
	{
		if (!texture.SourceKind.Equals("本地卡图", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return texture.Category.Contains("灵摆", StringComparison.OrdinalIgnoreCase)
			|| texture.Category.Contains("靈擺", StringComparison.OrdinalIgnoreCase)
			|| (!string.IsNullOrWhiteSpace(preferredFrameKey)
				&& CardFrameCatalog.IsPendulum(preferredFrameKey));
	}
}
