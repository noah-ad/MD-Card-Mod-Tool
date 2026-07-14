using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace MdCardModTool;

public sealed record OverFrameFrameSettings(string FrameKey = "card_frame01", bool UsesCustomFrame = false);

/// <summary>保存每张超框卡的透明原画与卡框选择，防止再次编辑时把旧卡框叠进新卡框。</summary>
public static class OverFrameArtStore
{
    public static string CardFolder(string gameRoot, ushort cardId) => Path.Combine(gameRoot, "_MD卡图素材", "超框", cardId.ToString());
    public static string ArtPath(string gameRoot, ushort cardId) => Path.Combine(CardFolder(gameRoot, cardId), "透明原画.png");
    public static string CustomFramePath(string gameRoot, ushort cardId) => Path.Combine(CardFolder(gameRoot, cardId), "自定义卡框.png");
    static string SettingsPath(string gameRoot, ushort cardId) => Path.Combine(CardFolder(gameRoot, cardId), "卡框设置.json");
    public static bool HasSettings(string gameRoot, ushort cardId) => File.Exists(SettingsPath(gameRoot, cardId));

    public static void SaveArt(string gameRoot, ushort cardId, string imagePath)
    {
        using var image = SharpImage.Load<Rgba32>(imagePath);
        Validate(image.Width, image.Height, "透明高图");
        Directory.CreateDirectory(CardFolder(gameRoot, cardId));
        image.SaveAsPng(ArtPath(gameRoot, cardId));
    }

    public static void SaveArt(string gameRoot, ushort cardId, byte[] png)
    {
        using var image = SharpImage.Load<Rgba32>(png);
        Validate(image.Width, image.Height, "透明高图");
        Directory.CreateDirectory(CardFolder(gameRoot, cardId));
        image.SaveAsPng(ArtPath(gameRoot, cardId));
    }

    public static string SaveCustomFrame(string gameRoot, ushort cardId, string imagePath)
    {
        using var image = SharpImage.Load<Rgba32>(imagePath);
        Validate(image.Width, image.Height, "自定义卡框");
        Directory.CreateDirectory(CardFolder(gameRoot, cardId));
        var target = CustomFramePath(gameRoot, cardId);
        image.SaveAsPng(target);
        return target;
    }

    public static OverFrameFrameSettings ReadSettings(string gameRoot, ushort cardId)
    {
        try
        {
            var path = SettingsPath(gameRoot, cardId);
            return File.Exists(path) ? JsonSerializer.Deserialize<OverFrameFrameSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch { return new(); }
    }

    public static void SaveSettings(string gameRoot, ushort cardId, OverFrameFrameSettings settings)
    {
        Directory.CreateDirectory(CardFolder(gameRoot, cardId));
        File.WriteAllText(SettingsPath(gameRoot, cardId), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void Validate(int width, int height, string label)
    {
        if (width != FrameComposer.Width || height != FrameComposer.Height)
            throw new InvalidDataException($"{label}必须严格为 {FrameComposer.Width}×{FrameComposer.Height}；当前为 {width}×{height}。");
    }
}
