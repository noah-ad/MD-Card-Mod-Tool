using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MdCardModTool;

public sealed class PortableGameIndex
{
    [JsonPropertyName("v")] public int FormatVersion { get; init; } = 1;
    [JsonPropertyName("b")] public string GameBuildId { get; init; } = "";
    [JsonPropertyName("a")] public int AlternateArtIndexVersion { get; init; }
    [JsonPropertyName("t")] public List<PortableTextureEntry> Textures { get; init; } = [];
}

public sealed class PortableTextureEntry
{
    [JsonPropertyName("r")] public string RelativeBundlePath { get; init; } = "";
    [JsonPropertyName("p")] public long PathId { get; init; }
    [JsonPropertyName("f")] public string AssetFileName { get; init; } = "";
    [JsonPropertyName("n")] public string Name { get; init; } = "";
    [JsonPropertyName("w")] public int Width { get; init; }
    [JsonPropertyName("h")] public int Height { get; init; }
    [JsonPropertyName("c")] public string Category { get; init; } = "其他贴图";
    [JsonPropertyName("a")] public bool IsAlternateArt { get; init; }
    [JsonPropertyName("m")] public bool IsTokenOrMisc { get; init; }
    [JsonPropertyName("s")] public string SourceKind { get; init; } = "";
    [JsonPropertyName("k")] public string CardKey { get; init; } = "";
}

/// <summary>把已建立的卡号/资源映射转换成不含用户哈希和绝对路径的随包索引。</summary>
public static class PortableIndexService
{
    public const string BundledFileName = "prebuilt-index-v1.json.br";

    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, BundledFileName);

    public static bool TryLoadBundled(string gameRoot, out GameIndex index, out string buildId)
    {
        index = new GameIndex(); buildId = "";
        if (!File.Exists(BundledPath)) return false;
        var portable = Read(BundledPath);
        if (portable.FormatVersion != 1 || portable.Textures.Count < 1_000) throw new InvalidDataException("随包预绑定索引格式错误或内容不完整。");
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var streamingRoot = IndexService.StreamingRoot(gameRoot);
        index = new GameIndex
        {
            AlternateArtIndexVersion = portable.AlternateArtIndexVersion,
            Textures = portable.Textures.Select(x => new TexRef
            {
                BundlePath = ResolveInside(SourceRoot(gameRoot, localRoot, streamingRoot, x.SourceKind), x.RelativeBundlePath),
                RelativeBundlePath = x.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar),
                PathId = x.PathId,
                AssetFileName = x.AssetFileName,
                Name = x.Name,
                Width = x.Width,
                Height = x.Height,
                Category = x.Category,
                IsAlternateArt = x.IsAlternateArt,
                IsTokenOrMisc = x.IsTokenOrMisc,
                SourceKind = x.SourceKind,
                CardKey = x.CardKey
            }).ToList()
        };
        buildId = portable.GameBuildId;
        return true;
    }

    public static void Export(string gameRoot, GameIndex index, string outputPath)
    {
        var entries = index.Textures.Select(ToPortable).ToList();
        NormalizeBackedUpBundles(gameRoot, index.Textures, entries);
        var portable = new PortableGameIndex
        {
            GameBuildId = GetGameBuildId(gameRoot),
            AlternateArtIndexVersion = index.AlternateArtIndexVersion,
            Textures = entries
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var file = File.Create(outputPath);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(brotli, portable);
    }

    public static PortableGameIndex Read(string path)
    {
        using var file = File.OpenRead(path);
        using var brotli = new BrotliStream(file, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<PortableGameIndex>(brotli) ?? throw new InvalidDataException("无法读取随包预绑定索引。");
    }

    static PortableTextureEntry ToPortable(TexRef x) => new()
    {
        RelativeBundlePath = x.RelativeBundlePath.Replace('\\', '/'),
        PathId = x.PathId,
        AssetFileName = x.AssetFileName,
        Name = x.Name,
        Width = x.Width,
        Height = x.Height,
        Category = x.Category,
        IsAlternateArt = x.IsAlternateArt,
        IsTokenOrMisc = x.IsTokenOrMisc,
        SourceKind = x.SourceKind,
        CardKey = x.CardKey
    };

    static void NormalizeBackedUpBundles(string gameRoot, IReadOnlyList<TexRef> textures, List<PortableTextureEntry> entries)
    {
        var engine = new ModEngine();
        for (var i = 0; i < textures.Count; i++)
        {
            var texture = textures[i];
            var backupRoot = Path.Combine(gameRoot, "_MD卡图备份", texture.SourceKind);
            var backup = Path.Combine(backupRoot, texture.RelativeBundlePath);
            if (!File.Exists(backup)) continue;
            try
            {
                var original = engine.ScanBundle(backup, backupRoot, texture.SourceKind, includeDependencies: false).Textures
                    .FirstOrDefault(x => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName);
                if (original is null) continue;
                var category = original.Category;
                if (texture.SourceKind == "本地卡图")
                {
                    if (original.Width == 512 && original.Height == 1024) category = "灵摆卡图";
                    else if (original.Width == 512 && original.Height == 512)
                        category = texture.IsAlternateArt ? "异画卡图" : texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图";
                }
                entries[i] = new PortableTextureEntry
                {
                    RelativeBundlePath = entries[i].RelativeBundlePath,
                    PathId = entries[i].PathId,
                    AssetFileName = entries[i].AssetFileName,
                    Name = entries[i].Name,
                    Width = original.Width,
                    Height = original.Height,
                    Category = category,
                    IsAlternateArt = entries[i].IsAlternateArt,
                    IsTokenOrMisc = entries[i].IsTokenOrMisc,
                    SourceKind = entries[i].SourceKind,
                    CardKey = entries[i].CardKey
                };
            }
            catch { }
        }
    }

    static string SourceRoot(string gameRoot, string localRoot, string streamingRoot, string sourceKind) => sourceKind switch
    {
        "本地卡图" => localRoot,
        "游戏内图片" => streamingRoot,
        "卡框资源" => gameRoot,
        _ => throw new InvalidDataException($"预绑定索引包含未知来源：{sourceKind}")
    };

    static string ResolveInside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("预绑定索引包含无效路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("预绑定索引路径越出了游戏目录。");
        return full;
    }

    public static string GetGameBuildId(string gameRoot)
    {
        try
        {
            var manifest = Path.GetFullPath(Path.Combine(gameRoot, "..", "..", "appmanifest_1449850.acf"));
            if (!File.Exists(manifest)) return "";
            var match = Regex.Match(File.ReadAllText(manifest), "\\\"buildid\\\"\\s+\\\"(?<id>\\d+)\\\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : "";
        }
        catch { return ""; }
    }
}
