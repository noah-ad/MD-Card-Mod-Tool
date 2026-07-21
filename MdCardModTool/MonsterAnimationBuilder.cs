using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpPoint = SixLabors.ImageSharp.Point;
using SharpRectangle = SixLabors.ImageSharp.Rectangle;

namespace MdCardModTool;

public sealed record MonsterAnimationTemplate(
    string SpineVersion,
    string AnimationName,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<string>? AnimationNames = null)
{
    public IReadOnlyList<string> EffectiveAnimationNames => AnimationNames is { Count: > 0 }
        ? AnimationNames
        : [string.IsNullOrWhiteSpace(AnimationName) ? "animation" : AnimationName];

    public static MonsterAnimationTemplate Parse(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n', ' ');
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var skeleton = root.TryGetProperty("skeleton", out var value) ? value : default;
        var spine = String(skeleton, "spine", "3.8.75");
        var width = Number(skeleton, "width", 0);
        var height = Number(skeleton, "height", 0);
        var x = Number(skeleton, "x", -width / 2d);
        var y = Number(skeleton, "y", -height / 2d);
        IReadOnlyList<string> animationNames = ["animation"];
        if (root.TryGetProperty("animations", out var animations) && animations.ValueKind == JsonValueKind.Object)
        {
            var names = animations.EnumerateObject().Select(p => p.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length > 0) animationNames = names;
        }
        return new MonsterAnimationTemplate(spine, animationNames[0], x, y, width, height, animationNames);
    }

    static string String(JsonElement element, string name, string fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    static double Number(JsonElement element, string name, double fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
}

public sealed class MonsterAnimationBuildResult : IDisposable
{
    public required Image<Rgba32> AtlasImage { get; init; }
    public required string AtlasText { get; init; }
    public required byte[] SkeletonJson { get; init; }
    public required int FrameCount { get; init; }
    public required int FramesPerSecond { get; init; }
    public required double DisplayWidth { get; init; }
    public required double DisplayHeight { get; init; }
    public int AtlasWidth => AtlasImage.Width;
    public int AtlasHeight => AtlasImage.Height;
    public void Dispose() => AtlasImage.Dispose();
}

public static class MonsterAnimationBuilder
{
    const int Padding = 2;
    public const double GameCanvasWidth = 4800d;
    public const double GameCanvasHeight = 2700d;

    public static MonsterAnimationBuildResult Build(
        IReadOnlyList<string> framePaths,
        string cardId,
        int framesPerSecond,
        int scalePercent,
        int maxAtlasEdge = 8192,
        CancellationToken cancellationToken = default)
    {
        return Build(framePaths, cardId, framesPerSecond, scalePercent, new MonsterAnimationTemplate("3.8.75", "animation", 0, 0, 0, 0), maxAtlasEdge, cancellationToken);
    }

    public static MonsterAnimationBuildResult Build(
        IReadOnlyList<string> framePaths,
        string cardId,
        int framesPerSecond,
        int scalePercent,
        MonsterAnimationTemplate template,
        int maxAtlasEdge = 8192,
        CancellationToken cancellationToken = default)
    {
        if (framePaths.Count == 0) throw new InvalidOperationException("没有可打包的动画帧。");
        if (framesPerSecond is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (scalePercent is < 10 or > 500) throw new ArgumentOutOfRangeException(nameof(scalePercent));
        if (maxAtlasEdge is not (2048 or 4096 or 8192 or 16384)) throw new ArgumentOutOfRangeException(nameof(maxAtlasEdge));

        var regions = new List<AtlasRegion>(framePaths.Count);
        var canvasWidth = 0;
        var canvasHeight = 0;
        for (var i = 0; i < framePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var frame = SharpImage.Load<Rgba32>(framePaths[i]);
            canvasWidth = Math.Max(canvasWidth, frame.Width);
            canvasHeight = Math.Max(canvasHeight, frame.Height);
            var bounds = FindOpaqueBounds(frame);
            regions.Add(new AtlasRegion
            {
                Index = i,
                Name = $"images/frame_{i:D5}",
                SourcePath = framePaths[i],
                OriginalWidth = frame.Width,
                OriginalHeight = frame.Height,
                SourceX = bounds.X,
                SourceY = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height
            });
        }

        var packed = Pack(regions, maxAtlasEdge);
        var atlas = new Image<Rgba32>(packed.Width, packed.Height, SixLabors.ImageSharp.Color.Transparent);
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var frame = SharpImage.Load<Rgba32>(region.SourcePath);
            using var trimmed = frame.Clone(x => x.Crop(new SharpRectangle(region.SourceX, region.SourceY, region.Width, region.Height)));
            PremultiplyAlpha(trimmed);
            atlas.Mutate(x => x.DrawImage(trimmed, new SharpPoint(region.X, region.Y), 1f));
        }

        var sourceAspect = canvasWidth / (double)Math.Max(1, canvasHeight);
        var fit = Math.Min(GameCanvasWidth / Math.Max(1, canvasWidth), GameCanvasHeight / Math.Max(1, canvasHeight));
        var displayWidth = canvasWidth * fit * scalePercent / 100d;
        var displayHeight = displayWidth / sourceAspect;
        var skeletonX = -displayWidth / 2d;
        var skeletonY = -displayHeight / 2d;

        return new MonsterAnimationBuildResult
        {
            AtlasImage = atlas,
            AtlasText = BuildAtlasText(cardId, packed.Width, packed.Height, regions),
            SkeletonJson = BuildSkeletonJson(template, regions, framesPerSecond, skeletonX, skeletonY, displayWidth, displayHeight),
            FrameCount = regions.Count,
            FramesPerSecond = framesPerSecond,
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };
    }

    static SharpRectangle FindOpaqueBounds(Image<Rgba32> image)
    {
        var left = image.Width;
        var top = image.Height;
        var right = -1;
        var bottom = -1;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A <= 3) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            }
        });
        return right < left || bottom < top ? new SharpRectangle(0, 0, 1, 1) : new SharpRectangle(left, top, right - left + 1, bottom - top + 1);
    }

    static void PremultiplyAlpha(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    pixel.R = (byte)((pixel.R * pixel.A + 127) / 255);
                    pixel.G = (byte)((pixel.G * pixel.A + 127) / 255);
                    pixel.B = (byte)((pixel.B * pixel.A + 127) / 255);
                    row[x] = pixel;
                }
            }
        });
    }

    static (int Width, int Height) Pack(List<AtlasRegion> regions, int maxEdge)
    {
        var sorted = regions.OrderByDescending(x => x.Height).ThenByDescending(x => x.Width).ToArray();
        PackingCandidate? best = null;
        for (var width = 256; width <= maxEdge; width *= 2)
        {
            if (sorted.Any(x => x.Width + Padding * 2 > width)) continue;
            var placements = new List<(AtlasRegion Region, int X, int Y)>();
            var x = Padding;
            var y = Padding;
            var rowHeight = 0;
            var failed = false;
            foreach (var region in sorted)
            {
                if (x + region.Width + Padding > width)
                {
                    x = Padding;
                    y += rowHeight + Padding;
                    rowHeight = 0;
                }
                if (y + region.Height + Padding > maxEdge) { failed = true; break; }
                placements.Add((region, x, y));
                x += region.Width + Padding;
                rowHeight = Math.Max(rowHeight, region.Height);
            }
            if (failed) continue;
            var height = NextPowerOfTwo(y + rowHeight + Padding);
            if (height > maxEdge) continue;
            var candidate = new PackingCandidate(width, Math.Max(16, height), placements);
            if (best is null || (long)candidate.Width * candidate.Height < (long)best.Width * best.Height) best = candidate;
        }
        if (best is null) throw new InvalidOperationException($"全部帧无法放进单张 {maxEdge}×{maxEdge} 图集。请降低帧率、时长或单帧清晰度。");
        foreach (var placement in best.Placements) { placement.Region.X = placement.X; placement.Region.Y = placement.Y; }
        return (best.Width, best.Height);
    }

    static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value) result <<= 1;
        return result;
    }

    static string BuildAtlasText(string cardId, int width, int height, IEnumerable<AtlasRegion> regions)
    {
        var output = new StringBuilder();
        output.AppendLine($"P{cardId}.png");
        output.AppendLine($"size: {width},{height}");
        output.AppendLine("format: RGBA8888");
        output.AppendLine("filter: Linear,Linear");
        output.AppendLine("repeat: none");
        foreach (var region in regions.OrderBy(x => x.Index))
        {
            output.AppendLine(region.Name);
            output.AppendLine("  rotate: false");
            output.AppendLine($"  xy: {region.X}, {region.Y}");
            output.AppendLine($"  size: {region.Width}, {region.Height}");
            output.AppendLine($"  orig: {region.OriginalWidth}, {region.OriginalHeight}");
            output.AppendLine($"  offset: {region.SourceX}, {region.OriginalHeight - region.SourceY - region.Height}");
            output.AppendLine("  index: -1");
        }
        return output.ToString();
    }

    static byte[] BuildSkeletonJson(
        MonsterAnimationTemplate template,
        IReadOnlyList<AtlasRegion> regions,
        int framesPerSecond,
        double x,
        double y,
        double width,
        double height)
    {
        var slotName = regions[0].Name;
        var attachments = new JsonObject();
        foreach (var region in regions)
            attachments[region.Name] = new JsonObject { ["width"] = Round(width), ["height"] = Round(height) };
        var animations = new JsonObject();
        foreach (var animationName in template.EffectiveAnimationNames.Distinct(StringComparer.Ordinal))
            animations[string.IsNullOrWhiteSpace(animationName) ? "animation" : animationName] = BuildAttachmentAnimation(slotName, regions, framesPerSecond);

        var root = new JsonObject
        {
            ["skeleton"] = new JsonObject
            {
                ["hash"] = "MDCardModTool",
                ["spine"] = string.IsNullOrWhiteSpace(template.SpineVersion) ? "3.8.75" : template.SpineVersion,
                ["x"] = Round(x),
                ["y"] = Round(y),
                ["width"] = Round(width),
                ["height"] = Round(height),
                ["fps"] = framesPerSecond,
                ["images"] = "../images/",
                ["audio"] = ""
            },
            ["bones"] = new JsonArray(new JsonObject { ["name"] = "root" }),
            ["slots"] = new JsonArray(new JsonObject { ["name"] = slotName, ["bone"] = "root", ["attachment"] = regions[0].Name }),
            ["skins"] = new JsonArray(new JsonObject
            {
                ["name"] = "default",
                ["attachments"] = new JsonObject { [slotName] = attachments }
            }),
            ["animations"] = animations
        };
        return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static JsonObject BuildAttachmentAnimation(string slotName, IReadOnlyList<AtlasRegion> regions, int framesPerSecond)
    {
        var keyframes = new JsonArray();
        for (var i = 0; i < regions.Count; i++)
            keyframes.Add(new JsonObject { ["time"] = Round(i / (double)framesPerSecond), ["name"] = regions[i].Name });
        keyframes.Add(new JsonObject { ["time"] = Round(regions.Count / (double)framesPerSecond), ["name"] = regions[^1].Name });
        return new JsonObject
        {
            ["slots"] = new JsonObject
            {
                [slotName] = new JsonObject { ["attachment"] = keyframes }
            }
        };
    }

    static double Round(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    sealed class AtlasRegion
    {
        public int Index { get; init; }
        public string Name { get; init; } = "";
        public string SourcePath { get; init; } = "";
        public int OriginalWidth { get; init; }
        public int OriginalHeight { get; init; }
        public int SourceX { get; init; }
        public int SourceY { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    sealed record PackingCandidate(int Width, int Height, List<(AtlasRegion Region, int X, int Y)> Placements);
}

public sealed class MonsterAnimationService
{
    readonly ModEngine _engine = new();

    public MonsterAnimationTemplate ReadTemplate(MonsterAnimationSet set)
    {
        if (set.Skeletons.Count == 0) throw new InvalidOperationException("未找到 P卡号JS。");
        return MergeTemplates(set.Skeletons.Select(skeleton => MonsterAnimationTemplate.Parse(_engine.ReadTextAsset(skeleton).Data)));
    }

    public MonsterAnimationTemplate ReadTemplate(string gameRoot, MonsterAnimationSet set)
    {
        if (set.Skeletons.Count == 0) throw new InvalidOperationException("未找到 P卡号JS。");
        var templates = new List<MonsterAnimationTemplate>();
        foreach (var skeleton in set.Skeletons)
        {
            var backupRoot = Path.Combine(gameRoot, "_MD卡图备份", skeleton.ModSourceKind);
            var backupPath = Path.Combine(backupRoot, skeleton.RelativeBundlePath);
            var original = File.Exists(backupPath) ? _engine.FindTextAssetFast(backupPath, backupRoot, skeleton.Name) : null;
            templates.Add(MonsterAnimationTemplate.Parse(original?.Data ?? _engine.ReadTextAsset(skeleton).Data));
        }
        return MergeTemplates(templates);
    }

    static MonsterAnimationTemplate MergeTemplates(IEnumerable<MonsterAnimationTemplate> source)
    {
        var templates = source.ToArray();
        if (templates.Length == 0) throw new InvalidOperationException("未找到可读取的动画模板。");
        var names = templates.SelectMany(x => x.EffectiveAnimationNames).Distinct(StringComparer.Ordinal).ToArray();
        return templates[0] with { AnimationName = names[0], AnimationNames = names };
    }

    public void Apply(string gameRoot, MonsterAnimationSet set, MonsterAnimationBuildResult animation)
    {
        if (!set.IsComplete) throw new InvalidOperationException("该卡没有定位到教程要求的两套 Texture2D + Atlas + JS，不能进行不完整替换。");
        var rollbackRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_rollback_" + Guid.NewGuid().ToString("N"));
        var bundles = set.Assets.Select(x => x.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Directory.CreateDirectory(rollbackRoot);
        try
        {
            for (var i = 0; i < bundles.Length; i++) File.Copy(bundles[i], Path.Combine(rollbackRoot, $"{i:D3}.bundle"), true);
            try
            {
                var encodedAtlas = _engine.EncodeAnimationAtlas(animation.AtlasImage);
                foreach (var texture in set.Textures) _engine.ReplaceAnimationAtlas(texture, encodedAtlas, Path.Combine(gameRoot, "_MD卡图备份", texture.ModSourceKind));
                var atlasData = Encoding.UTF8.GetBytes(animation.AtlasText);
                foreach (var atlas in set.Atlases) _engine.ReplaceTextAsset(_engine.ReadTextAsset(atlas), atlasData, Path.Combine(gameRoot, "_MD卡图备份", atlas.ModSourceKind));
                foreach (var skeleton in set.Skeletons) _engine.ReplaceTextAsset(_engine.ReadTextAsset(skeleton), animation.SkeletonJson, Path.Combine(gameRoot, "_MD卡图备份", skeleton.ModSourceKind));
            }
            catch
            {
                for (var i = 0; i < bundles.Length; i++) File.Copy(Path.Combine(rollbackRoot, $"{i:D3}.bundle"), bundles[i], true);
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(rollbackRoot, true); } catch { }
        }
    }

    public int Restore(string gameRoot, MonsterAnimationSet set)
    {
        var restored = 0;
        foreach (var asset in set.Assets.GroupBy(x => x.BundlePath, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
        {
            var backup = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
            if (!File.Exists(backup)) continue;
            File.Copy(backup, asset.BundlePath, true);
            restored++;
        }
        return restored;
    }
}
