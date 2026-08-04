using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

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
        if (args.Length == 4 && args[0] == "--test-raw-animation-roundtrip")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            var service = new MonsterAnimationRawAssetService();
            var manifest = service.ExportAll(set, args[3]);
            var imported = service.ImportAll(args[1], set, args[3]);
            var profiles = set.Assets.Select(x => service.ResolveProfile(x).DisplayName).Distinct().ToArray();
            var extensions = manifest.Files.GroupBy(x => Path.GetExtension(x.FileName).ToLowerInvariant()).ToDictionary(x => x.Key, x => x.Count());
            Console.WriteLine($"complete={set.IsComplete}; exported={manifest.Files.Count}; imported={imported}; profiles={string.Join(" / ", profiles)}; files={string.Join(',', extensions.Select(x => $"{x.Key}:{x.Value}"))}");
            if (!set.IsComplete || manifest.Files.Count < 6 || imported != manifest.Files.Count || extensions.GetValueOrDefault(".png") < 2 || extensions.GetValueOrDefault(".atlas") < 2 || extensions.GetValueOrDefault(".json") < 2) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-raw-animation-form")
        {
            using var form = new MonsterAnimationRawAssetsForm(args[1], args[2]) { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var list = (ListView?)typeof(MonsterAnimationRawAssetsForm).GetField("_assets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var image = (PictureBox?)typeof(MonsterAnimationRawAssetsForm).GetField("_imagePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var text = (TextBox?)typeof(MonsterAnimationRawAssetsForm).GetField("_textPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && ((list?.Items.Count ?? 0) < 6 || image?.Image is null && string.IsNullOrWhiteSpace(text?.Text))) { Application.DoEvents(); Thread.Sleep(25); }
            var labels = list?.Items.Cast<ListViewItem>().Select(x => x.SubItems[0].Text).Distinct().ToArray() ?? [];
            var previewLoaded = image?.Image is not null || !string.IsNullOrWhiteSpace(text?.Text);
            Console.WriteLine($"items={list?.Items.Count}; preview={previewLoaded}; profiles={string.Join(" / ", labels)}");
            var result = (list?.Items.Count ?? 0) >= 6 && previewLoaded && labels.Any(x => x.Contains("SD", StringComparison.OrdinalIgnoreCase)) && labels.Any(x => x.Contains("HighEnd_HD", StringComparison.OrdinalIgnoreCase));
            form.Close();
            if (!result) Environment.ExitCode = 2;
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
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && preview?.Frame is null && label?.Text.Contains("预览失败", StringComparison.Ordinal) != true)
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
            var result = currentSizeLoaded && realtimeScale && preview?.Frame is not null;
            Console.WriteLine($"status={label?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame is not null}; initialScale={initialScale}; realtimeScale={preview?.ScalePercent}");
            form.Close();
            if (!result) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 4 && args[0] == "--test-animation-form-copy-card")
        {
            using var form = new MonsterAnimationForm(args[1], args[2]) { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var preview = (AnimationPreviewCanvas?)typeof(MonsterAnimationForm).GetField("_preview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var source = (Label?)typeof(MonsterAnimationForm).GetField("_sourceStatus", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var sourceCard = (TextBox?)typeof(MonsterAnimationForm).GetField("_sourceCardId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var frameEdge = (ComboBox?)typeof(MonsterAnimationForm).GetField("_frameEdge", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var apply = (Button?)typeof(MonsterAnimationForm).GetField("_apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && preview?.Frame is null) { Application.DoEvents(); Thread.Sleep(25); }
            if (sourceCard is not null) sourceCard.Text = args[3];
            if (frameEdge is not null) frameEdge.SelectedItem = "512";
            var method = typeof(MonsterAnimationForm).GetMethod("LoadOtherCardAnimationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? throw new MissingMethodException("LoadOtherCardAnimationAsync");
            var task = (Task?)method.Invoke(form, null) ?? throw new InvalidOperationException("跨卡动画加载任务没有启动。");
            deadline = DateTime.UtcNow.AddSeconds(120);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(25); }
            task.GetAwaiter().GetResult();
            var result = task.IsCompletedSuccessfully && preview?.Frame is not null && apply?.Enabled == true &&
                         source?.Text.Contains($"源卡 {args[3]}", StringComparison.Ordinal) == true;
            Console.WriteLine($"source={source?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame is not null}; applyEnabled={apply?.Enabled}");
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
            var apply = (Button?)typeof(MonsterAnimationForm).GetField("_apply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var result = task.IsCompletedSuccessfully && preview?.Frame is not null && scale?.Value == 100 && preview.ScalePercent == 100 && apply?.Enabled == true
                && (args[0] != "--test-animation-form-chroma" || source?.Text.Contains("绿幕已透明", StringComparison.Ordinal) == true);
            Console.WriteLine($"media={source?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame is not null}; fullCanvasScale={scale?.Value}; applyEnabled={apply?.Enabled}");
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
        if (args.Length == 3 && args[0] == "--render-animation-ui")
        {
            ApplicationConfiguration.Initialize();
            using var form = new MonsterAnimationForm(args[1]) { ShowInTaskbar = false };
            form.Size = new System.Drawing.Size(940, 680);
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(args[2]);
            form.Close();
            Console.WriteLine(args[2]);
            return;
        }
        if (args.Length == 2 && args[0] == "--render-main-ui")
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm { ShowInTaskbar = false };
            form.Size = new System.Drawing.Size(1120, 720);
            form.Show();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                var list = (ListView?)typeof(MainForm).GetField("_list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
                if ((list?.Items.Count ?? 0) > 0) break;
                Thread.Sleep(25);
            }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(args[1]);
            form.Close();
            Console.WriteLine(args[1]);
            return;
        }
        if (args.Length == 4 && args[0] == "--render-crop-ui")
        {
            ApplicationConfiguration.Initialize();
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? throw new InvalidDataException("本地索引无法读取。");
            IndexService.RefreshCardFramesIfChanged(args[1], index);
            var frames = index.Textures.Where(x => x.SourceKind == "卡框资源").ToArray();
            using var form = new ImageCropForm(args[2], 512, 512, "界面缩放检查", frames, "card_frame01") { ShowInTaskbar = false };
            form.Size = new System.Drawing.Size(820, 620);
            form.Show();
            var mapping = (Label?)typeof(ImageCropForm).GetField("_mapping", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && mapping?.Text.StartsWith("正在", StringComparison.Ordinal) == true) { Application.DoEvents(); Thread.Sleep(25); }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size));
            bitmap.Save(args[3]);
            form.Close();
            Console.WriteLine(args[3]);
            return;
        }
        if (args.Length == 4 && args[0] == "--test-borrow-animation")
        {
            var service = new MonsterAnimationBorrowService();
            BorrowedMonsterAnimation? record = null;
            try
            {
                record = service.Install(args[1], args[2], args[3]);
                var set = MonsterAnimationIndexService.Find(args[1], args[2]);
                using var preview = MonsterAnimationCurrentPreview.TryLoad(args[1], set, 256, 15, 30);
                var registered = service.RegisteredCardIds(args[1]).Contains(int.Parse(args[2]));
                Console.WriteLine($"installed={record.CreatedBundlePaths.Count}; complete={set.IsComplete}; preview={preview is not null}; registered={registered}; donor={record.DonorCardId}");
                if (!set.IsComplete || preview is null || !registered) Environment.ExitCode = 2;
            }
            finally
            {
                if (record is not null)
                {
                    var removed = service.Remove(args[1], args[2]);
                    var stillRegistered = service.RegisteredCardIds(args[1]).Contains(int.Parse(args[2]));
                    var remaining = record.CreatedBundlePaths.Count(x => File.Exists(Path.Combine(IndexService.FindLocalRoot(args[1])!, x)));
                    Console.WriteLine($"removed={removed}; stillRegistered={stillRegistered}; remaining={remaining}");
                    if (!removed || stillRegistered || remaining != 0) Environment.ExitCode = 2;
                }
            }
            return;
        }
        if (args.Length == 4 && args[0] == "--test-independent-animation")
        {
            var gameRoot = args[1];
            var targetCardId = args[2];
            var donorCardId = args[3];
            var borrow = new MonsterAnimationBorrowService();
            var animation = new MonsterAnimationService();
            BorrowedMonsterAnimation? record = null;
            var temporary = Path.Combine(Path.GetTempPath(), "MDCardModTool", "independent-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            var donorSet = MonsterAnimationIndexService.Find(gameRoot, donorCardId);
            var donorHashes = donorSet.Assets.Select(x => x.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x, x => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x))), StringComparer.OrdinalIgnoreCase);
            try
            {
                record = borrow.InstallIndependent(gameRoot, targetCardId, donorCardId);
                var set = MonsterAnimationIndexService.Find(gameRoot, targetCardId);
                using var previewBefore = MonsterAnimationCurrentPreview.TryLoad(gameRoot, set, 256, 15, 30);
                var frames = new List<string>();
                foreach (var (name, color) in new[] { ("frame-01.png", SixLabors.ImageSharp.Color.CornflowerBlue), ("frame-02.png", SixLabors.ImageSharp.Color.OrangeRed) })
                {
                    var path = Path.Combine(temporary, name);
                    using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(320, 180);
                    image.Mutate(x => x.BackgroundColor(color));
                    image.SaveAsPng(path);
                    frames.Add(path);
                }
                var template = animation.ReadTemplate(gameRoot, set);
                using (var built = MonsterAnimationBuilder.Build(frames, targetCardId, 15, 100, template, 4096))
                    animation.Apply(gameRoot, set, built);
                var written = MonsterAnimationIndexService.Find(gameRoot, targetCardId);
                using var previewAfter = MonsterAnimationCurrentPreview.TryLoad(gameRoot, written, 256, 15, 30);
                var donorUnchanged = donorHashes.All(x => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x.Key))) == x.Value);
                Console.WriteLine($"created={record.CreatedBundlePaths.Count}; independent={record.IsIndependent}; complete={written.IsComplete}; previewBefore={previewBefore is not null}; previewAfter={previewAfter is not null}; donorUnchanged={donorUnchanged}");
                if (!record.IsIndependent || !written.IsComplete || previewBefore is null || previewAfter is null || !donorUnchanged) Environment.ExitCode = 2;
            }
            finally
            {
                if (record is not null)
                {
                    var removed = borrow.Remove(gameRoot, targetCardId);
                    var stillRegistered = borrow.RegisteredCardIds(gameRoot).Contains(int.Parse(targetCardId));
                    var localRoot = IndexService.FindLocalRoot(gameRoot)!;
                    var remaining = record.CreatedBundlePaths.Count(x => File.Exists(Path.Combine(localRoot, x)));
                    Console.WriteLine($"removed={removed}; stillRegistered={stillRegistered}; remaining={remaining}");
                    if (!removed || stillRegistered || remaining != 0) Environment.ExitCode = 2;
                }
                try { Directory.Delete(temporary, true); } catch { }
            }
            return;
        }
        if (args.Length == 3 && args[0] == "--test-animation-resolution")
        {
            var set = MonsterAnimationIndexService.Find(args[1], args[2]);
            var template = new MonsterAnimationService().ReadTemplate(args[1], set);
            var page = MonsterAnimationService.ReplaceAtlasPageName($"P{args[2]}.png\nsize: 1,1\n", set.Textures[0].Name + ".png")
                .Split('\n')[0].TrimEnd('\r');
            var uniqueBundles = set.Assets.Select(x => x.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Console.WriteLine($"complete={set.IsComplete}; assets={set.Assets.Count}; bundles={uniqueBundles}; page={page}; animations={string.Join('/', template.EffectiveAnimationNames)}");
            if (!set.IsComplete || set.Assets.Count != 6 || uniqueBundles != 6 || page != set.Textures[0].Name + ".png") Environment.ExitCode = 2;
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
            using var preview = MonsterAnimationCurrentPreview.TryLoad(args[1], set) ??
                throw new InvalidDataException("当前 Spine 动画无法合成预览：" + MonsterAnimationSpineRenderer.LastDiagnostic);
            Directory.CreateDirectory(args[3]);
            preview.Frames[0].Save(Path.Combine(args[3], "frame-0001.png"));
            preview.Frames[preview.Frames.Count / 2].Save(Path.Combine(args[3], "frame-middle.png"));
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
        if (args.Length == 2 && args[0] == "--bundle-dependencies")
        {
            foreach (var path in new ModEngine().ReadBundleDependencies(Path.GetFullPath(args[1]))) Console.WriteLine(path);
            return;
        }
        if (args.Length == 4 && args[0] == "--inspect-animation-dependency")
        {
            foreach (var asset in new ModEngine().ScanAnimationDependencyAssets(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), args[3]))
                Console.WriteLine($"{asset.Kind}; {asset.Name}; PathID={asset.PathId}; {asset.RelativeBundlePath}");
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
        if (args.Length == 4 && args[0] == "--test-animation-apply-copy")
        {
            var sourceSet = MonsterAnimationIndexService.Find(args[1], args[2]);
            var service = new MonsterAnimationService();
            var template = service.ReadTemplate(args[1], sourceSet);
            var root = Path.GetFullPath(args[3]);
            Directory.CreateDirectory(root);
            var copiedAssets = new List<MonsterAnimationAssetRef>();
            foreach (var asset in sourceSet.Assets)
            {
                var destination = Path.Combine(root, asset.RelativeBundlePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(asset.BundlePath, destination, true);
                copiedAssets.Add(new MonsterAnimationAssetRef
                {
                    BundlePath = destination,
                    RelativeBundlePath = asset.RelativeBundlePath,
                    AssetFileName = asset.AssetFileName,
                    PathId = asset.PathId,
                    Name = asset.Name,
                    CardId = asset.CardId,
                    Kind = asset.Kind,
                    StorageKind = asset.StorageKind,
                    ProfileTier = asset.ProfileTier,
                    ProfileRegion = asset.ProfileRegion,
                    ProfileScale = asset.ProfileScale
                });
            }
            var copySet = new MonsterAnimationSet { CardId = sourceSet.CardId, Assets = copiedAssets };
            var framePath = Path.Combine(root, "test-frame.png");
            using (var frame = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 64, SixLabors.ImageSharp.Color.Cyan)) frame.SaveAsPng(framePath);
            using var built = MonsterAnimationBuilder.Build([framePath, framePath], args[2], 12, 100, template, 2048);
            service.Apply(root, copySet, built);
            var engine = new ModEngine();
            var pages = copySet.Atlases.Select(x => System.Text.Encoding.UTF8.GetString(engine.ReadTextAsset(x).Data).TrimEnd('\0').Split('\n')[0].TrimEnd('\r')).ToArray();
            var dimensions = copySet.Textures.Select(x =>
            {
                using var decoded = SixLabors.ImageSharp.Image.Load(engine.DecodePng(x.AsTexture()));
                return $"{decoded.Width}x{decoded.Height}";
            }).ToArray();
            Console.WriteLine($"complete={copySet.IsComplete}; pages={string.Join(',', pages)}; textures={string.Join(',', dimensions)}");
            if (!copySet.IsComplete || pages.Any(x => x != copySet.Textures[0].Name + ".png") || dimensions.Any(x => x != $"{built.AtlasWidth}x{built.AtlasHeight}")) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 5 && args[0] == "--test-animation-cross-card-copy")
        {
            var targetSet = MonsterAnimationIndexService.Find(args[1], args[2]);
            var sourceSet = MonsterAnimationIndexService.Find(args[1], args[3]);
            using var rendered = MonsterAnimationCurrentPreview.TryLoad(args[1], sourceSet, 256, 12, 60) ??
                throw new InvalidDataException("源卡 Spine 无法渲染：" + MonsterAnimationSpineRenderer.LastDiagnostic);
            using var media = ExtractedAnimation.CreateFromFrames($"源卡 {args[3]}", rendered.Frames, rendered.FramesPerSecond);
            var service = new MonsterAnimationService();
            var template = service.ReadTemplate(args[1], targetSet);
            var root = Path.GetFullPath(args[4]);
            Directory.CreateDirectory(root);
            var copiedAssets = new List<MonsterAnimationAssetRef>();
            foreach (var asset in targetSet.Assets)
            {
                var destination = Path.Combine(root, asset.RelativeBundlePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(asset.BundlePath, destination, true);
                copiedAssets.Add(new MonsterAnimationAssetRef
                {
                    BundlePath = destination,
                    RelativeBundlePath = asset.RelativeBundlePath,
                    AssetFileName = asset.AssetFileName,
                    PathId = asset.PathId,
                    Name = asset.Name,
                    CardId = asset.CardId,
                    Kind = asset.Kind,
                    StorageKind = asset.StorageKind,
                    ProfileTier = asset.ProfileTier,
                    ProfileRegion = asset.ProfileRegion,
                    ProfileScale = asset.ProfileScale
                });
            }
            var copySet = new MonsterAnimationSet { CardId = targetSet.CardId, Assets = copiedAssets };
            using var built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], rendered.FramesPerSecond, 100, template, 4096);
            service.Apply(root, copySet, built);
            var engine = new ModEngine();
            var dimensions = copySet.Textures.Select(x =>
            {
                using var decoded = SixLabors.ImageSharp.Image.Load(engine.DecodePng(x.AsTexture()));
                return $"{decoded.Width}x{decoded.Height}";
            }).ToArray();
            var generatedAnimations = copySet.Skeletons.Select(x =>
            {
                using var document = JsonDocument.Parse(engine.ReadTextAsset(x).Data);
                return string.Join('/', document.RootElement.GetProperty("animations").EnumerateObject().Select(a => a.Name));
            }).ToArray();
            Console.WriteLine($"source={args[3]}; target={args[2]}; frames={rendered.Frames.Count}; textures={string.Join(',', dimensions)}; animations={string.Join(',', generatedAnimations)}");
            if (!copySet.IsComplete || dimensions.Any(x => x != $"{built.AtlasWidth}x{built.AtlasHeight}") ||
                generatedAnimations.Any(x => x != string.Join('/', template.EffectiveAnimationNames))) Environment.ExitCode = 2;
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
        if (args.Length == 2 && args[0] == "--inspect-mod-state")
        {
            var local = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(args[1]));
            var index = File.Exists(cache) ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex() : new GameIndex();
            var service = new ModPackageService();
            service.RefreshFlags(args[1], index.Textures);
            var summary = service.GetChangeSummary(args[1], index.Textures);
            Console.WriteLine($"bundles={summary.BundleCount}; animation={summary.AnimationBundleCount}; overframeGate={summary.OverFrameGateBundleCount}; imageResources={index.Textures.Count(x => x.IsModded)}; cardFramesMarked={index.Textures.Count(x => x.IsModded && x.SourceKind == "卡框资源")}");
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
        if (args.Length == 2 && args[0] == "--inspect-text-assets")
        {
            foreach (var asset in new ModEngine().ReadTextAssets(Path.GetFullPath(args[1])))
                Console.WriteLine($"{asset.Name}; bytes={asset.Data.Length}; PathID={asset.PathId}; file={asset.AssetFileName}; path={asset.PathName}");
            return;
        }
        if (args.Length == 2 && args[0] == "--inspect-overframe")
        {
            var service = new OverFrameService();
            var gate = service.FindGate(args[1], (done, total) => Console.WriteLine($"scan={done}/{total}"));
            var mappings = service.Read(args[1]);
            Console.WriteLine($"gate={gate.RelativeBundlePath}; bytes={gate.Data.Length}; PathID={gate.PathId}; file={gate.AssetFileName}; mappings={mappings.Count}");
            foreach (var item in mappings)
                Console.WriteLine($"{item.CardId}->{item.ArtId}");
            return;
        }
        if (args.Length == 3 && args[0] == "--inspect-overframe-file")
        {
            var bundle = Path.GetFullPath(args[1]);
            var root = Path.GetFullPath(args[2]);
            var gate = new ModEngine().FindTextAssetFast(bundle, root, OverFrameService.GateName)
                ?? throw new FileNotFoundException($"Bundle 中没有 {OverFrameService.GateName}。", bundle);
            var mappings = OverFrameService.Read(gate);
            Console.WriteLine($"gate={gate.RelativeBundlePath}; bytes={gate.Data.Length}; PathID={gate.PathId}; file={gate.AssetFileName}; mappings={mappings.Count}");
            foreach (var item in mappings) Console.WriteLine($"{item.CardId}->{item.ArtId}");
            return;
        }
        if (args.Length == 4 && args[0] == "--scan-text-asset")
        {
            var root = Path.GetFullPath(args[1]);
            var name = args[2];
            if (!long.TryParse(args[3], out var maxBytes) || maxBytes <= 0) throw new ArgumentException("最大文件字节数无效。");
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(x =>
                {
                    try { return new FileInfo(x).Length <= maxBytes; }
                    catch { return false; }
                })
                .ToArray();
            var found = new System.Collections.Concurrent.ConcurrentBag<TextAssetRef>();
            var done = 0;
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8)) }, file =>
            {
                try
                {
                    var asset = new ModEngine().FindTextAssetFast(file, root, name);
                    if (asset is not null) found.Add(asset);
                }
                catch { }
                var current = Interlocked.Increment(ref done);
                if (current % 500 == 0 || current == files.Length) Console.WriteLine($"scan={current}/{files.Length}");
            });
            foreach (var asset in found.OrderBy(x => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"FOUND gate={asset.RelativeBundlePath}; bytes={asset.Data.Length}; PathID={asset.PathId}; file={asset.AssetFileName}");
            Console.WriteLine($"matches={found.Count}");
            return;
        }
        if (args.Length == 2 && args[0] == "--repair-overframe")
        {
            var service = new OverFrameService();
            var result = service.ReapplySavedCards(args[1]);
            Console.WriteLine($"gate={result.GateLocation}; saved={result.SavedCardCount}; changed={result.ChangedMappingCount}; total={result.TotalMappingCount}");
            return;
        }
        if (args.Length == 2 && args[0] == "--test-overframe-form")
        {
            ApplicationConfiguration.Initialize();
            using var form = new OverFrameForm(args[1], "22747") { Opacity = 0, ShowInTaskbar = false };
            form.Show();
            var list = (ListView?)typeof(OverFrameForm).GetField("_mappings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var status = (Label?)typeof(OverFrameForm).GetField("_status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && (list?.Items.Count ?? 0) == 0 && status?.Text.Contains("失败", StringComparison.Ordinal) != true)
            {
                Application.DoEvents();
                Thread.Sleep(25);
            }
            var hasTarget = list?.Items.Cast<ListViewItem>().Any(x => x.Text == "22747" && x.SubItems.Count > 1 && x.SubItems[1].Text == "22747") == true;
            var locationOk = status?.Text.Contains(Path.Combine("22", "22817d01"), StringComparison.OrdinalIgnoreCase) == true;
            Console.WriteLine($"items={list?.Items.Count}; target22747={hasTarget}; localDataLocation={locationOk}; status={status?.Text}");
            form.Close();
            if (!hasTarget || !locationOk) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 2 && args[0] == "--test-overframe-repair")
        {
            var gameRoot = Path.GetFullPath(args[1]);
            var service = new OverFrameService();
            var beforeGate = service.FindGate(gameRoot);
            var beforeHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(beforeGate.BundlePath)));
            var before = service.Read(gameRoot);
            var saved = OverFrameArtStore.SavedCardIds(gameRoot);
            var repaired = service.ReapplySavedCards(gameRoot);
            var after = service.Read(gameRoot);
            var active = after.Where(x => x.UsesOwnArt).Select(x => x.CardId).ToHashSet();
            var allRestored = saved.All(active.Contains);
            var backupFound = service.HasBackup(gameRoot);
            service.RestoreBackup(gameRoot);
            var restoredGate = service.FindGate(gameRoot);
            var restoredHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(restoredGate.BundlePath)));
            Console.WriteLine($"gate={beforeGate.RelativeBundlePath}; before={before.Count}; saved={saved.Count}; changed={repaired.ChangedMappingCount}; after={after.Count}; allRestored={allRestored}; backup={backupFound}; restoredHash={restoredHash == beforeHash}");
            if (!allRestored || !backupFound || restoredHash != beforeHash) Environment.ExitCode = 2;
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
        if (args.Length == 2 && args[0] == "--test-classification-overrides")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out _)) throw new FileNotFoundException("缺少卡图预绑定索引。");
            index.AlternateArtIndexVersion = 3;
            YgoCdbCardCatalog.ClassifyAlternateArtsAsync(index).GetAwaiter().GetResult();
            var normalIds = new[] { "30000", "30064" };
            var alternateIds = new[] { "3401", "3899", "19736", "20040" };
            var normalOk = normalIds.All(id => index.Textures.Any(x => x.CardKey == id && x.Category == "卡图缩略图" && !x.IsAlternateArt && !x.IsTokenOrMisc));
            var alternateOk = alternateIds.All(id => index.Textures.Any(x => x.CardKey == id && x.Category == "异画卡图" && x.IsAlternateArt && !x.IsTokenOrMisc));
            var forcedNormal = index.Textures.Count(x => int.TryParse(x.CardKey, out var id) && id is >= 30000 and <= 30064 && x.Category == "卡图缩略图");
            var forcedAlternate = index.Textures.Count(x => int.TryParse(x.CardKey, out var id) && (id is >= 3401 and <= 3899 or 19736 or 20040) && x.Category == "异画卡图");
            Console.WriteLine($"version={index.AlternateArtIndexVersion}; normalOk={normalOk}; alternateOk={alternateOk}; forcedNormal={forcedNormal}; forcedAlternate={forcedAlternate}");
            if (index.AlternateArtIndexVersion != YgoCdbCardCatalog.ClassificationVersion || !normalOk || !alternateOk || forcedNormal < 2 || forcedAlternate < 4) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-index-repair")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var complete, out _)) throw new FileNotFoundException("缺少卡图预绑定索引。");
            var expected = complete.Textures.FirstOrDefault(x => x.CardKey == args[2]) ?? throw new InvalidDataException($"预绑定索引不含卡号 {args[2]}。");
            var incomplete = new GameIndex
            {
                AlternateArtIndexVersion = complete.AlternateArtIndexVersion,
                Textures = complete.Textures.Where(x => x.CardKey != args[2]).ToList()
            };
            incomplete.Textures.Add(new TexRef
            {
                BundlePath = expected.BundlePath,
                RelativeBundlePath = "diagnostic/retained-extra",
                PathId = long.MinValue,
                AssetFileName = expected.AssetFileName,
                Name = "diagnostic-extra",
                Width = 1,
                Height = 1,
                Category = "诊断",
                SourceKind = expected.SourceKind,
                CardKey = "999999"
            });
            if (!PortableIndexService.TryRepairFromBundled(args[1], incomplete, out var repaired, out var buildId, out var retainedExtras))
                throw new InvalidDataException("预绑定索引修复没有执行。");
            var restored = repaired.Textures.Any(x => x.CardKey == args[2]);
            var extraRetained = repaired.Textures.Any(x => x.PathId == long.MinValue);
            Console.WriteLine($"build={buildId}; before={incomplete.Textures.Count}; after={repaired.Textures.Count}; restored={restored}; retainedExtras={retainedExtras}; extraRetained={extraRetained}");
            if (!restored || !extraRetained || retainedExtras != 1) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-texture-reference-repair")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out _)) throw new FileNotFoundException("缺少卡图预绑定索引。");
            var texture = index.Textures.FirstOrDefault(x => x.SourceKind == "本地卡图" && x.CardKey == args[2])
                ?? throw new InvalidDataException($"预绑定索引不含卡号 {args[2]}。");
            var expectedPathId = texture.PathId;
            texture.PathId = long.MinValue;
            texture.AssetFileName = "stale-manual-mod-mapping";
            var engine = new ModEngine();
            var resolved = engine.ResolveTextureReference(texture) ?? throw new InvalidDataException("未能从当前 Bundle 重新定位 Texture2D。");
            texture.PathId = resolved.PathId;
            texture.AssetFileName = resolved.AssetFileName;
            var png = engine.DecodePng(texture, 512);
            Console.WriteLine($"card={args[2]}; expectedPathId={expectedPathId}; resolvedPathId={resolved.PathId}; assetFile={resolved.AssetFileName}; pngBytes={png.Length}");
            if (resolved.PathId != expectedPathId || png.Length < 100) Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 3 && args[0] == "--test-card-frame-recovery")
        {
            var gameRoot = args[1];
            var local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
            var cache = IndexService.CachePath(local, IndexService.StreamingRoot(gameRoot));
            var index = File.Exists(cache)
                ? JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex()
                : throw new FileNotFoundException("缺少本地索引。", cache);
            // Simulate a cache produced by an older frame locator even when Steam preserved
            // data.unity3d's timestamp. The locator version must still force a one-bundle refresh.
            index.CardFrameDataStamp = IndexService.CardFrameDataStamp(gameRoot);
            index.CardFrameIndexVersion = 0;
            var refreshed = IndexService.RefreshCardFramesIfChanged(gameRoot, index);
            var frame = index.Textures.First(x => x.SourceKind == "卡框资源" && x.Name == "card_frame01" && x.Width == 704 && x.Height == 1024);
            var card = index.Textures.First(x => x.SourceKind == "本地卡图" && x.CardKey == args[2]);
            var engine = new ModEngine();
            var dataBundle = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
            var wrong = engine.ScanBundle(dataBundle, gameRoot, "卡框资源", includeDependencies: false).Textures
                .First(x => x.Width == 128 && x.Height == 128);
            frame.PathId = wrong.PathId;
            frame.AssetFileName = wrong.AssetFileName;
            var wrongPng = engine.DecodePng(frame);
            var wrongInfo = SixLabors.ImageSharp.Image.Identify(wrongPng) ?? throw new InvalidDataException("串位贴图无法识别。");
            var recoveredFrame = CardFrameResource.DecodeVerified(engine, frame);
            var recoveredInfo = SixLabors.ImageSharp.Image.Identify(recoveredFrame) ?? throw new InvalidDataException("恢复后的卡框无法识别。");
            var cardPng = engine.DecodePng(card);
            var composed = CardFrameRenderer.ComposeStoredArtPreview(cardPng, recoveredFrame);
            var composedInfo = SixLabors.ImageSharp.Image.Identify(composed) ?? throw new InvalidDataException("合成预览无法识别。");
            Console.WriteLine($"indexRefreshed={refreshed}; wrong={wrong.Name}:{wrongInfo.Width}x{wrongInfo.Height}; recovered={frame.Name}:PathID={frame.PathId}:{recoveredInfo.Width}x{recoveredInfo.Height}; card={args[2]}; preview={composedInfo.Width}x{composedInfo.Height}");
            if (!refreshed || wrongInfo.Width != 128 || recoveredInfo.Width != 704 || recoveredInfo.Height != 1024 || composedInfo.Width != 704 || composedInfo.Height != 1024)
                Environment.ExitCode = 2;
            return;
        }
        if (args.Length == 2 && args[0] == "--test-safe-large-crop")
        {
            var temporary = Path.Combine(Path.GetTempPath(), "MDCardModTool", "wide-source-" + Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
            try
            {
                using (var source = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(33_000, 100))
                {
                    source.Mutate(x => x.BackgroundColor(SixLabors.ImageSharp.Color.CornflowerBlue));
                    source.SaveAsPng(temporary);
                }
                using var preview = ImageCropService.LoadPreview(temporary);
                var output = ImageCropService.RenderToTarget(temporary, new ImageRenderSpec(512, 512, 1, 0, 0), 512, 512);
                File.WriteAllBytes(args[1], output);
                var result = SixLabors.ImageSharp.Image.Identify(output) ?? throw new InvalidDataException("安全裁剪输出无法识别。");
                Console.WriteLine($"source=33000x100; safePreview={preview.Width}x{preview.Height}; output={result.Width}x{result.Height}; bytes={output.Length}");
                if (preview.Width > 4096 || preview.Height > 4096 || result.Width != 512 || result.Height != 512) Environment.ExitCode = 2;
            }
            finally
            {
                try { File.Delete(temporary); } catch { }
            }
            return;
        }
        if (args.Length == 4 && args[0] == "--test-texture-write-repair")
        {
            if (!PortableIndexService.TryLoadBundled(args[1], out var index, out _)) throw new FileNotFoundException("缺少卡图预绑定索引。");
            var source = index.Textures.FirstOrDefault(x => x.SourceKind == "本地卡图" && x.CardKey == args[2])
                ?? throw new InvalidDataException($"预绑定索引不含卡号 {args[2]}。");
            Directory.CreateDirectory(args[3]);
            var copy = Path.Combine(args[3], "manual-mod.bundle");
            File.Copy(source.BundlePath, copy, true);
            var target = new TexRef
            {
                BundlePath = copy,
                RelativeBundlePath = "manual-mod.bundle",
                PathId = long.MinValue,
                AssetFileName = "stale-manual-mod-mapping",
                Name = source.Name,
                Width = source.Width,
                Height = source.Height,
                Category = source.Category,
                SourceKind = source.SourceKind,
                CardKey = source.CardKey
            };
            var engine = new ModEngine();
            var before = engine.DecodePng(source);
            byte[] overFrame;
            using (var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(before))
            {
                image.Mutate(x => x.Resize(FrameComposer.Width, FrameComposer.Height));
                using var stream = new MemoryStream();
                image.SaveAsPng(stream);
                overFrame = stream.ToArray();
            }
            engine.Replace(target, overFrame, Path.Combine(args[3], "backup"));
            var after = engine.DecodePng(target);
            var info = SixLabors.ImageSharp.Image.Identify(after) ?? throw new InvalidDataException("写回后的 PNG 无法识别。");
            Console.WriteLine($"card={args[2]}; resolvedPathId={target.PathId}; assetFile={target.AssetFileName}; result={info.Width}x{info.Height}; pngBytes={after.Length}");
            if (target.PathId == long.MinValue || target.AssetFileName == "stale-manual-mod-mapping" || info.Width != FrameComposer.Width || info.Height != FrameComposer.Height) Environment.ExitCode = 2;
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
