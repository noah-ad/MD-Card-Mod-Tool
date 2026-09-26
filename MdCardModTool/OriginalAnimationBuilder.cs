using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public static partial class MonsterAnimationBuilder
{
    public sealed record OriginalPlan(int Width, int Height, int FramesPerPage, int PageCount, int AtlasWidth, int AtlasHeight, long GameBytes);

    public static OriginalPlan PlanOriginal(IReadOnlyList<string> paths, int edge)
    {
        if (paths.Count is < 1 or > 600 || edge is not (2048 or 4096 or 8192)) throw new InvalidDataException("原分辨率模式的帧数或图集上限无效。");
        int maxW = 0, maxH = 0;
        foreach (string path in paths)
        {
            var info = Image.Identify(path) ?? throw new InvalidDataException("无法读取动画帧尺寸。");
            maxW = Math.Max(maxW, info.Width); maxH = Math.Max(maxH, info.Height);
        }
        // Letterbox to 16:9 without resampling, including portrait videos.
        int width = Math.Max(maxW, checked((int)Math.Ceiling(maxH * 16d / 9d)));
        int height = Math.Max(maxH, checked((int)Math.Ceiling(width * 9d / 16d)));
        int columns = (edge - 2) / (width + 2), rows = (edge - 2) / (height + 2);
        if (columns <= 0 || rows <= 0) throw new InvalidDataException($"原始帧加留边后 {width}×{height} 超过 {edge} 图集，请换更大图集或使用普通画质；不会自动缩图。");
        int perPage = Math.Min(paths.Count, checked(columns * rows));
        columns = Math.Min(columns, perPage);
        int atlasW = NextPowerOfTwo(2 + columns * (width + 2));
        int atlasH = NextPowerOfTwo(2 + ((perPage + columns - 1) / columns) * (height + 2));
        int pages = (paths.Count + perPage - 1) / perPage;
        long bytes = checked((long)atlasW * atlasH * 4 * pages * 2); // HD + SD, both uncompressed.
        if (bytes > 1024L * 1024 * 1024) throw new InvalidDataException($"原分辨率 HD/SD 纹理约 {bytes / 1048576d:F0} MiB，超过 1024 MiB 安全上限。请缩短片段或减少总帧数，不会自动降清晰度。");
        return new(width, height, perPage, pages, atlasW, atlasH, bytes);
    }

    public static void ValidateOriginalCapacity(OriginalPlan plan, MonsterAnimationSet set)
    {
        var pairs = MonsterAnimationAssetPairing.FindComplete(set);
        if (!pairs.Any(p => p.Tier == "HighEnd_HD") || !pairs.Any(p => p.Tier == "SD")) throw new InvalidDataException("原分辨率模式需要完整 PC HD/SD 动画。");
        foreach (var pair in pairs)
            if (pair.Textures.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() < plan.PageCount)
                throw new InvalidDataException($"此素材需要每档 {plan.PageCount} 页图集，但目标 {pair.Region}/{pair.Tier}/{pair.Scale} 只有 {pair.Textures.Count} 页。请选 8192 图集、缩短片段或减少总帧数；当前不会新增游戏未登记的图集，也不会自动缩图。");
    }

    public static MonsterAnimationBuildResult BuildOriginal(IReadOnlyList<string> paths, string cardId, int fps, int scalePercent, MonsterAnimationTemplate template, int edge, CancellationToken cancellationToken = default)
    {
        if (fps is < 1 or > 60 || scalePercent is < 10 or > 500) throw new ArgumentOutOfRangeException();
        var plan = PlanOriginal(paths, edge);
        var images = new List<Image<Rgba32>>();
        var regions = new List<AtlasRegion>();
        var texts = new List<string>();
        try
        {
            for (int page = 0; page < plan.PageCount; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = new Image<Rgba32>(plan.AtlasWidth, plan.AtlasHeight);
                images.Add(image);
                int columns = (plan.AtlasWidth - 2) / (plan.Width + 2);
                var pageRegions = new List<AtlasRegion>();
                for (int slot = 0; slot < plan.FramesPerPage && page * plan.FramesPerPage + slot < paths.Count; slot++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int index = page * plan.FramesPerPage + slot;
                    using var source = Image.Load<Rgba32>(paths[index]);
                    PremultiplyAlpha(source); // Required by game's material; not texture block compression.
                    int x = 2 + slot % columns * (plan.Width + 2), y = 2 + slot / columns * (plan.Height + 2);
                    int dx = x + (plan.Width - source.Width) / 2, dy = y + (plan.Height - source.Height) / 2;
                    source.ProcessPixelRows(image, (src, dst) =>
                    {
                        for (int row = 0; row < src.Height; row++) src.GetRowSpan(row).CopyTo(dst.GetRowSpan(dy + row).Slice(dx, src.Width));
                    });
                    var region = new AtlasRegion { Index = index, Name = $"frame_{index}", OriginalWidth = plan.Width, OriginalHeight = plan.Height, Width = plan.Width, Height = plan.Height, X = x, Y = y };
                    regions.Add(region); pageRegions.Add(region);
                }
                string pageId = page == 0 ? cardId : cardId + "_page" + page;
                texts.Add(BuildAtlasText(pageId, image.Width, image.Height, pageRegions));
            }
            double w = GameCanvasWidth * scalePercent / 100d, h = GameCanvasHeight * scalePercent / 100d;
            var tier = new MonsterAnimationTierBuildResult { Tier = "HighEnd_HD", AtlasImage = images[0], ExtraPages = images.Skip(1).ToArray(), AtlasText = string.Join("\n", texts), SkeletonJson = BuildSkeletonJson(template, cardId, regions, fps, w, h) };
            return new MonsterAnimationBuildResult { Hd = tier, Sd = tier, Uncompressed = true, FrameCount = paths.Count, FramesPerSecond = fps, DisplayWidth = w, DisplayHeight = h };
        }
        catch { foreach (var image in images) image.Dispose(); throw; }
    }
}
