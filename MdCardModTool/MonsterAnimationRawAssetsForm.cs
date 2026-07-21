using System.Diagnostics;
using System.Text;

namespace MdCardModTool;

public sealed class MonsterAnimationRawAssetsForm : Form
{
    readonly string _gameRoot;
    readonly string _cardId;
    readonly MonsterAnimationRawAssetService _service = new();
    readonly ModEngine _engine = new();
    readonly BufferedListView _assets = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AllowDrop = true };
    readonly PictureBox _imagePreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = UiTheme.SurfaceAlt };
    readonly TextBox _textPreview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5F), BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text, BorderStyle = BorderStyle.None };
    readonly Label _details = new() { Dock = DockStyle.Bottom, Height = 88, Padding = new Padding(12, 9, 12, 6), ForeColor = UiTheme.Text, BackColor = UiTheme.Surface };
    readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted };
    readonly List<Button> _buttons = [];
    MonsterAnimationSet? _set;
    string? _lastExportDirectory;
    bool _busy;

    public MonsterAnimationRawAssetsForm(string gameRoot, string cardId)
    {
        _gameRoot = gameRoot;
        _cardId = cardId;
        UiTheme.ApplyDarkTitleBar(this);
        Text = $"原始动画资源 · {_cardId}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1240, 800);
        MinimumSize = new Size(960, 640);
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AllowDrop = true;

        UiTheme.StyleList(_assets);
        UiTheme.StyleTextBox(_textPreview);
        _assets.Columns.Add("版本 / 区域", 230);
        _assets.Columns.Add("文件类型", 110);
        _assets.Columns.Add("资源名", 150);
        _assets.Columns.Add("PathID", 120);
        _assets.Columns.Add("Bundle 路径", 350);
        _assets.Resize += (_, _) => { if (_assets.Columns.Count == 5) _assets.Columns[4].Width = Math.Max(220, _assets.ClientSize.Width - 610); };
        _assets.SelectedIndexChanged += async (_, _) => await ShowSelectedAsync();
        _assets.DoubleClick += async (_, _) => await ReplaceSelectedAsync();
        _assets.DragEnter += OnDragEnter;
        _assets.DragDrop += async (_, e) => await OnDragDropAsync(e);
        DragEnter += OnDragEnter;
        DragDrop += async (_, e) => await OnDragDropAsync(e);

        var exportAll = ActionButton("导出全部 6 个文件", async (_, _) => await ExportAllAsync(), ButtonTone.Primary);
        var importAll = ActionButton("批量导入编辑目录", async (_, _) => await ImportAllAsync(), ButtonTone.Gold);
        var exportSelected = ActionButton("导出所选", async (_, _) => await ExportSelectedAsync());
        var replaceSelected = ActionButton("替换所选", async (_, _) => await ReplaceSelectedAsync());
        var restoreSelected = ActionButton("恢复所选 Bundle", async (_, _) => await RestoreSelectedAsync(), ButtonTone.Danger);
        var openDirectory = ActionButton("打开导出目录", (_, _) => OpenExportDirectory());

        var banner = new GradientBanner { Dock = DockStyle.Fill, Padding = new Padding(22, 9, 22, 8) };
        banner.Controls.Add(new Label { Text = "RAW MONSTER ANIMATION ASSETS", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 16F), ForeColor = UiTheme.Text, BackColor = Color.Transparent });
        banner.Controls.Add(new Label { Text = $"P{_cardId}  ·  PNG / ATLAS / JSON  ·  SD + HIGHEND_HD", Dock = DockStyle.Bottom, Height = 22, ForeColor = UiTheme.Primary, BackColor = Color.Transparent });

        var statusBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 7, 18, 7), BackColor = UiTheme.Surface };
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusBar.Controls.Add(_status, 0, 0);
        statusBar.Controls.Add(new Label { Text = "高级功能：Atlas、JSON 与 PNG 必须互相匹配", AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = Color.Orange }, 1, 0);

        var listPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        listPanel.Controls.Add(_assets);
        listPanel.Controls.Add(SectionHeading("关联资源", "双击或拖入文件替换所选"));

        var previewPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        previewPanel.Controls.Add(_imagePreview);
        previewPanel.Controls.Add(_textPreview);
        previewPanel.Controls.Add(_details);
        previewPanel.Controls.Add(SectionHeading("资源预览", "PNG / TEXT PREVIEW"));
        _textPreview.BringToFront();
        _details.BringToFront();

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 720, SplitterWidth = 8, BackColor = UiTheme.Window };
        split.Panel1.Padding = new Padding(14, 14, 7, 8);
        split.Panel2.Padding = new Padding(7, 14, 14, 8);
        split.Panel1.Controls.Add(listPanel);
        split.Panel2.Controls.Add(previewPanel);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, BackColor = UiTheme.SurfaceAlt, Padding = new Padding(14, 7, 14, 7) };
        foreach (var button in new[] { exportAll, importAll, exportSelected, replaceSelected, restoreSelected, openDirectory }) actions.Controls.Add(button);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 4, 18, 4),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.Muted,
            BackColor = UiTheme.Surface,
            Text = "导出全部会同时生成 _动画资源映射.json。可在外部编辑同目录文件后使用“批量导入编辑目录”；任何一步失败都会恢复本次写入前的全部 Bundle。"
        };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = UiTheme.Window };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(banner, 0, 0);
        root.Controls.Add(statusBar, 0, 1);
        root.Controls.Add(split, 0, 2);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(note, 0, 4);
        Controls.Add(root);

        Shown += async (_, _) => await LoadAssetsAsync();
        FormClosed += (_, _) => _imagePreview.Image?.Dispose();
    }

    Button ActionButton(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral)
    {
        var button = UiTheme.Button(text, click, tone);
        _buttons.Add(button);
        return button;
    }

    async Task LoadAssetsAsync()
    {
        try
        {
            SetBusy(true, "正在定位 SD / HighEnd_HD 原始动画资源…");
            _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, _cardId));
            _assets.BeginUpdate();
            _assets.Items.Clear();
            foreach (var asset in _set.Assets.OrderBy(x => _service.ResolveProfile(x).Tier).ThenBy(x => x.Kind))
            {
                var profile = _service.ResolveProfile(asset);
                var kind = asset.Kind switch { MonsterAnimationAssetKind.Texture => "PNG 图集", MonsterAnimationAssetKind.Atlas => "Atlas 文本", _ => "Spine JSON" };
                _assets.Items.Add(new ListViewItem([profile.DisplayName, kind, asset.Name, asset.PathId.ToString(), asset.RelativeBundlePath]) { Tag = asset });
            }
            _assets.EndUpdate();
            _status.Text = $"{_set.CountSummary} · 共 {_set.Assets.Count} 个关联资源{(_set.IsComplete ? " · 完整" : " · 资源不完整")}";
            _status.ForeColor = _set.IsComplete ? UiTheme.Primary : Color.OrangeRed;
            if (_assets.Items.Count > 0) { _assets.Items[0].Selected = true; _assets.Items[0].Focused = true; }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法读取动画资源", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
        if (_assets.SelectedItems.Count == 1) await ShowSelectedAsync();
    }

    MonsterAnimationAssetRef? SelectedAsset() => _assets.SelectedItems.Count == 1 ? _assets.SelectedItems[0].Tag as MonsterAnimationAssetRef : null;

    async Task ShowSelectedAsync(bool force = false)
    {
        var asset = SelectedAsset();
        if (asset is null || _busy && !force) return;
        try
        {
            SetBusy(true, "正在读取所选底层资源…");
            var data = await Task.Run(() => asset.Kind == MonsterAnimationAssetKind.Texture ? _engine.DecodePng(asset.AsTexture(), 1024) : _service.Read(asset));
            if (asset.Kind == MonsterAnimationAssetKind.Texture)
            {
                using var stream = new MemoryStream(data);
                using var source = Image.FromStream(stream);
                var display = new Bitmap(source);
                _imagePreview.Image?.Dispose();
                _imagePreview.Image = display;
                _imagePreview.Visible = true;
                _imagePreview.BringToFront();
                _textPreview.Visible = false;
                _details.BringToFront();
            }
            else
            {
                _imagePreview.Visible = false;
                _textPreview.Visible = true;
                var text = Encoding.UTF8.GetString(data).TrimEnd('\0');
                _textPreview.Text = text.Length <= 1_500_000 ? text : text[..1_500_000] + "\r\n\r\n[预览已截断]";
                _textPreview.SelectionStart = 0;
                _textPreview.ScrollToCaret();
                _textPreview.BringToFront();
                _details.BringToFront();
            }
            var profile = _service.ResolveProfile(asset);
            _details.Text = $"{profile.DisplayName} · {asset.Kind}\n{asset.Name} · PathID {asset.PathId}\n{asset.StorageKind} / {asset.RelativeBundlePath}";
            _status.Text = $"已读取 {_service.ExportFileName(asset)}";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "资源预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task ExportSelectedAsync()
    {
        var asset = SelectedAsset();
        if (asset is null) { MessageBox.Show(this, "先选择一个动画资源。", Text); return; }
        var extension = asset.Kind switch { MonsterAnimationAssetKind.Texture => "PNG 图片|*.png", MonsterAnimationAssetKind.Atlas => "Spine Atlas|*.atlas", _ => "Spine JSON|*.json" };
        using var dialog = new SaveFileDialog { Filter = extension, FileName = _service.ExportFileName(asset) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在导出所选资源…");
            await File.WriteAllBytesAsync(dialog.FileName, await Task.Run(() => _service.Read(asset)));
            _lastExportDirectory = Path.GetDirectoryName(dialog.FileName);
            _status.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task ExportAllAsync()
    {
        if (_set is null || _set.Assets.Count == 0) return;
        var suggested = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MD动画资源", "P" + _cardId);
        using var dialog = new FolderBrowserDialog { Description = "选择 6 个原始动画文件的导出目录", InitialDirectory = Directory.Exists(suggested) ? suggested : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var directory = Path.Combine(dialog.SelectedPath, "P" + _cardId + "_动画资源");
        try
        {
            SetBusy(true, "正在导出 PNG、Atlas、JSON 与回导映射…");
            var manifest = await Task.Run(() => _service.ExportAll(_set, directory));
            _lastExportDirectory = directory;
            _status.Text = $"已导出 {manifest.Files.Count} 个资源：" + directory;
            OpenExportDirectory();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "整套导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task ReplaceSelectedAsync(string? inputPath = null)
    {
        var asset = SelectedAsset();
        if (asset is null) { MessageBox.Show(this, "先选择要替换的底层资源。", Text); return; }
        if (!EnsureGameClosed()) return;
        if (inputPath is null)
        {
            var filter = asset.Kind switch { MonsterAnimationAssetKind.Texture => "PNG 图片|*.png", MonsterAnimationAssetKind.Atlas => "Spine Atlas|*.atlas;*.txt", _ => "Spine JSON|*.json;*.txt" };
            using var dialog = new OpenFileDialog { Title = "选择替换文件", Filter = filter };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            inputPath = dialog.FileName;
        }
        if (MessageBox.Show(this, $"只替换所选的 {asset.Kind}：\n{_service.ExportFileName(asset)}\n\n不会自动同步另一套画质资源。PNG、Atlas 与 JSON 不匹配会导致动画无法播放。继续？", "高级单文件替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在备份并替换所选底层资源…");
            await Task.Run(() => _service.ReplaceOne(_gameRoot, asset, inputPath));
            await ShowSelectedAsync(true);
            _status.Text = "替换完成：" + _service.ExportFileName(asset);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "替换失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task ImportAllAsync()
    {
        if (_set is null || _set.Assets.Count == 0 || !EnsureGameClosed()) return;
        using var dialog = new FolderBrowserDialog { Description = $"选择包含 {MonsterAnimationRawAssetService.ManifestFileName} 的编辑目录", InitialDirectory = _lastExportDirectory ?? "" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "将按映射批量写入目录中的 PNG、Atlas 与 JSON。\n\n这是一项高级操作；全部资源会先备份，任一步失败会整批回滚。继续？", "批量导入原始动画资源", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在校验并批量写入原始动画资源…");
            var count = await Task.Run(() => _service.ImportAll(_gameRoot, _set, dialog.SelectedPath));
            _lastExportDirectory = dialog.SelectedPath;
            await ShowSelectedAsync(true);
            _status.Text = $"已批量导入 {count} 个资源";
            MessageBox.Show(this, $"已成功导入 {count} 个原始动画资源。\n请完全退出并重新启动 Master Duel 后测试。", "批量导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "批量导入失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task RestoreSelectedAsync()
    {
        var asset = SelectedAsset();
        if (asset is null || !EnsureGameClosed()) return;
        if (MessageBox.Show(this, "恢复操作以 Bundle 为单位；如果同一 Bundle 里还有其他改动，也会一起回到首次备份状态。继续？", "恢复所选 Bundle", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在恢复首次备份…");
            var restored = await Task.Run(() => _service.Restore(_gameRoot, asset));
            if (restored) await ShowSelectedAsync(true);
            _status.Text = restored ? "已恢复所选 Bundle" : "没有找到该 Bundle 的首次备份";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    async Task OnDragDropAsync(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await ReplaceSelectedAsync(files[0]);
    }

    bool EnsureGameClosed()
    {
        if (Process.GetProcessesByName("masterduel").Length == 0) return true;
        MessageBox.Show(this, "请先完全退出 Master Duel，再替换或恢复动画资源。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    void OpenExportDirectory()
    {
        if (string.IsNullOrWhiteSpace(_lastExportDirectory) || !Directory.Exists(_lastExportDirectory))
        {
            MessageBox.Show(this, "还没有导出目录。请先点击“导出全部 6 个文件”。", Text);
            return;
        }
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(_lastExportDirectory);
        Process.Start(start);
    }

    void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _assets.Enabled = !busy;
        foreach (var button in _buttons) button.Enabled = !busy;
        if (message is not null) _status.Text = message;
    }

    static Panel SectionHeading(string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = UiTheme.SurfaceAlt, Padding = new Padding(12, 5, 12, 4) };
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8F), Padding = new Padding(4, 2, 0, 0) });
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Left, Width = 112, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) });
        return panel;
    }
}
