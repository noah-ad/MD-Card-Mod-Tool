using System.Text.Json;

namespace MdCardModTool;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--build-index")
        {
            IndexService.BuildAndSave(args[1], (done, total, found) => Console.WriteLine($"{done}/{total}; textures={found}"));
            return;
        }
        if (args.Length == 3 && args[0] == "--scan-card")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = File.Exists(cache) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex() : new GameIndex();
            var result = IndexService.ScanMissingLocalCard(args[1], index, args[2], (done, total, added) => Console.WriteLine($"{done}/{total}; added={added}"));
            var known = index.Textures.Select(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            index.Textures.AddRange(result.Textures.Where(x => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")));
            IndexService.Save(args[1], index);
            foreach (var x in index.Textures.Where(x => x.SourceKind == "本地卡图" && x.CardKey == args[2])) Console.WriteLine($"FOUND {x.Name}; {x.Width}x{x.Height}; {x.RelativeBundlePath}");
            return;
        }
        if (args.Length == 2 && args[0] == "--enrich-local-card-index")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = File.Exists(cache) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex() : new GameIndex();
            var result = IndexService.ScanMissingLocalCard(args[1], index, "0", (done, total, added) => Console.WriteLine($"{done}/{total}; added={added}"));
            var known = index.Textures.Select(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = result.Textures.Where(x => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")).ToList();
            YgoCdbCardCatalog.ClassifyTexturesAsync(additions).GetAwaiter().GetResult();
            index.Textures.AddRange(additions);
            IndexService.Save(args[1], index);
            Console.WriteLine($"added={additions.Count}; total={index.Textures.Count}");
            return;
        }
        if (args.Length == 2 && args[0] == "--sanitize-card-index")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = File.Exists(cache) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex() : new GameIndex();
            var removed = IndexService.RemoveSpineAtlasParts(index);
            removed += IndexService.RemoveNonCardLocalTextures(index);
            // 补扫新增的 512 缩略图也要补上已有的异画／Token 分类，不把它们混进普通卡图。
            index.AlternateArtIndexVersion = 0;
            YgoCdbCardCatalog.ClassifyAlternateArtsAsync(index).GetAwaiter().GetResult();
            IndexService.Save(args[1], index);
            Console.WriteLine($"removed={removed}; total={index.Textures.Count}");
            return;
        }
        if (args.Length == 2 && args[0] == "--find-card-frame")
        {
            var game = args[1];
            var targets = new[]
            {
                Path.Combine(game, "masterduel_Data", "data.unity3d"),
                IndexService.StreamingRoot(game)
            };
            foreach (var target in targets)
            {
                var files = File.Exists(target) ? new[] { target } : Directory.Exists(target) ? Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories) : [];
                foreach (var file in files)
                {
                    try
                    {
                        foreach (var x in new ModEngine().ListTextures(file, game, "游戏内图片").Where(x => x.Name.Contains("card", StringComparison.OrdinalIgnoreCase) && x.Name.Contains("frame", StringComparison.OrdinalIgnoreCase)))
                            Console.WriteLine($"{x.Name}\t{x.Width}x{x.Height}\t{x.RelativeBundlePath}\tPathID={x.PathId}");
                    }
                    catch { }
                }
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--add-card-frames")
        {
            IndexService.AddCardFramesAndSave(args[1]);
            return;
        }
        if (args.Length == 3 && args[0] == "--export-mods")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = File.Exists(cache) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex() : new GameIndex();
            var info = new ModPackageService().Export(args[1], index.Textures, args[2]);
            Console.WriteLine($"{info.BundleCount} bundles; {info.TotalSize} bytes; {args[2]}");
            return;
        }
        if (args.Length == 2 && args[0] == "--inspect-mod")
        {
            var info = new ModPackageService().Inspect(args[1]);
            Console.WriteLine($"{info.Name}; {info.BundleCount} bundles; {info.TotalSize} bytes");
            return;
        }
        if (args.Length == 3 && args[0] == "--import-mods")
        {
            var result = new ModPackageService().Import(args[1], args[2]);
            Console.WriteLine($"{result.BundleCount} bundles imported");
            return;
        }
        if (args.Length == 4 && args[0] == "--export-card")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex();
            var texture = index.Textures.FirstOrDefault(x => x.SourceKind == "本地卡图" && x.CardKey == args[2]) ?? throw new FileNotFoundException($"索引中没有卡号 {args[2]}。");
            File.WriteAllBytes(args[3], new ModEngine().DecodePng(texture));
            Console.WriteLine(args[3]);
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect-bundle")
        {
            var bundle = Path.GetFullPath(args[1]);
            var root = Path.GetFullPath(args[2]);
            foreach (var texture in new ModEngine().ScanBundle(bundle, root, "诊断", includeDependencies: false).Textures)
                Console.WriteLine($"{texture.Name}; {texture.Width}x{texture.Height}; PathID={texture.PathId}; file={texture.AssetFileName}; {texture.RelativeBundlePath}");
            return;
        }
        if (args.Length == 3 && args[0] == "--export-portable-index")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? throw new InvalidDataException("本地索引无法读取。");
            PortableIndexService.Export(args[1], index, args[2]);
            var portable = PortableIndexService.Read(args[2]);
            Console.WriteLine($"build={portable.GameBuildId}; textures={portable.Textures.Count}; bytes={new FileInfo(args[2]).Length}");
            return;
        }
        if (args.Length == 2 && args[0] == "--inspect-portable-index")
        {
            var portable = PortableIndexService.Read(args[1]);
            Console.WriteLine($"format={portable.FormatVersion}; build={portable.GameBuildId}; textures={portable.Textures.Count}; alternateVersion={portable.AlternateArtIndexVersion}");
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect-portable-card")
        {
            var portable = PortableIndexService.Read(args[1]);
            foreach (var x in portable.Textures.Where(x => x.CardKey == args[2])) Console.WriteLine($"{x.CardKey}; {x.Width}x{x.Height}; {x.Category}; {x.RelativeBundlePath}");
            return;
        }
        if (args.Length == 2 && args[0] == "--test-prebuilt-index")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out var buildId)) throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
            var first = index.Textures.FirstOrDefault(x => x.SourceKind == "本地卡图");
            Console.WriteLine($"build={buildId}; textures={index.Textures.Count}; first={first?.BundlePath}");
            return;
        }
        if (args.Length == 2 && args[0] == "--install-prebuilt-index")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out var buildId)) throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
            IndexService.Save(args[1], index);
            var local = IndexService.FindLocalRoot(args[1])!;
            Console.WriteLine($"build={buildId}; textures={index.Textures.Count}; cache={IndexService.CachePath(local, IndexService.StreamingRoot(args[1]))}");
            return;
        }
        if (args.Length == 5 && args[0] == "--crop-image")
        {
            var targetWidth = int.Parse(args[3]); var targetHeight = int.Parse(args[4]);
            using var preview = ImageCropService.LoadPreview(args[1]);
            var targetAspect = targetWidth / (double)targetHeight;
            var cropWidth = preview.Width; var cropHeight = (int)Math.Round(cropWidth / targetAspect);
            if (cropHeight > preview.Height) { cropHeight = preview.Height; cropWidth = (int)Math.Round(cropHeight * targetAspect); }
            var crop = new RectangleF((preview.Width - cropWidth) / 2f, (preview.Height - cropHeight) / 2f, cropWidth, cropHeight);
            File.WriteAllBytes(args[2], ImageCropService.CropAndResize(args[1], crop, targetWidth, targetHeight));
            Console.WriteLine($"{targetWidth}x{targetHeight}; {new FileInfo(args[2]).Length} bytes; {args[2]}");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

}
