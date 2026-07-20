namespace MdCardModTool;

/// <summary>给单张超框卡选择／导入卡框，并把卡框与透明高图合成为游戏实际读取的 704×1024 贴图。</summary>
public sealed class OverFrameFrameEditorForm : Form
{
    readonly string _gameRoot;
    readonly TexRef _art;
    readonly ushort _cardId;
    readonly byte[]? _initialArt;
    readonly string? _initialFrameKey;
    readonly ModEngine _engine = new();
    readonly OverFrameService _overFrames = new();
    readonly ComboBox _frames = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 22, 34) };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 7, 10, 7), ForeColor = Color.Gainsboro };
    readonly Dictionary<string, byte[]> _frameCache = new(StringComparer.OrdinalIgnoreCase);
    byte[]? _artBytes;
    byte[]? _currentFrameBytes;
    byte[]? _previewBytes;
    string? _currentFrameKey;
    string? _previewFrameKey;
    int _generation;
    bool _loading;

    sealed class FrameChoice
    {
        public TexRef? Texture { get; init; }
        public string? FilePath { get; init; }
        public required string Key { get; init; }
        public required string DisplayName { get; init; }
        public bool IsCustom => FilePath is not null;
        public override string ToString() => DisplayName;
    }

    public string AppliedFrameName { get; private set; } = "";

    public OverFrameFrameEditorForm(string gameRoot, TexRef art, IEnumerable<TexRef> frames, byte[]? initialArt = null, string? initialFrameKey = null)
    {
        UiTheme.ApplyDarkTitleBar(this);
        if (!ushort.TryParse(art.CardKey, out _cardId)) throw new ArgumentException("所选资源没有有效卡号。", nameof(art));
        _gameRoot = gameRoot; _art = art; _initialArt = initialArt; _initialFrameKey = initialFrameKey;
        Text = $"卡框选择与编辑 · {_cardId}"; StartPosition = FormStartPosition.CenterParent; Size = new Size(980, 860); MinimumSize = new Size(760, 650);
        BackColor = Color.FromArgb(15, 22, 35); ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 9F);

        foreach (var texture in frames.Where(x => x.Width == FrameComposer.Width && x.Height == FrameComposer.Height).OrderBy(x => x.Name))
        {
            var suffix = $" · {CardFrameCatalog.FriendlyName(texture.Name)}";
            _frames.Items.Add(new FrameChoice { Texture = texture, Key = texture.Name, DisplayName = $"{texture.Name}{suffix}" });
        }

        var exportFrame = Button("导出卡框 PNG", async (_, _) => await ExportFrameAsync());
        var importFrame = Button("导入自定义卡框", async (_, _) => await ImportFrameAsync());
        var changeArt = Button("更换透明高图", async (_, _) => await ChangeArtAsync());
        var exportPreview = Button("导出合成预览", (_, _) => ExportPreview());
        var apply = Button("应用到游戏", async (_, _) => await ApplyAsync(), true);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(12, 10, 12, 8), ColumnCount = 3, RowCount = 3, BackColor = Color.FromArgb(20, 28, 45) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "卡框", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(160, 195, 255), Padding = new Padding(0, 5, 6, 0) }, 0, 0);
        top.Controls.Add(_frames, 1, 0); top.Controls.Add(apply, 2, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        actions.Controls.Add(exportFrame); actions.Controls.Add(importFrame); actions.Controls.Add(changeArt); actions.Controls.Add(exportPreview);
        top.Controls.Add(actions, 0, 1); top.SetColumnSpan(actions, 3);
        var help = new Label { Text = "卡框只合成进当前卡，不影响其他卡；透明原画会单独保存。自定义卡框应保留插图窗口透明，避免盖住原画。", AutoSize = true, ForeColor = Color.Gainsboro };
        top.Controls.Add(help, 0, 2); top.SetColumnSpan(help, 3);

        Controls.Add(_preview); Controls.Add(_status); Controls.Add(top);
        _frames.SelectedIndexChanged += async (_, _) => { if (!_loading) await RenderAsync(); };
        Shown += async (_, _) => await LoadAsync();
        FormClosed += (_, _) => _preview.Image?.Dispose();
    }

    static Button Button(string text, EventHandler click, bool accent = false)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4, 1, 0, 1), FlatStyle = FlatStyle.Flat, BackColor = accent ? Color.FromArgb(46, 102, 204) : Color.FromArgb(36, 50, 76), ForeColor = Color.White, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = accent ? Color.FromArgb(93, 148, 255) : Color.FromArgb(71, 91, 128); button.Click += click; return button;
    }

    async Task LoadAsync()
    {
        UseWaitCursor = true; _loading = true; _status.Text = "正在读取透明高图与卡框…";
        try
        {
            var capturedCurrent = false;
            if (_initialArt is not null) await Task.Run(() => OverFrameArtStore.SaveArt(_gameRoot, _cardId, _initialArt));
            var artPath = OverFrameArtStore.ArtPath(_gameRoot, _cardId);
            if (!File.Exists(artPath))
            {
                if (_art.Width != FrameComposer.Width || _art.Height != FrameComposer.Height)
                    throw new InvalidOperationException("还没有 704×1024 透明高图。请点击“更换透明高图”选择原画。\n\n如果是新卡，请从主界面使用“超框替换”。");
                var current = await Task.Run(() => _engine.DecodePng(_art));
                await Task.Run(() => OverFrameArtStore.SaveArt(_gameRoot, _cardId, current));
                capturedCurrent = true;
            }
            _artBytes = await File.ReadAllBytesAsync(artPath);

            var settings = OverFrameArtStore.ReadSettings(_gameRoot, _cardId);
            var custom = OverFrameArtStore.CustomFramePath(_gameRoot, _cardId);
            if (File.Exists(custom)) AddOrSelectCustom(custom, select: false);
            var wanted = !string.IsNullOrWhiteSpace(_initialFrameKey) ? _initialFrameKey : settings.UsesCustomFrame ? "__custom__" : settings.FrameKey;
            var index = Enumerable.Range(0, _frames.Items.Count).FirstOrDefault(i => (_frames.Items[i] as FrameChoice)?.Key == wanted, -1);
            if (index < 0) index = Enumerable.Range(0, _frames.Items.Count).FirstOrDefault(i => (_frames.Items[i] as FrameChoice)?.Key == "card_frame01", -1);
            if (index < 0 && _frames.Items.Count > 0) index = 0;
            _frames.SelectedIndex = index;
            if (_frames.Items.Count == 0) throw new InvalidOperationException("没有可用的 704×1024 card_frame，请回主界面重建索引。\n自定义卡框也可通过“导入自定义卡框”加入。");
            _status.Text = capturedCurrent ? "已把当前游戏图保存为可编辑原画；若它本身已有卡框，请点“更换透明高图”换回无框原图。" : "透明原画已载入。选择卡框即可实时预览。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "卡框编辑器载入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "载入失败：" + ex.Message; }
        finally { _loading = false; UseWaitCursor = false; }
        if (_artBytes is not null && _frames.SelectedItem is FrameChoice) await RenderAsync();
    }

    async Task RenderAsync()
    {
        if (_artBytes is null || _frames.SelectedItem is not FrameChoice choice) return;
        var generation = ++_generation; _currentFrameBytes = null; _currentFrameKey = null; _previewBytes = null; _previewFrameKey = null; UseWaitCursor = true; _status.Text = "正在合成预览…";
        try
        {
            var frameBytes = await GetFrameBytesAsync(choice);
            var preview = await Task.Run(() => FrameComposer.Compose(_artBytes, frameBytes));
            if (generation != _generation) return;
            _currentFrameBytes = frameBytes; _currentFrameKey = choice.Key; _previewBytes = preview; _previewFrameKey = choice.Key;
            var image = FrameComposer.PreviewBitmap(preview); var old = _preview.Image; _preview.Image = image; old?.Dispose();
            _status.Text = $"{_cardId}  +  {choice.DisplayName}  ·  {FrameComposer.Width}×{FrameComposer.Height}";
        }
        catch (Exception ex) { _status.Text = "预览失败：" + ex.Message; }
        finally { UseWaitCursor = false; }
    }

    async Task<byte[]> GetFrameBytesAsync(FrameChoice choice)
    {
        if (_frameCache.TryGetValue(choice.Key, out var cached)) return cached;
        var bytes = choice.IsCustom ? await File.ReadAllBytesAsync(choice.FilePath!) : await Task.Run(() => _engine.DecodePng(choice.Texture!));
        _frameCache[choice.Key] = bytes;
        return bytes;
    }

    async Task ImportFrameAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "PNG 图片|*.png|图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", Title = "导入 704×1024 自定义卡框" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var path = await Task.Run(() => OverFrameArtStore.SaveCustomFrame(_gameRoot, _cardId, dialog.FileName));
            _frameCache.Remove("__custom__"); AddOrSelectCustom(path, select: true);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "自定义卡框无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void AddOrSelectCustom(string path, bool select)
    {
        var existing = _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault(x => x.Key == "__custom__");
        if (existing is null)
        {
            existing = new FrameChoice { Key = "__custom__", FilePath = path, DisplayName = "自定义卡框 · " + Path.GetFileName(path) };
            _frames.Items.Add(existing);
        }
        if (select) _frames.SelectedItem = existing;
    }

    async Task ChangeArtAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", Title = "选择透明高图（不要预先带卡框）" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var builtInFrames = _frames.Items.Cast<object>().OfType<FrameChoice>().Where(x => x.Texture is not null).Select(x => x.Texture!).ToArray();
            var selectedKey = (_frames.SelectedItem as FrameChoice)?.Key;
            using var crop = new ImageCropForm(dialog.FileName, FrameComposer.Width, FrameComposer.Height, $"更换透明高图 {_cardId}", builtInFrames, selectedKey, fullCardOverlay: true);
            if (crop.ShowDialog(this) != DialogResult.OK || crop.OutputPng is null) return;
            await Task.Run(() => OverFrameArtStore.SaveArt(_gameRoot, _cardId, crop.OutputPng));
            _artBytes = await File.ReadAllBytesAsync(OverFrameArtStore.ArtPath(_gameRoot, _cardId));
            if (crop.SelectedFrameKey.Length > 0)
            {
                var choice = _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault(x => x.Key.Equals(crop.SelectedFrameKey, StringComparison.OrdinalIgnoreCase));
                if (choice is not null) _frames.SelectedItem = choice;
            }
            await RenderAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "透明高图无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    async Task ExportFrameAsync()
    {
        if (_frames.SelectedItem is not FrameChoice choice) return;
        try
        {
            var bytes = _currentFrameKey == choice.Key && _currentFrameBytes is not null ? _currentFrameBytes : await GetFrameBytesAsync(choice);
            using var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"{choice.Key}_可编辑.png" };
            if (dialog.ShowDialog(this) == DialogResult.OK) await File.WriteAllBytesAsync(dialog.FileName, bytes);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出卡框失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void ExportPreview()
    {
        if (_preview.Image is null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"{_cardId}_超框合成预览.png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _preview.Image.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
    }

    async Task ApplyAsync()
    {
        if (_previewBytes is null || _frames.SelectedItem is not FrameChoice choice || _previewFrameKey != choice.Key) { MessageBox.Show(this, "当前卡框预览尚未生成完成，请稍候再应用。", Text); return; }
        if (MessageBox.Show(this, $"把“{choice.DisplayName}”合成进卡号 {_cardId} 的高图并写入游戏？\n\n只修改这张卡的 Bundle，不会改全局 card_frame；原 Bundle 与透明原画均已保留。", "确认应用卡框", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true; _status.Text = "正在定位 LocalData 超框表…";
            await Task.Run(() => _overFrames.FindGate(_gameRoot, (done, total) => BeginInvoke(() => _status.Text = $"首次定位超框表：{done:N0}/{total:N0} Bundle…")));
            _status.Text = "正在写入单卡卡框合成图…";
            await Task.Run(() => _engine.Replace(_art, _previewBytes, Path.Combine(_gameRoot, "_MD卡图备份", _art.SourceKind)));
            try { await Task.Run(() => _overFrames.EnableOrUpdate(_gameRoot, _cardId, _cardId)); }
            catch (Exception gateError) { throw new InvalidOperationException("卡框合成图已经写入，但超框登记失败。请在主界面的“超框表”中为该卡重试启用。\n\n" + gateError.Message, gateError); }
            OverFrameArtStore.SaveSettings(_gameRoot, _cardId, new OverFrameFrameSettings(choice.Key, choice.IsCustom));
            _art.Category = "超框卡图"; AppliedFrameName = choice.DisplayName;
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "应用卡框失败", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "应用失败：" + ex.Message; }
        finally { UseWaitCursor = false; }
    }
}
