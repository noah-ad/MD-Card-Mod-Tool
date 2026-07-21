using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SharpImage = SixLabors.ImageSharp.Image;

namespace MdCardModTool;

public sealed record MonsterAnimationAssetProfile(string Tier, string Region, string Scale)
{
    public string DisplayName => Scale.Length == 0 ? $"{Tier} · {Region.ToUpperInvariant()}" : $"{Tier} · {Region.ToUpperInvariant()} · {Scale}";
    public string FilePrefix => string.Join('-', new[] { Tier, Region, Scale }.Where(x => x.Length > 0));
}

public sealed class RawAnimationManifest
{
    public int FormatVersion { get; init; } = 1;
    public string CardId { get; init; } = "";
    public List<RawAnimationManifestEntry> Files { get; init; } = [];
}

public sealed class RawAnimationManifestEntry
{
    public string FileName { get; init; } = "";
    public string RelativeBundlePath { get; init; } = "";
    public string AssetFileName { get; init; } = "";
    public long PathId { get; init; }
    public MonsterAnimationAssetKind Kind { get; init; }
}

public sealed class MonsterAnimationRawAssetService
{
    public const string ManifestFileName = "_动画资源映射.json";
    static readonly string[] Scales = BuildCandidateScales();
    readonly ModEngine _engine = new();

    public MonsterAnimationAssetProfile ResolveProfile(MonsterAnimationAssetRef asset)
    {
        var relative = Normalize(asset.RelativeBundlePath);
        foreach (var region in new[] { "tcg", "ocg" })
        {
            var basePath = $"Duel/Timeline/Duel/MonsterCutIn/{region}/P{asset.CardId}";
            foreach (var tier in new[] { "HighEnd_HD", "SD" })
            {
                if (asset.Kind == MonsterAnimationAssetKind.Skeleton)
                {
                    if (relative == Normalize(IndexService.ResourceBundleRelativePath($"{basePath}/{tier}/P{asset.CardId}JS")))
                        return new MonsterAnimationAssetProfile(tier, region, "");
                    continue;
                }

                foreach (var scale in Scales)
                {
                    var suffix = asset.Kind == MonsterAnimationAssetKind.Atlas ? $"/P{asset.CardId}.atlas" : $"/P{asset.CardId}";
                    if (relative == Normalize(IndexService.ResourceBundleRelativePath($"{basePath}/{tier}/{scale}{suffix}")))
                        return new MonsterAnimationAssetProfile(tier, region, scale);
                }
            }
        }
        return new MonsterAnimationAssetProfile("未识别组", asset.StorageKind, Path.GetFileName(asset.RelativeBundlePath));
    }

    public string ExportFileName(MonsterAnimationAssetRef asset)
    {
        var profile = ResolveProfile(asset);
        var extension = asset.Kind switch
        {
            MonsterAnimationAssetKind.Texture => ".png",
            MonsterAnimationAssetKind.Atlas => ".atlas",
            _ => ".json"
        };
        var resourceName = asset.Kind == MonsterAnimationAssetKind.Skeleton ? asset.Name + extension : Path.GetFileNameWithoutExtension(asset.Name) + extension;
        return Safe($"{profile.FilePrefix}_{resourceName}");
    }

    public byte[] Read(MonsterAnimationAssetRef asset) =>
        asset.Kind == MonsterAnimationAssetKind.Texture
            ? _engine.DecodePng(asset.AsTexture())
            : NormalizeTextData(_engine.ReadTextAsset(asset).Data);

    public RawAnimationManifest ExportAll(MonsterAnimationSet set, string directory)
    {
        Directory.CreateDirectory(directory);
        var manifest = new RawAnimationManifest { CardId = set.CardId };
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in Ordered(set.Assets))
        {
            var fileName = UniqueName(ExportFileName(asset), usedNames);
            File.WriteAllBytes(Path.Combine(directory, fileName), Read(asset));
            manifest.Files.Add(new RawAnimationManifestEntry
            {
                FileName = fileName,
                RelativeBundlePath = Normalize(asset.RelativeBundlePath),
                AssetFileName = asset.AssetFileName,
                PathId = asset.PathId,
                Kind = asset.Kind
            });
        }
        File.WriteAllText(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return manifest;
    }

