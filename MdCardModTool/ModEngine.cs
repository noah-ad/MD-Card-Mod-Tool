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

    static string CategoryFor(string name, int width, int height)
    {
        if (name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase)) return "卡框 card_frame";
        if (Regex.IsMatch(name, "^P\\d+$", RegexOptions.IgnoreCase)) return "卡图原画";
        if (Regex.IsMatch(name, "^\\d+$")) return "卡图缩略图";
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
        var backup = Path.Combine(backupRoot, texture.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(texture.BundlePath, backup);
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        var temporary = texture.ActiveBundlePath + ".mdcardtool.tmp";
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(texture.ActiveBundlePath);
            var assets = manager.LoadAssetsFileFromBundle(bundle, texture.AssetFileName);
            EnsureDatabase(manager, assets);
            var info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).First(x => x.PathId == texture.PathId);
            var field = manager.GetBaseField(assets, info);
            field["m_Width"].AsInt = image.Width;
            field["m_Height"].AsInt = image.Height;
            field["m_TextureFormat"].AsInt = 4; // RGBA32: portable for the mod loader
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
        texture.Width = image.Width;
        texture.Height = image.Height;
        texture.Category = CategoryFor(texture.Name, image.Width, image.Height);
    }

    public (int Width, int Height) ImageDimensions(string imagePath)
    {
        var info = SharpImage.Identify(imagePath) ?? throw new InvalidDataException("无法读取图片尺寸。");
        return (info.Width, info.Height);
    }
}
