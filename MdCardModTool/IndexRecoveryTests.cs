using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MdCardModTool;

internal static class IndexRecoveryTests
{
    internal static void Run(string root, string sourceCache, string output)
    {
        Directory.CreateDirectory(output);
        string local = IndexService.FindLocalRoot(root) ?? throw new Exception("Missing local root");
        string backupRoot = Path.Combine(root, "_MD卡图备份");
        var originals = Directory.GetFiles(backupRoot, "*", SearchOption.AllDirectories);
        var watched = originals.Concat(originals.Select(p => Path.Combine(local,
            string.Join(Path.DirectorySeparatorChar.ToString(), Path.GetRelativePath(backupRoot, p).Split(Path.DirectorySeparatorChar).Skip(1))))
            .Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, Hash);
        string cache = Path.Combine(output, "contaminated.json");
        File.Copy(sourceCache, cache, true);
        var old = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache))!;
        Require(old.Textures.Any(t => !IndexWorkspaceGuard.Belongs(root, t)), "Fixture contains foreign paths");
        var repaired = IndexWorkspaceGuard.Repair(root, old, cache);
        Require(repaired.Textures.All(t => IndexWorkspaceGuard.Belongs(root, t)), "All repaired paths belong to PC workspace");
        Require(Directory.GetFiles(output, "contaminated.json.foreign-*.bak").Length > 0, "Foreign cache preserved");
        new ModPackageService().RefreshFlags(root, repaired.Textures);
        foreach (string kind in new[] { "本地卡图", "视觉资源" })
        {
            string directory = Path.Combine(backupRoot, kind);
            var changed = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(p => (Backup: p, Live: Path.Combine(local, Path.GetRelativePath(directory, p))))
                .Where(p => File.Exists(p.Live) && Hash(p.Live) != Hash(p.Backup)).ToArray();
            Require(changed.Length > 0, kind + " changed fixture exists");
            foreach (var item in changed)
                Require(repaired.Textures.Any(t => t.IsModded && t.BundlePath.Equals(item.Live, StringComparison.OrdinalIgnoreCase)), "Visible Mod: " + item.Live);
            Console.WriteLine(kind + " changed bundles=" + changed.Length);
        }
        string activeCache = IndexService.CachePath(local, IndexService.StreamingRoot(root));
        string before = Hash(activeCache);
        bool rejected = false;
        try { IndexService.Save(root, old); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && Hash(activeCache) == before, "Foreign save rejected without cache mutation");
        foreach (var item in watched) Require(Hash(item.Key) == item.Value, "Unchanged: " + item.Key);
        File.WriteAllText(Path.Combine(output, "repaired.json"), JsonSerializer.Serialize(repaired));
        var settings = AppSettingsStore.Load();
        try
        {
            using var form = new MainForm(null, root, backgroundRefresh: false);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(MainForm);
            var items = (System.Collections.Generic.List<TexRef>)type.GetField("_textures", flags)!.GetValue(form)!;
            items.Clear(); items.AddRange(repaired.Textures);
            type.GetField("_modsOnly", flags)!.SetValue(form, true);
            type.GetMethod("RefreshCategories", flags)!.Invoke(form, null);
            type.GetMethod("RenderList", flags)!.Invoke(form, null);
            var visible = (System.Collections.Generic.List<TexRef>)type.GetField("_visibleTextures", flags)!.GetValue(form)!;
            Require(visible.Count > 0 && visible.All(t => t.IsModded), "MainForm Mod list contains modified resources only: " + visible.Count);
        }
        finally { AppSettingsStore.Save(settings); }
        Console.WriteLine("PASS; modified textures=" + repaired.Textures.Count(t => t.IsModded) + "; protected files=" + watched.Count);
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
