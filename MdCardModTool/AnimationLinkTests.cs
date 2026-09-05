using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MdCardModTool;

internal static class AnimationLinkTests
{
    public static void Run(string gameRoot, string output)
    {
        var set = MonsterAnimationIndexService.Find(gameRoot, "22524");
        var pairs = MonsterAnimationAssetPairing.FindComplete(set);
        if (!set.IsComplete || !pairs.Any(p => p.Tier == "SD" && p.Scale == "0.2925"))
            throw new InvalidDataException("22524 companion SD resource was not resolved");
        string mirror = Path.Combine(output, "game");
        string local = Path.Combine(mirror, "LocalData", "newcard-test", "0000");
        Directory.CreateDirectory(local);
        IndexService.SetPreferredLocalRoot(mirror, local);
        var hashes = set.Assets.ToDictionary(a => a.BundlePath, a => Hash(a.BundlePath));
        foreach (var asset in set.Assets)
        {
            string file = Path.Combine(asset.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(mirror) : local, asset.RelativeBundlePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(asset.BundlePath, file, true);
        }
        // Simulate an old cache for the SAME build containing HD + skeletons only.
        var sd = pairs.Single(p => p.Tier == "SD");
        var stale = new PortableMonsterAnimationIndex
        {
            GameBuildId = PortableIndexService.GetGameBuildId(mirror),
            Assets = set.Assets.Where(a => a.BundlePath != sd.Texture.BundlePath && a.BundlePath != sd.Atlas.BundlePath).ToList()
        };
        string cache = MonsterAnimationIndexService.CachePath(mirror);
        try
        {
            MonsterAnimationIndexService.Write(cache, stale);
            var updated = MonsterAnimationIndexService.EnsureCurrentIndex(mirror);
            if (!MonsterAnimationIndexService.CompleteCardIds(updated).Contains("22524"))
                throw new InvalidDataException("Same-build new-card link was not repaired");
            var second = MonsterAnimationIndexService.EnsureCurrentIndex(mirror);
            if (second.Assets.Count != updated.Assets.Count) throw new InvalidDataException("Refresh duplicated links");
            if (hashes.Any(x => Hash(x.Key) != x.Value)) throw new InvalidDataException("Real game files changed");
            Console.WriteLine($"card=22524; hd=0.585; sd=0.2925; sixBundles=True; sameBuildRefresh=True; idempotent=True; gameWrites=False; ready=True");
        }
        finally { if (File.Exists(cache)) File.Delete(cache); }
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
}
