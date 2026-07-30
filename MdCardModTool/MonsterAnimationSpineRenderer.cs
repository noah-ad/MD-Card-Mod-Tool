using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace MdCardModTool;

/// <summary>
/// Small, read-only Spine JSON 4.x renderer for Master Duel summon-animation previews.
/// It supports the data used by the game's cut-ins: normal bone transforms, region and
/// weighted/unweighted mesh attachments, slot colour/attachment timelines and deform.
/// The result is also used to bake another card's Spine animation into the tool's safe
/// single-page frame sequence instead of copying incompatible Unity object references.
/// </summary>
public static class MonsterAnimationSpineRenderer
{
    public static string LastDiagnostic { get; private set; } = "";

    public static CurrentMonsterAnimationPreview? TryRender(
        string gameRoot,
        MonsterAnimationSet set,
        int framesPerSecond = 30,
        int maximumFrames = 180,
        int maximumEdge = 768,
        string? requestedAnimation = null)
    {
        LastDiagnostic = "";
        if (!set.IsComplete) { LastDiagnostic = "动画资源不完整。"; return null; }
        framesPerSecond = Math.Clamp(framesPerSecond, 1, 60);
        maximumFrames = Math.Clamp(maximumFrames, 2, 600);
        maximumEdge = Math.Clamp(maximumEdge, 128, 2048);

        var engine = new ModEngine();
        var skeletonCandidates = set.Skeletons
            .Select(x => (Asset: x, Data: ReadJson(engine, x)))
            .Where(x => x.Data is not null)
            .Select(x => (x.Asset, Data: x.Data!, Area: SkeletonArea(x.Data!)))
            .OrderByDescending(x => x.Area)
            .ToList();
        var atlasCandidates = set.Atlases
            .Select(x => (Asset: x, Text: ReadText(engine, x)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => (x.Asset, x.Text, Atlas: SpineAtlas.Parse(x.Text!)))
            .Where(x => x.Atlas.Pages.Count > 0 && x.Atlas.Regions.Count > 0)
            .OrderByDescending(x => x.Atlas.Pages.Sum(p => (long)p.Width * p.Height))
            .ToList();
        if (skeletonCandidates.Count == 0 || atlasCandidates.Count == 0) { LastDiagnostic = "无法解析 Skeleton JSON 或 Atlas。"; return null; }

        var skeleton = skeletonCandidates[0];
        var atlas = atlasCandidates[0];
        using var package = ResolveTextures(gameRoot, set, atlas.Asset, atlas.Atlas, engine);
        if (!atlas.Atlas.Pages.All(x => package.Pages.ContainsKey(x.Name)))
        {
            LastDiagnostic = "缺少图集页：" + string.Join("、", atlas.Atlas.Pages.Where(x => !package.Pages.ContainsKey(x.Name)).Select(x => x.Name));
            return null;
        }

        using var document = JsonDocument.Parse(skeleton.Data);
        var root = document.RootElement;
        if (!root.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Object) { LastDiagnostic = "Skeleton 没有 animations。"; return null; }
        var animationProperty = requestedAnimation is not null && animations.TryGetProperty(requestedAnimation, out _)
            ? animations.EnumerateObject().First(x => x.Name == requestedAnimation)
            : animations.EnumerateObject().FirstOrDefault();
        if (animationProperty.Value.ValueKind != JsonValueKind.Object) { LastDiagnostic = "Skeleton 没有可播放动作。"; return null; }

        var duration = FindMaximumTime(animationProperty.Value);
        if (duration <= 0) duration = 1;
        var frameCount = Math.Clamp((int)Math.Ceiling(duration * framesPerSecond) + 1, 2, maximumFrames);
        var actualFps = Math.Clamp((int)Math.Round((frameCount - 1) / duration), 1, 60);
        var setup = ParseSetup(root);
        var bounds = SkeletonBounds(root, setup);
        var output = OutputSize(bounds.Width, bounds.Height, maximumEdge);
        var textures = package.Pages.ToDictionary(x => x.Key, x => new SpineTexture(x.Value), StringComparer.OrdinalIgnoreCase);
        var frames = new List<Bitmap>(frameCount);
        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                var time = Math.Min(duration, i / (double)actualFps);
                frames.Add(RenderFrame(setup, animationProperty.Value, atlas.Atlas, textures, bounds, output.Width, output.Height, time));
            }
            return new CurrentMonsterAnimationPreview
            {
                Frames = frames,
                FramesPerSecond = actualFps,
                AnimationName = animationProperty.Name,
                ScalePercent = 100
            };
        }
        catch (Exception ex)
        {
            foreach (var frame in frames) frame.Dispose();
            LastDiagnostic = ex.Message;
            return null;
        }
    }

    static byte[]? ReadJson(ModEngine engine, MonsterAnimationAssetRef asset)
    {
        try
        {
            var bytes = engine.ReadTextAsset(asset).Data;
            using var _ = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\r', '\n', ' '));
            return bytes;
        }
        catch { return null; }
    }

    static string? ReadText(ModEngine engine, MonsterAnimationAssetRef asset)
    {
        try { return Encoding.UTF8.GetString(engine.ReadTextAsset(asset).Data).TrimEnd('\0', '\r', '\n', ' '); }
        catch { return null; }
    }

    static double SkeletonArea(byte[] data)
    {
        using var document = JsonDocument.Parse(data);
        var skeleton = document.RootElement.TryGetProperty("skeleton", out var value) ? value : default;
        return Number(skeleton, "width", 0) * Number(skeleton, "height", 0);
    }

    static SpineTexturePackage ResolveTextures(
        string gameRoot,
        MonsterAnimationSet set,
        MonsterAnimationAssetRef atlasAsset,
        SpineAtlas atlas,
        ModEngine engine)
    {
        var result = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        var needed = atlas.Pages.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var storage = atlasAsset.StorageKind;
        var root = storage == "StreamingAssets" ? IndexService.StreamingRoot(gameRoot) : IndexService.FindLocalRoot(gameRoot);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return new SpineTexturePackage(result);
        var fullRoot = Path.GetFullPath(root);
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void EnqueueFull(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full) && full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    queue.Enqueue(Path.GetRelativePath(fullRoot, full));
            }
            catch { }
        }

        EnqueueFull(atlasAsset.BundlePath);
        foreach (var asset in set.Assets.Where(x => x.StorageKind == storage)) EnqueueFull(asset.BundlePath);
        var tiers = string.IsNullOrWhiteSpace(atlasAsset.ProfileTier)
            ? new[] { "HighEnd_HD", "SD" }
            : new[] { atlasAsset.ProfileTier };
        var regions = string.IsNullOrWhiteSpace(atlasAsset.ProfileRegion)
            ? new[] { "tcg", "ocg" }
            : new[] { atlasAsset.ProfileRegion };
        foreach (var region in regions)
        foreach (var tier in tiers)
        {
            var logical = $"Duel/Timeline/Duel/MonsterCutIn/{region}/P{set.CardId}/{tier}/P{set.CardId}";
            EnqueueFull(Path.Combine(fullRoot, IndexService.ResourceBundleRelativePath(logical)));
        }

        while (queue.Count > 0 && visited.Count < 128 && result.Count < needed.Count)
        {
            var relative = queue.Dequeue().Replace('/', Path.DirectorySeparatorChar);
            if (!visited.Add(relative) || Path.IsPathRooted(relative)) continue;
            var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
            try
            {
                foreach (var texture in engine.ListTextures(path, fullRoot, atlasAsset.ModSourceKind))
                {
                    var page = needed.Values.FirstOrDefault(x =>
                        string.Equals(Path.GetFileNameWithoutExtension(x.Name), texture.Name, StringComparison.OrdinalIgnoreCase) &&
                        (x.Width <= 0 || texture.Width == x.Width) &&
                        (x.Height <= 0 || texture.Height == x.Height));
                    if (page is null || result.ContainsKey(page.Name)) continue;
                    result[page.Name] = DecodeBitmap(engine, texture);
                }
                foreach (var dependency in engine.ReadBundleDependencies(path))
                    if (!visited.Contains(dependency)) queue.Enqueue(dependency);
            }
            catch { }
        }
        return new SpineTexturePackage(result);
    }

    static Bitmap DecodeBitmap(ModEngine engine, TexRef texture)
    {
        var png = engine.DecodePng(texture);
        using var image = SharpImage.Load<Rgba32>(png);
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
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;
        using var bitmap = Image.FromStream(stream);
        return new Bitmap(bitmap);
    }

    static SpineSetup ParseSetup(JsonElement root)
    {
        var bones = new List<SpineBone>();
        var boneIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("bones", out var boneArray))
        {
            foreach (var item in boneArray.EnumerateArray())
            {
                var name = String(item, "name", $"bone{bones.Count}");
                var parentName = String(item, "parent", "");
                var bone = new SpineBone
                {
                    Name = name,
                    ParentIndex = parentName.Length > 0 && boneIndices.TryGetValue(parentName, out var parent) ? parent : -1,
                    SetupX = Number(item, "x", 0),
                    SetupY = Number(item, "y", 0),
                    SetupRotation = Number(item, "rotation", 0),
                    SetupScaleX = Number(item, "scaleX", 1),
                    SetupScaleY = Number(item, "scaleY", 1),
                    SetupShearX = Number(item, "shearX", 0),
                    SetupShearY = Number(item, "shearY", 0)
                };
                boneIndices[name] = bones.Count;
                bones.Add(bone);
            }
        }

        var slots = new List<SpineSlot>();
        if (root.TryGetProperty("slots", out var slotArray))
        {
            foreach (var item in slotArray.EnumerateArray())
            {
                var boneName = String(item, "bone", "");
                slots.Add(new SpineSlot
                {
                    Name = String(item, "name", $"slot{slots.Count}"),
                    BoneIndex = boneIndices.GetValueOrDefault(boneName, 0),
                    SetupAttachment = String(item, "attachment", ""),
                    SetupColor = ParseColor(String(item, "color", "ffffffff"))
                });
            }
        }

        var attachments = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        if (root.TryGetProperty("skins", out var skins))
        {
            JsonElement defaultSkin = default;
            if (skins.ValueKind == JsonValueKind.Array)
                defaultSkin = skins.EnumerateArray().FirstOrDefault(x => String(x, "name", "default") == "default");
            else if (skins.ValueKind == JsonValueKind.Object && skins.TryGetProperty("default", out var legacy))
                defaultSkin = legacy;
            var map = defaultSkin.ValueKind == JsonValueKind.Object && defaultSkin.TryGetProperty("attachments", out var modern) ? modern : defaultSkin;
            if (map.ValueKind == JsonValueKind.Object)
            {
                foreach (var slot in map.EnumerateObject())
                {
                    var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    if (slot.Value.ValueKind == JsonValueKind.Object)
                        foreach (var attachment in slot.Value.EnumerateObject()) values[attachment.Name] = attachment.Value.Clone();
                    attachments[slot.Name] = values;
                }
            }
        }
        return new SpineSetup(bones, slots, attachments);
    }

    static RectangleF SkeletonBounds(JsonElement root, SpineSetup setup)
    {
        var skeleton = root.TryGetProperty("skeleton", out var value) ? value : default;
        var width = Number(skeleton, "width", 0);
        var height = Number(skeleton, "height", 0);
        var x = Number(skeleton, "x", -width / 2);
        var y = Number(skeleton, "y", -height / 2);
        if (width <= 1 || height <= 1)
        {
            width = 4800; height = 2700; x = -2400; y = -1350;
        }
        var margin = (float)(Math.Max(width, height) * 0.035);
        return new RectangleF((float)x - margin, (float)y - margin, (float)width + margin * 2, (float)height + margin * 2);
    }

    static Size OutputSize(float width, float height, int maximumEdge)
    {
        var ratio = maximumEdge / Math.Max(1f, Math.Max(width, height));
        return new Size(Math.Max(1, (int)Math.Round(width * ratio)), Math.Max(1, (int)Math.Round(height * ratio)));
    }

    static Bitmap RenderFrame(
        SpineSetup setup,
        JsonElement animation,
        SpineAtlas atlas,
        IReadOnlyDictionary<string, SpineTexture> pages,
        RectangleF bounds,
        int width,
        int height,
        double time)
    {
        var bones = setup.Bones.Select(x => x.Copy()).ToArray();
        ApplyBoneTimelines(bones, animation, time);
        UpdateWorldTransforms(bones);
        var canvas = new SoftwareCanvas(width, height);
        var sx = width / Math.Max(1f, bounds.Width);
        var sy = height / Math.Max(1f, bounds.Height);

        foreach (var slot in setup.Slots)
        {
            if (!setup.Attachments.TryGetValue(slot.Name, out var slotAttachments)) continue;
            var attachmentName = EvaluateAttachment(animation, slot.Name, slot.SetupAttachment, time);
            if (string.IsNullOrWhiteSpace(attachmentName) || !slotAttachments.TryGetValue(attachmentName, out var attachment)) continue;
            var type = String(attachment, "type", "region");
            if (type is "path" or "clipping" or "point" or "boundingbox") continue;
            var regionName = String(attachment, "path", attachmentName);
            if (!atlas.Regions.TryGetValue(regionName, out var region) || !pages.TryGetValue(region.PageName, out var page)) continue;
            var color = Multiply(slot.SetupColor, ParseColor(String(attachment, "color", "ffffffff")), EvaluateSlotColor(animation, slot.Name, time));
            if (color.A <= 0.001f) continue;
            if (type == "mesh")
                DrawMesh(canvas, page, region, attachment, bones, slot, animation, attachmentName, color, bounds, sx, sy, time);
            else
                DrawRegion(canvas, page, region, attachment, bones[slot.BoneIndex], color, bounds, sx, sy);
        }
        return canvas.ToBitmap();
    }

    static void ApplyBoneTimelines(SpineBone[] bones, JsonElement animation, double time)
    {
        if (!animation.TryGetProperty("bones", out var timelines) || timelines.ValueKind != JsonValueKind.Object)
        {
            foreach (var bone in bones) bone.Reset();
            return;
        }
        foreach (var bone in bones)
        {
            bone.Reset();
            if (!timelines.TryGetProperty(bone.Name, out var values)) continue;
            if (values.TryGetProperty("rotate", out var rotate)) bone.Rotation += EvaluateNumber(rotate, "value", 0, time);
            if (values.TryGetProperty("translate", out var translate))
            {
                bone.X += EvaluateNumber(translate, "x", 0, time, 0);
                bone.Y += EvaluateNumber(translate, "y", 0, time, 1);
            }
            if (values.TryGetProperty("translatex", out var translateX)) bone.X += EvaluateNumber(translateX, "value", 0, time);
            if (values.TryGetProperty("translatey", out var translateY)) bone.Y += EvaluateNumber(translateY, "value", 0, time);
            if (values.TryGetProperty("scale", out var scale))
            {
                bone.ScaleX *= EvaluateNumber(scale, "x", 1, time, 0);
                bone.ScaleY *= EvaluateNumber(scale, "y", 1, time, 1);
            }
            if (values.TryGetProperty("scalex", out var scaleX)) bone.ScaleX *= EvaluateNumber(scaleX, "value", 1, time);
            if (values.TryGetProperty("scaley", out var scaleY)) bone.ScaleY *= EvaluateNumber(scaleY, "value", 1, time);
            if (values.TryGetProperty("shear", out var shear))
            {
                bone.ShearX += EvaluateNumber(shear, "x", 0, time, 0);
                bone.ShearY += EvaluateNumber(shear, "y", 0, time, 1);
            }
        }
    }

    static void UpdateWorldTransforms(SpineBone[] bones)
    {
        foreach (var bone in bones)
        {
            var rotationX = Degrees(bone.Rotation + bone.ShearX);
            var rotationY = Degrees(bone.Rotation + 90 + bone.ShearY);
            var la = Math.Cos(rotationX) * bone.ScaleX;
            var lb = Math.Cos(rotationY) * bone.ScaleY;
            var lc = Math.Sin(rotationX) * bone.ScaleX;
            var ld = Math.Sin(rotationY) * bone.ScaleY;
            if (bone.ParentIndex < 0)
            {
                bone.A = la; bone.B = lb; bone.C = lc; bone.D = ld; bone.WorldX = bone.X; bone.WorldY = bone.Y;
                continue;
            }
            var parent = bones[bone.ParentIndex];
            bone.WorldX = parent.A * bone.X + parent.B * bone.Y + parent.WorldX;
            bone.WorldY = parent.C * bone.X + parent.D * bone.Y + parent.WorldY;
            bone.A = parent.A * la + parent.B * lc;
            bone.B = parent.A * lb + parent.B * ld;
            bone.C = parent.C * la + parent.D * lc;
            bone.D = parent.C * lb + parent.D * ld;
        }
    }

    static void DrawRegion(
        SoftwareCanvas canvas,
        SpineTexture page,
        SpineRegion region,
        JsonElement attachment,
        SpineBone bone,
        SpineColor color,
        RectangleF bounds,
        float sx,
        float sy)
    {
        var attachmentWidth = Number(attachment, "width", region.OriginalWidth);
        var attachmentHeight = Number(attachment, "height", region.OriginalHeight);
        var scaleX = Number(attachment, "scaleX", 1);
        var scaleY = Number(attachment, "scaleY", 1);
        var trimWidth = region.Degrees is 90 or 270 ? region.Height : region.Width;
        var trimHeight = region.Degrees is 90 or 270 ? region.Width : region.Height;
        var originalWidth = Math.Max(1, region.OriginalWidth);
        var originalHeight = Math.Max(1, region.OriginalHeight);
        var left = -attachmentWidth / 2 + region.OffsetX * attachmentWidth / originalWidth;
        var bottom = -attachmentHeight / 2 + region.OffsetY * attachmentHeight / originalHeight;
        var right = left + trimWidth * attachmentWidth / originalWidth;
        var top = bottom + trimHeight * attachmentHeight / originalHeight;
        var radians = Degrees(Number(attachment, "rotation", 0));
        var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        var ox = Number(attachment, "x", 0); var oy = Number(attachment, "y", 0);
        var locals = new[]
        {
            new PointF((float)left, (float)top), new PointF((float)right, (float)top),
            new PointF((float)right, (float)bottom), new PointF((float)left, (float)bottom)
        };
        var world = new PointF[4];
        for (var i = 0; i < locals.Length; i++)
        {
            var lx = locals[i].X * scaleX; var ly = locals[i].Y * scaleY;
            var rx = lx * cos - ly * sin + ox; var ry = lx * sin + ly * cos + oy;
            var wx = bone.A * rx + bone.B * ry + bone.WorldX;
            var wy = bone.C * rx + bone.D * ry + bone.WorldY;
            world[i] = ToScreen(wx, wy, bounds, sx, sy);
        }
        var uv = new[] { region.Map(0, 0), region.Map(1, 0), region.Map(1, 1), region.Map(0, 1) };
        canvas.DrawTexturedTriangle(page, uv[0], uv[1], uv[2], world[0], world[1], world[2], color);
        canvas.DrawTexturedTriangle(page, uv[0], uv[2], uv[3], world[0], world[2], world[3], color);
    }

    static void DrawMesh(
        SoftwareCanvas canvas,
        SpineTexture page,
        SpineRegion region,
        JsonElement attachment,
        SpineBone[] bones,
        SpineSlot slot,
        JsonElement animation,
        string attachmentName,
        SpineColor color,
        RectangleF bounds,
        float sx,
        float sy,
        double time)
    {
        if (!attachment.TryGetProperty("uvs", out var uvValues) || !attachment.TryGetProperty("triangles", out var triangleValues) ||
            !attachment.TryGetProperty("vertices", out var vertexValues)) return;
        var rawUvs = uvValues.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var rawVertices = vertexValues.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var triangles = triangleValues.EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (rawUvs.Length < 6 || (rawUvs.Length & 1) != 0 || triangles.Length < 3) return;
        var vertexCount = rawUvs.Length / 2;
        var weighted = rawVertices.Length != rawUvs.Length;
        var deformLength = weighted ? CountInfluences(rawVertices) * 2 : rawUvs.Length;
        var deform = EvaluateDeform(animation, slot.Name, attachmentName, deformLength, time);
        var world = weighted
            ? WeightedVertices(rawVertices, vertexCount, bones, deform)
            : UnweightedVertices(rawVertices, vertexCount, bones[slot.BoneIndex], deform);
        var screen = world.Select(x => ToScreen(x.X, x.Y, bounds, sx, sy)).ToArray();
        var uvs = new PointF[vertexCount];
        for (var i = 0; i < vertexCount; i++) uvs[i] = region.Map(rawUvs[i * 2], rawUvs[i * 2 + 1]);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
            if ((uint)a >= screen.Length || (uint)b >= screen.Length || (uint)c >= screen.Length) continue;
            canvas.DrawTexturedTriangle(page, uvs[a], uvs[b], uvs[c], screen[a], screen[b], screen[c], color);
        }
    }

    static PointF[] WeightedVertices(double[] values, int vertexCount, SpineBone[] bones, double[] deform)
    {
        var result = new PointF[vertexCount];
        var cursor = 0; var influence = 0;
        for (var vertex = 0; vertex < vertexCount && cursor < values.Length; vertex++)
        {
            var count = (int)values[cursor++];
            double wx = 0, wy = 0;
            for (var i = 0; i < count && cursor + 3 < values.Length; i++, influence++)
            {
                var boneIndex = (int)values[cursor++];
                var x = values[cursor++] + deform.ElementAtOrDefault(influence * 2);
                var y = values[cursor++] + deform.ElementAtOrDefault(influence * 2 + 1);
                var weight = values[cursor++];
                if ((uint)boneIndex >= bones.Length) continue;
                var bone = bones[boneIndex];
                wx += (bone.A * x + bone.B * y + bone.WorldX) * weight;
                wy += (bone.C * x + bone.D * y + bone.WorldY) * weight;
            }
            result[vertex] = new PointF((float)wx, (float)wy);
        }
        return result;
    }

    static PointF[] UnweightedVertices(double[] values, int vertexCount, SpineBone bone, double[] deform)
    {
        var result = new PointF[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var x = values.ElementAtOrDefault(i * 2) + deform.ElementAtOrDefault(i * 2);
            var y = values.ElementAtOrDefault(i * 2 + 1) + deform.ElementAtOrDefault(i * 2 + 1);
            result[i] = new PointF(
                (float)(bone.A * x + bone.B * y + bone.WorldX),
                (float)(bone.C * x + bone.D * y + bone.WorldY));
        }
        return result;
    }

    static int CountInfluences(double[] values)
    {
        var cursor = 0; var count = 0;
        while (cursor < values.Length)
        {
            var influences = (int)values[cursor++];
            count += influences;
            cursor += influences * 4;
        }
        return count;
    }

    static double[] EvaluateDeform(JsonElement animation, string slot, string attachment, int length, double time)
    {
        JsonElement timeline = default;
        if (animation.TryGetProperty("attachments", out var attachments) &&
            attachments.TryGetProperty("default", out var skin) &&
            skin.TryGetProperty(slot, out var slotMap) &&
            slotMap.TryGetProperty(attachment, out var attachmentMap) &&
            attachmentMap.TryGetProperty("deform", out var modern)) timeline = modern;
        else if (animation.TryGetProperty("deform", out var legacyRoot) &&
                 legacyRoot.TryGetProperty("default", out var legacySkin) &&
                 legacySkin.TryGetProperty(slot, out var legacySlot) &&
                 legacySlot.TryGetProperty(attachment, out var legacy)) timeline = legacy;
        if (timeline.ValueKind != JsonValueKind.Array || timeline.GetArrayLength() == 0) return new double[length];
        var frames = timeline.EnumerateArray().ToArray();
        var leftIndex = 0;
        while (leftIndex + 1 < frames.Length && Number(frames[leftIndex + 1], "time", 0) <= time) leftIndex++;
        var left = ExpandDeform(frames[leftIndex], length);
        if (leftIndex + 1 >= frames.Length || String(frames[leftIndex], "curve", "") == "stepped") return left;
        var t0 = Number(frames[leftIndex], "time", 0);
        var t1 = Number(frames[leftIndex + 1], "time", t0);
        if (t1 <= t0) return left;
        var right = ExpandDeform(frames[leftIndex + 1], length);
        var amount = Math.Clamp((time - t0) / (t1 - t0), 0, 1);
        for (var i = 0; i < left.Length; i++) left[i] += (right[i] - left[i]) * amount;
        return left;
    }

    static double[] ExpandDeform(JsonElement frame, int length)
    {
        var result = new double[length];
        if (!frame.TryGetProperty("vertices", out var values) || values.ValueKind != JsonValueKind.Array) return result;
        var offset = (int)Number(frame, "offset", 0);
        var i = offset;
        foreach (var value in values.EnumerateArray())
        {
            if ((uint)i < result.Length) result[i] = value.GetDouble();
            i++;
        }
        return result;
    }

    static PointF ToScreen(double x, double y, RectangleF bounds, float sx, float sy) =>
        new((float)((x - bounds.Left) * sx), (float)((bounds.Bottom - y) * sy));

    static string EvaluateAttachment(JsonElement animation, string slotName, string setup, double time)
    {
        if (!animation.TryGetProperty("slots", out var slots) || !slots.TryGetProperty(slotName, out var slot) ||
            !slot.TryGetProperty("attachment", out var timeline) || timeline.ValueKind != JsonValueKind.Array) return setup;
        var value = setup;
        foreach (var frame in timeline.EnumerateArray())
        {
            if (Number(frame, "time", 0) > time) break;
            value = String(frame, "name", "");
        }
        return value;
    }

    static SpineColor EvaluateSlotColor(JsonElement animation, string slotName, double time)
    {
        if (!animation.TryGetProperty("slots", out var slots) || !slots.TryGetProperty(slotName, out var slot) ||
            !slot.TryGetProperty("rgba", out var timeline) || timeline.ValueKind != JsonValueKind.Array || timeline.GetArrayLength() == 0)
            return SpineColor.White;
        var frames = timeline.EnumerateArray().ToArray();
        var left = 0;
        while (left + 1 < frames.Length && Number(frames[left + 1], "time", 0) <= time) left++;
        var a = ParseColor(String(frames[left], "color", "ffffffff"));
        if (left + 1 >= frames.Length || String(frames[left], "curve", "") == "stepped") return a;
        var t0 = Number(frames[left], "time", 0);
        var t1 = Number(frames[left + 1], "time", t0);
        if (t1 <= t0) return a;
        var b = ParseColor(String(frames[left + 1], "color", "ffffffff"));
        var amount = (float)Math.Clamp((time - t0) / (t1 - t0), 0, 1);
        return SpineColor.Lerp(a, b, amount);
    }

    static double EvaluateNumber(JsonElement timeline, string property, double fallback, double time, int curveGroup = 0)
    {
        if (timeline.ValueKind != JsonValueKind.Array || timeline.GetArrayLength() == 0) return fallback;
        var frames = timeline.EnumerateArray().ToArray();
        var left = 0;
        while (left + 1 < frames.Length && Number(frames[left + 1], "time", 0) <= time) left++;
        var a = Number(frames[left], property, fallback);
        if (left + 1 >= frames.Length) return a;
        var t0 = Number(frames[left], "time", 0);
        var t1 = Number(frames[left + 1], "time", t0);
        var b = Number(frames[left + 1], property, fallback);
        if (time <= t0 || t1 <= t0) return a;
        if (String(frames[left], "curve", "") == "stepped") return a;
        if (frames[left].TryGetProperty("curve", out var curve) && curve.ValueKind == JsonValueKind.Array)
        {
            var values = curve.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var offset = curveGroup * 4;
            if (values.Length >= offset + 4)
                return CubicAtTime(time, t0, a, values[offset], values[offset + 1], values[offset + 2], values[offset + 3], t1, b);
        }
        var amount = Math.Clamp((time - t0) / (t1 - t0), 0, 1);
        return a + (b - a) * amount;
    }

    static double CubicAtTime(double time, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        var low = 0d; var high = 1d;
        for (var i = 0; i < 14; i++)
        {
            var mid = (low + high) / 2;
            var x = Cubic(x0, x1, x2, x3, mid);
            if (x < time) low = mid; else high = mid;
        }
        return Cubic(y0, y1, y2, y3, (low + high) / 2);
    }

    static double Cubic(double a, double b, double c, double d, double t)
    {
        var u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }

    static double FindMaximumTime(JsonElement element)
    {
        var maximum = 0d;
        void Visit(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("time", out var time) && time.TryGetDouble(out var seconds)) maximum = Math.Max(maximum, seconds);
                foreach (var property in value.EnumerateObject()) Visit(property.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Visit(item);
        }
        Visit(element);
        return maximum;
    }

    static SpineColor ParseColor(string value)
    {
        if (value.Length is not (6 or 8)) return SpineColor.White;
        try
        {
            var r = Convert.ToByte(value[..2], 16) / 255f;
            var g = Convert.ToByte(value.Substring(2, 2), 16) / 255f;
            var b = Convert.ToByte(value.Substring(4, 2), 16) / 255f;
            var a = value.Length == 8 ? Convert.ToByte(value.Substring(6, 2), 16) / 255f : 1f;
            return new SpineColor(r, g, b, a);
        }
        catch { return SpineColor.White; }
    }

    static SpineColor Multiply(params SpineColor[] colors) =>
        colors.Aggregate(SpineColor.White, (x, y) => new SpineColor(x.R * y.R, x.G * y.G, x.B * y.B, x.A * y.A));

    static string String(JsonElement element, string name, string fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    static double Number(JsonElement element, string name, double fallback) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : fallback;

    static double Degrees(double value) => value * Math.PI / 180d;

    sealed class SpineTexture
    {
        public int Width { get; }
        public int Height { get; }
        public int[] Pixels { get; }

        public SpineTexture(Bitmap source)
        {
            Width = source.Width;
            Height = source.Height;
            Pixels = new int[Width * Height];
            using var converted = source.Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format32bppArgb);
            var data = converted.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == Width * 4)
                    Marshal.Copy(data.Scan0, Pixels, 0, Pixels.Length);
                else
                {
                    var row = new int[Width];
                    for (var y = 0; y < Height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, Width);
                        Array.Copy(row, 0, Pixels, y * Width, Width);
                    }
                }
            }
            finally { converted.UnlockBits(data); }
        }
    }

    sealed class SoftwareCanvas(int width, int height)
    {
        readonly int[] _pixels = new int[width * height];

        public void DrawTexturedTriangle(
            SpineTexture texture,
            PointF s0,
            PointF s1,
            PointF s2,
            PointF d0,
            PointF d1,
            PointF d2,
            SpineColor color)
        {
            var area = Edge(d0, d1, d2.X, d2.Y);
            if (Math.Abs(area) < 0.0001f) return;
            var inverse = 1f / area;
            var minimumX = Math.Max(0, (int)Math.Floor(Math.Min(d0.X, Math.Min(d1.X, d2.X))));
            var maximumX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(d0.X, Math.Max(d1.X, d2.X))));
            var minimumY = Math.Max(0, (int)Math.Floor(Math.Min(d0.Y, Math.Min(d1.Y, d2.Y))));
            var maximumY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(d0.Y, Math.Max(d1.Y, d2.Y))));
            if (minimumX > maximumX || minimumY > maximumY) return;
            var cr = Math.Clamp((int)Math.Round(color.R * 255), 0, 255);
            var cg = Math.Clamp((int)Math.Round(color.G * 255), 0, 255);
            var cb = Math.Clamp((int)Math.Round(color.B * 255), 0, 255);
            var ca = Math.Clamp((int)Math.Round(color.A * 255), 0, 255);
            for (var y = minimumY; y <= maximumY; y++)
            {
                var py = y + 0.5f;
                var target = y * width + minimumX;
                for (var x = minimumX; x <= maximumX; x++, target++)
                {
                    var px = x + 0.5f;
                    var w0 = Edge(d1, d2, px, py) * inverse;
                    var w1 = Edge(d2, d0, px, py) * inverse;
                    var w2 = 1f - w0 - w1;
                    if (w0 < -0.0005f || w1 < -0.0005f || w2 < -0.0005f) continue;
                    var u = s0.X * w0 + s1.X * w1 + s2.X * w2;
                    var v = s0.Y * w0 + s1.Y * w1 + s2.Y * w2;
                    var tx = Math.Clamp((int)u, 0, texture.Width - 1);
                    var ty = Math.Clamp((int)v, 0, texture.Height - 1);
                    var source = texture.Pixels[ty * texture.Width + tx];
                    var sourceAlpha = ((source >>> 24) & 255) * ca / 255;
                    if (sourceAlpha == 0) continue;
                    var sourceRed = ((source >>> 16) & 255) * cr / 255;
                    var sourceGreen = ((source >>> 8) & 255) * cg / 255;
                    var sourceBlue = (source & 255) * cb / 255;
                    var destination = _pixels[target];
                    var destinationAlpha = (destination >>> 24) & 255;
                    var inverseAlpha = 255 - sourceAlpha;
                    var outputAlpha = sourceAlpha + destinationAlpha * inverseAlpha / 255;
                    var outputRed = sourceRed * sourceAlpha / 255 + ((destination >>> 16) & 255) * inverseAlpha / 255;
                    var outputGreen = sourceGreen * sourceAlpha / 255 + ((destination >>> 8) & 255) * inverseAlpha / 255;
                    var outputBlue = sourceBlue * sourceAlpha / 255 + (destination & 255) * inverseAlpha / 255;
                    _pixels[target] = (outputAlpha << 24) | (Math.Min(255, outputRed) << 16) |
                                      (Math.Min(255, outputGreen) << 8) | Math.Min(255, outputBlue);
                }
            }
        }

        public Bitmap ToBitmap()
        {
            var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            result.SetResolution(96, 96);
            var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                if (data.Stride == width * 4)
                    Marshal.Copy(_pixels, 0, data.Scan0, _pixels.Length);
                else
                {
                    var row = new int[width];
                    for (var y = 0; y < height; y++)
                    {
                        Array.Copy(_pixels, y * width, row, 0, width);
                        Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, width);
                    }
                }
            }
            finally { result.UnlockBits(data); }
            return result;
        }

        static float Edge(PointF a, PointF b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
    }

    sealed class SpineTexturePackage(Dictionary<string, Bitmap> pages) : IDisposable
    {
        public Dictionary<string, Bitmap> Pages { get; } = pages;
        public void Dispose() { foreach (var page in Pages.Values) page.Dispose(); Pages.Clear(); }
    }

    sealed record SpineSetup(
        List<SpineBone> Bones,
        List<SpineSlot> Slots,
        Dictionary<string, Dictionary<string, JsonElement>> Attachments);

    sealed class SpineBone
    {
        public string Name { get; init; } = "";
        public int ParentIndex { get; init; }
        public double SetupX { get; init; }
        public double SetupY { get; init; }
        public double SetupRotation { get; init; }
        public double SetupScaleX { get; init; } = 1;
        public double SetupScaleY { get; init; } = 1;
        public double SetupShearX { get; init; }
        public double SetupShearY { get; init; }
        public double X, Y, Rotation, ScaleX, ScaleY, ShearX, ShearY;
        public double A, B, C, D, WorldX, WorldY;
        public void Reset()
        {
            X = SetupX; Y = SetupY; Rotation = SetupRotation; ScaleX = SetupScaleX; ScaleY = SetupScaleY;
            ShearX = SetupShearX; ShearY = SetupShearY;
        }
        public SpineBone Copy() => (SpineBone)MemberwiseClone();
    }

    sealed class SpineSlot
    {
        public string Name { get; init; } = "";
        public int BoneIndex { get; init; }
        public string SetupAttachment { get; init; } = "";
        public SpineColor SetupColor { get; init; } = SpineColor.White;
    }

    readonly record struct SpineColor(float R, float G, float B, float A)
    {
        public static SpineColor White => new(1, 1, 1, 1);
        public static SpineColor Lerp(SpineColor a, SpineColor b, float t) =>
            new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    sealed class SpineAtlas
    {
        public List<SpinePage> Pages { get; } = [];
        public Dictionary<string, SpineRegion> Regions { get; } = new(StringComparer.Ordinal);

        public static SpineAtlas Parse(string text)
        {
            var atlas = new SpineAtlas();
            var lines = text.Replace("\r", "").Split('\n');
            SpinePage? page = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (string.IsNullOrWhiteSpace(raw) || char.IsWhiteSpace(raw[0]) || raw.Contains(':')) continue;
                var name = raw.Trim();
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var j = i + 1;
                while (j < lines.Length && (string.IsNullOrWhiteSpace(lines[j]) || lines[j].Contains(':')))
                {
                    var line = lines[j].Trim();
                    var colon = line.IndexOf(':');
                    if (colon > 0) values[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    j++;
                }
                if (values.TryGetValue("size", out var pageSize))
                {
                    var pair = Pair(pageSize);
                    page = new SpinePage(name, pair[0], pair[1]);
                    atlas.Pages.Add(page);
                    continue;
                }
                if (page is null) continue;
                var bounds = values.TryGetValue("bounds", out var modernBounds) ? Numbers(modernBounds, 4) : null;
                var xy = Pair(values.GetValueOrDefault("xy", "0,0"));
                var size = Pair(values.GetValueOrDefault("size", "0,0"));
                var x = bounds?[0] ?? xy[0];
                var y = bounds?[1] ?? xy[1];
                var width = bounds?[2] ?? size[0];
                var height = bounds?[3] ?? size[1];
                var offsets = values.TryGetValue("offsets", out var modernOffsets) ? Numbers(modernOffsets, 4) : null;
                var offset = Pair(values.GetValueOrDefault("offset", "0,0"));
                var original = Pair(values.GetValueOrDefault("orig", $"{width},{height}"));
                var degrees = values.GetValueOrDefault("rotate", "0").Equals("true", StringComparison.OrdinalIgnoreCase)
                    ? 90
                    : int.TryParse(values.GetValueOrDefault("rotate", "0"), out var rotate) ? rotate : 0;
                var trimWidth = degrees is 90 or 270 ? height : width;
                var trimHeight = degrees is 90 or 270 ? width : height;
                atlas.Regions[name] = new SpineRegion(
                    page.Name, page.Width, page.Height, x, y, width, height,
                    offsets?[2] ?? original[0] switch { 0 => trimWidth, var value => value },
                    offsets?[3] ?? original[1] switch { 0 => trimHeight, var value => value },
                    offsets?[0] ?? offset[0],
                    offsets?[1] ?? offset[1],
                    degrees);
            }
            return atlas;
        }

        static int[] Pair(string value) => Numbers(value, 2) ?? [0, 0];
        static int[]? Numbers(string value, int count)
        {
            var values = value.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length < count) return null;
            var result = new int[count];
            for (var i = 0; i < count; i++) if (!int.TryParse(values[i], out result[i])) result[i] = 0;
            return result;
        }
    }

    sealed record SpinePage(string Name, int Width, int Height);

    sealed record SpineRegion(
        string PageName,
        int PageWidth,
        int PageHeight,
        int X,
        int Y,
        int Width,
        int Height,
        int OriginalWidth,
        int OriginalHeight,
        int OffsetX,
        int OffsetY,
        int Degrees)
    {
        public PointF Map(double u, double v)
        {
            var px = Degrees switch
            {
                90 => X + v * Width,
                180 => X + (1 - u) * Width,
                270 => X + (1 - v) * Width,
                _ => X + u * Width
            };
            var py = Degrees switch
            {
                90 => Y + (1 - u) * Height,
                180 => Y + (1 - v) * Height,
                270 => Y + u * Height,
                _ => Y + v * Height
            };
            return new PointF((float)px, (float)py);
        }
    }
}
