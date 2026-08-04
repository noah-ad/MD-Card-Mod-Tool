using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdCardModTool;

public enum MonsterAnimationAssetKind
{
    Texture = 1,
    Atlas = 2,
    Skeleton = 3
}

public sealed class MonsterAnimationAssetRef
{
    [JsonIgnore] public string BundlePath { get; init; } = "";
    [JsonPropertyName("r")] public string RelativeBundlePath { get; init; } = "";
    [JsonPropertyName("f")] public string AssetFileName { get; init; } = "";
    [JsonPropertyName("p")] public long PathId { get; init; }
    [JsonPropertyName("n")] public string Name { get; init; } = "";
    [JsonPropertyName("c")] public string CardId { get; init; } = "";
    [JsonPropertyName("k")] public MonsterAnimationAssetKind Kind { get; init; }
    [JsonPropertyName("s")] public string StorageKind { get; init; } = "LocalData";
    [JsonPropertyName("t")] public string ProfileTier { get; init; } = "";
    [JsonPropertyName("g")] public string ProfileRegion { get; init; } = "";
    [JsonPropertyName("z")] public string ProfileScale { get; init; } = "";
    [JsonIgnore] public string ModSourceKind => StorageKind == "StreamingAssets" ? "召唤动画-游戏内" : "召唤动画";

    public TexRef AsTexture() => new()
    {
        BundlePath = BundlePath,
        RelativeBundlePath = RelativeBundlePath,
        AssetFileName = AssetFileName,
        PathId = PathId,
        Name = Name,
        SourceKind = ModSourceKind,
        Category = "怪兽动画图集",
        CardKey = CardId
    };
}

public sealed class MonsterAnimationSet
{
    public string CardId { get; init; } = "";
    public List<MonsterAnimationAssetRef> Assets { get; init; } = [];
    public IReadOnlyList<MonsterAnimationAssetRef> Textures => Assets.Where(x => x.Kind == MonsterAnimationAssetKind.Texture).ToArray();
    public IReadOnlyList<MonsterAnimationAssetRef> Atlases => Assets.Where(x => x.Kind == MonsterAnimationAssetKind.Atlas).ToArray();
    public IReadOnlyList<MonsterAnimationAssetRef> Skeletons => Assets.Where(x => x.Kind == MonsterAnimationAssetKind.Skeleton).ToArray();
    public bool IsComplete => Textures.Count >= 2 && Atlases.Count >= 2 && Skeletons.Count >= 2;
    public string CountSummary => $"Texture2D {Textures.Count}/2 · Atlas {Atlases.Count}/2 · JS {Skeletons.Count}/2";
}

public sealed class PortableMonsterAnimationIndex
{
    [JsonPropertyName("v")] public int FormatVersion { get; init; } = 1;
    [JsonPropertyName("b")] public string GameBuildId { get; init; } = "";
    [JsonPropertyName("a")] public List<MonsterAnimationAssetRef> Assets { get; init; } = [];
}

