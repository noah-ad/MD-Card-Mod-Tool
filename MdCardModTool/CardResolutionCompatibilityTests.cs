using System;
using System.Collections.Generic;
using System.Diagnostics;
using Point = System.Drawing.Point;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class CardResolutionCompatibilityTests
{
    internal static void Run(string gameRoot, string output)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        output = Path.GetFullPath(output);
        if (StandaloneResourceService.IsInside(output, gameRoot)) throw new ArgumentException("Test output must be outside the game.");
        Directory.CreateDirectory(output);
        var engine = new ModEngine();
        var frames = BuiltInCardFrameCatalog.Load();
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in new[] { "4007", "7687" })
        {
            string streaming = IndexService.StreamingRoot(gameRoot);
            TexRef source = IndexService.CardIllustrationBundleCandidates(streaming, id)
                .SelectMany(path => engine.ScanBundle(path, streaming, "游戏内图片", false).Textures)
                .First(x => x.CardKey == id && CardTextureRules.IsCardArt(x));
            originals.TryAdd(source.BundlePath, Hash(source.BundlePath));
            foreach (string kind in new[] { "本地卡图", "游戏内图片" })
            foreach ((int width, int height) in new[] { (512, 512), (1024, 1024), (2048, 2048), (1408, 2048) })
            {
                string mirror = Path.Combine(output, $"case-{id}-{kind}-{width}x{height}");
                var texture = Fixture(source, mirror, kind, width, height, engine);
                string fixtureHash = Hash(texture.BundlePath);
                Require(CardTextureRules.IsCardArt(texture), $"{id} {kind} {width}x{height} recognized");
                var missing = IndexService.ScanMissingLocalCard(mirror, new GameIndex(), id);
                Require(missing.Found && missing.Textures.Any(x => x.Width == width && x.Height == height), "missing-card scan includes resized card");
                using (var main = Main(mirror))
                {
                    texture.Width = texture.Height = 0; // Reproduce stale/unknown cached metadata.
                    Task ready = main.PrepareCardArtAsync(texture);
                    PumpUntil(() => ready.IsCompleted, "prepare resized card");
                    ready.GetAwaiter().GetResult();
                    Require(texture.Width == width && texture.Height == height, "shared editor entrance repairs dimensions before qualification");
                }
                if (width == 1024 && kind == "游戏内图片") TestActualEntrance(mirror, texture, frames);
                else TestEditor(mirror, texture, frames);
                TestFramePreview(texture, engine, frames);
                if (width == height)
                {
                    var compatible = CardFrameCatalog.CompatibleFrames(frames, width, height).ToList();
                    Require(compatible.Count > 0 && compatible.All(x => !CardFrameCatalog.IsPendulum(x.Name)), "square high-resolution card never gets pendulum preview frame");
                    var mapping = GameTextureDisplayMapping.ResolveForInstalledPreview(texture);
                    Require(!mapping.RequiresMapping && mapping.DisplayWidth == width, "native resolution retained for replacement");
                    byte[] replacement = Pattern(width, height);
                    engine.Replace(texture, mapping.EncodeForStorage(replacement), Path.Combine(mirror, "_MD卡图备份", kind));
                    Require(SamePixels(replacement, engine.DecodePng(texture)), "native replacement pixels preserved");
                }
                byte[] overframe = AstellarOverFrameComposer.Compose(Pattern(704, 1024), AstellarOverFrameTemplateCatalog.Load("card_frame01")).GamePng;
                engine.Replace(texture, overframe, Path.Combine(mirror, "_MD卡图备份", kind));
                Require(SamePixels(overframe, engine.DecodePng(texture)), "overframe write / reopen RGBA");
                Require(new ModPackageService().RefreshFlags(mirror, new[] { texture }) == 1, "resized card tracked as a Mod");
                File.Copy(Path.Combine(mirror, "_MD卡图备份", kind, texture.RelativeBundlePath), texture.BundlePath, true);
                engine.DecodePng(texture);
                Require(Hash(texture.BundlePath) == fixtureHash && texture.Width == width && texture.Height == height, "first backup restores original fixture resolution");
            }
        }

        string local = IndexService.FindLocalRoot(gameRoot)!;
        var pendulum = IndexService.CardIllustrationBundleCandidates(local, "19716")
            .SelectMany(path => engine.ScanBundle(path, local, "本地卡图", false).Textures)
            .Single(x => x.CardKey == "19716" && CardTextureRules.IsPendulumCanvas(x.Width, x.Height));
        originals.TryAdd(pendulum.BundlePath, Hash(pendulum.BundlePath));
        string pendulumMirror = Path.Combine(output, "pendulum-1024x2048");
        var highPendulum = Fixture(pendulum, pendulumMirror, "本地卡图", 1024, 2048, engine);
        var pendulumFrames = CardFrameCatalog.CompatibleFrames(frames, 1024, 2048).ToList();
        Require(pendulumFrames.Count == 6 && pendulumFrames.All(x => CardFrameCatalog.IsPendulum(x.Name)), "high-resolution pendulum frame filtering");
        var pendulumMapping = GameTextureDisplayMapping.ResolveForInstalledPreview(highPendulum, "card_frame14");
        Console.WriteLine($"pendulum display={pendulumMapping.DisplayWidth}x{pendulumMapping.DisplayHeight}");
        Require(pendulumMapping.Kind == TextureDisplayMappingKind.PendulumCardArt
            && pendulumMapping.DisplayHeight == (int)Math.Round(1024.0 * 452 / 605), "high-resolution pendulum visible-window mapping");
        string image = Path.Combine(output, "circle.png");
        File.WriteAllBytes(image, Pattern(605, 452));
        using (var crop = new ImageCropForm(image, pendulumMapping.DisplayWidth, pendulumMapping.DisplayHeight,
            "high-resolution regression", frames, "card_frame14", displayMapping: pendulumMapping)
            { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) })
        {
            crop.Show();
            var canvas = (CropCanvas)Field(typeof(ImageCropForm), "_canvas").GetValue(crop)!;
            PumpUntil(() => canvas.HasFrame, "high-resolution pendulum crop frame");
            Method(typeof(ImageCropForm), "ConfirmCrop").Invoke(crop, null);
            Require(crop.OutputStoragePng != null, "actual high-resolution crop uses game UV output");
            engine.Replace(highPendulum, crop.OutputStoragePng!, Path.Combine(pendulumMirror, "_MD卡图备份", "本地卡图"));
            byte[] stored = engine.DecodePng(highPendulum);
            Require(SamePixels(stored, crop.OutputStoragePng!), "high-resolution pendulum replacement not resized twice");
            byte[] displayed = pendulumMapping.DecodeForDisplay(stored);
            Require(IsRound(displayed), "high-resolution pendulum main preview circle stays round");
            byte[] installed = CardFrameRenderer.ComposeStoredArtPreview(stored, engine.DecodePng(frames.Single(x => x.Name == "card_frame14")));
            using var frameBitmap = FrameComposer.BitmapFrom(engine.DecodePng(frames.Single(x => x.Name == "card_frame14")));
            var artWindow = System.Drawing.Rectangle.Round(CardFrameRenderer.FindArtWindow(frameBitmap));
            artWindow.Inflate(-2, -2);
            Require(IsRound(installed, artWindow), "high-resolution pendulum card-frame preview circle stays round");
            File.WriteAllBytes(Path.Combine(output, "pendulum-display.png"), displayed);
            File.WriteAllBytes(Path.Combine(output, "pendulum-installed.png"), installed);
        }

        var visual = Ref("视觉资源", "4007", "4007", 1024, 1024);
        var atlas = Ref("本地卡图", "4007", "P4007", 2048, 2048);
        var invalid = Ref("游戏内图片", "0", "0", 1024, 1024);
        Require(!CardTextureRules.IsCardArt(visual) && !CardTextureRules.IsCardArt(atlas) && !CardTextureRules.IsCardArt(invalid), "numeric visuals / summon atlas / invalid card IDs rejected");
        Require(!GameTextureDisplayMapping.Resolve(Ref("视觉资源", "4007", "wallpaper", 1024, 2048)).RequiresMapping, "tall visuals never get pendulum mapping");
        var renamedCard = Ref("本地卡图", "4007", "P4007", 1024, 1024);
        renamedCard.BundlePath = IndexService.CardIllustrationRelativePath("4007");
        Require(CardTextureRules.IsCardArt(renamedCard), "renamed illustration recognized by its actual card bundle path");
        var cached = new GameIndex(); cached.Textures.AddRange(new[] { renamedCard, atlas });
        Require(IndexService.RemoveSpineAtlasParts(cached) == 1 && cached.Textures.Single() == renamedCard, "cached atlas cleanup preserves renamed illustration Mods");

        var source4007 = IndexService.CardIllustrationBundleCandidates(IndexService.StreamingRoot(gameRoot), "4007")
            .SelectMany(path => engine.ScanBundle(path, IndexService.StreamingRoot(gameRoot), "游戏内图片", false).Textures).First();
        string unsupportedMirror = Path.Combine(output, "unsupported-1024x1536");
        var unsupported = Fixture(source4007, unsupportedMirror, "游戏内图片", 1024, 1536, engine);
        using (var main = Main(unsupportedMirror))
        {
            unsupported.Width = unsupported.Height = 512;
            Task task = main.PrepareCardArtAsync(unsupported);
            PumpUntil(() => task.IsCompleted, "unsupported-layout diagnostic");
            try { task.GetAwaiter().GetResult(); throw new Exception("Unsupported layout accepted."); }
            catch (InvalidDataException ex) { Require(ex.Message.Contains("1024×1536"), "unsupported real dimensions reported instead of a missing-selection message"); }
        }
        foreach (var pair in originals) Require(Hash(pair.Key) == pair.Value, "game bundle unchanged " + Path.GetFileName(pair.Key));
        Console.WriteLine("PASS resolution-independent cards / actual editor entrance / stale dimensions / pendulum / restore; gameWrites=False; remoteBundleAvailable=False");
    }

    private static TexRef Fixture(TexRef source, string mirror, string kind, int width, int height, ModEngine engine)
    {
        string fixtureRoot = Path.Combine(mirror, "fixture");
        string fixturePath = Path.Combine(fixtureRoot, source.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
        File.Copy(source.BundlePath, fixturePath, true);
        var seed = engine.ScanBundle(fixturePath, fixtureRoot, kind, false).Textures.Single(x => x.PathId == source.PathId);
        engine.Replace(seed, Pattern(width, height), Path.Combine(fixtureRoot, "backups"));
        string account = Path.Combine(mirror, "LocalData", "test", "0000");
        Directory.CreateDirectory(account);
        IndexService.SetPreferredLocalRoot(mirror, account);
        string root = kind == "本地卡图" ? account : IndexService.StreamingRoot(mirror);
        string path = Path.Combine(root, source.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(fixturePath, path, true);
        return engine.ScanBundle(path, root, kind, false).Textures.Single(x => x.PathId == source.PathId);
    }

    private static MainForm Main(string mirror)
    {
        var main = new MainForm(null, null, false) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        Field(typeof(MainForm), "_assetRoot").SetValue(main, null); // No auto-scan or writes to the real game.
        Field(typeof(MainForm), "_gameRoot").SetValue(main, mirror);
        return main;
    }

    private static void TestActualEntrance(string mirror, TexRef texture, IReadOnlyList<TexRef> frames)
    {
        using var main = Main(mirror);
        var all = (List<TexRef>)Field(typeof(MainForm), "_textures").GetValue(main)!;
        all.Clear(); all.Add(texture); all.AddRange(frames);
        var visible = (List<TexRef>)Field(typeof(MainForm), "_visibleTextures").GetValue(main)!;
        visible.Clear(); visible.Add(texture);
        var list = (ListView)Field(typeof(MainForm), "_list").GetValue(main)!;
        list.VirtualListSize = 1;
        main.Show(); list.SelectedIndices.Add(0);
        texture.Width = texture.Height = 0;
        bool rendered = false;
        var watch = Stopwatch.StartNew();
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            var editor = Application.OpenForms.OfType<OverFrameFrameEditorForm>().FirstOrDefault();
            if (editor != null && Field(typeof(OverFrameFrameEditorForm), "_outputBytes").GetValue(editor) is byte[])
            {
                rendered = true; timer.Stop(); editor.DialogResult = DialogResult.Cancel; editor.Close();
            }
            if (watch.ElapsedMilliseconds > 30000) { Console.Error.WriteLine("FAIL actual editor entrance timeout"); Environment.Exit(2); }
        };
        timer.Start();
        var task = (Task)Method(typeof(MainForm), "OpenCardFrameStudioAsync").Invoke(main, null)!;
        PumpUntil(() => task.IsCompleted, "actual selected-card entrance");
        task.GetAwaiter().GetResult();
        Require(rendered, texture.CardKey + " 1024x1024 selected-card action opens and renders editor");
        main.Close();
    }

    private static void TestEditor(string mirror, TexRef texture, IReadOnlyList<TexRef> frames)
    {
        using var editor = new OverFrameFrameEditorForm(mirror, texture, frames)
        { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        editor.Show();
        PumpUntil(() => Field(typeof(OverFrameFrameEditorForm), "_outputBytes").GetValue(editor) is byte[], "overframe render");
        Require(true, texture.CardKey + " resized original illustration opens overframe editor");
        editor.Close();
    }

    private static void TestFramePreview(TexRef texture, ModEngine engine, IReadOnlyList<TexRef> frames)
    {
        using var preview = new FramePreviewForm(engine, texture, frames)
        { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
        preview.Show();
        var image = (PictureBox)Field(typeof(FramePreviewForm), "_preview").GetValue(preview)!;
        PumpUntil(() => image.Image != null, "resized frame preview");
        Require(image.Image!.Width == 704 && image.Image.Height == 1024, "frame preview normalized without changing stored resolution");
        preview.Close();
    }

    private static TexRef Ref(string kind, string id, string name, int width, int height) => new()
    { BundlePath = "unrelated-bundle", RelativeBundlePath = "unrelated-bundle", SourceKind = kind, CardKey = id, Name = name, Width = width, Height = height };
    private static byte[] Pattern(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(25, 65, 140, 255));
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if ((x - width / 2f) * (x - width / 2f) + (y - height / 2f) * (y - height / 2f) < 85 * 85) image[x, y] = new Rgba32(245, 25, 25, 255);
        using var stream = new MemoryStream(); image.Save(stream, new PngEncoder { TransparentColorMode = PngTransparentColorMode.Preserve }); return stream.ToArray();
    }
    private static bool IsRound(byte[] png, System.Drawing.Rectangle? region = null)
    {
        using var image = Image.Load<Rgba32>(png);
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        var area = region ?? new System.Drawing.Rectangle(0, 0, image.Width, image.Height);
        for (int y = area.Top; y < area.Bottom; y++) for (int x = area.Left; x < area.Right; x++)
            if (image[x, y].R > 180 && image[x, y].B < 80) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right - left > 80 && Math.Abs((right - left) - (bottom - top)) <= 2;
    }
    private static bool SamePixels(byte[] a, byte[] b)
    {
        using var ia = Image.Load<Rgba32>(a); using var ib = Image.Load<Rgba32>(b);
        if (ia.Size != ib.Size) return false;
        var pa = new Rgba32[ia.Width * ia.Height]; var pb = new Rgba32[pa.Length];
        ia.CopyPixelDataTo(pa); ib.CopyPixelDataTo(pb); return pa.AsSpan().SequenceEqual(pb);
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void PumpUntil(Func<bool> predicate, string name)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.ElapsedMilliseconds < 30000) { Application.DoEvents(); Thread.Sleep(10); }
        Require(predicate(), name);
    }
    private static void Require(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
}
