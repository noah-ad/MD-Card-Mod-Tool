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
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
