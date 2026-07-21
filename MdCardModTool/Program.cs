using System.Text.Json;
using SixLabors.ImageSharp;

namespace MdCardModTool;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--build-animation-index")
        {
            var index = MonsterAnimationIndexService.Rebuild(args[1], (done, total, found) => Console.WriteLine($"{done}/{total}; animation assets={found}"));
            MonsterAnimationIndexService.Export(args[1], index, args[2]);
            Console.WriteLine($"build={index.GameBuildId}; assets={index.Assets.Count}; cards={index.Assets.Select(x => x.CardId).Distinct().Count()}; {args[2]}");
            return;
        }
        if (args.Length == 2 && args[0] == "--list-animation-cards")
        {
            foreach (var cardId in MonsterAnimationIndexService.FindInstalledCardIds(args[1])) Console.WriteLine(cardId);
            return;
        }
        if (args.Length == 2 && args[0] == "--test-animation-form")
        {
            using var form = new MonsterAnimationForm(args[1]);
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            Console.WriteLine($"shown={form.ClientSize.Width}x{form.ClientSize.Height}");
            form.Close();
            return;
        }
        if (args.Length == 3 && args[0] == "--test-animation-form-current")
        {
            using var form = new MonsterAnimationForm(args[1], args[2]) { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var label = (Label?)typeof(MonsterAnimationForm).GetField("_sourceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var preview = (AnimationPreviewCanvas?)typeof(MonsterAnimationForm).GetField("_preview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && preview?.Frame is null && label?.Text.Contains("原版多骨骼", StringComparison.Ordinal) != true)
            {
                Application.DoEvents();
                Thread.Sleep(25);
            }
            var initialScale = preview?.ScalePercent ?? 0;
            var currentSizeLoaded = initialScale is >= 10 and <= 500 && Math.Abs((preview?.AnimationScale ?? 0f) - initialScale / 100f) < 0.001f;
            var scale = (NumericUpDown?)typeof(MonsterAnimationForm).GetField("_scale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            if (scale is not null) scale.Value = 35;
            Application.DoEvents();
            var realtimeScale = preview?.ScalePercent == 35 && Math.Abs(preview.AnimationScale - 0.35f) < 0.001f;
            var result = currentSizeLoaded && realtimeScale && (preview?.Frame is not null || label?.Text.Contains("原版多骨骼", StringComparison.Ordinal) == true);
            Console.WriteLine($"status={label?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame is not null}; initialScale={initialScale}; realtimeScale={preview?.ScalePercent}");
            form.Close();
            if (!result) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 4 && args[0] is "--test-animation-form-media" or "--test-animation-form-chroma")
        {
            using var form = new MonsterAnimationForm(args[1], args[2]) { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var preview = (AnimationPreviewCanvas?)typeof(MonsterAnimationForm).GetField("_preview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var source = (Label?)typeof(MonsterAnimationForm).GetField("_sourceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var removeGreenScreen = (CheckBox?)typeof(MonsterAnimationForm).GetField("_removeGreenScreen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            if (args[0] == "--test-animation-form-chroma" && removeGreenScreen is not null) removeGreenScreen.Checked = true;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && preview?.Frame is null && source?.Text.Contains("原版多骨骼", StringComparison.Ordinal) != true) { Application.DoEvents(); Thread.Sleep(25); }
            var method = typeof(MonsterAnimationForm).GetMethod("LoadMediaAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? throw new MissingMethodException("LoadMediaAsync");
            var task = (Task?)method.Invoke(form, [args[3]]) ?? throw new InvalidOperationException("媒体加载任务没有启动。");
            deadline = DateTime.UtcNow.AddSeconds(60);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(25); }
            task.GetAwaiter().GetResult();
            var scale = (NumericUpDown?)typeof(MonsterAnimationForm).GetField("_scale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var result = task.IsCompletedSuccessfully && preview?.Frame is not null && scale?.Value == 100 && preview.ScalePercent == 100
                && (args[0] != "--test-animation-form-chroma" || source?.Text.Contains("绿幕已透明", StringComparison.Ordinal) == true);
            Console.WriteLine($"media={source?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame is not null}; fullCanvasScale={scale?.Value}");
            form.Close();
            if (!result) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 2 && args[0] == "--test-animation-catalog")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out _)) throw new FileNotFoundException("缺少卡图预绑定索引。");
            var ids = MonsterAnimationIndexService.LoadBundledCardIds();
            var tagged = index.Textures.Where(x => x.SourceKind == "本地卡图" && ids.Contains(x.CardKey)).ToArray();
            Console.WriteLine($"ids={ids.Count}; taggedTextures={tagged.Length}; distinctCards={tagged.Select(x => x.CardKey).Distinct().Count()}");
            return;
        }
        if (args.Length == 1 && args[0] == "--test-main-form")
        {
            using var form = new MainForm { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var groups = (TreeView?)typeof(MainForm).GetField("_groups", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            TreeNode? animationNode = null;
            while (DateTime.UtcNow < deadline && animationNode is null)
            {
                Application.DoEvents();
                animationNode = groups?.Nodes.Cast<TreeNode>().SelectMany(x => x.Nodes.Cast<TreeNode>()).FirstOrDefault(x => x.Text.StartsWith("有怪兽动画", StringComparison.Ordinal));
                if (animationNode is null) Thread.Sleep(25);
            }
            Console.WriteLine(animationNode is null ? "animationCategory=missing" : $"animationCategory={animationNode.Text}");
            form.Close();
            if (animationNode is null) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect-animation-card")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            Console.WriteLine($"card={set.CardId}; complete={set.IsComplete}; {set.CountSummary}");
            foreach (var asset in set.Assets) Console.WriteLine($"{asset.Kind}; {asset.Name}; PathID={asset.PathId}; {asset.RelativeBundlePath}");
            return;
        }
        if (args.Length == 4 && args[0] == "--dump-animation-card")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            Directory.CreateDirectory(args[3]);
            var engine = new ModEngine();
            File.WriteAllBytes(Path.Combine(args[3], $"P{args[2]}JS.json"), engine.ReadTextAsset(set.Skeletons[0]).Data);
            File.WriteAllBytes(Path.Combine(args[3], $"P{args[2]}.atlas"), engine.ReadTextAsset(set.Atlases[0]).Data);
            var root = set.Textures[0].StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(args[1]) : IndexService.FindLocalRoot(args[1])!;
            foreach (var texture in engine.ScanBundle(set.Textures[0].BundlePath, root, set.Textures[0].ModSourceKind, includeDependencies: false).Textures)
                File.WriteAllBytes(Path.Combine(args[3], texture.Name + ".png"), engine.DecodePng(texture));
            Console.WriteLine(args[3]);
            return;
        }
        if (args.Length == 4 && args[0] == "--test-current-animation-preview")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            using var preview = MonsterAnimationCurrentPreview.TryLoad(set) ?? throw new InvalidDataException("当前动画不是可逐帧还原的单槽序列动画。");
            Directory.CreateDirectory(args[3]);
            preview.Frames[0].Save(Path.Combine(args[3], "frame-0001.png"));
            Console.WriteLine($"frames={preview.Frames.Count}; fps={preview.FramesPerSecond}; animation={preview.AnimationName}");
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect-animation-bundle")
        {
            foreach (var asset in new ModEngine().ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])))
            {
                Console.WriteLine($"{asset.Kind}; {asset.Name}; PathID={asset.PathId}; {asset.RelativeBundlePath}");
                if (asset.Kind != MonsterAnimationAssetKind.Texture)
                {
                    var data = new ModEngine().ReadTextAsset(asset).Data;
                    var text = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                    Console.WriteLine(text[..Math.Min(text.Length, 3000)]);
                }
            }
            return;
        }
        if (args.Length == 4 && args[0] == "--dump-animation-text")
        {
            var engine = new ModEngine();
            var asset = engine.ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).First(x => x.Kind != MonsterAnimationAssetKind.Texture);
            File.WriteAllBytes(Path.GetFullPath(args[3]), engine.ReadTextAsset(asset).Data);
            Console.WriteLine($"{asset.Kind}; {asset.Name}; {new FileInfo(args[3]).Length} bytes; {Path.GetFullPath(args[3])}");
            return;
        }
        if (args.Length == 2 && args[0] == "--bundle-containers")
        {
            foreach (var path in new ModEngine().ReadAssetBundleContainerPaths(Path.GetFullPath(args[1]))) Console.WriteLine(path);
            return;
        }
        if (args.Length == 4 && args[0] == "--test-animation-media")
        {
            Directory.CreateDirectory(args[3]);
            Console.WriteLine("extracting");
            using var media = MonsterAnimationMedia.ExtractAsync(args[1], 12, 48, 256).GetAwaiter().GetResult();
            Console.WriteLine($"extracted {media.FramePaths.Count}");
            using var built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 12, 100, 4096);
            Console.WriteLine($"built {built.AtlasWidth}x{built.AtlasHeight}");
            built.AtlasImage.SaveAsPng(Path.Combine(args[3], $"P{args[2]}.png"));
            File.WriteAllText(Path.Combine(args[3], $"P{args[2]}.atlas.txt"), built.AtlasText);
            File.WriteAllBytes(Path.Combine(args[3], $"P{args[2]}JS.json"), built.SkeletonJson);
            Console.WriteLine("encoding dxt5");
            var encoded = new ModEngine().EncodeAnimationAtlas(built.AtlasImage);
            Console.WriteLine($"frames={built.FrameCount}; fps={built.FramesPerSecond}; atlas={built.AtlasWidth}x{built.AtlasHeight}; dxt5={encoded.Data.Length}");
            return;
        }
        if (args.Length == 4 && args[0] == "--test-animation-hd-media")
        {
            Directory.CreateDirectory(args[3]);
            using var media = MonsterAnimationMedia.ExtractAsync(args[1], 15, 28, 1920).GetAwaiter().GetResult();
            using var first = media.LoadFrame(0);
            using var built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 15, 100, 8192);
            var encoded = new ModEngine().EncodeAnimationAtlas(built.AtlasImage);
            Console.WriteLine($"frames={built.FrameCount}; frame={first.Width}x{first.Height}; atlas={built.AtlasWidth}x{built.AtlasHeight}; bc3={encoded.Data.Length}");
            return;
        }
        if (args.Length == 1 && args[0] == "--test-animation-quality-plan")
        {
            var shortAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(22, 16, 9, 8192);
            var mediumAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(60, 16, 9, 8192);
            var longAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(180, 16, 9, 8192);
            Console.WriteLine($"22frames={shortAnimation}; 60frames={mediumAnimation}; 180frames={longAnimation}");
            if (shortAnimation != 1920 || mediumAnimation != 1280 || longAnimation != 768) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-animation-chroma-key")
        {
            using var keyed = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512, 0, true).GetAwaiter().GetResult();
            using var plain = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512).GetAwaiter().GetResult();
            using var keyedFrame = keyed.LoadFrame(0);
            using var plainFrame = plain.LoadFrame(0);
            var keyedBackground = keyedFrame.GetPixel(8, 8);
            var keyedSubject = keyedFrame.GetPixel(keyedFrame.Width / 2, keyedFrame.Height / 2);
            var plainBackground = plainFrame.GetPixel(8, 8);
            keyedFrame.Save(args[2]);
            Console.WriteLine($"keyedBackground={keyedBackground}; keyedSubject={keyedSubject}; plainBackground={plainBackground}; saved={args[2]}");
            if (!keyed.GreenScreenRemoved || keyedBackground.A > 16 || keyedSubject.A < 240 || plainBackground.A < 240) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 5 && args[0] == "--test-animation-texture")
        {
            var engine = new ModEngine();
            var asset = engine.ScanAnimationAssetsFast(args[1], args[2]).First(x => x.Kind == MonsterAnimationAssetKind.Texture);
            using var atlas = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(args[3]);
            engine.ReplaceAnimationAtlas(asset, atlas, Path.Combine(args[2], "backup"));
            File.WriteAllBytes(args[4], engine.DecodePng(asset.AsTexture()));
            Console.WriteLine($"roundtrip={atlas.Width}x{atlas.Height}; {new FileInfo(args[1]).Length} bytes");
            return;
        }
        if (args.Length == 4 && args[0] == "--test-animation-apply")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            var service = new MonsterAnimationService();
            var template = service.ReadTemplate(args[1], set);
            using var media = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 128).GetAwaiter().GetResult();
            using var built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 12, 100, template, 4096);
            service.Apply(args[1], set, built);
            var engine = new ModEngine();
            var dimensions = set.Textures.Select(x =>
            {
                using var decoded = SixLabors.ImageSharp.Image.Load(engine.DecodePng(x.AsTexture()));
                return $"{decoded.Width}x{decoded.Height}";
            });
            var texts = set.Atlases.Concat(set.Skeletons).Select(x => engine.ReadTextAsset(x).Data.Length);
            Console.WriteLine($"complete={set.IsComplete}; textures={string.Join(',', dimensions)}; textBytes={string.Join(',', texts)}");
            return;
        }
        if (args.Length == 4 && args[0] == "--test-animation-build-profile")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            var template = new MonsterAnimationService().ReadTemplate(args[1], set);
            using var media = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 256).GetAwaiter().GetResult();
            using var built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 12, 100, template, 4096);
            using var built35 = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 12, 35, template, 4096);
            using var document = JsonDocument.Parse(built.SkeletonJson);
            var animationNames = document.RootElement.GetProperty("animations").EnumerateObject().Select(x => x.Name).ToArray();
            var timelineCounts = document.RootElement.GetProperty("animations").EnumerateObject().Select(animation => animation.Value.GetProperty("slots").EnumerateObject().First().Value.GetProperty("attachment").GetArrayLength()).ToArray();
            var timelinesValid = timelineCounts.All(x => x == media.FramePaths.Count + 1);
            Console.WriteLine($"display100={built.DisplayWidth:0.##}x{built.DisplayHeight:0.##}; display35={built35.DisplayWidth:0.##}x{built35.DisplayHeight:0.##}; template={string.Join(',', template.EffectiveAnimationNames)}; generated={string.Join(',', animationNames)}; timelines={string.Join(',', timelineCounts)}");
            if (Math.Abs(built.DisplayWidth - MonsterAnimationBuilder.GameCanvasWidth) > 0.1 || Math.Abs(built.DisplayHeight - MonsterAnimationBuilder.GameCanvasHeight) > 0.1 || Math.Abs(built35.DisplayWidth - MonsterAnimationBuilder.GameCanvasWidth * 0.35) > 0.1 || Math.Abs(built35.DisplayHeight - MonsterAnimationBuilder.GameCanvasHeight * 0.35) > 0.1 || !template.EffectiveAnimationNames.SequenceEqual(animationNames) || !timelinesValid) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-animation-restore")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            var restored = new MonsterAnimationService().Restore(args[1], set);
            Console.WriteLine($"restored={restored}");
            return;
        }
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
            var crop = new System.Drawing.RectangleF((preview.Width - cropWidth) / 2f, (preview.Height - cropHeight) / 2f, cropWidth, cropHeight);
            File.WriteAllBytes(args[2], ImageCropService.CropAndResize(args[1], crop, targetWidth, targetHeight));
            Console.WriteLine($"{targetWidth}x{targetHeight}; {new FileInfo(args[2]).Length} bytes; {args[2]}");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

}