    public void ReplaceOne(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
    {
        var rollback = TemporaryRollbackDirectory();
        Directory.CreateDirectory(rollback);
        var snapshot = Path.Combine(rollback, "000.bundle");
        File.Copy(asset.BundlePath, snapshot, true);
        try
        {
            ValidateInput(asset.Kind, inputPath);
            Apply(gameRoot, asset, inputPath);
        }
        catch
        {
            File.Copy(snapshot, asset.BundlePath, true);
            throw;
        }
        finally { TryDeleteDirectory(rollback); }
    }

    public int ImportAll(string gameRoot, MonsterAnimationSet set, string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException($"所选目录缺少 {ManifestFileName}，请先用本窗口“导出全部”建立可回导目录。", manifestPath);
        var manifest = JsonSerializer.Deserialize<RawAnimationManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("动画资源映射无法读取。");
        if (manifest.FormatVersion != 1 || manifest.CardId != set.CardId) throw new InvalidDataException($"映射属于卡号 {manifest.CardId}，当前窗口是 {set.CardId}。");

        var imports = new List<(MonsterAnimationAssetRef Asset, string Path)>();
        foreach (var entry in manifest.Files)
        {
            if (entry.FileName != Path.GetFileName(entry.FileName)) throw new InvalidDataException("映射中包含越界文件名。");
            var asset = set.Assets.FirstOrDefault(x =>
                x.Kind == entry.Kind &&
                x.PathId == entry.PathId &&
                string.Equals(x.AssetFileName, entry.AssetFileName, StringComparison.Ordinal) &&
                string.Equals(Normalize(x.RelativeBundlePath), Normalize(entry.RelativeBundlePath), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"当前游戏版本找不到映射资源：{entry.FileName}");
            var input = Path.Combine(directory, entry.FileName);
            if (!File.Exists(input)) throw new FileNotFoundException($"缺少待导入文件：{entry.FileName}", input);
            ValidateInput(asset.Kind, input);
            imports.Add((asset, input));
        }
        if (imports.Count == 0) throw new InvalidDataException("映射中没有可导入的动画资源。");

        var rollback = TemporaryRollbackDirectory();
        Directory.CreateDirectory(rollback);
        var bundles = imports.Select(x => x.Asset.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            for (var i = 0; i < bundles.Length; i++) File.Copy(bundles[i], Path.Combine(rollback, $"{i:D3}.bundle"), true);
            try
            {
                foreach (var item in imports) Apply(gameRoot, item.Asset, item.Path);
            }
            catch
            {
                for (var i = 0; i < bundles.Length; i++) File.Copy(Path.Combine(rollback, $"{i:D3}.bundle"), bundles[i], true);
                throw;
            }
        }
        finally { TryDeleteDirectory(rollback); }
        return imports.Count;
    }

    public bool Restore(string gameRoot, MonsterAnimationAssetRef asset)
    {
        var backup = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
        if (!File.Exists(backup)) return false;
        File.Copy(backup, asset.BundlePath, true);
        return true;
    }

    void Apply(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
    {
        var backupRoot = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind);
        if (asset.Kind == MonsterAnimationAssetKind.Texture)
        {
            using var image = SharpImage.Load<Rgba32>(inputPath);
            _engine.ReplaceAnimationAtlas(asset, image, backupRoot);
        }
        else
        {
            var data = NormalizeTextData(File.ReadAllBytes(inputPath));
            _engine.ReplaceTextAsset(_engine.ReadTextAsset(asset), data, backupRoot);
        }
    }

    static void ValidateInput(MonsterAnimationAssetKind kind, string inputPath)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("找不到待替换文件。", inputPath);
        if (kind == MonsterAnimationAssetKind.Texture)
        {
            var info = SharpImage.Identify(inputPath) ?? throw new InvalidDataException("无法读取动画图集图片。");
            if (info.Width < 1 || info.Height < 1 || info.Width > 16384 || info.Height > 16384)
                throw new InvalidDataException("动画图集尺寸必须在 1–16384 像素范围内。");
            return;
        }
        var data = NormalizeTextData(File.ReadAllBytes(inputPath));
        if (data.Length == 0) throw new InvalidDataException("文本资源不能为空。");
        if (kind == MonsterAnimationAssetKind.Skeleton) using (JsonDocument.Parse(data)) { }
        else _ = new UTF8Encoding(false, true).GetString(data);
    }

    static byte[] NormalizeTextData(byte[] data)
    {
        var start = data.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var end = data.Length;
        while (end > start && data[end - 1] == 0) end--;
        return data.AsSpan(start, end - start).ToArray();
    }

    static IEnumerable<MonsterAnimationAssetRef> Ordered(IEnumerable<MonsterAnimationAssetRef> assets) =>
        assets.OrderBy(x => x.Kind).ThenBy(x => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase);

    static string[] BuildCandidateScales()
    {
        var common = new[] { "1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4" };
        return common.Concat(Enumerable.Range(1, 2000).Select(x => (x / 1000d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))).Distinct(StringComparer.Ordinal).ToArray();
    }

    static string UniqueName(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var i = 2; ; i++)
        {
            var value = $"{stem}_{i}{extension}";
            if (used.Add(value)) return value;
        }
    }

    static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
    static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    static string TemporaryRollbackDirectory() => Path.Combine(Path.GetTempPath(), "MDCardModTool", "raw_animation_rollback_" + Guid.NewGuid().ToString("N"));
    static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}
