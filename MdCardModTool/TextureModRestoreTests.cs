using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class TextureModRestoreTests
{
    internal static void Run(string gameRoot, string output)
    {
        if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Tests must not write to real game resources.");
        Directory.CreateDirectory(output);
        var engine = new ModEngine();
        string realRoot = IndexService.StreamingRoot(gameRoot);
        var fixture = IndexService.CardIllustrationBundleCandidates(realRoot, "4007")
            .SelectMany(path => engine.ScanBundle(path, realRoot, "游戏内图片", false).Textures).First(CardTextureRules.IsCardArt);
        var realGate = new OverFrameService().FindCachedGate(gameRoot) ?? throw new InvalidDataException("Need a read-only gate fixture.");
        var originalHashes = new[] { fixture.BundlePath, realGate.BundlePath }.ToDictionary(path => path, Hash);
        string mirror = Path.Combine(output, "mirror");
        string local = Path.Combine(mirror, "LocalData", "restore001", "0000");
        Directory.CreateDirectory(local); IndexService.SetPreferredLocalRoot(mirror, local);
        var frames = new OverFrameService(Path.Combine(output, "catalog-cache"));
        string gatePath = Path.Combine(local, realGate.RelativeBundlePath);
        Copy(realGate.BundlePath, gatePath);
        void Rows(params OverFrameMapping[] rows) => engine.ReplaceTextAsset(engine.FindTextAssetFast(gatePath, local, OverFrameService.GateName)!, ModRecoveryService.EncodeMappings(rows), Path.Combine(output, "fixture-backups"));
        Rows(new(65001, 1234), new(65002, 65002));
        string gateBackup = Path.Combine(mirror, "_MD卡图备份", "超框开关", realGate.RelativeBundlePath);
        Copy(gatePath, gateBackup);
        var a = Card(4007); var b = Card(65001);
        Rows(new(4007, 4007), new(65001, 65001), new(65002, 4567), new(65003, 65003));
        string modifiedGate = Hash(gatePath), modifiedA = Hash(a.BundlePath), modifiedB = Hash(b.BundlePath);
        string baseline = Hash(gateBackup);
        var store = new ModRecoveryStore(Path.Combine(output, "library"));
        var restore = new TextureModRestoreService(store, frames);
        foreach (int failAt in new[] { 1, 3 })
        {
            try { restore.Restore(mirror, new[] { a, b }, count => { if (count == failAt) throw new IOException("injected"); }); throw new Exception("Missing injected failure"); }
            catch (IOException ex) when (ex.InnerException != null) { }
            Check(Hash(gatePath) == modifiedGate && Hash(a.BundlePath) == modifiedA && Hash(b.BundlePath) == modifiedB, "failed batch rolls textures and gate back together");
        }
        using (File.OpenRead(gatePath))
        {
            try { restore.Restore(mirror, new[] { a, b }); throw new Exception("Locked gate accepted"); }
            catch (IOException) { }
        }
        Check(Hash(gatePath) == modifiedGate && Hash(a.BundlePath) == modifiedA && Hash(b.BundlePath) == modifiedB, "locked registration table fails before any texture commit");
        restore.Restore(mirror, new[] { a, b });
        var rows = frames.Read(mirror);
        Check(!rows.Any(row => row.CardId == 4007), "stale normal category still disables added overframe");
        Check(rows.Single(row => row.CardId == 65001).ArtId == 1234, "original official overframe restored, not deleted");
        Check(rows.Single(row => row.CardId == 65002).ArtId == 4567 && rows.Single(row => row.CardId == 65003).ArtId == 65003, "unselected card registrations preserved");
        Check(Hash(a.BundlePath) == Hash(fixture.BundlePath) && Hash(b.BundlePath) == Hash(fixture.BundlePath), "all selected textures restored");
        Check(Hash(gateBackup) == baseline && store.Load(mirror).DisabledLegacyCards.IsSupersetOf(new ushort[] { 4007, 65001 }), "original backup retained; recovery does not reactivate restored cards");
        restore.Restore(mirror, new[] { a });
        Check(!frames.Read(mirror).Any(row => row.CardId == 4007), "repeat restore idempotent");
        // Exercise the actual main-window restore entry, not just its service.
        a = Card(4007);
        Rows(new(4007, 4007), new(65001, 1234), new(65003, 65003));
        using (var form = new MainForm(null, backgroundRefresh: false) { ShowInTaskbar = false, Opacity = 0 })
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetField("_assetRoot", flags)!.SetValue(form, null);
            typeof(MainForm).GetField("_gameRoot", flags)!.SetValue(form, mirror);
            var textures = (List<TexRef>)typeof(MainForm).GetField("_textures", flags)!.GetValue(form)!;
            textures.Clear(); textures.Add(a);
            form.Show(); Application.DoEvents();
            typeof(MainForm).GetField("_assetRoot", flags)!.SetValue(form, local);
            var task = form.RestoreTextureModsAsync(mirror, new[] { a });
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(60)) { Application.DoEvents(); Thread.Sleep(10); }
            Check(task.IsCompleted, "UI restore completes"); task.GetAwaiter().GetResult();
            Check(!frames.Read(mirror).Any(row => row.CardId == 4007) && textures.All(texture => texture.Category != "超框卡图"), "UI entry restores actual gate and refreshes categories");
            form.Close();
        }
        Check(originalHashes.All(item => Hash(item.Key) == item.Value), "real game fixtures unchanged");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { StaleCategory = true, BatchRestore = true, OriginalNativeFrame = true, OtherCardsPreserved = true, Rollback = true, MainFormEntry = true, GameWrites = false }));
        Console.WriteLine("PASS texture + registration restore / stale category / official native frame / batch rollback / gameWrites=False");

        TexRef Card(ushort id)
        {
            string relative = IndexService.CardIllustrationRelativePath(id.ToString());
            string path = Path.Combine(local, relative); Copy(fixture.BundlePath, path);
            Copy(path, Path.Combine(mirror, "_MD卡图备份", "本地卡图", relative));
            var texture = new TexRef { BundlePath = path, RelativeBundlePath = relative, Name = fixture.Name, CardKey = id.ToString(),
                AssetFileName = fixture.AssetFileName, PathId = fixture.PathId, SourceKind = "本地卡图", Category = "普通卡图", Width = 704, Height = 1024 };
            using var image = new Image<Rgba32>(704, 1024, new Rgba32(50, 150, 200, 255));
            using var encoded = new MemoryStream(); image.SaveAsPng(encoded);
            engine.Replace(texture, encoded.ToArray(), Path.Combine(output, "fixture-image-backups"));
            return texture;
        }
    }
    private static void Copy(string from, string to) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, true); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
