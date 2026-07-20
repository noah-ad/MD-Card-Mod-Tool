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
        // 大多数缩略图 Bundle 位于这个区间，首次索引先走快速路径。
        // 少数不符合该大小规律的卡，在用户按卡号补查时再后台扫描并永久写进本地缓存。
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
        Save(gameRoot, new GameIndex
        {
            Textures = textures.ToList(),
            AlternateArtIndexVersion = existing?.AlternateArtIndexVersion ?? 0,
            CheckedLocalBundlePaths = existing?.CheckedLocalBundlePaths ?? []
        });
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

    /// <summary>
    /// 补查首次快速索引漏掉的本地卡图。只在用户明确检索一个缺失卡号时调用；
    /// 扫描在后台进行、并记录已检查过的 Bundle，后续不再重复扫描。
    /// </summary>
    public static MissingCardScanResult ScanMissingLocalCard(
        string gameRoot,
        GameIndex index,
        string cardKey,
        Action<int, int, int>? progress = null,
        CancellationToken cancellationToken = default,
        bool ignorePreviouslyChecked = false)
    {
        if (string.IsNullOrWhiteSpace(cardKey) || !cardKey.All(char.IsAsciiDigit))
            throw new ArgumentException("补查只接受纯数字卡号。", nameof(cardKey));

        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        var known = index.Textures
            .Where(x => x.SourceKind == "本地卡图")
            .Select(x => Path.GetFullPath(x.BundlePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedPaths = index.CheckedLocalBundlePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)
            .Where(path => !known.Contains(Path.GetFullPath(path)))
            .Where(path => ignorePreviouslyChecked || !checkedPaths.Contains(Path.GetRelativePath(localRoot, path)))
            .Where(IsUnityBundle)
            // 200–300 KB 的 Bundle 已在首次快速索引中完整解析；即使它不含贴图，
            // 也不应在补查时重新进入队列并拖慢一次按卡号检索。
            .Select(path => new FileInfo(path))
            .Where(file => file.Length < 200_000 || file.Length >= 300_000)
            // 漏项通常只是原图压缩率让文件刚好越过 200–300 KB 边界；
            // 先查最接近常规卡图 Bundle 大小的文件，避免一开始就卡在大型 UI 资源上。
            .OrderBy(file => Math.Abs(file.Length - 250_000L))
            .Select(file => file.FullName)
            .ToArray();

        var found = 0; var done = 0;
        var added = new ConcurrentBag<TexRef>();
        var newlyChecked = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var engine = new ModEngine();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken }, (path, state) =>
        {
            var relative = Path.GetRelativePath(localRoot, path);
            try
            {
                var scan = engine.ScanBundle(path, localRoot, "本地卡图", includeDependencies: false);
                foreach (var texture in scan.Textures.Where(IsMissingCardCandidate))
                {
                    added.Add(texture);
                    if (texture.CardKey == cardKey)
                    {
                        Interlocked.Exchange(ref found, 1);
                        state.Stop();
                    }
                }
            }
            catch { }
            finally
            {
                newlyChecked.TryAdd(relative, 0);
                var current = Interlocked.Increment(ref done);
                if (current % 25 == 0 || current == files.Length || Volatile.Read(ref found) != 0)
                    progress?.Invoke(current, files.Length, added.Count);
            }
        });

        foreach (var relative in newlyChecked.Keys)
            if (!checkedPaths.Contains(relative)) index.CheckedLocalBundlePaths.Add(relative);

        var unique = added
            .GroupBy(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        return new MissingCardScanResult { Textures = unique, ScannedBundles = done, TotalBundles = files.Length, Found = Volatile.Read(ref found) != 0 };
    }

    static bool IsMissingCardCandidate(TexRef texture) =>
        texture.CardKey.Length > 0 && texture.Width == 512 && texture.Height == 512;

    /// <summary>LocalData 内的 P数字 2048 图是 Spine/角色动画图集部件，不是可替换的卡图缩略图。</summary>
    public static int RemoveSpineAtlasParts(GameIndex index) => index.Textures.RemoveAll(x =>
        x.SourceKind == "本地卡图" && x.Name.Length > 1 && x.Name[0] == 'P' && x.Name.AsSpan(1).ToString().All(char.IsAsciiDigit));

    static bool IsUnityBundle(string path)
    {
        try { using var stream = File.OpenRead(path); var bytes = new byte[7]; return stream.Read(bytes) == 7 && Encoding.ASCII.GetString(bytes) == "UnityFS"; }
        catch { return false; }
    }
}

public sealed class MissingCardScanResult
{
    public List<TexRef> Textures { get; init; } = [];
    public int ScannedBundles { get; init; }
    public int TotalBundles { get; init; }
    public bool Found { get; init; }
}
