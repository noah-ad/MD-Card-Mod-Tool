using SharpImage = SixLabors.ImageSharp.Image;

namespace MdCardModTool;

/// <summary>
/// card_frame 的防串位读取。旧 PathID 可能在游戏更新后仍可读取，但内容已是另一张贴图。
/// </summary>
public static class CardFrameResource
{
    public static byte[] DecodeVerified(ModEngine engine, TexRef texture)
    {
        Exception? firstFailure = null;
        try
        {
            var current = engine.DecodePng(texture);
            if (HasExpectedDimensions(current)) return current;
            firstFailure = new InvalidDataException(DescribeDimensions(current));
        }
        catch (Exception ex)
        {
            firstFailure = ex;
        }

        var resolved = engine.ResolveTextureReference(texture);
        if (resolved is null ||
            !string.Equals(resolved.Name, texture.Name, StringComparison.OrdinalIgnoreCase) ||
            resolved.Width != FrameComposer.Width ||
            resolved.Height != FrameComposer.Height)
            throw new InvalidDataException($"卡框 {texture.Name} 的旧索引已经失效，且无法在当前 data.unity3d 中按名称重新定位。", firstFailure);

        texture.PathId = resolved.PathId;
        texture.AssetFileName = resolved.AssetFileName;
        texture.Width = resolved.Width;
        texture.Height = resolved.Height;
        var repaired = engine.DecodePng(texture);
        if (!HasExpectedDimensions(repaired))
            throw new InvalidDataException($"重新定位后的卡框 {texture.Name} 仍不是 {FrameComposer.Width}×{FrameComposer.Height}：{DescribeDimensions(repaired)}");
        return repaired;
    }

    public static bool HasExpectedDimensions(byte[] png)
    {
        var info = SharpImage.Identify(png);
        return info?.Width == FrameComposer.Width && info.Height == FrameComposer.Height;
    }

    static string DescribeDimensions(byte[] png)
    {
        var info = SharpImage.Identify(png);
        return info is null ? "图片格式无法识别" : $"当前为 {info.Width}×{info.Height}，应为 {FrameComposer.Width}×{FrameComposer.Height}";
    }
}
