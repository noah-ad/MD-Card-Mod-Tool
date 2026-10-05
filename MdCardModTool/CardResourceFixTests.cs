using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class CardResourceFixTests
{
    internal static void Run(string gameRoot, string output)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        output = Path.GetFullPath(output);
        if (StandaloneResourceService.IsInside(output, gameRoot)) throw new ArgumentException("Tests require an output directory outside the game.");
        Directory.CreateDirectory(output);
        string streaming = IndexService.StreamingRoot(gameRoot);
        string builtin = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
        var originals = Directory.EnumerateFiles(streaming, "*", SearchOption.AllDirectories).Where(IndexService.IsUnityBundle)
            .Append(builtin).ToDictionary(x => x, Hash, StringComparer.OrdinalIgnoreCase);
        var engine = new ModEngine();
        var cards = originals.Keys.Where(x => x != builtin).SelectMany(x => engine.ScanBundle(x, streaming, "游戏内图片", false).Textures)
            .Where(CardTextureRules.IsCardArt).ToList();
        File.WriteAllText(Path.Combine(output, "builtin-cards.json"), JsonSerializer.Serialize(cards.Select(x => new { x.CardKey, Name = CardCatalogService.LoadBestAvailable().Find(x.CardKey)?.Name(AppLanguage.SimplifiedChinese), x.RelativeBundlePath, x.Width, x.Height }), new JsonSerializerOptions { WriteIndented = true }));
        Require(cards.Any(x => x.CardKey == "4007") && cards.Any(x => x.CardKey == "7687"), "Blue-Eyes 4007 / Junk Synchron 7687 detected as built-in cards");
        Console.WriteLine($"builtInTextures={cards.Count}; distinctCards={cards.Select(x => x.CardKey).Distinct().Count()}");
        foreach (var card in cards) Require(IndexWorkspaceGuard.Belongs(gameRoot, card), "built-in root " + card.CardKey, quiet: true);

        string mirror = Path.Combine(output, "mirror-" + Guid.NewGuid().ToString("N"));
        string local = Path.Combine(mirror, "LocalData", "test-account", "0000");
        Directory.CreateDirectory(local);
        foreach (string id in new[] { "4007", "7687" })
        {
            var card = CopyTexture(cards.First(x => x.CardKey == id), mirror, engine);
            var missing = IndexService.ScanMissingLocalCard(mirror, new GameIndex(), id);
            Require(missing.Found && missing.Textures.Single().SourceKind == "游戏内图片" && IndexWorkspaceGuard.Belongs(mirror, missing.Textures.Single()), id + " locate / root / index-save guard");
            TestBestSelection(card);
            TestOverFrameEditor(mirror, card, output);
            string backup = Path.Combine(mirror, "_MD卡图备份", card.SourceKind);
            byte[] art = Pattern(704, 1024);
            var composed = AstellarOverFrameComposer.Compose(art, AstellarOverFrameTemplateCatalog.Load("card_frame01"));
            byte[] png = composed.GamePng;
            string initialHash = Hash(card.BundlePath);
            engine.Replace(card, png, backup);
            Require(PixelsEqual(png, engine.DecodePng(card)), id + " built-in overframe write/reopen RGBA");
            Require(new ModPackageService().RefreshFlags(mirror, new[] { card }) == 1, id + " Mod list tracks StreamingAssets changes");
            File.Copy(Path.Combine(backup, card.RelativeBundlePath), card.BundlePath, true);
            engine.DecodePng(card);
            Require(Hash(card.BundlePath) == initialHash && card.Width == 512 && card.Height == 512, id + " first backup / restore");
        }

        var cachedCards = new GameIndex();
        var recoveredCards = IndexService.ScanMissingLocalCard(mirror, cachedCards, "0");
        Require(recoveredCards.Textures.Count == 2 && recoveredCards.Textures.All(x => x.SourceKind == "游戏内图片"), "old-index background merge discovers built-in illustrations");
        cachedCards.Textures.AddRange(recoveredCards.Textures);
        Require(IndexService.ScanMissingLocalCard(mirror, cachedCards, "0").Textures.Count == 0, "built-in index merge does not duplicate known cards");

        // Use a genuine pendulum bundle, in both resource roots, with a stale square
        // cache and an incorrect frame hint. Verify geometry after the game UV mapping.
        string? liveLocal = IndexService.FindLocalRoot(gameRoot);
        TexRef? pendulum = null;
        foreach (var entry in CardCatalogService.LoadBestAvailable().Entries.Where(x => CardFrameCatalog.IsPendulum(CardFrameCatalog.RecommendedKey(x, 512, 1024))))
        {
            foreach (string path in IndexService.CardIllustrationBundleCandidates(liveLocal!, entry.CardId.ToString()))
            {
                var candidate = engine.ScanBundle(path, liveLocal!, "本地卡图", false).Textures.FirstOrDefault(x => x.Width == 512 && x.Height == 1024);
                if (candidate != null) { pendulum = candidate; break; }
            }
            if (pendulum != null) break;
        }
        if (pendulum == null) throw new Exception("A real pendulum illustration is required for geometry regression.");
        originals.TryAdd(pendulum.BundlePath, Hash(pendulum.BundlePath));
        foreach (string kind in new[] { "本地卡图", "游戏内图片" })
        {
            string root = kind == "本地卡图" ? local : IndexService.StreamingRoot(mirror);
            string path = Path.Combine(root, pendulum.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(pendulum.BundlePath, path, true);
            var texture = engine.ScanBundle(path, root, kind, false).Textures.Single();
            texture.Width = texture.Height = 512;
            texture.Category = "超框卡图";
            engine.DecodePng(texture);
            Require(texture.Width == 512 && texture.Height == 1024, kind + " stale dimensions repaired on successful decode");
            texture.Category = "超框卡图";
            var mapping = GameTextureDisplayMapping.ResolveForInstalledPreview(texture, "card_frame01");
            Require(mapping.Kind == TextureDisplayMappingKind.PendulumCardArt && mapping.DisplayHeight == 383 && !mapping.StoredArtWindow.IsEmpty,
                kind + " installed pendulum mapping independent of old category/frame hint");
            byte[] normal = Pattern(mapping.DisplayWidth, mapping.DisplayHeight);
            byte[] encoded = mapping.EncodeForStorage(normal);
            engine.Replace(texture, encoded, Path.Combine(mirror, "_MD卡图备份", kind));
            byte[] stored = engine.DecodePng(texture);
            Require(PixelsEqual(encoded, stored), kind + " pendulum storage pixels / no second resize");
            byte[] display = mapping.DecodeForDisplay(stored);
            using var roundtrip = Image.Load<Rgba32>(display);
            Require(roundtrip.Width == mapping.DisplayWidth && roundtrip.Height == mapping.DisplayHeight && CircleIsRound(roundtrip),
                kind + " round-circle geometry survives visible-region replacement mapping");
            File.WriteAllBytes(Path.Combine(output, "pendulum-" + kind + ".png"), display);
        }
        var unrelated = new TexRef { BundlePath = "wallpaper", RelativeBundlePath = "wallpaper", Name = "Wallpaper", Width = 512, Height = 1024, SourceKind = "视觉资源" };
        Require(!GameTextureDisplayMapping.Resolve(unrelated, "card_frame14").RequiresMapping, "unrelated tall visuals are not converted");

        var symbols = engine.ScanBundle(builtin, gameRoot, VisualAssetIndexService.BuiltInSourceKind, false).Textures
            .Where(x => x.Category is "属性图标" or "星级图标" or "属性／星级界面图集").ToList();
        Require(symbols.Count >= 5 && symbols.Any(x => x.Name == "CardAttributeSet") && symbols.Any(x => x.Name == "tex_IconRank"), "attribute / level / rank / UI atlas located");
        string mirrorBuiltin = Path.Combine(mirror, "masterduel_Data", "data.unity3d");
        Directory.CreateDirectory(Path.GetDirectoryName(mirrorBuiltin)!);
        File.Copy(builtin, mirrorBuiltin, true);
        foreach (string sidecar in Directory.EnumerateFiles(Path.GetDirectoryName(builtin)!, "*.resource"))
            File.Copy(sidecar, Path.Combine(Path.GetDirectoryName(mirrorBuiltin)!, Path.GetFileName(sidecar)), true);
        foreach (var symbol in symbols)
        {
            byte[] png = engine.DecodePng(symbol);
            File.WriteAllBytes(Path.Combine(output, symbol.Name + ".png"), png);
            var copy = engine.ScanBundle(mirrorBuiltin, mirror, VisualAssetIndexService.BuiltInSourceKind, false).Textures.Single(x => x.Name == symbol.Name && x.PathId == symbol.PathId);
            using var changed = Image.Load<Rgba32>(png);
            changed[changed.Width / 2, changed.Height / 2] = new Rgba32(17, 83, 231, 127);
            byte[] expected = Encode(changed);
            engine.Replace(copy, expected, Path.Combine(mirror, "_MD卡图备份", copy.SourceKind));
            Require(PixelsEqual(expected, engine.DecodePng(copy)), symbol.Name + " modification / reopen RGBA");
        }
        string symbolsBackup = Path.Combine(mirror, "_MD卡图备份", VisualAssetIndexService.BuiltInSourceKind, "masterduel_Data", "data.unity3d");
        File.Copy(symbolsBackup, mirrorBuiltin, true);
        Require(Hash(mirrorBuiltin) == originals[builtin], "shared icons restore entire original bundle");
        foreach (var pair in originals) Require(Hash(pair.Key) == pair.Value, "source unchanged " + Path.GetFileName(pair.Key), quiet: true);
        Console.WriteLine("PASS built-in card / pendulum / shared symbols; gameWrites=False; ready=True");
    }

    private static TexRef CopyTexture(TexRef source, string gameRoot, ModEngine engine)
    {
        string root = IndexService.StreamingRoot(gameRoot);
        string path = Path.Combine(root, source.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(source.BundlePath, path, true);
        return engine.ScanBundle(path, root, source.SourceKind, false).Textures.Single(x => x.PathId == source.PathId && x.AssetFileName == source.AssetFileName);
    }

    private static void TestBestSelection(TexRef card)
    {
        using var form = new MainForm(null, null, false);
        var textures = (List<TexRef>)typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        textures.Clear(); textures.Add(card);
        var selected = typeof(MainForm).GetMethod("BestCardTexture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { card.CardKey });
        Require(ReferenceEquals(selected, card), card.CardKey + " card-name search selects built-in illustration");
    }

    private static void TestOverFrameEditor(string gameRoot, TexRef card, string output)
    {
        using var editor = new OverFrameFrameEditorForm(gameRoot, card, BuiltInCardFrameCatalog.Load())
        { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-32000, -32000) };
        editor.Show();
        var ready = typeof(OverFrameFrameEditorForm).GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var watch = Stopwatch.StartNew();
        while (ready.GetValue(editor) is not byte[] && watch.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(10); }
        Require(ready.GetValue(editor) is byte[], card.CardKey + " built-in original art opens unified overframe editor");
        editor.Close();
    }

    private static byte[] Pattern(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(20, 60, 140, 255));
        float cx = width / 2f, cy = height / 2f;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < 100 * 100) image[x, y] = new Rgba32(230, 40, 30, 255);
        return Encode(image);
    }
    private static bool CircleIsRound(Image<Rgba32> image)
    {
        int left = image.Width, top = image.Height, right = 0, bottom = 0;
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            if (image[x, y].R > 180 && image[x, y].B < 80) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return Math.Abs((right - left) - (bottom - top)) <= 2 && right - left > 190;
    }
    private static byte[] Encode(Image<Rgba32> image) { using var s = new MemoryStream(); image.Save(s, new PngEncoder { TransparentColorMode = PngTransparentColorMode.Preserve }); return s.ToArray(); }
    private static bool PixelsEqual(byte[] a, byte[] b) { using var ia = Image.Load<Rgba32>(a); using var ib = Image.Load<Rgba32>(b); if (ia.Size != ib.Size) return false; var pa = new Rgba32[ia.Width * ia.Height]; var pb = new Rgba32[pa.Length]; ia.CopyPixelDataTo(pa); ib.CopyPixelDataTo(pb); return pa.AsSpan().SequenceEqual(pb); }
    private static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    private static void Require(bool pass, string name, bool quiet = false) { if (!pass) throw new InvalidOperationException(name); if (!quiet) Console.WriteLine("PASS " + name); }
}
