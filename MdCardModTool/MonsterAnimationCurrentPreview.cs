using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpRectangle = SixLabors.ImageSharp.Rectangle;

namespace MdCardModTool;

public sealed class CurrentMonsterAnimationPreview : IDisposable
{
    public required List<Bitmap> Frames { get; init; }
    public required int FramesPerSecond { get; init; }
    public required string AnimationName { get; init; }
    public required int ScalePercent { get; init; }
    public void Dispose() { foreach (var frame in Frames) frame.Dispose(); Frames.Clear(); }
}

/// <summary>
/// Reconstructs the single-slot frame sequence written by this tool and by the GIF tutorial.
/// Original Master Duel cut-ins are multi-bone/mesh Spine projects and are deliberately not
/// represented by a misleading slideshow of detached atlas parts.
/// </summary>
public static class MonsterAnimationCurrentPreview
{
    public static CurrentMonsterAnimationPreview? TryLoad(MonsterAnimationSet set, int previewMaxEdge = 768)
    {
        if (!set.IsComplete) return null;
        var engine = new ModEngine();
        var skeletonBytes = engine.ReadTextAsset(set.Skeletons[0]).Data;
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(skeletonBytes).TrimEnd('\0', '\r', '\n', ' '));
        var root = document.RootElement;
        if (!root.TryGetProperty("bones", out var bones) || bones.ValueKind != JsonValueKind.Array || bones.GetArrayLength() > 2) return null;
        if (!root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() != 1) return null;
        if (!TrySequence(root, out var animationName, out var names, out var framesPerSecond)) return null;

        var atlasBytes = engine.ReadTextAsset(set.Atlases[0]).Data;
        var atlas = ParseAtlas(Encoding.UTF8.GetString(atlasBytes).TrimEnd('\0'));
        if (atlas.Regions.Count == 0 || names.Any(name => !atlas.Regions.ContainsKey(name))) return null;

        byte[]? texturePng = null;
        foreach (var texture in set.Textures)
        {
            try
            {
                var candidate = engine.DecodePng(texture.AsTexture());
                var info = SharpImage.Identify(candidate);
                if (info?.Width == atlas.Width && info.Height == atlas.Height) { texturePng = candidate; break; }
                texturePng ??= candidate;
            }
            catch { }
        }
        if (texturePng is null) return null;

