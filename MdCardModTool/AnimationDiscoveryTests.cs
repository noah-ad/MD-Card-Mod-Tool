using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class AnimationDiscoveryTests
{
    internal static void Run(string gameRoot, string output)
    {
        if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Test output must not be in the game.");
        Directory.CreateDirectory(output);
        var native = MonsterAnimationIndexService.Find(gameRoot, "14741");
        Check(native.Availability == MonsterAnimationAvailability.NativeTimeline && native.HasOfficialPrefab && !native.IsComplete, "14741 native Timeline classified without false Spine support");
        Check(native.ResourceBundles.Count >= 8 && native.Diagnostics.Count == 0, "14741 prefab dependencies fully traversed");
        Check(MonsterAnimationIndexService.HasInstalledAnimation(gameRoot, "14741"), "native cut-in discovered without P14741JS");
        Check(MonsterAnimationIndexService.FindInstalledCardIds(gameRoot).Contains("14741"), "native cut-in included in search/tag candidates");
        Check(!MonsterAnimationIndexService.FindInstalledCardIds(gameRoot, spineOnly: true).Contains("14741"), "native cut-in excluded from Spine donor list");
        var nativeHashes = native.ResourceBundles.ToDictionary(bundle => bundle.BundlePath, bundle => Hash(bundle.BundlePath), StringComparer.OrdinalIgnoreCase);
        foreach (string id in new[] { "6969", "14581", "18825", "22341", "22524", "3899", "7302", "10783", "19152", "19633", "20196", "20280", "21038" })
        {
            var set = MonsterAnimationIndexService.Find(gameRoot, id);
            var pairs = MonsterAnimationAssetPairing.FindComplete(set);
            Check(set.IsComplete && pairs.Any(pair => pair.Tier == "SD") && pairs.Any(pair => pair.Tier == "HighEnd_HD"), id + " official alias/nondecimal companion/HD-SD pairing");
            foreach (var pair in pairs)
                Check(MonsterAnimationAssetPairing.AtlasPages(new ModEngine().ReadTextAsset(pair.Atlas).Data).All(page =>
                    pair.Textures.Any(texture => (texture.Name + ".png").Equals(page, StringComparison.OrdinalIgnoreCase))), id + " all declared atlas pages available");
            Console.WriteLine($"card={id}; complete=True; assets={set.Assets.Count}");
        }
        string mirror = Path.Combine(output, "mirror");
        string local = Path.Combine(mirror, "LocalData", "test001", "0000");
        Directory.CreateDirectory(local);
        IndexService.SetPreferredLocalRoot(mirror, local);
        foreach (var bundle in native.ResourceBundles)
        {
            string root = bundle.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(mirror) : local;
            string target = Path.Combine(root, bundle.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(bundle.BundlePath, target, true);
        }
        string cache = MonsterAnimationIndexService.CachePath(mirror);
        try
        {
            MonsterAnimationIndexService.Write(cache, new PortableMonsterAnimationIndex { GameBuildId = PortableIndexService.GetGameBuildId(mirror) });
            var index = MonsterAnimationIndexService.EnsureCurrentIndex(mirror);
            Check(index.ResolverVersion == MonsterAnimationIndexService.CurrentResolverVersion && MonsterAnimationIndexService.KnownCardIds(index).Contains("14741"), "old same-build index upgrades official animation tags");
            Check(!MonsterAnimationIndexService.CompleteCardIds(index).Contains("14741"), "native registration does not grant six-bundle write eligibility");
            var repeat = MonsterAnimationIndexService.EnsureCurrentIndex(mirror);
            Check(repeat.Assets.Count == index.Assets.Count && repeat.OfficialCardIds.SequenceEqual(index.OfficialCardIds), "same-build refresh idempotent");
            var pageFixture = MonsterAnimationIndexService.Find(gameRoot, "7302");
            foreach (var source in pageFixture.ResourceBundles.Select(bundle => (bundle.BundlePath, bundle.RelativeBundlePath, bundle.StorageKind))
                .Concat(pageFixture.Assets.Select(asset => (asset.BundlePath, asset.RelativeBundlePath, asset.StorageKind)))
                .DistinctBy(value => value.BundlePath))
            {
                string targetRoot = source.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(mirror) : local;
                string target = Path.Combine(targetRoot, source.RelativeBundlePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source.BundlePath, target, true);
            }
            var stale = new PortableMonsterAnimationIndex
            {
                GameBuildId = index.GameBuildId, ResolverVersion = MonsterAnimationIndexService.CurrentResolverVersion,
                Assets = pageFixture.Assets.Select(asset => new MonsterAnimationAssetRef
                {
                    RelativeBundlePath = asset.RelativeBundlePath, AssetFileName = asset.AssetFileName, Name = asset.Name,
                    PathId = long.MinValue, CardId = asset.CardId, Kind = asset.Kind, StorageKind = asset.StorageKind
                }).ToList()
            };
            MonsterAnimationIndexService.Write(cache, stale);
            var repaired = MonsterAnimationIndexService.Find(mirror, "7302");
            Check(repaired.IsComplete && repaired.Assets.All(asset => asset.PathId != long.MinValue), "existing bundle with stale cached PathIDs re-read");
            var multi = MonsterAnimationAssetPairing.FindComplete(repaired).First(pair => pair.Tier == "HighEnd_HD");
            var extraPage = multi.Textures.First(texture => texture.Name != multi.Texture.Name);
            string held = extraPage.BundlePath + ".test-held";
            File.Move(extraPage.BundlePath, held, true);
            try
            {
                var partial = MonsterAnimationIndexService.Find(mirror, "7302");
                Check(!partial.IsComplete && partial.Availability == MonsterAnimationAvailability.IncompleteSpine, "missing later atlas page does not grant replacement eligibility");
            }
            finally { File.Move(held, extraPage.BundlePath, true); }
            var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            string before = Hash(cache);
            try { MonsterAnimationIndexService.RefreshMissingLinks(mirror, index, cancellationToken: cancellation.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Check(Hash(cache) == before, "cancelled upgrade does not overwrite index");
            TestUi(mirror, output);
        }
        finally { if (File.Exists(cache)) File.Delete(cache); }
        Check(nativeHashes.All(item => Hash(item.Key) == item.Value), "real native resources unchanged");
        Console.WriteLine("PASS native discovery / aliases / cache upgrade / UI messages / gameWrites=False");
    }

    private static void TestUi(string gameRoot, string output)
    {
        AppLanguage original = Localizer.Language;
        try
        {
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                Localizer.SetLanguage(language);
                using var form = new MonsterAnimationForm(gameRoot, "14741") { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                // Let the production Shown handler finish its first lookup.
                var watch = Stopwatch.StartNew();
                var field = typeof(MonsterAnimationForm).GetField("_set", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var busy = typeof(MonsterAnimationForm).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                while ((field.GetValue(form) == null || (bool)busy.GetValue(form)!) && watch.Elapsed < TimeSpan.FromSeconds(40)) { Application.DoEvents(); Thread.Sleep(10); }
                Check(field.GetValue(form) is MonsterAnimationSet set && set.Availability == MonsterAnimationAvailability.NativeTimeline && !(bool)busy.GetValue(form)!, "native UI lookup completes");
                Label status = (Label)typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Check(status.Text == Localizer.T("animation.source.native"), "native UI reports official Timeline, not no animation");
                Button replace = (Button)typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Button diagnostics = (Button)typeof(MonsterAnimationForm).GetField("_resourceDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
                Check(!replace.Enabled && diagnostics.Enabled, "unsupported native writes disabled; diagnostics accessible");
                if (language == AppLanguage.SimplifiedChinese)
                {
                    using var screenshot = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, form.Size));
                    screenshot.Save(Path.Combine(output, "native-animation-status.png"));
                }
                form.Close();
            }
        }
        finally { Localizer.SetLanguage(original); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
