using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpSize = SixLabors.ImageSharp.Size;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace MdCardModTool;

public sealed class ModEngine
{
    readonly string _classData = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
    // 索引会连续解析数万 Bundle。每个文件重载一次 classdata.tpk 会让扫描时间成倍膨胀；
    // AssetsManager 不跨线程共享，因此按工作线程缓存，在每个 Bundle 后仍 UnloadAll 释放文件句柄。
    [ThreadStatic] static AssetsManager? _threadManager;

    AssetsManager NewManager()
    {
        var manager = _threadManager ??= new AssetsManager();
        if (manager.ClassPackage is null && File.Exists(_classData)) manager.LoadClassPackage(_classData);
        return manager;
    }

    void EnsureDatabase(AssetsManager manager, AssetsFileInstance assets)
    {
        if (manager.ClassDatabase is null && manager.ClassPackage is not null)
            manager.LoadClassDatabaseFromPackage(assets.file.Metadata.UnityVersion);
    }

    public List<TexRef> ListTextures(string bundlePath, string root, string sourceKind = "")
    {
        return ScanBundle(bundlePath, root, sourceKind).Textures;
    }

    public BundleScan ScanBundle(string bundlePath, string root, string sourceKind = "", bool includeDependencies = true)
    {
        var result = new BundleScan();
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry);
                    EnsureDatabase(manager, assets);
                    foreach (var info in assets.file.GetAssetsOfType(AssetClassID.Texture2D))
                    {
                        var field = manager.GetBaseField(assets, info);
                        var name = field["m_Name"].AsString;
                        result.Textures.Add(new TexRef
                        {
                            BundlePath = bundlePath,
                            RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
                            AssetFileName = entry,
                            PathId = info.PathId,
                            Name = name,
                            Width = field["m_Width"].AsInt,
                            Height = field["m_Height"].AsInt,
                            Category = CategoryFor(name, field["m_Width"].AsInt, field["m_Height"].AsInt),
                            CardKey = CardKey(name),
                            SourceKind = sourceKind
                        });
                    }
                    if (includeDependencies) foreach (var info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
                    {
                        var field = manager.GetBaseField(assets, info);
                        var name = field["m_Name"].AsString;
                        var key = CardKey(name);
                        if (key.Length > 0) result.Dependencies.Add(new AssetDependency { Name = name, CardKey = key, Kind = "TextAsset", BundlePath = bundlePath, RelativeBundlePath = Path.GetRelativePath(root, bundlePath) });
                    }
                }
                catch { }
            }
        }
        finally { manager.UnloadAll(); }
        return result;
    }

    /// <summary>
    /// Reads only the leading m_Name field of Texture2D/TextAsset objects. This is used to build
    /// the portable summon-animation map without decoding tens of thousands of unrelated assets.
    /// </summary>
    public List<MonsterAnimationAssetRef> ScanAnimationAssetsFast(string bundlePath, string root)
    {
        var result = new List<MonsterAnimationAssetRef>();
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry, false);
                    foreach (var type in new[] { AssetClassID.Texture2D, AssetClassID.TextAsset })
                    {
                        foreach (var info in assets.file.GetAssetsOfType(type))
                        {
                            var reader = assets.file.Reader;
                            var end = info.GetAbsoluteByteStart(assets.file) + info.ByteSize;
                            reader.Position = info.GetAbsoluteByteStart(assets.file);
                            var name = ReadAlignedString(reader, end);
                            if (!TryAnimationName(name, type, out var cardId, out var kind)) continue;
                            result.Add(new MonsterAnimationAssetRef
                            {
                                BundlePath = bundlePath,
                                RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
                                AssetFileName = entry,
                                PathId = info.PathId,
                                Name = name,
                                CardId = cardId,
                                Kind = kind
                            });
                        }
                    }
                }
                catch { }
            }
            return result;
        }
        finally { manager.UnloadAll(); }
    }

    static bool TryAnimationName(string name, AssetClassID type, out string cardId, out MonsterAnimationAssetKind kind)
    {
        cardId = ""; kind = default;
        Match match;
        if (type == AssetClassID.Texture2D)
        {
            match = Regex.Match(name, "^P(?<id>\\d+)$", RegexOptions.IgnoreCase);
            kind = MonsterAnimationAssetKind.Texture;
        }
        else
        {
            match = Regex.Match(name, "^P(?<id>\\d+)(?<suffix>JS|\\.atlas)$", RegexOptions.IgnoreCase);
            kind = name.EndsWith("JS", StringComparison.OrdinalIgnoreCase) ? MonsterAnimationAssetKind.Skeleton : MonsterAnimationAssetKind.Atlas;
        }
        if (!match.Success) return false;
        cardId = match.Groups["id"].Value;
        return true;
    }

    static string CategoryFor(string name, int width, int height)
    {
        if (name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase)) return "卡框 card_frame";
        if (Regex.IsMatch(name, "^P\\d+$", RegexOptions.IgnoreCase)) return "卡图原画";
        if (Regex.IsMatch(name, "^\\d+$")) return width == 512 && height == 1024 ? "灵摆卡图" : "卡图缩略图";
        if (width >= 1024 && height >= 1024) return "大图贴图";
        return "其他贴图";
    }

    public static string CardKey(string name)
    {
        var match = Regex.Match(name, "^P?(\\d+)(?:JS|\\.atlas)?$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "";
    }

    public BundleSummary InspectBundle(string bundlePath, string root)
    {
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            var types = new Dictionary<string, int>();
            var serialized = 0;
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry); serialized++;
                    foreach (var asset in assets.file.AssetInfos)
                    {
                        var name = ((AssetClassID)asset.TypeId).ToString();
                        types[name] = types.GetValueOrDefault(name) + 1;
                    }
                }
                catch { }
            }
            return new BundleSummary { RelativePath = Path.GetRelativePath(root, bundlePath), SerializedFiles = serialized, AssetTypes = types };
        }
        finally { manager.UnloadAll(); }
    }

    public List<string> ReadAssetBundleContainerPaths(string bundlePath)
    {
        var result = new List<string>();
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry);
                    EnsureDatabase(manager, assets);
                    foreach (var info in assets.file.GetAssetsOfType(AssetClassID.AssetBundle))
                    {
                        var field = manager.GetBaseField(assets, info);
                        var array = field["m_Container"]["Array"];
                        foreach (var pair in array.Children)
                        {
                            var path = pair["first"].AsString;
                            if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                        }
                    }
                }
                catch { }
            }
            return result;
        }
        finally { manager.UnloadAll(); }
    }

    public List<TextAssetRef> ReadTextAssets(string bundlePath)
    {
        var result = new List<TextAssetRef>();
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry); EnsureDatabase(manager, assets);
                    foreach (var info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
                    {
                        var field = manager.GetBaseField(assets, info);
                        result.Add(new TextAssetRef { BundlePath = bundlePath, RelativeBundlePath = Path.GetFileName(bundlePath), AssetFileName = entry, Name = field["m_Name"].AsString, PathName = field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString, PathId = info.PathId, Data = field["m_Script"].AsByteArray });
                    }
                }
                catch { }
            }
            return result;
        }
        finally { manager.UnloadAll(); }
    }

    public TextAssetRef? FindTextAsset(string bundlePath, string root, string name)
    {
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry); EnsureDatabase(manager, assets);
                    foreach (var info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
                    {
                        var field = manager.GetBaseField(assets, info);
                        if (!string.Equals(field["m_Name"].AsString, name, StringComparison.OrdinalIgnoreCase)) continue;
                        return new TextAssetRef
                        {
                            BundlePath = bundlePath,
                            RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
                            AssetFileName = entry,
                            Name = field["m_Name"].AsString,
                            PathName = field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString,
                            PathId = info.PathId,
                            Data = field["m_Script"].AsByteArray
                        };
                    }
                }
                catch { }
            }
            return null;
        }
        finally { manager.UnloadAll(); }
    }

    public TextAssetRef ReadTextAsset(MonsterAnimationAssetRef asset)
    {
        if (asset.Kind == MonsterAnimationAssetKind.Texture) throw new ArgumentException("动画纹理不是 TextAsset。", nameof(asset));
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(asset.BundlePath);
            var assets = manager.LoadAssetsFileFromBundle(bundle, asset.AssetFileName);
            EnsureDatabase(manager, assets);
            var info = assets.file.GetAssetsOfType(AssetClassID.TextAsset).First(x => x.PathId == asset.PathId);
            var field = manager.GetBaseField(assets, info);
            return new TextAssetRef
            {
                BundlePath = asset.BundlePath,
                RelativeBundlePath = asset.RelativeBundlePath,
                AssetFileName = asset.AssetFileName,
                Name = field["m_Name"].AsString,
                PathName = field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString,
                PathId = info.PathId,
                Data = field["m_Script"].AsByteArray
            };
        }
        finally { manager.UnloadAll(); }
    }

    /// <summary>
    /// Locates a TextAsset without loading the class database or decoding every object in the
    /// bundle.  Master Duel has tens of thousands of bundles; reading only the serialized-file
    /// type table keeps a one-time LocalData lookup practical.
    /// </summary>
    public TextAssetRef? FindTextAssetFast(string bundlePath, string root, string name)
    {
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            foreach (var entry in bundle.file.GetAllFileNames())
            {
                try
                {
                    var assets = manager.LoadAssetsFileFromBundle(bundle, entry, false);
                    foreach (var info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
                    {
                        var reader = assets.file.Reader;
                        var start = info.GetAbsoluteByteStart(assets.file);
                        var end = start + info.ByteSize;
                        reader.Position = start;
                        var assetName = ReadAlignedString(reader, end);
                        if (!string.Equals(assetName, name, StringComparison.OrdinalIgnoreCase)) continue;
                        var data = ReadAlignedBytes(reader, end);
                        var pathName = reader.Position < end ? ReadAlignedString(reader, end) : "";
                        return new TextAssetRef
                        {
                            BundlePath = bundlePath,
                            RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
                            AssetFileName = entry,
                            Name = assetName,
                            PathName = pathName,
                            PathId = info.PathId,
                            Data = data
                        };
                    }
                }
                catch { }
            }
            return null;
        }
        finally { manager.UnloadAll(); }
    }

    static string ReadAlignedString(AssetsFileReader reader, long end)
    {
        if (reader.Position + 4 > end) throw new InvalidDataException("TextAsset string length is missing.");
        var length = reader.ReadInt32();
        if (length < 0 || reader.Position + length > end) throw new InvalidDataException("TextAsset string length is invalid.");
        var value = Encoding.UTF8.GetString(reader.ReadBytes(length));
        reader.Align();
        return value;
    }

    static byte[] ReadAlignedBytes(AssetsFileReader reader, long end)
    {
        if (reader.Position + 4 > end) throw new InvalidDataException("TextAsset data length is missing.");
        var length = reader.ReadInt32();
        if (length < 0 || reader.Position + length > end) throw new InvalidDataException("TextAsset data length is invalid.");
        var value = reader.ReadBytes(length);
        reader.Align();
        return value;
    }

    public void ReplaceTextAsset(TextAssetRef asset, byte[] data, string backupRoot)
    {
        var backup = Path.Combine(backupRoot, asset.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(asset.BundlePath, backup);
        var temporary = asset.BundlePath + ".mdcardtool.tmp";
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(asset.BundlePath);
            var assets = manager.LoadAssetsFileFromBundle(bundle, asset.AssetFileName); EnsureDatabase(manager, assets);
            var info = assets.file.GetAssetsOfType(AssetClassID.TextAsset).First(x => x.PathId == asset.PathId);
            var field = manager.GetBaseField(assets, info);
            field["m_Script"].AsByteArray = data;
            var replacements = new List<AssetsReplacer> { new AssetsReplacerFromMemory(assets.file, info, field) };
            byte[] serialized;
            using (var stream = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(stream);
                assets.file.Write(writer, 0, replacements);
                serialized = stream.ToArray();
            }
            using var bundleWriter = new AssetsFileWriter(temporary);
            bundle.file.Write(bundleWriter, [new BundleReplacerFromMemory(assets.name, assets.name, true, serialized, -1)]);
        }
        finally { manager.UnloadAll(); }
        File.Move(temporary, asset.BundlePath, true);
    }

    public byte[] DecodePng(TexRef texture, int maxSize = 0)
    {
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(texture.ActiveBundlePath);
            var assets = manager.LoadAssetsFileFromBundle(bundle, texture.AssetFileName);
            EnsureDatabase(manager, assets);
            var info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).First(x => x.PathId == texture.PathId);
            var field = manager.GetBaseField(assets, info);
            var width = field["m_Width"].AsInt;
            var height = field["m_Height"].AsInt;
            var textureFile = TextureFile.ReadTextureFile(field);
            var pixels = textureFile.GetTextureData(assets);
            if (pixels is null || pixels.Length < width * height * 4) throw new InvalidDataException($"贴图 {texture.Name} 的解码数据不足（格式 {textureFile.m_TextureFormat}）。");
            using var image = SharpImage.LoadPixelData<Bgra32>(pixels.AsSpan(0, width * height * 4), width, height);
            image.Mutate(x => x.Flip(FlipMode.Vertical));
            if (maxSize <= 0)
            {
                using var original = new MemoryStream();
                image.SaveAsPng(original);
                return original.ToArray();
            }
            if (image.Width > maxSize || image.Height > maxSize) image.Mutate(x => x.Resize(new ResizeOptions { Size = new SharpSize(maxSize, maxSize), Mode = ResizeMode.Max }));
            using var resized = new MemoryStream();
            image.SaveAsPng(resized);
            return resized.ToArray();
        }
        finally { manager.UnloadAll(); }
    }

    public void Replace(TexRef texture, string imagePath, string backupRoot)
    {
        using var image = SharpImage.Load<Rgba32>(imagePath);
        ReplaceImage(texture, image, backupRoot);
    }

    public void Replace(TexRef texture, byte[] encodedImage, string backupRoot)
    {
        using var image = SharpImage.Load<Rgba32>(encodedImage);
        ReplaceImage(texture, image, backupRoot);
    }

    void ReplaceImage(TexRef texture, SixLabors.ImageSharp.Image<Rgba32> image, string backupRoot)
    {
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        ReplaceTextureData(texture, image.Width, image.Height, 4, pixels, backupRoot);
    }

    public void ReplaceAnimationAtlas(MonsterAnimationAssetRef asset, SixLabors.ImageSharp.Image<Rgba32> atlas, string backupRoot)
    {
        if (asset.Kind != MonsterAnimationAssetKind.Texture) throw new ArgumentException("目标不是动画 Texture2D。", nameof(asset));
        ReplaceAnimationAtlas(asset, EncodeAnimationAtlas(atlas), backupRoot);
    }

    public AnimationAtlasTextureData EncodeAnimationAtlas(SixLabors.ImageSharp.Image<Rgba32> atlas)
    {
        var texconv = Path.Combine(AppContext.BaseDirectory, "tools", "texconv.exe");
        if (!File.Exists(texconv)) throw new FileNotFoundException("缺少动画图集编码器 tools\\texconv.exe，请使用完整分享包。", texconv);
        var temporary = Path.Combine(Path.GetTempPath(), "MDCardModTool", "texconv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var input = Path.Combine(temporary, "atlas.png");
            using (var flipped = atlas.Clone(x => x.Flip(FlipMode.Vertical))) flipped.SaveAsPng(input);
            var start = new ProcessStartInfo
            {
                FileName = texconv,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in new[] { "-nologo", "-y", "-f", "BC3_UNORM", "-m", "1", "-o", temporary, input }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 texconv.exe。");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            if (process.ExitCode != 0) throw new InvalidDataException("texconv 无法压缩动画图集：" + (standardError.Result + " " + standardOutput.Result).Trim());
            var dds = Directory.EnumerateFiles(temporary, "*.dds", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? Directory.EnumerateFiles(temporary, "*.DDS", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? throw new InvalidDataException("texconv 没有生成 DDS 数据。");
            var bytes = File.ReadAllBytes(dds);
            var expected = ((atlas.Width + 3) / 4) * ((atlas.Height + 3) / 4) * 16;
            if (bytes.Length < expected + 128 || !bytes.AsSpan(0, 4).SequenceEqual("DDS "u8)) throw new InvalidDataException("texconv 生成的 DDS/BC3 数据不完整。");
            return new AnimationAtlasTextureData(atlas.Width, atlas.Height, bytes.AsSpan(bytes.Length - expected, expected).ToArray());
        }
        finally
        {
            try { Directory.Delete(temporary, true); } catch { }
        }
    }

    public void ReplaceAnimationAtlas(MonsterAnimationAssetRef asset, AnimationAtlasTextureData encoded, string backupRoot)
    {
        if (asset.Kind != MonsterAnimationAssetKind.Texture) throw new ArgumentException("目标不是动画 Texture2D。", nameof(asset));
        ReplaceTextureData(asset.AsTexture(), encoded.Width, encoded.Height, 12, encoded.Data, backupRoot); // Unity TextureFormat.DXT5
    }

    void ReplaceTextureData(TexRef texture, int width, int height, int textureFormat, byte[] pixels, string backupRoot)
    {
        var backup = Path.Combine(backupRoot, texture.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(texture.BundlePath, backup);
        var temporary = texture.ActiveBundlePath + ".mdcardtool.tmp";
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(texture.ActiveBundlePath);
            var assets = manager.LoadAssetsFileFromBundle(bundle, texture.AssetFileName);
            EnsureDatabase(manager, assets);
            var info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).First(x => x.PathId == texture.PathId);
            var field = manager.GetBaseField(assets, info);
            field["m_Width"].AsInt = width;
            field["m_Height"].AsInt = height;
            field["m_TextureFormat"].AsInt = textureFormat;
            field["m_MipCount"].AsInt = 1;
            if (field["m_CompleteImageSize"] is { IsDummy: false } size) size.AsInt = pixels.Length;
            field["image data"].AsByteArray = pixels;
            var streamData = field["m_StreamData"];
            streamData["offset"].AsLong = 0;
            streamData["size"].AsLong = 0;
            streamData["path"].AsString = "";
            var replacements = new List<AssetsReplacer> { new AssetsReplacerFromMemory(assets.file, info, field) };
            byte[] serialized;
            using (var stream = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(stream);
                assets.file.Write(writer, 0, replacements);
                serialized = stream.ToArray();
            }
            using var bundleWriter = new AssetsFileWriter(temporary);
            bundle.file.Write(bundleWriter, new List<BundleReplacer>
            {
                new BundleReplacerFromMemory(assets.name, assets.name, true, serialized, -1)
            });
        }
        finally { manager.UnloadAll(); }
        File.Move(temporary, texture.ActiveBundlePath, true);
        texture.Width = width;
        texture.Height = height;
        texture.Category = CategoryFor(texture.Name, width, height);
    }

    public (int Width, int Height) ImageDimensions(string imagePath)
    {
        var info = SharpImage.Identify(imagePath) ?? throw new InvalidDataException("无法读取图片尺寸。");
        return (info.Width, info.Height);
    }
}

public sealed record AnimationAtlasTextureData(int Width, int Height, byte[] Data);
