using System.Security.Cryptography;
using System.Text;

namespace MdCardModTool;

public sealed record OverFrameMapping(ushort CardId, ushort ArtId)
{
    public bool UsesOwnArt => CardId == ArtId;
}

/// <summary>管理 Master Duel 的 of_card_asset 映射表。每项是两个 little-endian UInt16：显示卡号、原画卡号。</summary>
public sealed class OverFrameService
{
    readonly ModEngine _engine = new();
    public const string GateName = "of_card_asset";

    public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var cached = GateCachePath(localRoot);
        if (File.Exists(cached))
        {
            try
            {
                var path = File.ReadAllText(cached).Trim();
                if (File.Exists(path))
                {
                    var value = _engine.FindTextAssetFast(path, localRoot, GateName);
                    if (value is not null) return value;
                }
            }
            catch { }
            File.Delete(cached);
        }

        var files = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Where(IsUnityBundle).OrderBy(x => new FileInfo(x).Length).ToArray();
        TextAssetRef? found = null, emptyCandidate = null; var done = 0; var sync = new object();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount - 1) }, (file, state) =>
        {
            if (Volatile.Read(ref found) is not null) { state.Stop(); return; }
            try
            {
                var value = _engine.FindTextAssetFast(file, localRoot, GateName);
                if (value is not null) lock (sync)
                {
                    if (value.Data.Length > 0) { found = value; state.Stop(); }
                    else emptyCandidate ??= value;
                }
            }
            catch { }
            var current = Interlocked.Increment(ref done);
            if (current % 250 == 0 || current == files.Length) progress?.Invoke(current, files.Length);
        });
        found ??= emptyCandidate;
        if (found is null) throw new FileNotFoundException($"没有在 LocalData 中找到 {GateName}。请先启动游戏完成资源下载。");
        Directory.CreateDirectory(Path.GetDirectoryName(cached)!); File.WriteAllText(cached, found.BundlePath);
        return found;
    }

    public TextAssetRef? FindCachedGate(string gameRoot)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot); if (localRoot is null) return null;
        var cached = GateCachePath(localRoot); if (!File.Exists(cached)) return null;
        try { var path = File.ReadAllText(cached).Trim(); return File.Exists(path) ? _engine.FindTextAssetFast(path, localRoot, GateName) : null; }
        catch { return null; }
    }

    public List<OverFrameMapping> Read(string gameRoot, Action<int, int>? progress = null)
    {
        var gate = FindGate(gameRoot, progress);
        if (gate.Data.Length % 4 != 0) throw new InvalidDataException($"{GateName} 数据长度 {gate.Data.Length} 不是 4 的倍数，已停止写入以保护文件。");
        var values = new List<OverFrameMapping>(gate.Data.Length / 4);
        for (var i = 0; i < gate.Data.Length; i += 4) values.Add(new OverFrameMapping(BitConverter.ToUInt16(gate.Data, i), BitConverter.ToUInt16(gate.Data, i + 2)));
        return values.OrderBy(x => x.CardId).ThenBy(x => x.ArtId).ToList();
    }

    public void EnableOrUpdate(string gameRoot, ushort cardId, ushort artId)
    {
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        var found = mappings.FindIndex(x => x.CardId == cardId);
        if (found >= 0) mappings[found] = new OverFrameMapping(cardId, artId);
        else mappings.Add(new OverFrameMapping(cardId, artId));
        Save(gameRoot, gate, mappings);
    }

    public void Disable(string gameRoot, ushort cardId)
    {
        var gate = FindGate(gameRoot);
        var mappings = Parse(gate.Data);
        mappings.RemoveAll(x => x.CardId == cardId);
        Save(gameRoot, gate, mappings);
    }

    public bool HasBackup(string gameRoot)
    {
        var gate = FindGate(gameRoot);
        return File.Exists(BackupPath(gameRoot, gate));
    }

    public void RestoreBackup(string gameRoot)
    {
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
        var data = new byte[mappings.Count * 4];
        for (var i = 0; i < mappings.Count; i++) { BitConverter.TryWriteBytes(data.AsSpan(i * 4, 2), mappings[i].CardId); BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2, 2), mappings[i].ArtId); }
        _engine.ReplaceTextAsset(gate, data, Path.Combine(gameRoot, "_MD卡图备份", "超框开关"));
    }
    static string BackupPath(string gameRoot, TextAssetRef gate) => Path.Combine(gameRoot, "_MD卡图备份", "超框开关", gate.RelativeBundlePath);
    static string GateCachePath(string localRoot)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localRoot))).Substring(0, 12);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", $"overframe_gate_{id}.txt");
    }
    static bool IsUnityBundle(string path)
    {
        try { using var stream = File.OpenRead(path); Span<byte> bytes = stackalloc byte[7]; return stream.Read(bytes) == 7 && Encoding.ASCII.GetString(bytes) == "UnityFS"; }
        catch { return false; }
    }
}
