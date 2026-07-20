using System.Text.Json;
using System.Text.RegularExpressions;

namespace MdCardModTool;

public sealed class MainForm : Form
{
    const string DefaultGame = @"E:\steam\steamapps\common\Yu-Gi-Oh!  Master Duel";
    const string ModGroupKey = "__mods__";
    readonly ModEngine _engine = new();
    readonly OverFrameService _overFrames = new();
    readonly ModPackageService _mods = new();
    readonly TextBox _gameFolder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    readonly TextBox _search = new() { PlaceholderText = "搜索卡号、贴图名、分类或 Bundle…", Dock = DockStyle.Fill };
    readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly TreeView _groups = new() { Dock = DockStyle.Fill, HideSelection = false };
    readonly BufferedListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AllowDrop = true };
    readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.Surface };
    readonly Label _previewHint = new() { Dock = DockStyle.Fill, Text = "SELECT A RESOURCE\n\n选择一张卡图查看预览", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface };
    readonly Label _resultCount = new() { Dock = DockStyle.Right, AutoSize = false, Width = 160, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted, Padding = new Padding(0, 0, 12, 0) };
    readonly Label _info = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8), ForeColor = UiTheme.Text, BackColor = UiTheme.SurfaceAlt };
    readonly Label _modSummary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Padding = new Padding(8, 0, 8, 0) };
    readonly ToolStripStatusLabel _status = new() { Text = "选择游戏目录后扫描；首次扫描会建立缓存。" };
    readonly List<TexRef> _textures = [];
    readonly Button _modsOnlyButton;
    readonly Button _scanMissingButton;
    string? _gameRoot;
    string? _assetRoot;
    string? _streamingRoot;
    bool _modsOnly;
    GameIndex? _index;

    public MainForm()
    {
        UiTheme.ApplyDarkTitleBar(this);
        Text = "MD 卡图查看替换器"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(1180, 760); Size = new Size(1480, 900);
        BackColor = UiTheme.Window; ForeColor = UiTheme.Text; Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
        UiTheme.StyleTextBox(_gameFolder); UiTheme.StyleTextBox(_search); UiTheme.StyleComboBox(_category); UiTheme.StyleTree(_groups); UiTheme.StyleList(_list);
        _list.Columns.Add("资源名称", 205); _list.Columns.Add("来源 / 分类", 190); _list.Columns.Add("尺寸", 100); _list.Columns.Add("Bundle 路径", 360);
        _list.Resize += (_, _) => { if (_list.Columns.Count == 4) _list.Columns[3].Width = Math.Max(220, _list.ClientSize.Width - 495); };
        _list.SelectedIndexChanged += async (_, _) => await ShowSelectionAsync(); _list.DoubleClick += async (_, _) => await ReplaceSelectedAsync();
        _list.ItemDrag += async (_, e) => await DragOutAsync(e.Item as ListViewItem); _list.DragEnter += OnDragEnter; _list.DragDrop += async (_, e) => await OnDragDropAsync(e);
        _search.TextChanged += (_, _) => RenderList();
        _search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ScanMissingCardAsync(); } };
        _category.Items.Add("全部"); _category.SelectedIndex = 0; _category.SelectedIndexChanged += (_, _) => RenderList();
        _groups.AfterSelect += (_, e) => SelectGroup(e.Node?.Tag as string);
        var choose = Button("选择目录", async (_, _) => await ChooseGameAsync()); var scan = Button("重建索引", async (_, _) => await RebuildIndexAsync());
        var replace = Button("替换所选", async (_, _) => await ReplaceSelectedAsync(), ButtonTone.Primary); var export = Button("导出 PNG", async (_, _) => await ExportSelectedAsync());
        var backup = Button("打开备份", (_, _) => OpenBackup()); var restore = Button("还原所选", async (_, _) => await RestoreSelectedAsync(), ButtonTone.Danger); var inspect = Button("检查 Bundle", async (_, _) => await InspectSelectedAsync());
        var overFrameReplace = Button("超框替换", async (_, _) => await OverFrameReplaceAsync(), ButtonTone.Gold); var frameEditor = Button("卡框选择 / 编辑", async (_, _) => await OpenFrameEditorAsync()); var framePreview = Button("卡框预览", (_, _) => OpenFramePreview()); var overFrameTable = Button("超框表", (_, _) => OpenOverFrameTable());
        _modsOnlyButton = Button("只看我的 Mod", (_, _) => ToggleModsOnly(), ButtonTone.Gold);
        _scanMissingButton = Button("定位卡图", async (_, _) => await ScanMissingCardAsync(), ButtonTone.Neutral);
        var exportMods = Button("一键导出全部 Mod", async (_, _) => await ExportAllModsAsync(), ButtonTone.Primary);
        var importMods = Button("导入 Mod 包", async (_, _) => await ImportModsAsync());

        var brand = new GradientBanner { Dock = DockStyle.Fill, Padding = new Padding(22, 11, 22, 9) };
        brand.Controls.Add(new Label { Text = "MD CARD STUDIO", Dock = DockStyle.Top, Height = 29, Font = new Font("Segoe UI Semibold", 17F), ForeColor = UiTheme.Text, BackColor = Color.Transparent });
        brand.Controls.Add(new Label { Text = "MASTER DUEL  ·  卡图查看 / 替换 / 超框工作台", Dock = DockStyle.Bottom, Height = 23, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = UiTheme.Primary, BackColor = Color.Transparent });

        var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 7, 18, 7), BackColor = UiTheme.Surface, ColumnCount = 4, RowCount = 1 };
        pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathBar.Controls.Add(Caption("GAME ROOT"), 0, 0); pathBar.Controls.Add(_gameFolder, 1, 0); pathBar.Controls.Add(choose, 2, 0); pathBar.Controls.Add(scan, 3, 0);

        var commands = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(13, 7, 18, 7), BackColor = UiTheme.SurfaceAlt, ColumnCount = 7, RowCount = 1 };
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); for (var i = 0; i < 5; i++) commands.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        commands.Controls.Add(_search, 0, 0); commands.Controls.Add(_category, 1, 0); commands.Controls.Add(replace, 2, 0); commands.Controls.Add(export, 3, 0); commands.Controls.Add(restore, 4, 0); commands.Controls.Add(backup, 5, 0); commands.Controls.Add(_scanMissingButton, 6, 0);

        var modBar = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 5, 18, 5), BackColor = Color.FromArgb(18, 31, 50), ColumnCount = 5, RowCount = 1 };
        modBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); modBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); modBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); modBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); modBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        modBar.Controls.Add(Caption("MOD MANAGER"), 0, 0); modBar.Controls.Add(_modsOnlyButton, 1, 0); modBar.Controls.Add(_modSummary, 2, 0); modBar.Controls.Add(exportMods, 3, 0); modBar.Controls.Add(importMods, 4, 0);

        var categoryPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 8, 0) };
        categoryPanel.Controls.Add(_groups); categoryPanel.Controls.Add(SectionHeading("资源分类", "RESOURCE GROUPS"));
        var listPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(8, 0, 0, 0) };
        var listHeader = SectionHeading("图片资源", "拖入替换 · 拖出导出"); listHeader.Controls.Add(_resultCount); _resultCount.BringToFront();
        listPanel.Controls.Add(_list); listPanel.Controls.Add(listHeader);
        var resourceSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 235, SplitterWidth = 8, IsSplitterFixed = false, BackColor = UiTheme.Window };
        resourceSplit.Panel1.Controls.Add(categoryPanel); resourceSplit.Panel2.Controls.Add(listPanel);

        var previewFrame = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18) };
        previewFrame.Controls.Add(_preview); previewFrame.Controls.Add(_previewHint); _previewHint.BringToFront();
        var rightActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, BackColor = UiTheme.Surface, Padding = new Padding(5, 4, 4, 4) };
        rightActions.Controls.Add(overFrameReplace); rightActions.Controls.Add(frameEditor); rightActions.Controls.Add(framePreview); rightActions.Controls.Add(overFrameTable); rightActions.Controls.Add(inspect);
        var previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, RowCount = 4, ColumnCount = 1 };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104)); previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        previewLayout.Controls.Add(SectionHeading("实时预览", "LIVE PREVIEW"), 0, 0); previewLayout.Controls.Add(previewFrame, 0, 1); previewLayout.Controls.Add(_info, 0, 2); previewLayout.Controls.Add(rightActions, 0, 3);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 14, 7, 14), BackColor = UiTheme.Window }; left.Controls.Add(resourceSplit);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(7, 14, 14, 14), BackColor = UiTheme.Window }; right.Controls.Add(previewLayout);
        var workspace = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 900, SplitterWidth = 6, BackColor = UiTheme.Border }; workspace.Panel1.Controls.Add(left); workspace.Panel2.Controls.Add(right);

        _status.Spring = true; _status.TextAlign = ContentAlignment.MiddleLeft; _status.ForeColor = UiTheme.Muted; _status.Font = new Font("Microsoft YaHei UI", 8.5F);
        var status = new StatusStrip { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ForeColor = UiTheme.Muted, SizingGrip = false, Padding = new Padding(12, 0, 12, 0) }; status.Items.Add(_status);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Window, RowCount = 6, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(brand, 0, 0); root.Controls.Add(pathBar, 0, 1); root.Controls.Add(commands, 0, 2); root.Controls.Add(modBar, 0, 3); root.Controls.Add(workspace, 0, 4); root.Controls.Add(status, 0, 5); Controls.Add(root);
        if (Directory.Exists(DefaultGame)) { _gameRoot = DefaultGame; _gameFolder.Text = DefaultGame; SetGameRoot(); }
        Shown += async (_, _) =>
        {
            if (resourceSplit.Width > 650) resourceSplit.SplitterDistance = 235;
            if (workspace.Width > 1150) workspace.SplitterDistance = Math.Min((int)(workspace.Width * 0.68), workspace.Width - 410);
            if (_assetRoot is not null) await ScanAsync();
        };
    }

    static Button Button(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral) => UiTheme.Button(text, click, tone);
    static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Gold, Font = new Font("Segoe UI Semibold", 8.5F), Padding = new Padding(0, 8, 10, 0) };
    static Panel SectionHeading(string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = UiTheme.SurfaceAlt, Padding = new Padding(12, 5, 12, 4) };
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8F), Padding = new Padding(4, 2, 0, 0) });
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Left, Width = 100, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) });
        return panel;
    }
    async Task ChooseGameAsync() { using var d = new FolderBrowserDialog { Description = "选择 Yu-Gi-Oh! Master Duel 游戏根目录", InitialDirectory = Directory.Exists(DefaultGame) ? DefaultGame : "" }; if (d.ShowDialog(this) == DialogResult.OK) { _gameRoot = d.SelectedPath; _gameFolder.Text = d.SelectedPath; SetGameRoot(); await ScanAsync(); } }
    async Task RebuildIndexAsync()
    {
        if (MessageBox.Show(this, "重建索引会忽略随包预绑定和本地缓存，重新扫描当前游戏文件，可能需要较长时间。\n\n只有游戏更新后资源对应错误时才需要执行。继续？", "确认重建索引", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        await ScanAsync(forceRebuild: true);
    }
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

    async Task ScanAsync(bool forceRebuild = false)
    {
        if (_assetRoot is null || !Directory.Exists(_assetRoot)) { MessageBox.Show(this, "未找到 LocalData\\<用户哈希>\\0000。请选择 Master Duel 游戏根目录。", Text); return; }
        UseWaitCursor = true; _textures.Clear(); _preview.Image = null; _previewHint.Visible = true;
        try
        {
            var cache = CachePath();
            GameIndex? cached = null;
            var loadedFromPrebuilt = false;
            var prebuiltBuildId = "";
            if (!forceRebuild && File.Exists(cache))
            {
                try { cached = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache)); }
                catch { File.Delete(cache); }
            }
            if (cached is null && !forceRebuild)
            {
                try
                {
                    _status.Text = "正在载入随程序提供的卡号预绑定索引…";
                    var prebuilt = await Task.Run(() =>
                    {
                        var found = PortableIndexService.TryLoadBundled(_gameRoot!, out var index, out var buildId);
                        return (Found: found, Index: index, BuildId: buildId);
                    });
                    loadedFromPrebuilt = prebuilt.Found; cached = loadedFromPrebuilt ? prebuilt.Index : null; prebuiltBuildId = prebuilt.BuildId;
                    if (loadedFromPrebuilt && cached is not null) await Task.Run(() => IndexService.Save(_gameRoot!, cached));
                }
                catch (Exception ex) { _status.Text = "随包预绑定索引不可用，将自动重建：" + ex.Message; cached = null; }
            }
            if (cached is null)
            {
                _status.Text = forceRebuild ? "正在按要求重新扫描全部资源…" : "未找到预绑定索引，正在建立本地索引（仅此一次）…";
                await Task.Run(() => IndexService.BuildAndSave(_gameRoot!, (done, total, found) => BeginInvoke(() => _status.Text = $"正在重建索引：{done:N0}/{total:N0} Bundle，已索引 {found:N0} 张图片…")));
                cached = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache)) ?? new GameIndex();
            }
            // 480×700 是另一套界面用小卡框，不属于游戏超框预览。
            cached.Textures.RemoveAll(x => x.SourceKind == "卡框资源" && (x.Width != 704 || x.Height != 1024));
            // v1.3.1 曾误把 P数字 Spine 图集部件归为卡图原画；迁移旧缓存时一并移除。
            var removedSpineParts = IndexService.RemoveSpineAtlasParts(cached);
            var removedNonCards = IndexService.RemoveNonCardLocalTextures(cached);
            if (removedSpineParts + removedNonCards > 0) await Task.Run(() => IndexService.Save(_gameRoot!, cached));
            _index = cached;
            _textures.AddRange(cached.Textures);
            var currentBuildId = loadedFromPrebuilt ? PortableIndexService.GetGameBuildId(_gameRoot!) : "";
            var buildNote = prebuiltBuildId.Length > 0 && currentBuildId.Length > 0 && prebuiltBuildId != currentBuildId ? $"；预绑定 Build {prebuiltBuildId}，本机 Build {currentBuildId}，若个别资源失效请重建索引" : "";
            _status.Text = loadedFromPrebuilt
                ? $"已用随包预绑定瞬时建立本机索引：{_textures.Count:N0} 张图片，无需首次扫描{buildNote}。"
                : forceRebuild ? $"索引已重建：{_textures.Count:N0} 张图片。" : $"已载入本地索引：{_textures.Count:N0} 张图片。";
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
            await ApplyOverFrameTagsAsync();
            await RefreshModFlagsAsync();
            RefreshCategories(); RenderList();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    void RefreshCategories()
    {
        var old = _category.Text; _category.Items.Clear(); _category.Items.Add("全部"); _groups.Nodes.Clear();
        var modCount = _textures.Count(x => x.IsModded);
        var modNode = _groups.Nodes.Add($"我的 Mod（{modCount}）"); modNode.Tag = ModGroupKey; modNode.ForeColor = UiTheme.Gold;
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
        UpdateModSummary();
    }
    void RenderList()
    {
        var q = _search.Text.Trim(); var filter = _category.Text; _list.BeginUpdate(); _list.Items.Clear();
        // 输入关键字时始终全局检索，不能被侧栏分类或“只看我的 Mod”悄悄过滤掉。
        var globalSearch = q.Length > 0;
        foreach (var x in _textures.Where(x => (globalSearch || (!_modsOnly || x.IsModded) && (filter == "全部" || filter == $"{x.SourceKind}|{x.Category}")) && MatchesSearch(x, q)))
            _list.Items.Add(new ListViewItem([x.Name, $"{x.SourceKind} / {x.Category}{(x.IsModded ? "  · MOD" : "")}", $"{x.Width}×{x.Height}", x.RelativeBundlePath]) { Tag = x });
        _list.EndUpdate();
        var total = globalSearch ? _textures.Count : _modsOnly ? _textures.Count(x => x.IsModded) : _textures.Count;
        _resultCount.Text = globalSearch ? $"全局 { _list.Items.Count:N0} / {total:N0}" : $"{_list.Items.Count:N0} / {total:N0} 项";
    }

    static bool MatchesSearch(TexRef texture, string query)
    {
        if (query.Length == 0) return true;
        // 卡号必须精确匹配，避免 11213 把 112132、112133 等无关资源带进结果。
        if (query.All(char.IsAsciiDigit)) return texture.CardKey == query || texture.Name == query;
        return $"{texture.Name} {texture.CardKey} {texture.SourceKind} {texture.Category} {texture.RelativeBundlePath}".Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    async Task ScanMissingCardAsync()
    {
        var cardKey = _search.Text.Trim();
        if (!Regex.IsMatch(cardKey, "^\\d+$"))
        {
            MessageBox.Show(this, "“定位卡图”只用于纯数字卡号，例如 11213。\n\n普通名称搜索会直接在现有索引中全局查找。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_gameRoot is null || _index is null) return;
        var existing = _textures.FirstOrDefault(x => x.SourceKind == "本地卡图" && x.CardKey == cardKey);
        if (existing is not null)
        {
            SetModsOnly(false); _category.SelectedItem = "全部"; RenderList(); SelectTexture(existing);
            _status.Text = $"卡号 {cardKey} 已在本机索引中；当前以全局检索显示，不受分类筛选影响。";
            return;
        }
        try
        {
            _scanMissingButton.Enabled = false;
            _status.Text = $"正在按游戏资源路径直接定位卡号 {cardKey}…";
            var result = await Task.Run(() => IndexService.ScanMissingLocalCard(_gameRoot, _index, cardKey, (done, total, added) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() => _status.Text = $"正在定位 {cardKey}：{done:N0}/{total:N0} 个候选 Bundle…");
            }));
            var known = _textures.Select(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = result.Textures.Where(x => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")).ToList();
            if (additions.Count > 0)
            {
                await YgoCdbCardCatalog.ClassifyTexturesAsync(additions);
                _index.Textures.AddRange(additions); _textures.AddRange(additions);
            }
            await Task.Run(() => IndexService.Save(_gameRoot, _index));
            RefreshCategories(); RenderList();
            var found = _textures.FirstOrDefault(x => x.SourceKind == "本地卡图" && x.CardKey == cardKey);
            if (found is not null)
            {
                SetModsOnly(false); _category.SelectedItem = "全部"; RenderList(); SelectTexture(found);
                _status.Text = found.Height == 1024
                    ? $"已定位灵摆卡 {cardKey} 的 512×1024 原生卡图；可直接预览、裁剪和替换，映射已保存。"
                    : $"已定位卡号 {cardKey} 的 512×512 卡图；映射已保存。";
            }
            else _status.Text = $"本机 LocalData 中没有已下载的卡号 {cardKey} 卡图 Bundle；本次只检查了游戏计算出的 {result.TotalBundles:N0} 个候选路径，没有扫描整个文件夹。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "定位卡图失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _scanMissingButton.Enabled = true; }
    }

    void SelectGroup(string? key)
    {
        if (key is null) return;
        if (key == ModGroupKey) { SetModsOnly(true); _category.SelectedItem = "全部"; RenderList(); return; }
        SetModsOnly(false);
        if (_category.Items.Contains(key)) _category.SelectedItem = key;
    }

    void ToggleModsOnly() { SetModsOnly(!_modsOnly); RenderList(); }
    void SetModsOnly(bool enabled)
    {
        _modsOnly = enabled;
        _modsOnlyButton.Text = enabled ? "显示全部资源" : "只看我的 Mod";
        UpdateModSummary();
    }

    void UpdateModSummary()
    {
        var textures = _textures.Where(x => x.IsModded).ToArray();
        var bundles = textures.Select(x => x.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _modSummary.Text = textures.Length == 0 ? "暂无改动；替换后的卡图会自动出现在这里" : $"已管理 {textures.Length:N0} 个资源 · {bundles:N0} 个已修改 Bundle";
    }

    async Task RefreshModFlagsAsync()
    {
        if (_gameRoot is null) return;
        await Task.Run(() => _mods.RefreshFlags(_gameRoot, _textures));
        UpdateModSummary();
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
            else if (PreviewFrameFor(x) is { } previewFrame)
            {
                var frameData = await Task.Run(() => _engine.DecodePng(previewFrame));
                display = FrameComposer.BitmapFrom(CardFrameRenderer.ComposeStoredArtPreview(data, frameData));
            }
            else { using var s = new MemoryStream(data); using var im = Image.FromStream(s); display = new Bitmap(im); }
            _preview.Image?.Dispose(); _preview.Image = display;
            _previewHint.Visible = false;
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
            else if (PreviewFrameFor(x) is { } normalFrame)
            {
                var window = x.Height == 1024 ? $"灵摆显示区 512×{CardFrameCatalog.PendulumVisibleStorageHeight} → 完整 512×1024 纹理" : "标准插图区 → 512×512 存储";
                frameLine = $"\n实装预览：{normalFrame.Name} · {CardFrameCatalog.FriendlyName(normalFrame.Name)}  ·  {window}";
            }
            _info.Text = $"{x.Name}  ·  {x.Category}\n{x.Width} × {x.Height}   PathID {x.PathId}\n{x.RelativeBundlePath}{frameLine}";
        }
        catch (Exception ex) { _status.Text = "预览失败：" + ex.Message; }
    }
    async Task ReplaceSelectedAsync(string? image = null)
    {
        var x = Selected(); if (x is null || _gameRoot is null) { MessageBox.Show(this, "先选择一张图片。", Text); return; }
        if (image is null) { using var d = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp" }; if (d.ShowDialog(this) != DialogResult.OK) return; image = d.FileName; }
        byte[] cropped;
        try
        {
            var frames = x.SourceKind == "本地卡图" && x.CardKey.Length > 0 && x.Width == 512 && (x.Height == 512 || x.Height == 1024) ? OverFrameFrames() : null;
            using var crop = new ImageCropForm(image, x.Width, x.Height, $"替换 {x.Name}", frames, x.PreviewFrameKey);
            if (crop.ShowDialog(this) != DialogResult.OK || crop.OutputPng is null) return;
            cropped = crop.OutputPng;
            if (crop.SelectedFrameKey.Length > 0) x.PreviewFrameKey = crop.SelectedFrameKey;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开裁剪器", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        if (MessageBox.Show(this, $"裁剪结果将以 {x.Width}×{x.Height} 写入游戏 Bundle。原始文件会备份到游戏目录的 _MD卡图备份 中。继续？", "确认替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true; x.OverrideBundlePath = null;
            await Task.Run(() => _engine.Replace(x, cropped, Path.Combine(_gameRoot, "_MD卡图备份", x.SourceKind)));
            await RefreshModFlagsAsync();
            await Task.Run(() => IndexService.Save(_gameRoot, _textures));
            RefreshCategories(); RenderList(); SelectTexture(x);
            _status.Text = "替换完成：已写入游戏本体，并加入“我的 Mod”。"; await ShowSelectionAsync();
        }
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
            ? "确认还原该超框卡？会还原原始卡图、关闭本卡超框登记，并重新归类到卡图列表。"
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
                    foreach (var linked in _textures.Where(t => t.BundlePath.Equals(x.BundlePath, StringComparison.OrdinalIgnoreCase) && t.PathId == x.PathId && t.AssetFileName == x.AssetFileName))
                    {
                        linked.Width = reloaded.Width; linked.Height = reloaded.Height; linked.Category = reloaded.Category;
                        IndexService.NormalizeLocalCardCategory(linked);
                    }
                }
            });
            await ApplyOverFrameTagsAsync();
            await RefreshModFlagsAsync();
            await Task.Run(() => IndexService.Save(_gameRoot, _textures));
            RefreshCategories();
            var targetCategory = $"{x.SourceKind}|{x.Category}";
            if (isOverFrame) SetModsOnly(false);
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
                else if (x.Category == "超框卡图") IndexService.NormalizeLocalCardCategory(x);
            }
        }
        catch { }
    }
    void OpenFramePreview()
    {
        var art = Selected();
        if (art is null) { MessageBox.Show(this, "先选择一张卡图，再打开卡框预览。", Text); return; }
        var supported = art.Width == FrameComposer.Width && art.Height == FrameComposer.Height || art.Width == 512 && (art.Height == 512 || art.Height == 1024);
        if (!supported) { MessageBox.Show(this, $"当前图片尺寸 {art.Width}×{art.Height} 不是可预览的卡图。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var frames = OverFrameFrames();
        if (frames.Length == 0) { MessageBox.Show(this, "索引中没有 card_frame。请点击“重建索引”后重试。", Text); return; }
        var preview = new FramePreviewForm(_engine, art, frames);
        preview.FormClosed += async (_, _) =>
        {
            if (_gameRoot is not null) await Task.Run(() => IndexService.Save(_gameRoot, _textures));
            if (ReferenceEquals(Selected(), art)) await ShowSelectionAsync();
        };
        preview.Show(this);
    }
    void OpenOverFrameTable()
    {
        if (_gameRoot is null) { MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text); return; }
        new OverFrameForm(_gameRoot, Selected()?.CardKey).Show(this);
    }

    TexRef[] OverFrameFrames() => _textures.Where(x => x.SourceKind == "卡框资源" && x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == FrameComposer.Width && x.Height == FrameComposer.Height).ToArray();

    TexRef? PreviewFrameFor(TexRef texture)
    {
        if (texture.SourceKind != "本地卡图" || texture.Width != 512 || (texture.Height != 512 && texture.Height != 1024)) return null;
        var frames = CardFrameCatalog.CompatibleFrames(OverFrameFrames(), texture.Width, texture.Height).ToArray();
        var wanted = texture.PreviewFrameKey.Length > 0 ? texture.PreviewFrameKey : CardFrameCatalog.DefaultKey(texture.Width, texture.Height);
        return frames.FirstOrDefault(x => x.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? frames.FirstOrDefault();
    }

    async Task<bool> OpenFrameEditorAsync(TexRef? texture = null, byte[]? initialArt = null, string? initialFrameKey = null)
    {
        var x = texture ?? Selected();
        if (x is null || _gameRoot is null) { MessageBox.Show(this, "先选择一张超框卡。", Text); return false; }
        if (x.SourceKind != "本地卡图" || !ushort.TryParse(x.CardKey, out var cardId)) { MessageBox.Show(this, "卡框选择／编辑只能用于名称为卡号的“本地卡图”。", Text); return false; }
        if (initialArt is null && (x.Width != FrameComposer.Width || x.Height != FrameComposer.Height) && !File.Exists(OverFrameArtStore.ArtPath(_gameRoot, cardId)))
        {
            MessageBox.Show(this, $"卡号 {cardId} 目前不是 704×1024 超框图。\n\n请先使用“超框替换”选择透明高图。", "尚未启用超框", MessageBoxButtons.OK, MessageBoxIcon.Information); return false;
        }
        var frames = OverFrameFrames();
        if (frames.Length == 0) { MessageBox.Show(this, "索引中没有 704×1024 card_frame。请点击“重建索引”后重试。", Text); return false; }
        using var editor = new OverFrameFrameEditorForm(_gameRoot, x, frames, initialArt, initialFrameKey);
        if (editor.ShowDialog(this) != DialogResult.OK) return false;
        await RefreshModFlagsAsync();
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
        using var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", Title = "选择超框卡图（下一步可固定比例裁剪）" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var frames = OverFrameFrames();
            if (frames.Length == 0) { MessageBox.Show(this, "索引中没有 704×1024 card_frame。请点击“重建索引”后重试。", Text); return; }
            var settings = OverFrameArtStore.ReadSettings(_gameRoot, cardId);
            using var crop = new ImageCropForm(dialog.FileName, FrameComposer.Width, FrameComposer.Height, $"超框卡图 {cardId}", frames, settings.FrameKey, fullCardOverlay: true);
            if (crop.ShowDialog(this) != DialogResult.OK || crop.OutputPng is null) return;
            await OpenFrameEditorAsync(x, crop.OutputPng, crop.SelectedFrameKey);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "超框替换失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    async Task ExportAllModsAsync()
    {
        if (_gameRoot is null) { MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text); return; }
        using var dialog = new SaveFileDialog
        {
            Filter = "Master Duel Mod 包|*.mdmod.zip",
            FileName = $"MD_Mods_{DateTime.Now:yyyyMMdd_HHmm}.mdmod.zip",
            Title = "导出全部已启用 Mod"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true; _status.Text = "正在打包全部已修改 Bundle…";
            var info = await Task.Run(() => _mods.Export(_gameRoot, _textures, dialog.FileName));
            _status.Text = $"已导出 {info.BundleCount:N0} 个 Mod Bundle：{dialog.FileName}";
            MessageBox.Show(this, $"导出完成。\n\nBundle：{info.BundleCount:N0} 个\n原始大小：{FormatSize(info.TotalSize)}\n文件：{dialog.FileName}", "全部 Mod 已导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出 Mod 失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    async Task ImportModsAsync()
    {
        if (_gameRoot is null) { MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text); return; }
        using var dialog = new OpenFileDialog { Filter = "Master Duel Mod 包|*.mdmod.zip|ZIP 压缩包|*.zip", Title = "导入 MD Mod 包" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = await Task.Run(() => _mods.Inspect(dialog.FileName));
            var confirm = $"准备导入“{info.Name}”。\n\nBundle：{info.BundleCount:N0} 个\n原始大小：{FormatSize(info.TotalSize)}\n\n将直接覆盖对应游戏 Bundle；每个文件首次覆盖前都会自动保存原版备份。继续？";
            if (MessageBox.Show(this, confirm, "确认导入 Mod", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            UseWaitCursor = true; _status.Text = "正在校验并导入 Mod 包…";
            var result = await Task.Run(() => _mods.Import(_gameRoot, dialog.FileName));
            await ReloadChangedBundlesAsync(result.ChangedBundlePaths);
            await ApplyOverFrameTagsAsync();
            await RefreshModFlagsAsync();
            await Task.Run(() => IndexService.Save(_gameRoot, _textures));
            RefreshCategories(); SetModsOnly(true); _category.SelectedItem = "全部"; RenderList();
            _status.Text = $"已导入 {result.BundleCount:N0} 个 Mod Bundle，并加入“我的 Mod”。";
            MessageBox.Show(this, $"已导入 {result.BundleCount:N0} 个 Bundle。\n\n现在可在“我的 Mod”中集中查看和还原。请完全退出并重启 Master Duel。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导入 Mod 失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    async Task ReloadChangedBundlesAsync(IReadOnlyList<string> changedPaths)
    {
        if (_gameRoot is null || _assetRoot is null || _streamingRoot is null) return;
        foreach (var path in changedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var existing = _textures.Where(x => x.BundlePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Length == 0) continue;
            var sourceKind = existing[0].SourceKind;
            var root = sourceKind switch { "本地卡图" => _assetRoot, "游戏内图片" => _streamingRoot, _ => _gameRoot };
            var scanned = await Task.Run(() => _engine.ScanBundle(path, root, sourceKind, includeDependencies: false).Textures);
            foreach (var texture in existing)
            {
                var updated = scanned.FirstOrDefault(x => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName);
                if (updated is null) continue;
                texture.Width = updated.Width; texture.Height = updated.Height; texture.Category = updated.Category; texture.OverrideBundlePath = null;
                IndexService.NormalizeLocalCardCategory(texture);
            }
        }
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
    static string FormatSize(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.##} MB" : $"{bytes / 1024d:0.##} KB";
    static string Safe(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
