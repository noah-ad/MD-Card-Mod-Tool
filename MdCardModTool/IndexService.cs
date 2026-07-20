using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MdCardModTool;

public static class IndexService
{
    const string CacheVersion = "v6";
    static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(index =>
    {
        var value = (uint)index;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();
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
        var allLocalFiles = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Select(x => new FileInfo(x)).ToArray();
        // 普通卡图大多位于这个区间；灵摆卡图是 512×1024，Bundle 往往更大。
        // 卡图资源的逻辑路径固定，因此用 CRC32 直接加入全部已下载卡图，不再盲扫 LocalData。
        // 已由本工具备份过的 Bundle 也要纳入：超框图会变成 704×1024，文件大小不再落在这个区间内。
        var localPaths = new HashSet<string>(allLocalFiles.Where(x => x.Length >= 200_000 && x.Length < 300_000 && IsUnityBundle(x.FullName)).Select(x => x.FullName), StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateDownloadedCardIllustrationBundles(localRoot, allLocalFiles)) localPaths.Add(path);
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
                foreach (var texture in scan.Textures.Where(x => file.Kind != "本地卡图" || IsDirectCardIllustration(x) || File.Exists(Path.Combine(backedLocal, x.RelativeBundlePath)))) bag.Add(texture);
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
    /// 按游戏实际使用的 Card/Images/Illust/{tcg|ocg}/{卡号} 逻辑路径计算 CRC32，
    /// 直接定位一个卡号的 Bundle。正常卡为 512×512，灵摆卡为 512×1024。
    /// </summary>
    public static MissingCardScanResult ScanMissingLocalCard(
        string gameRoot,
        GameIndex index,
        string cardKey,
        Action<int, int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cardKey) || !cardKey.All(char.IsAsciiDigit))
            throw new ArgumentException("补查只接受纯数字卡号。", nameof(cardKey));

        var localRoot = FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000");
        var knownBundles = index.Textures
            .Where(x => x.SourceKind == "本地卡图")
            .Select(x => Path.GetFullPath(x.BundlePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = cardKey == "0"
            ? EnumerateDownloadedCardIllustrationBundles(localRoot).Where(path => !knownBundles.Contains(Path.GetFullPath(path))).ToArray()
            : CardIllustrationBundleCandidates(localRoot, cardKey).Where(path => !knownBundles.Contains(Path.GetFullPath(path))).ToArray();

        var found = 0; var done = 0;
        var added = new ConcurrentBag<TexRef>();
        var engine = new ModEngine();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2), CancellationToken = cancellationToken }, (path, state) =>
        {
            try
            {
                var scan = engine.ScanBundle(path, localRoot, "本地卡图", includeDependencies: false);
                foreach (var texture in scan.Textures.Where(IsDirectCardIllustration))
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
                var current = Interlocked.Increment(ref done);
                if (current % 25 == 0 || current == files.Length || Volatile.Read(ref found) != 0)
                    progress?.Invoke(current, files.Length, added.Count);
            }
        });

        var unique = added
            .GroupBy(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        return new MissingCardScanResult { Textures = unique, ScannedBundles = done, TotalBundles = files.Length, Found = Volatile.Read(ref found) != 0 };
    }

    public static bool IsDirectCardIllustration(TexRef texture) =>
        texture.CardKey.Length > 0 && texture.Width == 512 && (texture.Height == 512 || texture.Height == 1024);

    public static void NormalizeLocalCardCategory(TexRef texture)
    {
        if (texture.SourceKind != "本地卡图" || texture.CardKey.Length == 0) return;
        if (texture.Width == 512 && texture.Height == 1024) texture.Category = "灵摆卡图";
        else if (texture.Width == 512 && texture.Height == 512)
            texture.Category = texture.IsAlternateArt ? "异画卡图" : texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图";
    }

    public static string CardIllustrationRelativePath(string cardKey, string illustrationType = "tcg")
    {
        if (string.IsNullOrWhiteSpace(cardKey) || !cardKey.All(char.IsAsciiDigit)) throw new ArgumentException("卡号必须是纯数字。", nameof(cardKey));
        var logicalPath = $"Card/Images/Illust/{illustrationType}/{cardKey}";
        var hash = Crc32(logicalPath).ToString("x8");
        return Path.Combine(hash[..2], hash);
    }

    public static IEnumerable<string> CardIllustrationBundleCandidates(string localRoot, string cardKey)
    {
        foreach (var type in new[] { "tcg", "ocg" })
        {
            var path = Path.Combine(localRoot, CardIllustrationRelativePath(cardKey, type));
            if (File.Exists(path) && IsUnityBundle(path)) yield return path;
        }
    }

    static IEnumerable<string> EnumerateDownloadedCardIllustrationBundles(string localRoot, IReadOnlyCollection<FileInfo>? localFiles = null)
    {
        localFiles ??= Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Select(x => new FileInfo(x)).ToArray();
        var available = localFiles.ToDictionary(x => Path.GetRelativePath(localRoot, x.FullName), x => x.FullName, StringComparer.OrdinalIgnoreCase);
        for (var cardId = 1; cardId <= ushort.MaxValue; cardId++)
        {
            foreach (var type in new[] { "tcg", "ocg" })
            {
                var relative = CardIllustrationRelativePath(cardId.ToString(), type);
                if (available.TryGetValue(relative, out var path) && IsUnityBundle(path)) yield return path;
            }
        }
    }

    static uint Crc32(string value)
    {
        var crc = uint.MaxValue;
        foreach (var item in Encoding.UTF8.GetBytes(value)) crc = Crc32Table[(crc ^ item) & 0xFF] ^ (crc >> 8);
        return crc ^ uint.MaxValue;
    }

    /// <summary>LocalData 内的 P数字 2048 图是 Spine/角色动画图集部件，不是可替换的卡图缩略图。</summary>
    public static int RemoveSpineAtlasParts(GameIndex index) => index.Textures.RemoveAll(x =>
        x.SourceKind == "本地卡图" && x.Name.Length > 1 && x.Name[0] == 'P' && x.Name.AsSpan(1).ToString().All(char.IsAsciiDigit));

    /// <summary>LocalData 只保留能对应到卡号的完整卡图；StreamingAssets 仍不做图片过滤。</summary>
    public static int RemoveNonCardLocalTextures(GameIndex index) => index.Textures.RemoveAll(x =>
        x.SourceKind == "本地卡图" && x.CardKey.Length == 0);

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
