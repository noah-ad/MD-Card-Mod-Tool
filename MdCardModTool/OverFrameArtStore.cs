using System.IO;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public static class OverFrameArtStore
{
	public static string CardFolder(string gameRoot, ushort cardId)
	{
		return Path.Combine(gameRoot, "_MD卡图素材", "超框", cardId.ToString());
	}

	public static string ArtPath(string gameRoot, ushort cardId)
	{
		return Path.Combine(CardFolder(gameRoot, cardId), "透明原画.png");
	}

	public static string SourcePath(string gameRoot, ushort cardId)
	{
		return Path.Combine(CardFolder(gameRoot, cardId), "卡图源.png");
	}

	public static string BackgroundPath(string gameRoot, ushort cardId)
	{
		return Path.Combine(CardFolder(gameRoot, cardId), "叠底背景.png");
	}

	public static string CustomFramePath(string gameRoot, ushort cardId)
	{
		return Path.Combine(CardFolder(gameRoot, cardId), "自定义卡框.png");
	}

	private static string SettingsPath(string gameRoot, ushort cardId)
	{
		return Path.Combine(CardFolder(gameRoot, cardId), "卡框设置.json");
	}

	public static bool HasSettings(string gameRoot, ushort cardId)
	{
		return File.Exists(SettingsPath(gameRoot, cardId));
	}

	public static void SaveArt(string gameRoot, ushort cardId, string imagePath)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
		Validate(image.Width, image.Height, "透明高图");
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		image.SaveAsPng(ArtPath(gameRoot, cardId));
	}

	public static void SaveArt(string gameRoot, ushort cardId, byte[] png)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(png);
		Validate(image.Width, image.Height, "透明高图");
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		image.SaveAsPng(ArtPath(gameRoot, cardId));
	}

	public static void SaveSource(string gameRoot, ushort cardId, string imagePath)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
		image.Mutate(context => context.AutoOrient());
		SaveSourceImage(gameRoot, cardId, image);
	}

	public static void SaveSource(string gameRoot, ushort cardId, byte[] imageBytes)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imageBytes);
		image.Mutate(context => context.AutoOrient());
		SaveSourceImage(gameRoot, cardId, image);
	}

	public static void SaveBackground(string gameRoot, ushort cardId, string imagePath)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
		image.Mutate(context => context.AutoOrient());
		SaveBackgroundImage(gameRoot, cardId, image);
	}

	public static void SaveBackground(string gameRoot, ushort cardId, byte[] png)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(png);
		image.Mutate(context => context.AutoOrient());
		SaveBackgroundImage(gameRoot, cardId, image);
	}

	public static void DeleteBackground(string gameRoot, ushort cardId)
	{
		string path = BackgroundPath(gameRoot, cardId);
		if (File.Exists(path)) File.Delete(path);
	}

	public static string SaveCustomFrame(string gameRoot, ushort cardId, string imagePath)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
		Validate(image.Width, image.Height, "自定义卡框");
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		string target = CustomFramePath(gameRoot, cardId);
		image.SaveAsPng(target);
		return target;
	}

	public static OverFrameFrameSettings ReadSettings(string gameRoot, ushort cardId)
	{
		try
		{
			string path = SettingsPath(gameRoot, cardId);
			return File.Exists(path) ? (JsonSerializer.Deserialize<OverFrameFrameSettings>(File.ReadAllText(path)) ?? new OverFrameFrameSettings()) : new OverFrameFrameSettings();
		}
		catch
		{
			return new OverFrameFrameSettings();
		}
	}

	public static void SaveSettings(string gameRoot, ushort cardId, OverFrameFrameSettings settings)
	{
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		File.WriteAllText(SettingsPath(gameRoot, cardId), JsonSerializer.Serialize(settings, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static void Validate(int width, int height, string label)
	{
		if (width != 704 || height != 1024)
		{
			throw new InvalidDataException($"{label}必须严格为 {704}×{1024}；当前为 {width}×{height}。");
		}
	}

	private static void SaveBackgroundImage(string gameRoot, ushort cardId, Image<Rgba32> image)
	{
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		image.Save(BackgroundPath(gameRoot, cardId), new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve
		});
	}

	private static void SaveSourceImage(string gameRoot, ushort cardId, Image<Rgba32> image)
	{
		if (image.Width <= 0 || image.Height <= 0)
		{
			throw new InvalidDataException("卡图源尺寸无效。");
		}
		Directory.CreateDirectory(CardFolder(gameRoot, cardId));
		image.Save(SourcePath(gameRoot, cardId), new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve
		});
	}
}