/// <summary>
/// Stores animation asset-to-bundle mappings without absolute LocalData paths. The public build
/// carries this small map so users do not have to decompress every cached Bundle on first use.
/// </summary>
public static class MonsterAnimationIndexService
{
    public const string BundledFileName = "prebuilt-animation-index-v1.json.br";
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, BundledFileName);

    public static MonsterAnimationSet Find(string gameRoot, string cardId)
    {
        if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0) throw new ArgumentException("卡号必须是纯数字。", nameof(cardId));
        var assets = FindDeterministicCandidates(gameRoot, cardId);
        try
        {
            assets.AddRange(LoadBestAvailable(gameRoot, out _).Where(x => x.CardId == cardId && File.Exists(x.BundlePath)));
        }
        catch { }
        assets = assets
            .GroupBy(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MonsterAnimationSet { CardId = cardId, Assets = assets };
    }

    public static HashSet<string> LoadBundledCardIds()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "monster-animation-cards-v1.txt");
        if (!File.Exists(path)) return [];
        return File.ReadLines(path)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && x.All(char.IsAsciiDigit))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static bool HasInstalledAnimation(string gameRoot, string cardId)
    {
        if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0) return false;
        var roots = AnimationRoots(gameRoot);
        foreach (var region in new[] { "tcg", "ocg" })
        {
            var paths = SkeletonRelativePaths(cardId, region);
            if (roots.Any(root => File.Exists(Path.Combine(root, paths.High))) &&
                roots.Any(root => File.Exists(Path.Combine(root, paths.Sd)))) return true;
        }
        return false;
    }

    public static IReadOnlyList<string> FindInstalledCardIds(string gameRoot, int maximumCardId = 40_000)
    {
        var roots = AnimationRoots(gameRoot);
        var files = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (var id = 1; id <= maximumCardId; id++)
        {
            var cardId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var region in new[] { "tcg", "ocg" })
            {
                var paths = SkeletonRelativePaths(cardId, region);
                if (roots.Any(root => files.Contains(Path.GetFullPath(Path.Combine(root, paths.High)))) &&
                    roots.Any(root => files.Contains(Path.GetFullPath(Path.Combine(root, paths.Sd)))))
                {
                    result.Add(cardId);
                    break;
                }
            }
        }
        return result;
    }

    static string[] AnimationRoots(string gameRoot)
    {
        var roots = new List<string>();
        var local = IndexService.FindLocalRoot(gameRoot);
        if (local is not null && Directory.Exists(local)) roots.Add(local);
        var streaming = IndexService.StreamingRoot(gameRoot);
        if (Directory.Exists(streaming)) roots.Add(streaming);
        return roots.ToArray();
    }

    static (string High, string Sd) SkeletonRelativePaths(string cardId, string region)
    {
        var basePath = $"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}";
        return
        (
            IndexService.ResourceBundleRelativePath($"{basePath}/HighEnd_HD/P{cardId}JS"),
            IndexService.ResourceBundleRelativePath($"{basePath}/SD/P{cardId}JS")
        );
    }

    static List<MonsterAnimationAssetRef> FindDeterministicCandidates(string gameRoot, string cardId)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var streamingRoot = IndexService.StreamingRoot(gameRoot);
        var roots = new[] { (Root: localRoot, Storage: "LocalData"), (Root: streamingRoot, Storage: "StreamingAssets") };
        var logicalPaths = new List<string>();
        var commonScales = new[] { "1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4" };
        var scales = commonScales.Concat(Enumerable.Range(1, 2000).Select(x => (x / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var region in new[] { "tcg", "ocg" })
        {
            var basePath = $"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}";
            logicalPaths.Add($"{basePath}/HighEnd_HD/P{cardId}JS");
            logicalPaths.Add($"{basePath}/SD/P{cardId}JS");
            foreach (var tier in new[] { "HighEnd_HD", "SD" }) foreach (var scale in scales)
            {
                logicalPaths.Add($"{basePath}/{tier}/{scale}/P{cardId}");
                logicalPaths.Add($"{basePath}/{tier}/{scale}/P{cardId}.atlas");
            }
        }
        var result = new List<MonsterAnimationAssetRef>();
        var engine = new ModEngine();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Root)) continue;
            foreach (var logicalPath in logicalPaths)
            {
                var path = Path.Combine(root.Root, IndexService.ResourceBundleRelativePath(logicalPath));
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var asset in engine.ScanAnimationAssetsFast(path, root.Root).Where(x => x.CardId == cardId))
                    {
                        result.Add(new MonsterAnimationAssetRef
                        {
                            BundlePath = asset.BundlePath,
                            RelativeBundlePath = asset.RelativeBundlePath,
                            AssetFileName = asset.AssetFileName,
                            PathId = asset.PathId,
                            Name = asset.Name,
                            CardId = asset.CardId,
                            Kind = asset.Kind,
                            StorageKind = root.Storage
                        });
                    }
                }
                catch { }
            }
        }
        if (result.Count(x => x.Kind == MonsterAnimationAssetKind.Texture) < 2 ||
            result.Count(x => x.Kind == MonsterAnimationAssetKind.Atlas) < 2)
            result.AddRange(FindPrefabDependencyCandidates(roots, cardId, engine));
        return result;
    }

    static IEnumerable<MonsterAnimationAssetRef> FindPrefabDependencyCandidates(
        IEnumerable<(string Root, string Storage)> roots,
        string cardId,
        ModEngine engine)
    {
        var result = new List<MonsterAnimationAssetRef>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Root)) continue;
            foreach (var region in new[] { "tcg", "ocg" })
            foreach (var tier in new[] { "HighEnd_HD", "SD" })
            {
                var logical = $"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}/{tier}/P{cardId}";
                var entry = IndexService.ResourceBundleRelativePath(logical);
                if (!File.Exists(Path.Combine(root.Root, entry))) continue;
                var queue = new Queue<string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                queue.Enqueue(entry);
                while (queue.Count > 0 && visited.Count < 96)
                {
                    var relative = queue.Dequeue().Replace('/', Path.DirectorySeparatorChar);
                    if (!visited.Add(relative) || Path.IsPathRooted(relative)) continue;
                    var path = Path.GetFullPath(Path.Combine(root.Root, relative));
                    if (!path.StartsWith(Path.GetFullPath(root.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(path)) continue;
                    try
                    {
                        // A borrowed cut-in keeps the donor's atlas/material dependency bundles.
                        // Those containers still contain /p{donor}/ even though the entry prefab
                        // is installed under /p{target}/.  Follow the actual dependency graph and
                        // identify only the narrowly named animation assets instead of rejecting
                        // them by container path.
                        foreach (var asset in engine.ScanAnimationDependencyAssets(path, root.Root, cardId))
                            result.Add(new MonsterAnimationAssetRef
                            {
                                BundlePath = asset.BundlePath,
                                RelativeBundlePath = asset.RelativeBundlePath,
                                AssetFileName = asset.AssetFileName,
                                PathId = asset.PathId,
                                Name = asset.Name,
                                CardId = asset.CardId,
                                Kind = asset.Kind,
                                StorageKind = root.Storage,
                                ProfileTier = tier,
                                ProfileRegion = region,
                                ProfileScale = "Prefab依赖"
                            });
                        foreach (var dependency in engine.ReadBundleDependencies(path))
                            if (!visited.Contains(dependency)) queue.Enqueue(dependency);
                    }
                    catch { }
                }
            }
        }
        return result;
    }

    public static List<MonsterAnimationAssetRef> LoadBestAvailable(string gameRoot, out string buildId)
    {
        var cache = CachePath(gameRoot);
        var source = File.Exists(cache) ? cache : BundledPath;
        if (!File.Exists(source)) { buildId = ""; return []; }
        var portable = Read(source);
        if (portable.FormatVersion != 1) throw new InvalidDataException("动画预绑定索引版本不受支持。");
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LocalData"] = Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            ["StreamingAssets"] = Path.GetFullPath(IndexService.StreamingRoot(gameRoot)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
        };
        buildId = portable.GameBuildId;
        return portable.Assets.Select(x => new MonsterAnimationAssetRef
        {
            BundlePath = ResolveInside(roots.GetValueOrDefault(x.StorageKind, roots["LocalData"]), x.RelativeBundlePath),
            RelativeBundlePath = x.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar),
            AssetFileName = x.AssetFileName,
            PathId = x.PathId,
            Name = x.Name,
            CardId = x.CardId,
            Kind = x.Kind,
            StorageKind = x.StorageKind,
            ProfileTier = x.ProfileTier,
            ProfileRegion = x.ProfileRegion,
            ProfileScale = x.ProfileScale
        }).ToList();
    }

    public static PortableMonsterAnimationIndex Rebuild(
        string gameRoot,
        Action<int, int, int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var streamingRoot = IndexService.StreamingRoot(gameRoot);
        var files = Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Select(x => (Path: x, Root: localRoot, Storage: "LocalData")).ToList();
        if (Directory.Exists(streamingRoot)) files.AddRange(Directory.EnumerateFiles(streamingRoot, "*", SearchOption.AllDirectories).Select(x => (Path: x, Root: streamingRoot, Storage: "StreamingAssets")));
        var found = new ConcurrentBag<MonsterAnimationAssetRef>();
        var done = 0;
        var engine = new ModEngine();
        Parallel.ForEach(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 4, 12),
            CancellationToken = cancellationToken
        }, file =>
        {
            try
            {
                foreach (var asset in engine.ScanAnimationAssetsFast(file.Path, file.Root)) found.Add(new MonsterAnimationAssetRef
                {
                    BundlePath = asset.BundlePath,
                    RelativeBundlePath = asset.RelativeBundlePath,
                    AssetFileName = asset.AssetFileName,
                    PathId = asset.PathId,
                    Name = asset.Name,
                    CardId = asset.CardId,
                    Kind = asset.Kind,
                    StorageKind = file.Storage
                });
            }
            catch { }
            var current = Interlocked.Increment(ref done);
            if (current % 100 == 0 || current == files.Count) progress?.Invoke(current, files.Count, found.Count);
        });
        var result = new PortableMonsterAnimationIndex
        {
            GameBuildId = PortableIndexService.GetGameBuildId(gameRoot),
            Assets = found
                .GroupBy(x => $"{x.RelativeBundlePath}\0{x.AssetFileName}\0{x.PathId}", StringComparer.OrdinalIgnoreCase)
                .Select(x => WithoutAbsolutePath(x.First()))
                .OrderBy(x => int.TryParse(x.CardId, out var id) ? id : int.MaxValue)
                .ThenBy(x => x.Kind)
                .ThenBy(x => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        Write(CachePath(gameRoot), result);
        return result;
    }

    public static void Export(string gameRoot, PortableMonsterAnimationIndex index, string outputPath) => Write(outputPath, index);

    public static PortableMonsterAnimationIndex Read(string path)
    {
        using var file = File.OpenRead(path);
        using var brotli = new BrotliStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<PortableMonsterAnimationIndex>(brotli) ?? throw new InvalidDataException("动画预绑定索引无法读取。");
    }

    public static void Write(string path, PortableMonsterAnimationIndex index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(brotli, index);
    }

    public static string CachePath(string gameRoot)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{localRoot}|animation-v1")))[..12];
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"animation_index_{id}.json.br");
    }

    static MonsterAnimationAssetRef WithoutAbsolutePath(MonsterAnimationAssetRef x) => new()
    {
        RelativeBundlePath = x.RelativeBundlePath.Replace('\\', '/'),
        AssetFileName = x.AssetFileName,
        PathId = x.PathId,
        Name = x.Name,
        CardId = x.CardId,
        Kind = x.Kind,
        StorageKind = x.StorageKind,
        ProfileTier = x.ProfileTier,
        ProfileRegion = x.ProfileRegion,
        ProfileScale = x.ProfileScale
    };

    static string ResolveInside(string fullRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("动画索引包含无效路径。");
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("动画索引路径越出了 LocalData。");
        return full;
    }
}
