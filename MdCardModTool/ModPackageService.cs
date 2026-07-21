using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MdCardModTool;

public sealed class ModPackageManifest
{
    public int FormatVersion { get; init; } = 1;
    public string Name { get; init; } = "Master Duel Mod";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public List<ModPackageEntry> Entries { get; init; } = [];
}

public sealed class ModPackageEntry
{
    public string ArchivePath { get; init; } = "";
    public string TargetKind { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string SourceKind { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
}

public sealed record ModPackageInfo(string Name, DateTimeOffset CreatedAt, int BundleCount, long TotalSize);
public sealed record ModImportResult(int BundleCount, IReadOnlyList<string> ChangedBundlePaths);
public sealed record ModChangeSummary(int BundleCount, int AnimationBundleCount);

/// <summary>
/// 用 _MD卡图备份 作为轻量 Mod 台账。只比较实际改过的 Bundle，不扫描整个游戏目录。
/// </summary>
public sealed class ModPackageService
{
    const string ManifestName = "manifest.json";
    static readonly string[] SourceKinds = ["本地卡图", "游戏内图片", "卡框资源", "超框开关", "召唤动画", "召唤动画-游戏内"];

    public int RefreshFlags(string gameRoot, IEnumerable<TexRef> textures)
    {
        var list = textures.ToList();
        foreach (var texture in list) texture.IsModded = false;
        var changed = EnumerateChangedBundles(gameRoot, list).Select(x => x.LivePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var texture in list.Where(x => changed.Contains(x.BundlePath))) texture.IsModded = true;
        return list.Count(x => x.IsModded);
    }

    public ModChangeSummary GetChangeSummary(string gameRoot, IEnumerable<TexRef> textures)
    {
        var changed = EnumerateChangedBundles(gameRoot, textures.ToList()).ToArray();
        return new ModChangeSummary(changed.Length, changed.Count(x => x.SourceKind.StartsWith("召唤动画", StringComparison.Ordinal)));
    }

    public ModPackageInfo Export(string gameRoot, IEnumerable<TexRef> textures, string outputPath)
    {
        var changed = EnumerateChangedBundles(gameRoot, textures.ToList()).ToList();
        if (changed.Count == 0) throw new InvalidOperationException("当前没有可导出的 Mod。替换过但已还原的 Bundle 不会被导出。");

        var manifest = new ModPackageManifest
        {
            Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(outputPath)),
            Entries = changed.Select((x, i) => new ModPackageEntry
            {
                ArchivePath = $"files/{i + 1:D4}.bundle",
                TargetKind = x.TargetKind,
                RelativePath = NormalizeRelativePath(x.RelativePath),
                SourceKind = x.SourceKind,
                DisplayName = x.DisplayName,
                Sha256 = HashFile(x.LivePath),
                Size = new FileInfo(x.LivePath).Length
            }).ToList()
        };

        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        var temporary = fullOutput + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                for (var i = 0; i < changed.Count; i++) archive.CreateEntryFromFile(changed[i].LivePath, manifest.Entries[i].ArchivePath, CompressionLevel.Optimal);
                var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var stream = manifestEntry.Open();
                JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
            }
            File.Move(temporary, fullOutput, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return new ModPackageInfo(manifest.Name, manifest.CreatedAt, manifest.Entries.Count, manifest.Entries.Sum(x => x.Size));
    }

    public ModPackageInfo Inspect(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadManifest(archive);
        return new ModPackageInfo(manifest.Name, manifest.CreatedAt, manifest.Entries.Count, manifest.Entries.Sum(x => x.Size));
    }

    public ModImportResult Import(string gameRoot, string packagePath)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var staging = Path.Combine(Path.GetTempPath(), "MDCardModTool", "import_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifest = ReadManifest(archive);
            var plans = new List<ImportPlan>();
            for (var i = 0; i < manifest.Entries.Count; i++)
            {
                var entry = manifest.Entries[i];
                ValidateSourceKind(entry.SourceKind);
                if (!entry.TargetKind.Equals(TargetKindFor(entry.SourceKind), StringComparison.Ordinal)) throw new InvalidDataException($"资源类型与目标目录不匹配：{entry.DisplayName}");
                var root = TargetRoot(gameRoot, localRoot, entry.TargetKind);
                var target = ResolveInside(root, entry.RelativePath);
                if (!File.Exists(target)) throw new FileNotFoundException($"本机没有 Mod 所需的目标 Bundle：{entry.RelativePath}", target);
                var zipEntry = archive.GetEntry(entry.ArchivePath) ?? throw new InvalidDataException($"Mod 包缺少文件：{entry.ArchivePath}");
                if (zipEntry.Length != entry.Size) throw new InvalidDataException($"Mod 包文件大小不匹配：{entry.DisplayName}");
                var staged = Path.Combine(staging, $"{i:D4}.bundle");
                zipEntry.ExtractToFile(staged, true);
                if (!HashFile(staged).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Mod 包校验失败：{entry.DisplayName}");
                var backup = ResolveInside(Path.Combine(gameRoot, "_MD卡图备份", entry.SourceKind), entry.RelativePath);
                plans.Add(new ImportPlan(target, staged, backup, Path.Combine(staging, $"rollback_{i:D4}.bundle")));
            }
            if (plans.Select(x => x.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count) throw new InvalidDataException("Mod 包包含重复的游戏目标路径。");

            var applied = new List<ImportPlan>();
            try
            {
                foreach (var plan in plans)
                {
                    File.Copy(plan.Target, plan.Rollback, true);
                    Directory.CreateDirectory(Path.GetDirectoryName(plan.Backup)!);
                    if (!File.Exists(plan.Backup)) File.Copy(plan.Target, plan.Backup);
                    var writeTemp = plan.Target + ".mdcardmod.import.tmp";
                    try { File.Copy(plan.Staged, writeTemp, true); File.Move(writeTemp, plan.Target, true); }
                    finally { if (File.Exists(writeTemp)) File.Delete(writeTemp); }
                    applied.Add(plan);
                }
            }
            catch
            {
                foreach (var plan in applied.AsEnumerable().Reverse()) File.Copy(plan.Rollback, plan.Target, true);
                throw;
            }
            return new ModImportResult(plans.Count, plans.Select(x => x.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    IEnumerable<ChangedBundle> EnumerateChangedBundles(string gameRoot, IReadOnlyCollection<TexRef> textures)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot);
        if (localRoot is null) yield break;
        var backupRoot = Path.Combine(gameRoot, "_MD卡图备份");
        foreach (var sourceKind in SourceKinds)
        {
            var sourceBackup = Path.Combine(backupRoot, sourceKind);
            if (!Directory.Exists(sourceBackup)) continue;
            var targetKind = TargetKindFor(sourceKind);
            var targetRoot = TargetRoot(gameRoot, localRoot, targetKind);
            foreach (var backup in Directory.EnumerateFiles(sourceBackup, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceBackup, backup);
                var live = ResolveInside(targetRoot, relative);
                if (!File.Exists(live) || FilesEqual(backup, live)) continue;
                var names = textures.Where(x => x.BundlePath.Equals(live, StringComparison.OrdinalIgnoreCase)).Select(x => x.CardKey.Length > 0 ? x.CardKey : x.Name).Distinct().Take(4).ToArray();
                var display = names.Length > 0 ? string.Join("、", names) : Path.GetFileName(relative);
                yield return new ChangedBundle(live, relative, targetKind, sourceKind, display);
            }
        }
    }

    static ModPackageManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("不是有效的 MD Mod 包：缺少 manifest.json。");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<ModPackageManifest>(stream) ?? throw new InvalidDataException("Mod 包清单无法读取。");
        if (manifest.FormatVersion != 1) throw new InvalidDataException($"暂不支持 Mod 包格式版本 {manifest.FormatVersion}。");
        if (manifest.Entries.Count is < 1 or > 10_000) throw new InvalidDataException("Mod 包内没有 Bundle，或文件数量异常。");
        if (manifest.Entries.Any(x => x.Size is < 1 or > 1_073_741_824) || manifest.Entries.Sum(x => x.Size) > 4_294_967_296L) throw new InvalidDataException("Mod 包解压后的文件大小异常。");
        if (manifest.Entries.Select(x => x.ArchivePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Entries.Count) throw new InvalidDataException("Mod 包内存在重复文件路径。");
        return manifest;
    }

    static string TargetKindFor(string sourceKind) => sourceKind switch
    {
        "本地卡图" or "超框开关" or "召唤动画" => "LocalData",
        "召唤动画-游戏内" => "StreamingAssets",
        "游戏内图片" => "StreamingAssets",
        "卡框资源" => "GameRoot",
        _ => throw new InvalidDataException($"不支持的资源类型：{sourceKind}")
    };

    static string TargetRoot(string gameRoot, string localRoot, string targetKind) => targetKind switch
    {
        "LocalData" => localRoot,
        "StreamingAssets" => IndexService.StreamingRoot(gameRoot),
        "GameRoot" => gameRoot,
        _ => throw new InvalidDataException($"不支持的目标类型：{targetKind}")
    };

    static void ValidateSourceKind(string sourceKind)
    {
        if (!SourceKinds.Contains(sourceKind, StringComparer.Ordinal)) throw new InvalidDataException($"不支持的资源类型：{sourceKind}");
    }

    static string ResolveInside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Mod 包包含无效的绝对路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mod 包路径越出了游戏目录，已停止导入。");
        return full;
    }

    static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
    static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static bool FilesEqual(string left, string right)
    {
        var a = new FileInfo(left); var b = new FileInfo(right);
        return a.Length == b.Length && HashFile(left).Equals(HashFile(right), StringComparison.OrdinalIgnoreCase);
    }

    sealed record ChangedBundle(string LivePath, string RelativePath, string TargetKind, string SourceKind, string DisplayName);
    sealed record ImportPlan(string Target, string Staged, string Backup, string Rollback);
}
