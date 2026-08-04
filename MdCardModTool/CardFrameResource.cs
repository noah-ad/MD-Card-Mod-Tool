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
        if (!IsExpectedFrame(resolved, texture.Name))
            resolved = ResolveFromCanonicalBundle(engine, texture);
        if (!IsExpectedFrame(resolved, texture.Name))
            throw new InvalidDataException($"卡框 {texture.Name} 的旧索引已经失效，且无法在当前 data.unity3d 中按名称重新定位。", firstFailure);

        var repairedReference = resolved!;
        texture.PathId = repairedReference.PathId;
        texture.AssetFileName = repairedReference.AssetFileName;
        texture.Width = repairedReference.Width;
        texture.Height = repairedReference.Height;
        texture.OverrideBundlePath = repairedReference.BundlePath.Equals(texture.BundlePath, StringComparison.OrdinalIgnoreCase) ? null : repairedReference.BundlePath;
        var repaired = engine.DecodePng(texture);
        if (!HasExpectedDimensions(repaired))
            throw new InvalidDataException($"重新定位后的卡框 {texture.Name} 仍不是 {FrameComposer.Width}×{FrameComposer.Height}：{DescribeDimensions(repaired)}");
        return repaired;
    }

    static bool IsExpectedFrame(TexRef? texture, string name) =>
        texture is not null &&
        texture.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
        texture.Width == FrameComposer.Width &&
        texture.Height == FrameComposer.Height;

    static TexRef? ResolveFromCanonicalBundle(ModEngine engine, TexRef texture)
    {
        // OverrideBundlePath 可能来自旧 Mod／旧缓存。卡框永远以当前游戏的
        // masterduel_Data\data.unity3d 为权威来源，再按名称和 704×1024 精确匹配。
        var candidates = new[] { texture.BundlePath, texture.ActiveBundlePath }
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in candidates)
        {
            try
            {
                var root = Path.GetDirectoryName(bundle) ?? bundle;
                var found = engine.ScanBundle(bundle, root, texture.SourceKind, includeDependencies: false).Textures
                    .FirstOrDefault(x => x.Name.Equals(texture.Name, StringComparison.OrdinalIgnoreCase) &&
                                         x.Width == FrameComposer.Width && x.Height == FrameComposer.Height);
                if (found is not null) return found;
            }
            catch { }
        }
        return null;
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
