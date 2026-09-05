using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class AnimationWriteTests
{
    public static void Run(string gameRoot, string output)
    {
        Directory.CreateDirectory(output);
        var original = MonsterAnimationIndexService.Find(gameRoot, "22524");
        var hashes = original.Assets.ToDictionary(a => a.BundlePath, a => Hash(a.BundlePath));
        var target = new MonsterAnimationSet { CardId = original.CardId, Assets = original.Assets.Select(a =>
        {
            string file = Path.Combine(output, "mirror", a.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(a.BundlePath, file, true);
            return MonsterAnimationTransferService.AtPath(a, file);
        }).ToList() };
        string lockedPath = target.Textures.First().BundlePath;
        var service = new MonsterAnimationService();
        Stopwatch timer = Stopwatch.StartNew();
        using (var reader = File.OpenRead(lockedPath))
        {
            try { service.Apply(output, target, null!); throw new Exception("Lock not detected before compression"); }
            catch (IOException ex) when (ex.Message.Contains("尚未开始")) { }
        }
        if (timer.Elapsed.TotalSeconds > 4) throw new Exception("Locked target did not fail promptly");
        long lockMs = timer.ElapsedMilliseconds;
        string[] frames = [Path.Combine(output, "a.png"), Path.Combine(output, "b.png")];
        using (Image<Rgba32> a = new(160,90,new Rgba32(200,30,40,255))) a.SaveAsPng(frames[0]);
        using (Image<Rgba32> b = new(160,90,new Rgba32(20,130,220,255))) b.SaveAsPng(frames[1]);
        using var built = MonsterAnimationBuilder.Build(frames, "22524", 12, 100, service.ReadTemplate(target), 4096);
        if (Math.Abs(built.DisplayWidth - 6720) > .001 || Math.Abs(built.DisplayHeight - 3780) > .001)
            throw new Exception("New 100% must equal old 140% geometry");
        var before = target.Assets.ToDictionary(a => a.BundlePath, a => Hash(a.BundlePath));
        try
        {
            service.Apply(output, target, built, count => { if (count == 2) throw new IOException("injected"); });
            throw new Exception("Missing injected failure");
        }
        catch(IOException ex) when(ex.Message == "injected") { }
        if (before.Any(x => Hash(x.Key) != x.Value)) throw new Exception("Rollback mismatch");
        timer.Restart();
        service.Apply(output, target, built);
        long applyMs = timer.ElapsedMilliseconds;
        MonsterAnimationCompatibilityValidator.Validate(target, false);
        using (var current = MonsterAnimationCurrentPreview.TryLoad(target)
            ?? throw new Exception("Written calibrated animation cannot be read"))
            if (current.ScalePercent != 100) throw new Exception("Calibrated scale does not round-trip");
        // Inspect both re-opened triplets, then render the selected HD pair.
        // Every edge must remain covered at every generated time sample.
        foreach (string tier in new[] { "HighEnd_HD", "SD" })
        {
            var pair = MonsterAnimationAssetPairing.FindComplete(target).First(p => p.Tier == tier);
            var engine = new ModEngine();
            using var json = System.Text.Json.JsonDocument.Parse(engine.ReadTextAsset(pair.Skeleton).Data);
            var root = json.RootElement;
            var body = root.GetProperty("bones").EnumerateArray().First(b => b.GetProperty("name").GetString() == "Body");
            if (body.GetProperty("y").GetDouble() != -120) throw new Exception("Viewport center mismatch");
            foreach (var anim in root.GetProperty("animations").EnumerateObject())
            {
                var bones = anim.Value.GetProperty("bones");
                if (bones.TryGetProperty("Body", out _)) throw new Exception("Unexpected video drift");
                foreach (var key in bones.GetProperty("root").GetProperty("scale").EnumerateArray())
                    if (key.GetProperty("x").GetDouble() != 1 || key.GetProperty("y").GetDouble() != 1)
                        throw new Exception("Hidden popup scale still shrinks video");
            }
        }
        using (var preview = Spine42PreviewRenderer.TryLoad(target, framesPerSecond: 12, maxFrames: 24, previewMaxEdge: 160)
            ?? throw new Exception("Written video could not be rendered"))
        {
            foreach (var frame in preview.Frames)
            {
                foreach (var point in new[] { new System.Drawing.Point(2,2), new System.Drawing.Point(frame.Width-3,2),
                    new System.Drawing.Point(2,frame.Height-3), new System.Drawing.Point(frame.Width-3,frame.Height-3) })
                    if (frame.GetPixel(point.X,point.Y).A < 240) throw new Exception("Video does not cover the viewport corners");
            }
            preview.Frames[0].Save(Path.Combine(output, "written-first-frame.png"));
            preview.Frames[^1].Save(Path.Combine(output, "written-last-frame.png"));
        }
        foreach (var asset in target.Assets)
        {
            string backup = Path.Combine(output,"_MD卡图备份",asset.ModSourceKind,asset.RelativeBundlePath);
            if(Hash(backup) != before[asset.BundlePath]) throw new Exception("Backup overwritten");
            File.Copy(backup,asset.BundlePath,true);
        }
        if(hashes.Any(x=>Hash(x.Key)!=x.Value)) throw new Exception("Real game changed");
        Console.WriteLine($"card=22524; lockFailMs={lockMs}; applyMs={applyMs}; sixBundleApply=True; flatVideoGeometryHD_SD=True; renderedCorners=True; rollback=True; restore=True; gameWrites=False; ready=True");
    }
    private static string Hash(string path) {using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s));}
}
