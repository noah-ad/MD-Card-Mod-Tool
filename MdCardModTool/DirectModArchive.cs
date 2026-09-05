using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AssetsTools.NET.Extra;

namespace MdCardModTool;

internal static class DirectModArchive
{
    public const string InstallInstructions = "先完全退出 Master Duel，并备份将被覆盖的文件。\r\n本包保留游戏目录结构：把 LocalData、masterduel_Data 等资源目录合并到游戏根目录。\r\nLocalData 下的账号名来自导出者；在其他账号使用时，先改成自己的账号文件夹名，不要创建导出者的账号目录。\r\n推荐使用工具的‘导入 Mod 包’：自动映射当前选中账号、校验文件并保存首次备份。\r\nmanifest.json 与本说明只用于工具和说明，不必复制到游戏目录。\r\n只适用于已下载对应资源的兼容游戏版本；整套替换可能覆盖同一 Bundle 内其他修改。\r\n";

    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/')
            || path.Any(c => c < 32 || ":*?\"<>|".Contains(c)))
            throw new InvalidDataException("ZIP 包含非法路径：" + path);
        foreach (string part in path.Split('/'))
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidDataException("ZIP 路径不安全：" + path);
    }

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > 12000) throw new InvalidDataException("ZIP 文件数量超出限制。");
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName.TrimEnd('/'));
            if (!paths.Add(entry.FullName.TrimEnd('/'))) throw new InvalidDataException("ZIP 包含重复路径。");
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("ZIP 不允许符号链接。");
            total = checked(total + entry.Length);
            if (entry.Length > 1073741824L || total > 4294967296L) throw new InvalidDataException("ZIP 解压大小超出限制。");
        }
    }

    public static ModPackageManifest Read(ZipArchive archive)
    {
        List<ModPackageEntry> entries = new();
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var file in archive.Entries)
        {
            if (file.FullName.EndsWith('/')) continue;
            // Instructions are never installed. All other entries must be actual bundles.
            if (file.FullName.Equals("安装说明.txt", StringComparison.OrdinalIgnoreCase)
                || file.FullName.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
                || file.FullName.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
            string path = file.FullName;
            string[] segments = path.Split('/');
            string targetKind, relative, source;
            int local = Array.FindIndex(segments, s => s.Equals("LocalData", StringComparison.OrdinalIgnoreCase));
            int data = Array.FindIndex(segments, s => s.Equals("masterduel_Data", StringComparison.OrdinalIgnoreCase));
            if (local >= 0 && segments.Length > local + 3 && segments[local + 2] == "0000")
            {
                targetKind = "LocalData"; relative = string.Join('/', segments.Skip(local + 3)); source = "本地卡图";
            }
            else if (data >= 0 && segments.Length > data + 3
                && segments[data + 1].Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase)
                && segments[data + 2].Equals("AssetBundle", StringComparison.OrdinalIgnoreCase))
            {
                targetKind = "StreamingAssets"; relative = string.Join('/', segments.Skip(data + 3)); source = "游戏内图片";
            }
            else if (segments[0] == "0000" && segments.Length >= 3)
            {
                targetKind = "LocalData"; relative = string.Join('/', segments.Skip(1)); source = "本地卡图";
            }
            else if (Regex.IsMatch(path, @"^[0-9a-fA-F]{2}/[0-9a-fA-F]{8}$"))
            {
                targetKind = "LocalData"; relative = path; source = "本地卡图";
            }
            else if (data == 0)
            {
                // Only an existing, valid Unity Bundle may be overwritten at this path.
                targetKind = "GameRoot"; relative = path; source = "基础视觉资源";
            }
            else throw new InvalidDataException("无法确定此文件的游戏目标目录：" + path + "。请保留 LocalData 或 masterduel_Data 目录结构。");
            if (!targets.Add(targetKind + "/" + relative)) throw new InvalidDataException("ZIP 的多个文件映射到同一目标（可能包含多个账号）。");
            using Stream input = file.Open();
            ValidateSignature(input, path);
            using Stream hashInput = file.Open();
            entries.Add(new ModPackageEntry { ArchivePath = path, TargetKind = targetKind,
                RelativePath = relative, SourceKind = source, DisplayName = Path.GetFileName(path),
                Size = file.Length, Sha256 = Convert.ToHexString(SHA256.HashData(hashInput)) });
        }
        return new ModPackageManifest { Name = "直接替换 ZIP", Entries = entries };
    }

    private static void ValidateSignature(Stream stream, string name)
    {
        Span<byte> header = stackalloc byte[8];
        int read = stream.ReadAtLeast(header, 8, throwOnEndOfStream: false);
        string signature = Encoding.ASCII.GetString(header[..read]);
        if (signature != "UnityFS\0" && signature != "UnityRaw" && signature != "UnityWeb")
            throw new InvalidDataException("文件不是 Unity Bundle，已拒绝安装：" + name);
    }

    public static void ValidateBundleFile(string file)
    {
        using (var input = File.OpenRead(file)) ValidateSignature(input, file);
        AssetsManager manager = new();
        try
        {
            var bundle = manager.LoadBundleFile(file);
            if (!bundle.file.GetAllFileNames().Any()) throw new InvalidDataException("空 Bundle：" + file);
        }
        catch (Exception error) when (error is IOException or ArgumentException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Bundle 结构损坏，已拒绝导入：" + Path.GetFileName(file), error);
        }
        finally { manager.UnloadAll(); }
    }

    public static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Mod 目标或备份路径包含目录链接，已停止：" + current);
    }

    public static void EnsureGameClosed()
    {
        var games = Process.GetProcessesByName("masterduel");
        try { if (games.Length > 0) throw new InvalidOperationException("请完全退出 Master Duel 后再导入 Mod。"); }
        finally { foreach (var game in games) game.Dispose(); }
    }
}
