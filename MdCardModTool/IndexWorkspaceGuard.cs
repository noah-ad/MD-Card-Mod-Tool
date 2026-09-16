using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MdCardModTool;

internal static class IndexWorkspaceGuard
{
    internal static bool Belongs(string gameRoot, TexRef texture)
    {
        if (BuiltInCardFrameCatalog.IsPackagedFrame(texture)) return true;
        string? root = texture.SourceKind switch
        {
            "本地卡图" or "视觉资源" => IndexService.FindLocalRoot(gameRoot),
            "游戏内图片" => IndexService.StreamingRoot(gameRoot),
            _ => gameRoot
        };
        if (root == null) return false;
        try
        {
            string relative = texture.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar);
            string expected = Path.GetFullPath(Path.Combine(root, relative));
            return !Path.IsPathRooted(relative) && StandaloneResourceService.IsInside(expected, root)
                && expected.Equals(Path.GetFullPath(texture.BundlePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    internal static GameIndex Repair(string gameRoot, GameIndex cached, string cachePath)
    {
        if (cached.Textures.All(t => Belongs(gameRoot, t))) return cached;
        // Preserve the contaminated cache for recovery, never game files.
        File.Copy(cachePath, cachePath + ".foreign-" + Guid.NewGuid().ToString("N") + ".bak");
        if (!PortableIndexService.TryLoadBundled(gameRoot, out var repaired, out _))
            repaired = IndexService.Build(gameRoot);
        var valid = cached.Textures.Where(t => Belongs(gameRoot, t)).ToList();
        foreach (var t in valid)
        {
            repaired.Textures.RemoveAll(x => x.BundlePath.Equals(t.BundlePath, StringComparison.OrdinalIgnoreCase) && x.PathId == t.PathId);
            repaired.Textures.Add(t);
        }
        string local = IndexService.FindLocalRoot(gameRoot)!;
        // Re-read backed-up cards: their PathID/name/size may have changed in a Mod.
        foreach (string kind in new[] { "本地卡图", "视觉资源" })
        {
            string backups = Path.Combine(gameRoot, "_MD卡图备份", kind);
            if (!Directory.Exists(backups)) continue;
            foreach (string backup in Directory.EnumerateFiles(backups, "*", SearchOption.AllDirectories))
            {
                string live = Path.Combine(local, Path.GetRelativePath(backups, backup));
                if (!File.Exists(live)) continue;
                var textures = new ModEngine().ScanBundle(live, local, kind, false).Textures;
                if (textures.Count == 0) continue;
                repaired.Textures.RemoveAll(t => t.BundlePath.Equals(live, StringComparison.OrdinalIgnoreCase));
                repaired.Textures.AddRange(textures);
            }
        }
        return repaired;
    }
}
