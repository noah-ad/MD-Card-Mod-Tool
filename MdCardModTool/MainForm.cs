using System.Text.Json;

namespace MdCardModTool;

public sealed class MainForm : Form
{
    const string DefaultGame = @"E:\steam\steamapps\common\Yu-Gi-Oh!  Master Duel";
    readonly ModEngine _engine = new();
    readonly OverFrameService _overFrames = new();
    readonly TextBox _gameFolder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    readonly TextBox _search = new() { PlaceholderText = "搜索卡号、贴图名、分类或 Bundle…", Dock = DockStyle.Fill };
    readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly TreeView _groups = new() { Dock = DockStyle.Left, Width = 190, BackColor = Color.FromArgb(18, 24, 38), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle, HideSelection = false };
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AllowDrop = true };
    readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 24, 38) };
    readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 82, Padding = new Padding(12, 6, 12, 6), ForeColor = Color.Gainsboro };
    readonly ToolStripStatusLabel _status = new() { Text = "选择游戏目录后扫描；首次扫描会建立缓存。" };
    readonly List<TexRef> _textures = [];
    string? _gameRoot;
    string? _assetRoot;
    string? _streamingRoot;

    public MainForm()
    {
        Text = "MD 卡图查看替换器"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(1100, 720); Size = new Size(1360, 850);
        BackColor = Color.FromArgb(12, 17, 28); ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 9F);
        _list.Columns.Add("名称", 220); _list.Columns.Add("分类", 110); _list.Columns.Add("尺寸", 110); _list.Columns.Add("Bundle", 310);
        _list.SelectedIndexChanged += async (_, _) => await ShowSelectionAsync(); _list.DoubleClick += async (_, _) => await ReplaceSelectedAsync();
        _list.ItemDrag += async (_, e) => await DragOutAsync(e.Item as ListViewItem); _list.DragEnter += OnDragEnter; _list.DragDrop += async (_, e) => await OnDragDropAsync(e);
        _search.TextChanged += (_, _) => RenderList();
        _category.Items.Add("全部"); _category.SelectedIndex = 0; _category.SelectedIndexChanged += (_, _) => RenderList();
        _groups.AfterSelect += (_, e) => { if (e.Node?.Tag is string key && _category.Items.Contains(key)) _category.SelectedItem = key; };
        var choose = Button("游戏目录", async (_, _) => await ChooseGameAsync()); var scan = Button("重建索引", async (_, _) => await ScanAsync());
        var replace = Button("替换所选", async (_, _) => await ReplaceSelectedAsync(), true); var export = Button("导出 PNG", async (_, _) => await ExportSelectedAsync());
        var backup = Button("打开备份", (_, _) => OpenBackup()); var restore = Button("还原所选", async (_, _) => await RestoreSelectedAsync()); var inspect = Button("检查 Bundle", async (_, _) => await InspectSelectedAsync());
        var overFrameReplace = Button("超框替换", async (_, _) => await OverFrameReplaceAsync(), true); var frameEditor = Button("卡框选择／编辑", async (_, _) => await OpenFrameEditorAsync()); var framePreview = Button("卡框预览", (_, _) => OpenFramePreview()); var overFrameTable = Button("超框表", (_, _) => OpenOverFrameTable());
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 90, Padding = new Padding(14, 12, 14, 8), ColumnCount = 7, RowCount = 2, BackColor = Color.FromArgb(20, 28, 45) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); for (var i = 0; i < 5; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Master Duel", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(154, 191, 255) }, 0, 0); top.Controls.Add(_gameFolder, 1, 0); top.SetColumnSpan(_gameFolder, 4); top.Controls.Add(choose, 5, 0); top.Controls.Add(scan, 6, 0);
        top.Controls.Add(_search, 0, 1); top.SetColumnSpan(_search, 2); top.Controls.Add(_category, 2, 1); top.Controls.Add(replace, 3, 1); top.Controls.Add(export, 4, 1); top.Controls.Add(restore, 5, 1); top.Controls.Add(backup, 6, 1);
        var left = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 22, 35) }; left.Controls.Add(_list); left.Controls.Add(_groups); left.Controls.Add(new Label { Text = "卡图缩略图与游戏内图片  ·  拖图片进列表替换；把条目拖到资源管理器导出", Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 8, 0, 0), ForeColor = Color.FromArgb(154, 191, 255), Font = new Font(Font, FontStyle.Bold) });
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(15, 22, 35) }; var rightActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.LeftToRight }; rightActions.Controls.Add(overFrameReplace); rightActions.Controls.Add(frameEditor); rightActions.Controls.Add(framePreview); rightActions.Controls.Add(overFrameTable); rightActions.Controls.Add(inspect); right.Controls.Add(_preview); right.Controls.Add(_info); right.Controls.Add(rightActions);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 780, BackColor = Color.FromArgb(45, 59, 83) }; split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
        var status = new StatusStrip { BackColor = Color.FromArgb(20, 28, 45), ForeColor = Color.Gainsboro }; status.Items.Add(_status);
        Controls.Add(split); Controls.Add(top); Controls.Add(status);
        if (Directory.Exists(DefaultGame)) { _gameRoot = DefaultGame; _gameFolder.Text = DefaultGame; SetGameRoot(); }
        Shown += async (_, _) => { if (_assetRoot is not null) await ScanAsync(); };
    }

    static Button Button(string text, EventHandler click, bool accent = false) { var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(5, 2, 0, 2), FlatStyle = FlatStyle.Flat, BackColor = accent ? Color.FromArgb(46, 102, 204) : Color.FromArgb(36, 50, 76), ForeColor = Color.White, Cursor = Cursors.Hand }; b.FlatAppearance.BorderColor = accent ? Color.FromArgb(93, 148, 255) : Color.FromArgb(71, 91, 128); b.Click += click; return b; }
    async Task ChooseGameAsync() { using var d = new FolderBrowserDialog { Description = "选择 Yu-Gi-Oh! Master Duel 游戏根目录", InitialDirectory = Directory.Exists(DefaultGame) ? DefaultGame : "" }; if (d.ShowDialog(this) == DialogResult.OK) { _gameRoot = d.SelectedPath; _gameFolder.Text = d.SelectedPath; SetGameRoot(); await ScanAsync(); } }
    void SetGameRoot()
    {
        if (_gameRoot is null) return;
        var localData = Path.Combine(_gameRoot, "LocalData");
        var candidates = Directory.Exists(localData)
            ? Directory.GetDirectories(localData).Select(x => Path.Combine(x, "0000")).Where(Directory.Exists).ToArray()
            : [];
        _assetRoot = candidates.OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
        _streamingRoot = Path.Combine(_gameRoot, "masterduel_Data", "StreamingAssets", "AssetBundle");
        if (_assetRoot is not null) _status.Text = "资源目录：" + _assetRoot + "（替换直接写入游戏本体，自动备份）";
    }
    string CachePath() => IndexService.CachePath(_assetRoot!, _streamingRoot!);

    async Task ScanAsync()
    {
        if (_assetRoot is null || !Directory.Exists(_assetRoot)) { MessageBox.Show(this, "未找到 LocalData\\<用户哈希>\\0000。请选择 Master Duel 游戏根目录。", Text); return; }
        UseWaitCursor = true; _textures.Clear(); _preview.Image = null;
        try
        {
            var cache = CachePath();
            GameIndex? cached = null;
            if (File.Exists(cache))
            {
                try { cached = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache)); }
                catch { File.Delete(cache); }
            }
            if (cached is not null)
            {
                // 480×700 是另一套界面用小卡框，不属于游戏超框预览。
                cached.Textures.RemoveAll(x => x.SourceKind == "卡框资源" && (x.Width != 704 || x.Height != 1024));
                _textures.AddRange(cached.Textures); _status.Text = $"已载入本地索引：{_textures.Count} 张图片。";
            }
            else
            {
                _status.Text = "正在重建本地索引（仅此一次）…";
                await Task.Run(() => IndexService.BuildAndSave(_gameRoot!, (done, total, found) => BeginInvoke(() => _status.Text = $"正在重建索引：{done:N0}/{total:N0} Bundle，已索引 {found:N0} 张图片…")));
                var found = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache)) ?? new GameIndex();
                _textures.AddRange(found.Textures); _status.Text = $"索引已重建：{_textures.Count:N0} 张图片；以后启动直接读取本地索引。";
                cached = found;
            }
            if (cached is not null && cached.AlternateArtIndexVersion < YgoCdbCardCatalog.ClassificationVersion)
            {
                _status.Text = "正在建立本地异画卡名单（仅首次，需要下载一次百鸽卡片库）…";
                try
                {
                    await YgoCdbCardCatalog.ClassifyAlternateArtsAsync(cached);
                    _textures.Clear(); _textures.AddRange(cached.Textures);
                    await Task.Run(() => IndexService.Save(_gameRoot!, cached));
                    _status.Text = $"异画卡与 Token／杂图已分类并保存到本地：异画 {_textures.Count(x => x.IsAlternateArt):N0} 张，Token／杂图 {_textures.Count(x => x.IsTokenOrMisc):N0} 张。";
                }
                catch (Exception ex)
                {
                    _status.Text = "异画卡名单下载失败：" + ex.Message + "；本次仍可正常使用，下次会自动重试。";
                }
            }
            await ApplyOverFrameTagsAsync(); RefreshCategories(); RenderList();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    void RefreshCategories()
    {
        var old = _category.Text; _category.Items.Clear(); _category.Items.Add("全部"); _groups.Nodes.Clear();
        foreach (var source in _textures.GroupBy(x => x.SourceKind).OrderBy(x => x.Key))
        {
            var sourceNode = _groups.Nodes.Add($"{source.Key}（{source.Count()}）");
            foreach (var group in source.GroupBy(x => x.Category).OrderBy(x => x.Key))
            {
                var key = $"{source.Key}|{group.Key}"; _category.Items.Add(key);
                var node = sourceNode.Nodes.Add($"{group.Key}（{group.Count()}）"); node.Tag = key;
            }
            sourceNode.Expand();
        }
        _category.SelectedItem = _category.Items.Contains(old) ? old : "全部";
    }
    void RenderList()
    {
        var q = _search.Text.Trim(); var filter = _category.Text; _list.BeginUpdate(); _list.Items.Clear();
        foreach (var x in _textures.Where(x => (filter == "全部" || filter == $"{x.SourceKind}|{x.Category}") && (q.Length == 0 || $"{x.Name} {x.SourceKind} {x.Category} {x.RelativeBundlePath}".Contains(q, StringComparison.OrdinalIgnoreCase)))) _list.Items.Add(new ListViewItem([x.Name, $"{x.SourceKind} / {x.Category}", $"{x.Width}×{x.Height}", x.RelativeBundlePath]) { Tag = x });
        _list.EndUpdate();
    }
    TexRef? Selected() => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as TexRef : null;
    async Task ShowSelectionAsync()
    {
        var x = Selected(); if (x is null) return;
        try
        {
            var data = await Task.Run(() => _engine.DecodePng(x));
            Bitmap display;
            if (x.Width == FrameComposer.Width && x.Height == FrameComposer.Height) display = FrameComposer.PreviewBitmap(data);
            else { using var s = new MemoryStream(data); using var im = Image.FromStream(s); display = new Bitmap(im); }
            _preview.Image?.Dispose(); _preview.Image = display;
            var frameLine = "";
            if (_gameRoot is not null && x.SourceKind == "本地卡图" && x.Width == FrameComposer.Width && x.Height == FrameComposer.Height && ushort.TryParse(x.CardKey, out var cardId))
            {
                if (OverFrameArtStore.HasSettings(_gameRoot, cardId))
                {
                    var settings = OverFrameArtStore.ReadSettings(_gameRoot, cardId);
                    frameLine = $"\n卡框：{(settings.UsesCustomFrame ? "自定义卡框" : settings.FrameKey)}  ·  可用右下角“卡框选择／编辑”更换";
                }
                else frameLine = "\n卡框：尚未单独合成  ·  点击右下角“卡框选择／编辑”修复白框或选择卡框";
            }
            _info.Text = $"{x.Name}  ·  {x.Category}\n{x.Width} × {x.Height}   PathID {x.PathId}\n{x.RelativeBundlePath}{frameLine}";
        }
        catch (Exception ex) { _status.Text = "预览失败：" + ex.Message; }
    }
    async Task ReplaceSelectedAsync(string? image = null)
    {
        var x = Selected(); if (x is null || _gameRoot is null) { MessageBox.Show(this, "先选择一张图片。", Text); return; }
        if (image is null) { using var d = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp" }; if (d.ShowDialog(this) != DialogResult.OK) return; image = d.FileName; }
        if (MessageBox.Show(this, "将直接修改游戏 Bundle。原始文件会备份到游戏目录的 _MD卡图备份 中。继续？", "确认替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { UseWaitCursor = true; x.OverrideBundlePath = null; await Task.Run(() => _engine.Replace(x, image, Path.Combine(_gameRoot, "_MD卡图备份", x.SourceKind))); await Task.Run(() => IndexService.Save(_gameRoot, _textures)); _status.Text = "替换完成：已直接写入游戏本体，原文件已备份。"; await ShowSelectionAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } finally { UseWaitCursor = false; }
    }
    async Task RestoreSelectedAsync()
    {
        var x = Selected(); if (x is null || _gameRoot is null) return;
        var backup = Path.Combine(_gameRoot, "_MD卡图备份", x.SourceKind, x.RelativeBundlePath);
        if (!File.Exists(backup)) { MessageBox.Show(this, "该 Bundle 尚未被本工具备份，无法还原。", Text); return; }
        ushort cardId = 0;
        var isOverFrame = x.Category == "超框卡图" && ushort.TryParse(x.CardKey, out cardId);
        var message = isOverFrame
            ? "确认还原该超框卡？会还原原始 512×512 卡图、关闭本卡超框登记，并重新归类到卡图列表。"
            : "确认把该 Bundle 还原为备份版本？";
        if (MessageBox.Show(this, message, "确认还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            await Task.Run(() =>
            {
                File.Copy(backup, x.BundlePath, true);
                if (isOverFrame) _overFrames.Disable(_gameRoot, cardId);
                var reloaded = _engine.ScanBundle(x.BundlePath, _assetRoot!, x.SourceKind, includeDependencies: false).Textures
                    .FirstOrDefault(t => t.PathId == x.PathId && t.AssetFileName == x.AssetFileName);
                if (reloaded is not null)
                {
                    x.Width = reloaded.Width; x.Height = reloaded.Height; x.Category = reloaded.Category;
                    if (x.Width == 512 && x.Height == 512) x.Category = x.IsAlternateArt ? "异画卡图" : x.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图";
                }
            });
            await ApplyOverFrameTagsAsync();
            await Task.Run(() => IndexService.Save(_gameRoot, _textures));
            RefreshCategories();
            var targetCategory = $"{x.SourceKind}|{x.Category}";
            if (_category.Items.Contains(targetCategory)) _category.SelectedItem = targetCategory;
            RenderList(); SelectTexture(x);
            _status.Text = isOverFrame ? "已还原原始卡图并关闭本卡超框登记，已回到卡图列表。" : "已还原游戏本体中的该 Bundle。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
    async Task ExportSelectedAsync() { var x = Selected(); if (x is null) return; using var d = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = Safe(x.Name) + ".png" }; if (d.ShowDialog(this) == DialogResult.OK) await File.WriteAllBytesAsync(d.FileName, await Task.Run(() => _engine.DecodePng(x))); }
    async Task DragOutAsync(ListViewItem? item) { if (item?.Tag is not TexRef x) return; var path = Path.Combine(Path.GetTempPath(), "MDCardModTool", Safe(x.Name) + "_" + x.PathId + ".png"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, await Task.Run(() => _engine.DecodePng(x))); _list.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy); }
    void OnDragEnter(object? sender, DragEventArgs e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    async Task OnDragDropAsync(DragEventArgs e) { if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return; if (!new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(files[0]).ToLowerInvariant())) return; await ReplaceSelectedAsync(files[0]); }
    async Task InspectSelectedAsync() { var x = Selected(); if (x is null || _assetRoot is null) return; try { var s = await Task.Run(() => _engine.InspectBundle(x.BundlePath, _assetRoot)); MessageBox.Show(this, $"Bundle: {s.RelativePath}\nSerialized 文件: {s.SerializedFiles}\n\nAsset 类型：\n{s.Describe()}", "Bundle 检查"); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "检查失败"); } }
    async Task ApplyOverFrameTagsAsync()
    {
        if (_gameRoot is null) return;
        try
        {
            var mappings = await Task.Run(() => _overFrames.ReadCached(_gameRoot));
            var ids = mappings.Select(x => x.CardId.ToString()).ToHashSet(StringComparer.Ordinal);
            foreach (var x in _textures.Where(x => x.SourceKind == "本地卡图" && x.CardKey.Length > 0))
            {
                if (ids.Contains(x.CardKey)) x.Category = "超框卡图";
                else if (x.Category == "超框卡图") x.Category = x.Width == 512 && x.Height == 512 ? (x.IsAlternateArt ? "异画卡图" : x.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图") : "其他贴图";
            }
        }
        catch { }
    }
    void OpenFramePreview()
    {
        var art = Selected();
        if (art is null) { MessageBox.Show(this, "先选择一张卡图，再打开卡框预览。", Text); return; }
        if (art.Width != 704 || art.Height != 1024) { MessageBox.Show(this, $"超框预览只接受 704×1024 高图；当前所选为 {art.Width}×{art.Height}。\n\n请先使用“超框替换”，或选中已经替换完成的高图。", "不是超框高图", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var frames = OverFrameFrames();
        if (frames.Length == 0) { MessageBox.Show(this, "索引中没有 card_frame。请点击“重建索引”后重试。", Text); return; }
        new FramePreviewForm(_engine, art, frames).Show(this);
    }
    void OpenOverFrameTable()
    {
        if (_gameRoot is null) { MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text); return; }
        new OverFrameForm(_gameRoot, Selected()?.CardKey).Show(this);
    }

    TexRef[] OverFrameFrames() => _textures.Where(x => x.SourceKind == "卡框资源" && x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == FrameComposer.Width && x.Height == FrameComposer.Height).ToArray();

    async Task<bool> OpenFrameEditorAsync(TexRef? texture = null, string? initialArtPath = null)
    {
        var x = texture ?? Selected();
        if (x is null || _gameRoot is null) { MessageBox.Show(this, "先选择一张超框卡。", Text); return false; }
        if (x.SourceKind != "本地卡图" || !ushort.TryParse(x.CardKey, out var cardId)) { MessageBox.Show(this, "卡框选择／编辑只能用于名称为卡号的“本地卡图”。", Text); return false; }
        if (initialArtPath is null && (x.Width != FrameComposer.Width || x.Height != FrameComposer.Height))
        {
            MessageBox.Show(this, $"卡号 {cardId} 目前不是 704×1024 超框图。\n\n请先使用“超框替换”选择透明高图。", "尚未启用超框", MessageBoxButtons.OK, MessageBoxIcon.Information); return false;
        }
        var frames = OverFrameFrames();
        if (frames.Length == 0) { MessageBox.Show(this, "索引中没有 704×1024 card_frame。请点击“重建索引”后重试。", Text); return false; }
        using var editor = new OverFrameFrameEditorForm(_gameRoot, x, frames, initialArtPath);
        if (editor.ShowDialog(this) != DialogResult.OK) return false;
        await Task.Run(() => IndexService.Save(_gameRoot, _textures));
        RefreshCategories(); RenderList();
        _status.Text = $"卡号 {cardId} 已应用单卡卡框：{editor.AppliedFrameName}。请完全退出并重启 Master Duel。";
        return true;
    }

    async Task OverFrameReplaceAsync()
    {
        var x = Selected();
        if (x is null || _gameRoot is null) { MessageBox.Show(this, "先选择一张本地卡图。", Text); return; }
        if (x.SourceKind != "本地卡图" || !ushort.TryParse(x.CardKey, out var cardId)) { MessageBox.Show(this, "超框替换只能用于名称为卡号的“本地卡图”。", Text); return; }
        using var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", Title = "选择 704×1024 的超框卡图" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var size = await Task.Run(() => _engine.ImageDimensions(dialog.FileName));
            if (size.Width != 704 || size.Height != 1024) { MessageBox.Show(this, $"超框图必须严格为 704×1024；当前为 {size.Width}×{size.Height}。", "尺寸不符合", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            await OpenFrameEditorAsync(x, dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "超框替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
    void OpenBackup() { if (_gameRoot is null) return; var path = Path.Combine(_gameRoot, "_MD卡图备份"); Directory.CreateDirectory(path); System.Diagnostics.Process.Start("explorer.exe", path); }
    void SelectTexture(TexRef texture)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (!ReferenceEquals(item.Tag, texture)) continue;
            item.Selected = true; item.Focused = true; item.EnsureVisible();
            break;
        }
    }
    static string Safe(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