        var frames = new List<Bitmap>(names.Count);
        try
        {
            using var atlasImage = SharpImage.Load<Rgba32>(texturePng);
            foreach (var name in names)
            {
                var region = atlas.Regions[name];
                if (region.Rotate || region.Width < 1 || region.Height < 1 || region.X < 0 || region.Y < 0 || region.X + region.Width > atlasImage.Width || region.Y + region.Height > atlasImage.Height)
                    throw new InvalidDataException("动画图集区域超出纹理范围。");
                using var part = atlasImage.Clone(x => x.Crop(new SharpRectangle(region.X, region.Y, region.Width, region.Height)));
                UnpremultiplyAlpha(part);
                using var canvas = new Image<Rgba32>(Math.Max(1, region.OriginalWidth), Math.Max(1, region.OriginalHeight), SixLabors.ImageSharp.Color.Transparent);
                var top = region.OriginalHeight - region.OffsetY - region.Height;
                canvas.Mutate(x => x.DrawImage(part, new SixLabors.ImageSharp.Point(region.OffsetX, top), 1f));
                if (Math.Max(canvas.Width, canvas.Height) > previewMaxEdge)
                    canvas.Mutate(x => x.Resize(new ResizeOptions { Size = new SixLabors.ImageSharp.Size(previewMaxEdge, previewMaxEdge), Mode = ResizeMode.Max }));
                using var encoded = new MemoryStream();
                canvas.SaveAsPng(encoded); encoded.Position = 0;
                using var bitmap = System.Drawing.Image.FromStream(encoded);
                frames.Add(new Bitmap(bitmap));
            }
            var first = atlas.Regions[names[0]];
            var fullFit = Math.Min(MonsterAnimationBuilder.GameCanvasWidth / Math.Max(1, first.OriginalWidth), MonsterAnimationBuilder.GameCanvasHeight / Math.Max(1, first.OriginalHeight));
            var fullWidth = first.OriginalWidth * fullFit;
            var fullHeight = first.OriginalHeight * fullFit;
            var skeleton = root.TryGetProperty("skeleton", out var value) ? value : default;
            var storedWidth = Number(skeleton, "width", fullWidth);
            var storedHeight = Number(skeleton, "height", fullHeight);
            var scalePercent = Math.Clamp((int)Math.Round(Math.Min(storedWidth / Math.Max(1, fullWidth), storedHeight / Math.Max(1, fullHeight)) * 100d), 10, 500);
            return new CurrentMonsterAnimationPreview { Frames = frames, FramesPerSecond = framesPerSecond, AnimationName = animationName, ScalePercent = scalePercent };
        }
        catch
        {
            foreach (var frame in frames) frame.Dispose();
            return null;
        }
    }

    static bool TrySequence(JsonElement root, out string animationName, out List<string> names, out int framesPerSecond)
    {
        animationName = "animation"; names = []; framesPerSecond = 15;
        if (!root.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Object) return false;
        var animation = animations.EnumerateObject().FirstOrDefault();
        if (animation.Value.ValueKind != JsonValueKind.Object) return false;
        animationName = animation.Name;
        if (!animation.Value.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Object) return false;
        JsonElement timeline = default;
        foreach (var slot in slots.EnumerateObject())
            if (slot.Value.ValueKind == JsonValueKind.Object && slot.Value.TryGetProperty("attachment", out timeline) && timeline.ValueKind == JsonValueKind.Array) break;
        if (timeline.ValueKind != JsonValueKind.Array) return false;
        var times = new List<double>();
        foreach (var frame in timeline.EnumerateArray())
        {
            if (!frame.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            var value = name.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (names.Count == 0 || !string.Equals(names[^1], value, StringComparison.Ordinal)) names.Add(value);
            if (frame.TryGetProperty("time", out var time) && time.TryGetDouble(out var seconds)) times.Add(seconds);
        }
        if (names.Count < 2) return false;
        var steps = times.Zip(times.Skip(1), (left, right) => right - left).Where(x => x > 0.0001).OrderBy(x => x).ToArray();
        if (steps.Length > 0) framesPerSecond = Math.Clamp((int)Math.Round(1d / steps[steps.Length / 2]), 1, 60);
        return true;
    }

    static ParsedAtlas ParseAtlas(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var width = 0; var height = 0; var currentPage = "";
        var regions = new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw) || char.IsWhiteSpace(raw[0]) || raw.Contains(':')) continue;
            var name = raw.Trim();
            var next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
            if (next.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
            {
                currentPage = name;
                var pair = Pair(next[5..]); width = pair.X; height = pair.Y;
                continue;
            }
            if (currentPage.Length == 0) continue;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var j = i + 1;
            while (j < lines.Length && (string.IsNullOrWhiteSpace(lines[j]) || char.IsWhiteSpace(lines[j][0])))
            {
                var line = lines[j].Trim();
                var colon = line.IndexOf(':');
                if (colon > 0) values[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                j++;
            }
            var xy = Pair(values.GetValueOrDefault("xy", "0,0"));
            var size = Pair(values.GetValueOrDefault("size", "0,0"));
            var original = Pair(values.GetValueOrDefault("orig", $"{size.X},{size.Y}"));
            var offset = Pair(values.GetValueOrDefault("offset", "0,0"));
            regions[name] = new AtlasRegion(xy.X, xy.Y, size.X, size.Y, original.X, original.Y, offset.X, offset.Y, values.GetValueOrDefault("rotate", "false").Equals("true", StringComparison.OrdinalIgnoreCase));
        }
        return new ParsedAtlas(width, height, regions);
    }

    static (int X, int Y) Pair(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        return (parts.Length > 0 && int.TryParse(parts[0], out var x) ? x : 0, parts.Length > 1 && int.TryParse(parts[1], out var y) ? y : 0);
    }

    static double Number(JsonElement element, string name, double fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;

    static void UnpremultiplyAlpha(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A is > 0 and < 255)
                    {
                        pixel.R = (byte)Math.Min(255, (pixel.R * 255 + pixel.A / 2) / pixel.A);
                        pixel.G = (byte)Math.Min(255, (pixel.G * 255 + pixel.A / 2) / pixel.A);
                        pixel.B = (byte)Math.Min(255, (pixel.B * 255 + pixel.A / 2) / pixel.A);
                        row[x] = pixel;
                    }
                }
            }
        });
    }

    sealed record ParsedAtlas(int Width, int Height, Dictionary<string, AtlasRegion> Regions);
    sealed record AtlasRegion(int X, int Y, int Width, int Height, int OriginalWidth, int OriginalHeight, int OffsetX, int OffsetY, bool Rotate);
}
