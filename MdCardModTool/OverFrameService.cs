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
    public const string CoreBackupKind = "超框开关-游戏内";
    static readonly string CoreGateRelativePath = Path.Combine("masterduel_Data", "data.unity3d");
    static readonly string LegacyGateRelativePath = Path.Combine("a5", "a589d3b5");

    public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var localRoot = IndexService.FindLocalRoot(gameRoot);
        var candidates = new List<(string Path, string Root)>();

        // 2026-07-30 版本起，of_card_asset 被移进主程序的 data.unity3d。
        // 固定路径必须优先，避免再遍历数万个 LocalData Bundle。
        candidates.Add((Path.Combine(gameRoot, CoreGateRelativePath), gameRoot));
        AddCachedCandidate(candidates, GateCachePath(gameRoot), gameRoot, localRoot);
        if (localRoot is not null)
        {
            // 兼容旧版程序写下的、以 LocalData 路径计算名称的缓存。
            AddCachedCandidate(candidates, LegacyGateCachePath(localRoot), gameRoot, localRoot);
            candidates.Add((Path.Combine(localRoot, LegacyGateRelativePath), localRoot));
        }

        var unique = candidates
            .Where(x => File.Exists(x.Path))
            .DistinctBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < unique.Length; i++)
        {
            progress?.Invoke(i + 1, unique.Length);
            try
            {
                var found = _engine.FindTextAssetFast(unique[i].Path, unique[i].Root, GateName);
                if (found is not null)
                {
                    WriteGateCache(gameRoot, found.BundlePath);
                    return found;
                }
            }
            catch { }
        }

        throw new FileNotFoundException(
            $"没有在当前版本固定位置找到 {GateName}。已检查 {CoreGateRelativePath} 与旧版 {LegacyGateRelativePath}，不会再扫描整个 LocalData。请先验证游戏文件完整性后重试。");
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
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        SetMapping(mappings, cardId, artId);
        Save(gameRoot, gate, mappings);
    }

    public void Disable(string gameRoot, ushort cardId)
    {
        EnsureGameStopped(gameRoot);
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        mappings.RemoveAll(x => x.CardId == cardId);
        Save(gameRoot, gate, mappings);
    }

    public OverFrameRepairResult ReapplySavedCards(string gameRoot)
    {
        EnsureGameStopped(gameRoot);
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
        _engine.ReplaceTextAsset(gate, data, Path.Combine(gameRoot, "_MD卡图备份", BackupKindFor(gameRoot, gate)));
        WriteGateCache(gameRoot, gate.BundlePath);
    }
    static void SetMapping(List<OverFrameMapping> mappings, ushort cardId, ushort artId)
    {
        mappings.RemoveAll(x => x.CardId == cardId);
        mappings.Add(new OverFrameMapping(cardId, artId));
    }
    static string BackupPath(string gameRoot, TextAssetRef gate) => Path.Combine(gameRoot, "_MD卡图备份", BackupKindFor(gameRoot, gate), gate.RelativeBundlePath);
    static string BackupKindFor(string gameRoot, TextAssetRef gate) =>
        Path.GetFullPath(gate.BundlePath).Equals(Path.GetFullPath(Path.Combine(gameRoot, CoreGateRelativePath)), StringComparison.OrdinalIgnoreCase)
            ? CoreBackupKind
            : LegacyBackupKind;
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
        File.WriteAllText(cache, bundlePath);
    }
    static void AddCachedCandidate(List<(string Path, string Root)> candidates, string cachePath, string gameRoot, string? localRoot)
    {
        if (!File.Exists(cachePath)) return;
        try
        {
            var path = File.ReadAllText(cachePath).Trim();
            if (!File.Exists(path)) return;
            if (IsInside(gameRoot, path)) candidates.Add((path, gameRoot));
            else if (localRoot is not null && IsInside(localRoot, path)) candidates.Add((path, localRoot));
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
