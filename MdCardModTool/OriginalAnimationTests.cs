using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class OriginalAnimationTests
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        string png = Path.Combine(output, "frame.png");
        using (var image = new Image<Rgba32>(160, 90))
        {
            for (int y = 0; y < 90; y++) for (int x = 0; x < 160; x++) image[x,y] = new Rgba32((byte)x, (byte)y, 199, 255);
            image.SaveAsPng(png);
        }
        using var media = MonsterAnimationMedia.ExtractSequenceAsync(new[] { png }, 24, 1, 0).GetAwaiter().GetResult();
        using (var frame = Image.Load<Rgba32>(media.FramePaths[0]))
            Require(frame.Width == 160 && frame.Height == 90 && frame[79,40].Equals(new Rgba32(79,40,199,255)), "native extraction pixels");
        string video = Path.Combine(output, "lossless.mkv");
        var start = new System.Diagnostics.ProcessStartInfo(MonsterAnimationMedia.FindFfmpeg() ?? throw new Exception("FFmpeg missing")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-v", "error", "-y", "-loop", "1", "-i", png, "-frames:v", "3", "-r", "24", "-c:v", "ffv1", "-pix_fmt", "bgra", video }) start.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(start)!) { process.WaitForExit(); Require(process.ExitCode == 0, "lossless video fixture"); }
        using (var extracted = MonsterAnimationMedia.ExtractAsync(video, 24, 3, 0).GetAwaiter().GetResult())
        using (var decoded = Image.Load<Rgba32>(extracted.FramePaths[0]))
            Require(decoded.Width == 160 && decoded.Height == 90 && decoded[79,40].Equals(new Rgba32(79,40,199,255)), "native video extraction");
        var original = MonsterAnimationIndexService.Find(root, "10001");
        var hashes = original.Assets.Select(a => a.BundlePath).Distinct().ToDictionary(p => p, Hash);
        var target = new MonsterAnimationSet { CardId = original.CardId, Assets = original.Assets.Select(a => {
            string path = Path.Combine(output, "mirror", a.StorageKind, a.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(a.BundlePath, path, true);
            return MonsterAnimationTransferService.AtPath(a, path);
        }).ToList() };
        var service = new MonsterAnimationService();
        var template = service.ReadTemplate(target);
        var paths = Enumerable.Repeat(png, 3).ToArray();
        var plan = MonsterAnimationBuilder.PlanOriginal(paths, 2048);
        MonsterAnimationBuilder.ValidateOriginalCapacity(plan, target);
        using var built = MonsterAnimationBuilder.BuildOriginal(paths, "10001", 24, 100, template, 2048);
        Require(built.Uncompressed && built.AtlasImage[81,42].Equals(new Rgba32(79,40,199,255)), "no scaling / no block compression");
        try { service.Apply(output, target, built, n => { if (n == 2) throw new IOException("test rollback"); }); throw new Exception("missing failure"); }
        catch (IOException e) when (e.Message == "test rollback") { }
        Require(target.Assets.All(a => Hash(a.BundlePath) == hashes[original.Assets.First(o => o.RelativeBundlePath == a.RelativeBundlePath).BundlePath]), "rollback");
        service.Apply(output, target, built);
        Require(target.Textures.All(a => new ModEngine().ReadAnimationTextureMetadata(a).TextureFormat == 4), "RGBA32 reopened");
        service.Restore(output, target);
        Require(target.Assets.All(a => Hash(a.BundlePath) == hashes[original.Assets.First(o => o.RelativeBundlePath == a.RelativeBundlePath).BundlePath]), "restore");
        Require(hashes.All(h => Hash(h.Key) == h.Value), "game files unchanged");
        string big = Path.Combine(output, "big.png");
        using (var image = new Image<Rgba32>(1200, 675, new Rgba32(13,29,47,255))) image.SaveAsPng(big);
        var many = Enumerable.Repeat(big, 4).ToArray();
        var multiPlan = MonsterAnimationBuilder.PlanOriginal(many, 2048);
        Require(multiPlan.PageCount == 2, "multipage plan");
        using var multi = MonsterAnimationBuilder.BuildOriginal(many, "10001", 24, 100, template, 2048);
        Require(multi.Hd.Pages.Count() == 2 && multi.AtlasText.Contains("P10001_page1.png") && multi.Hd.ExtraPages[0][2,2].Equals(new Rgba32(13,29,47,255)), "multipage pixels/names");
        bool rejected = false;
        try { MonsterAnimationBuilder.ValidateOriginalCapacity(multiPlan, target); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "insufficient registered pages rejected");
        rejected = false;
        try { MonsterAnimationBuilder.PlanOriginal(Enumerable.Repeat(big, 600).ToArray(), 8192); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "memory budget before atlas allocation");
        var multiOriginal = MonsterAnimationIndexService.Find(root, "13669");
        var multiHashes = multiOriginal.Assets.Select(a => a.BundlePath).Distinct().ToDictionary(p => p, Hash);
        string multiRoot = Path.Combine(output, "multi-workspace");
        var multiTarget = new MonsterAnimationSet { CardId = "13669", Assets = multiOriginal.Assets.Select(a => {
            string path = Path.Combine(multiRoot, "mirror", a.StorageKind, a.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(a.BundlePath, path, true);
            return MonsterAnimationTransferService.AtPath(a, path);
        }).ToList() };
        var multiTemplate = service.ReadTemplate(multiTarget);
        using var hd = MonsterAnimationBuilder.BuildOriginal(many, "13669", 24, 100, multiTemplate, 2048);
        using var sd = MonsterAnimationBuilder.BuildOriginal(many, "13669", 24, 100, multiTemplate, 4096);
        var combined = new MonsterAnimationBuildResult { Hd = hd.Hd, Sd = sd.Sd, Uncompressed = true, FrameCount = 4, FramesPerSecond = 24, DisplayWidth = hd.DisplayWidth, DisplayHeight = hd.DisplayHeight };
        service.Apply(multiRoot, multiTarget, combined);
        Require(hd.Hd.Pages.Count() == 2 && sd.Sd.Pages.Count() == 1, "real HD multipage / SD singlepage staged validation");
        service.Restore(multiRoot, multiTarget);
        Require(multiTarget.Assets.All(a => Hash(a.BundlePath) == multiHashes[multiOriginal.Assets.First(o => o.RelativeBundlePath == a.RelativeBundlePath).BundlePath]), "multipage restore");
        Require(multiHashes.All(h => Hash(h.Key) == h.Value), "multipage source unchanged");
        using var ordinary = MonsterAnimationBuilder.Build(paths, "10001", 24, 100, template, 2048);
        Require(!ordinary.Uncompressed && new ModEngine().EncodeAnimationAtlas(ordinary.Hd.AtlasImage).TextureFormat == 25, "ordinary mode still BC7");
        Console.WriteLine("PASS originalResolution/RGBA32/multipage/rollback/restore; gameWrites=False");
    }
    private static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
    private static void Require(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
}
