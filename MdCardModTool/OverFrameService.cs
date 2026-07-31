using System.Security.Cryptography;
using System.Text;

namespace MdCardModTool;

public sealed record OverFrameMapping(ushort CardId, ushort ArtId)
{
    public bool UsesOwnArt => CardId == ArtId;
}

public sealed record OverFrameRepairResult(int SavedCardCount, int ChangedMappingCount, int TotalMappingCount, string GateLocation);

/// <summary>管理 Master Duel 的 of_card_asset 映射表。每项是两个 little-endian UInt16：显示卡号、原画卡号。</summary>
public sealed class OverFrameService
{
    readonly ModEngine _engine = new();
    public const string GateName = "of_card_asset";
    public const string LegacyBackupKind = "超框开关";
    // v1.6.13 曾误把 data.unity3d 内的同名内置模板当成运行时表。
    // 保留该名称只用于识别并修复那一版留下的备份，不再把它当作 Mod 来源。
    public const string CoreBackupKind = "超框开关-游戏内";
    public const string CoreRepairBackupKind = "超框开关-核心误写安全备份";
    static readonly string CoreGateRelativePath = Path.Combine("masterduel_Data", "data.unity3d");
    static readonly string LegacyGateRelativePath = Path.Combine("a5", "a589d3b5");
    static readonly IReadOnlyDictionary<string, string> KnownRuntimeGatePaths = new Dictionary<string, string>
    {
        ["24462996"] = Path.Combine("22", "22817d01")
    };
    const long RuntimeGateSearchMaxBytes = 64 * 1024;

    public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var localRoot = IndexService.FindLocalRoot(gameRoot)
            ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000，无法定位游戏实际读取的超框登记。");
        var candidates = new List<string>();

        // 运行时表始终从 LocalData 加载。data.unity3d 也有一个同名 TextAsset，
        // 但它只是随程序附带的默认模板，修改它不会启用游戏内超框。
        AddCachedCandidate(candidates, GateCachePath(gameRoot), gameRoot, localRoot);
        AddCachedCandidate(candidates, LegacyGateCachePath(localRoot), gameRoot, localRoot);
        var buildId = PortableIndexService.GetGameBuildId(gameRoot);
        if (KnownRuntimeGatePaths.TryGetValue(buildId, out var knownPath)) candidates.Add(Path.Combine(localRoot, knownPath));
        candidates.Add(Path.Combine(localRoot, LegacyGateRelativePath));

