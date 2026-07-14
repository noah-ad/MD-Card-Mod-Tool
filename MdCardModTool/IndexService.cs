using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MdCardModTool;

public static class IndexService
{
    const string CacheVersion = "v5";
    public static string? FindLocalRoot(string gameRoot)
    {
        var local = Path.Combine(gameRoot, "LocalData");
        if (!Directory.Exists(local)) return null;
        return Directory.GetDirectories(local).Select(x => Path.Combine(x, "0000")).Where(Directory.Exists).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static string StreamingRoot(string gameRoot) => Path.Combine(gameRoot, "masterduel_Data", "StreamingAssets", "AssetBundle");

    public static string CachePath(string localRoot, string streamingRoot)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{localRoot}|{streamingRoot}|{CacheVersion}"))).Substring(0, 12);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"index_{id}.json");
    }

    public static GameIndex Build(string gameRoot, Action<int, int, int>? progress = null)
    {
        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        var streamingRoot = StreamingRoot(gameRoot);
        // 原始 512×512 卡图 Bundle 压缩后集中在 200–300 KB；先预筛，避免解析其余 2 万多个非卡图 Bundle。
        // 已由本工具备份过的 Bundle 也要纳入：超框图会变成 704×1024，文件大小不再落在这个区间内。
        var localPaths = new HashSet<string>(Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Select(x => new FileInfo(x)).Where(x => x.Length >= 200_000 && x.Length < 300_000 && IsUnityBundle(x.FullName)).Select(x => x.FullName), StringComparer.OrdinalIgnoreCase);
        var backedLocal = Path.Combine(gameRoot, "_MD卡图备份", "本地卡图");
        if (Directory.Exists(backedLocal)) foreach (var backup in Directory.EnumerateFiles(backedLocal, "*", SearchOption.AllDirectories))
        {
            var live = Path.Combine(localRoot, Path.GetRelativePath(backedLocal, backup));
            if (File.Exists(live) && IsUnityBundle(live)) localPaths.Add(live);
        }
        var files = localPaths.Select(x => (Path: x, Root: localRoot, Kind: "本地卡图")).ToList();
        if (Directory.Exists(streamingRoot)) files.AddRange(Directory.EnumerateFiles(streamingRoot, "*", SearchOption.AllDirectories).Where(IsUnityBundle).Select(x => (Path: x, Root: streamingRoot, Kind: "游戏内图片")));
        var engine = new ModEngine(); var bag = new ConcurrentBag<TexRef>(); var done = 0;
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount - 1) }, file =>
        {
            try
            {
                var scan = engine.ScanBundle(file.Path, file.Root, file.Kind, includeDependencies: false);
                foreach (var texture in scan.Textures.Where(x => file.Kind != "本地卡图" || (x.Width == 512 && x.Height == 512) || File.Exists(Path.Combine(backedLocal, x.RelativeBundlePath)))) bag.Add(texture);
            }
            catch { }
            var count = Interlocked.Increment(ref done);
            if (count % 100 == 0 || count == files.Count) progress?.Invoke(count, files.Count, bag.Count);
        });
        foreach (var frame in GetCardFrames(gameRoot)) bag.Add(frame);
        return new GameIndex { Textures = bag.OrderBy(x => x.SourceKind).ThenBy(x => x.Category).ThenBy(x => x.Name).ToList() };
    }

    public static void BuildAndSave(string gameRoot, Action<int, int, int>? progress = null)
    {
        var index = Build(gameRoot, progress);
        var localRoot = FindLocalRoot(gameRoot)!;
        File.WriteAllText(CachePath(localRoot, StreamingRoot(gameRoot)), JsonSerializer.Serialize(index));
    }

    public static void Save(string gameRoot, IEnumerable<TexRef> textures)
    {
        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        var path = CachePath(localRoot, StreamingRoot(gameRoot));
        var existing = File.Exists(path) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path)) : null;
        Save(gameRoot, new GameIndex { Textures = textures.ToList(), AlternateArtIndexVersion = existing?.AlternateArtIndexVersion ?? 0 });
    }

    public static void Save(string gameRoot, GameIndex index)
    {
        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        File.WriteAllText(CachePath(localRoot, StreamingRoot(gameRoot)), JsonSerializer.Serialize(index));
    }

    /// <summary>把 data.unity3d 内已命名的 card_frame 贴图加入独立分类；可直接复用上一版卡图缓存。</summary>
    public static void AddCardFramesAndSave(string gameRoot)
    {
        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        var streamingRoot = StreamingRoot(gameRoot);
        var cache = CachePath(localRoot, streamingRoot);
        GameIndex index;
        if (File.Exists(cache)) index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex();
        else
        {
            var legacyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{localRoot}|{streamingRoot}|v4"))).Substring(0, 12);
            var legacy = Path.Combine(Path.GetDirectoryName(cache)!, $"index_{legacyId}.json");
            index = File.Exists(legacy) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(legacy)) ?? new GameIndex() : Build(gameRoot);
        }
        index.Textures.RemoveAll(x => x.SourceKind == "卡框资源");
        index.Textures.AddRange(GetCardFrames(gameRoot));
        index.Textures.Sort((a, b) => string.Compare($"{a.SourceKind}\0{a.Category}\0{a.Name}\0{a.Width:D8}", $"{b.SourceKind}\0{b.Category}\0{b.Name}\0{b.Width:D8}", StringComparison.Ordinal));
        File.WriteAllText(cache, JsonSerializer.Serialize(index));
    }

    static IEnumerable<TexRef> GetCardFrames(string gameRoot)
    {
        var baseData = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
        if (!File.Exists(baseData)) return [];
        try
        {
            return new ModEngine().ListTextures(baseData, gameRoot, "卡框资源").Where(x => x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == 704 && x.Height == 1024).ToArray();
        }
        catch { return []; }
    }

    static bool IsUnityBundle(string path)
    {
        try { using var stream = File.OpenRead(path); var bytes = new byte[7]; return stream.Read(bytes) == 7 && Encoding.ASCII.GetString(bytes) == "UnityFS"; }
        catch { return false; }
    }
}