        var unique = candidates
            .Where(File.Exists)
            .DistinctBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < unique.Length; i++)
        {
            progress?.Invoke(i + 1, unique.Length);
            try
            {
                var found = _engine.FindTextAssetFast(unique[i], localRoot, GateName);
                if (found is not null)
                {
                    WriteGateCache(gameRoot, found.BundlePath);
                    return found;
                }
            }
            catch { }
        }

        // 更新会改变哈希路径。of_card_asset Bundle 本身极小，按文件大小升序时通常
        // 前几项即可命中；只检查 <=64 KiB 的小 Bundle，不再解析 13 GB 全量资源。
        var smallBundles = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                try { return (Path: path, Length: new FileInfo(path).Length); }
                catch { return (Path: path, Length: long.MaxValue); }
            })
            .Where(x => x.Length <= RuntimeGateSearchMaxBytes)
            .OrderBy(x => x.Length)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Path)
            .ToArray();
        for (var i = 0; i < smallBundles.Length; i++)
        {
            progress?.Invoke(i + 1, smallBundles.Length);
            try
            {
                var found = _engine.FindTextAssetFast(smallBundles[i], localRoot, GateName);
                if (found is null) continue;
                WriteGateCache(gameRoot, found.BundlePath);
                return found;
            }
            catch { }
        }

        throw new FileNotFoundException(
            $"没有在 LocalData 的小型 Bundle 中找到 {GateName}。请先进入游戏完成资源下载，再重试。");
    }

    public TextAssetRef? FindCachedGate(string gameRoot)
    {
        try { return FindGate(gameRoot); }
        catch { return null; }
    }

    public List<OverFrameMapping> Read(string gameRoot, Action<int, int>? progress = null)
    {
        return Read(FindGate(gameRoot, progress));
    }

    public static List<OverFrameMapping> Read(TextAssetRef gate)
    {
        if (gate.Data.Length % 4 != 0) throw new InvalidDataException($"{GateName} 数据长度 {gate.Data.Length} 不是 4 的倍数，已停止写入以保护文件。");
        var values = new List<OverFrameMapping>(gate.Data.Length / 4);
        for (var i = 0; i < gate.Data.Length; i += 4) values.Add(new OverFrameMapping(BitConverter.ToUInt16(gate.Data, i), BitConverter.ToUInt16(gate.Data, i + 2)));
        return values.OrderBy(x => x.CardId).ThenBy(x => x.ArtId).ToList();
    }

    public void EnableOrUpdate(string gameRoot, ushort cardId, ushort artId)
    {
        EnsureGameStopped(gameRoot);
        RepairObsoleteCoreGateModification(gameRoot);
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        SetMapping(mappings, cardId, artId);
        Save(gameRoot, gate, mappings);
    }

    public void Disable(string gameRoot, ushort cardId)
    {
        EnsureGameStopped(gameRoot);
        RepairObsoleteCoreGateModification(gameRoot);
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        mappings.RemoveAll(x => x.CardId == cardId);
        Save(gameRoot, gate, mappings);
    }

    public OverFrameRepairResult ReapplySavedCards(string gameRoot)
    {
        EnsureGameStopped(gameRoot);
        RepairObsoleteCoreGateModification(gameRoot);
        var saved = OverFrameArtStore.SavedCardIds(gameRoot);
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        var changed = 0;
        foreach (var cardId in saved)
        {
            var current = mappings.Where(x => x.CardId == cardId).ToArray();
            if (current.Length == 1 && current[0].ArtId == cardId) continue;
            SetMapping(mappings, cardId, cardId);
            changed++;
        }
        if (changed > 0) Save(gameRoot, gate, mappings);
        return new OverFrameRepairResult(saved.Count, changed, mappings.Count, gate.RelativeBundlePath);
    }

    public IReadOnlyList<ushort> MissingSavedRegistrations(string gameRoot)
    {
        var saved = OverFrameArtStore.SavedCardIds(gameRoot);
        if (saved.Count == 0) return [];
        var active = ReadCached(gameRoot).Where(x => x.UsesOwnArt).Select(x => x.CardId).ToHashSet();
        return saved.Where(x => !active.Contains(x)).ToArray();
    }

    public bool HasBackup(string gameRoot)
    {
        return HasBackup(gameRoot, FindGate(gameRoot));
    }

    public static bool HasBackup(string gameRoot, TextAssetRef gate) => File.Exists(BackupPath(gameRoot, gate));

    public void RestoreBackup(string gameRoot)
    {
        EnsureGameStopped(gameRoot);
        var gate = FindGate(gameRoot);
        var backup = BackupPath(gameRoot, gate);
        if (!File.Exists(backup)) throw new FileNotFoundException("尚未找到本工具创建的超框表备份。", backup);
        File.Copy(backup, gate.BundlePath, true);
    }

    public string GateLocation(string gameRoot) => FindGate(gameRoot).RelativeBundlePath;

    /// <summary>
    /// 修复 v1.6.13 把超框卡号写进 data.unity3d 同名模板的问题。
    /// 仅当当前表是原始模板加上本工具保存卡号的严格超集时才回写，避免覆盖其他修改。
    /// </summary>
    public bool RepairObsoleteCoreGateModification(string gameRoot)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var livePath = Path.Combine(gameRoot, CoreGateRelativePath);
        var templatePath = Path.Combine(gameRoot, "_MD卡图备份", CoreBackupKind, CoreGateRelativePath);
        if (!File.Exists(livePath) || !File.Exists(templatePath)) return false;

        try
        {
            var live = _engine.FindTextAssetFast(livePath, gameRoot, GateName);
            var template = _engine.FindTextAssetFast(templatePath, gameRoot, GateName);
            if (live is null || template is null || live.Data.AsSpan().SequenceEqual(template.Data)) return false;

            var liveMappings = Parse(live.Data);
            var templateMappings = Parse(template.Data);
            var templatePairs = templateMappings.Select(x => (x.CardId, x.ArtId)).ToHashSet();
            if (!templatePairs.All(pair => liveMappings.Any(x => x.CardId == pair.CardId && x.ArtId == pair.ArtId))) return false;

            var saved = OverFrameArtStore.SavedCardIds(gameRoot).ToHashSet();
            var extras = liveMappings.Where(x => !templatePairs.Contains((x.CardId, x.ArtId))).ToArray();
            if (extras.Length == 0 || extras.Any(x => x.CardId != x.ArtId || !saved.Contains(x.CardId))) return false;

            _engine.ReplaceTextAsset(live, template.Data, Path.Combine(gameRoot, "_MD卡图备份", CoreRepairBackupKind));
            return true;
        }
        catch
        {
            // 这是旧版迁移清理；失败不应阻止写入真正的 LocalData 超框表。
            return false;
        }
    }

    public List<OverFrameMapping> ReadCached(string gameRoot)
    {
        var gate = FindCachedGate(gameRoot); return gate is null ? [] : Parse(gate.Data).OrderBy(x => x.CardId).ThenBy(x => x.ArtId).ToList();
    }
    static List<OverFrameMapping> Parse(byte[] data)
    {
        if (data.Length % 4 != 0) throw new InvalidDataException($"{GateName} 数据长度 {data.Length} 不是 4 的倍数，已停止写入以保护文件。");
        var result = new List<OverFrameMapping>(data.Length / 4);
        for (var i = 0; i < data.Length; i += 4) result.Add(new OverFrameMapping(BitConverter.ToUInt16(data, i), BitConverter.ToUInt16(data, i + 2)));
        return result;
    }
    void Save(string gameRoot, TextAssetRef gate, List<OverFrameMapping> mappings)
    {
        mappings = mappings.OrderBy(x => x.CardId).ThenBy(x => x.ArtId).ToList();
        var data = new byte[mappings.Count * 4];
        for (var i = 0; i < mappings.Count; i++) { BitConverter.TryWriteBytes(data.AsSpan(i * 4, 2), mappings[i].CardId); BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2, 2), mappings[i].ArtId); }
        _engine.ReplaceTextAsset(gate, data, Path.Combine(gameRoot, "_MD卡图备份", LegacyBackupKind));
        WriteGateCache(gameRoot, gate.BundlePath);
    }
    static void SetMapping(List<OverFrameMapping> mappings, ushort cardId, ushort artId)
    {
        mappings.RemoveAll(x => x.CardId == cardId);
        mappings.Add(new OverFrameMapping(cardId, artId));
    }
    static string BackupPath(string gameRoot, TextAssetRef gate) => Path.Combine(gameRoot, "_MD卡图备份", LegacyBackupKind, gate.RelativeBundlePath);
    static string GateCachePath(string gameRoot) => CachePathFor(gameRoot);
    static string LegacyGateCachePath(string localRoot) => CachePathFor(localRoot);
    static string CachePathFor(string scope)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(scope)))).Substring(0, 12);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", $"overframe_gate_{id}.txt");
    }
    static void WriteGateCache(string gameRoot, string bundlePath)
    {
        var cache = GateCachePath(gameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllLines(cache, [bundlePath, PortableIndexService.GetGameBuildId(gameRoot)]);
    }
    static void AddCachedCandidate(List<string> candidates, string cachePath, string gameRoot, string localRoot)
    {
        if (!File.Exists(cachePath)) return;
        try
        {
            var lines = File.ReadAllLines(cachePath);
            var path = lines.FirstOrDefault()?.Trim() ?? "";
            if (!File.Exists(path)) return;
            var cachedBuild = lines.Skip(1).FirstOrDefault()?.Trim() ?? "";
            var currentBuild = PortableIndexService.GetGameBuildId(gameRoot);
            if (cachedBuild.Length > 0 && currentBuild.Length > 0 && !cachedBuild.Equals(currentBuild, StringComparison.Ordinal)) return;
            if (IsInside(localRoot, path)) candidates.Add(path);
        }
        catch { }
    }
    static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
    public static void EnsureGameStopped(string gameRoot)
    {
        var fullRoot = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("masterduel"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Master Duel 正在运行。请完全退出游戏后再修改超框登记，避免 data.unity3d 被占用或写坏。");
            }
            finally { process.Dispose(); }
        }
    }
}
