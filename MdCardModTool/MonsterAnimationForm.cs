namespace MdCardModTool;

public sealed class MonsterAnimationForm : Form
{
    const string AutomaticQuality = "自动高清（推荐）";
    readonly string _gameRoot;
    readonly MonsterAnimationService _service = new();
    readonly TextBox _cardId = new() { Width = 130, PlaceholderText = "例如 4007" };
    readonly Label _resourceStatus = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _sourceStatus = new() { Dock = DockStyle.Top, Height = 62, ForeColor = UiTheme.Text, Padding = new Padding(0, 7, 0, 7) };
    readonly AnimationPreviewCanvas _preview = new() { Dock = DockStyle.Fill };
    readonly TrackBar _timeline = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None, Enabled = false };
    readonly NumericUpDown _fps = new() { Minimum = 1, Maximum = 60, Value = 15, Width = 88 };
    readonly NumericUpDown _maxFrames = new() { Minimum = 10, Maximum = 600, Value = MonsterAnimationMedia.DefaultMaxFrames, Increment = 10, Width = 88 };
    readonly NumericUpDown _startSeconds = new() { Minimum = 0, Maximum = 3600, Value = 0, DecimalPlaces = 1, Increment = 0.5M, Width = 88 };
    readonly NumericUpDown _scale = new() { Minimum = 10, Maximum = 500, Value = 100, Increment = 5, Width = 88 };
    readonly ComboBox _frameEdge = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly ComboBox _atlasEdge = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly CheckBox _removeGreenScreen = new() { Text = "自动去绿幕", AutoSize = true, ForeColor = UiTheme.Text, BackColor = Color.Transparent };
    readonly Label _frameLabel = new() { AutoSize = true, ForeColor = UiTheme.Muted, Padding = new Padding(8, 8, 0, 0) };
    readonly Button _play;
    readonly Button _apply;
    readonly System.Windows.Forms.Timer _timer = new();
    readonly List<Bitmap> _previewFrames = [];
    ExtractedAnimation? _media;
    MonsterAnimationSet? _set;
    int _previewFramesPerSecond = 15;
    bool _playing;
    bool _busy;
    bool _automaticQuality;
    int _resolvedFrameEdge;
    int _mediaWidth;
    int _mediaHeight;

    public MonsterAnimationForm(string gameRoot, string? initialCardId = null)
    {
        _gameRoot = gameRoot;
        UiTheme.ApplyDarkTitleBar(this);
        Text = "怪兽召唤动画替换";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1160, 820);
        MinimumSize = new Size(940, 680);
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        AllowDrop = true;
        UiTheme.StyleTextBox(_cardId);
        UiTheme.StyleComboBox(_frameEdge);
        UiTheme.StyleComboBox(_atlasEdge);
        _frameEdge.Items.AddRange([AutomaticQuality, "512", "768", "1024", "1280", "1600", "1920", "2048"]); _frameEdge.SelectedItem = AutomaticQuality;
        _atlasEdge.Items.AddRange(["4096", "8192", "16384"]); _atlasEdge.SelectedItem = "8192";
        _cardId.Text = initialCardId?.All(char.IsAsciiDigit) == true ? initialCardId : "";
        _cardId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LocateAsync(); } };
        _timeline.ValueChanged += (_, _) => { if (!_busy) ShowFrame(_timeline.Value); };
        _fps.ValueChanged += (_, _) => { if (_media is not null) SetPreviewRate((int)_fps.Value); UpdateSourceStatus(); };
        _scale.ValueChanged += (_, _) => { _preview.AnimationScale = (float)_scale.Value / 100f; _preview.ScalePercent = (int)_scale.Value; _preview.Invalidate(); };
        _timer.Interval = 1000 / (int)_fps.Value;
        _timer.Tick += (_, _) => AdvanceFrame();
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await LoadMediaAsync(files[0]); };

        var locate = UiTheme.Button("定位 6 个资源", async (_, _) => await LocateAsync(), ButtonTone.Primary);
        var rebuild = UiTheme.Button("重建动画映射", async (_, _) => await RebuildIndexAsync());
        var cardRow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18, 8, 18, 8), ColumnCount = 5, RowCount = 1 };
        cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cardRow.Controls.Add(Label("卡号", UiTheme.Gold), 0, 0); cardRow.Controls.Add(_cardId, 1, 0); cardRow.Controls.Add(locate, 2, 0); cardRow.Controls.Add(_resourceStatus, 3, 0); cardRow.Controls.Add(rebuild, 4, 0);

        var choose = UiTheme.Button("选择 GIF / 视频", async (_, _) => await ChooseMediaAsync(), ButtonTone.Primary);
        _play = UiTheme.Button("播放", (_, _) => TogglePlay());
        _apply = UiTheme.Button("写入两套动画资源", async (_, _) => await ApplyAsync(), ButtonTone.Gold); _apply.Enabled = false;
        var restore = UiTheme.Button("还原该卡动画", async (_, _) => await RestoreAsync(), ButtonTone.Danger);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 92, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 4), BackColor = UiTheme.Surface };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var button in new[] { choose, _play, _apply, restore }) { button.AutoSize = false; button.Dock = DockStyle.Fill; button.Margin = new Padding(3); }
        buttons.Controls.Add(choose, 0, 0); buttons.Controls.Add(_play, 1, 0); buttons.Controls.Add(_apply, 0, 1); buttons.Controls.Add(restore, 1, 1);

        var options = new TableLayoutPanel { Dock = DockStyle.Top, Height = 244, ColumnCount = 2, RowCount = 7, Padding = new Padding(0, 4, 0, 4), BackColor = UiTheme.Surface };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        AddOption(options, 0, "帧率 / 游戏速度", _fps);
        AddOption(options, 1, "视频起始秒", _startSeconds);
        AddOption(options, 2, "最多读取帧数", _maxFrames);
        AddOption(options, 3, "画质 / 单帧最长边", _frameEdge);
        AddOption(options, 4, "单张图集上限", _atlasEdge);
        AddOption(options, 5, "全游戏画面占比（实时）%", _scale);
        AddOption(options, 6, "绿幕背景透明化", _removeGreenScreen);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Muted,
            Text = "100% 对应 Master Duel 完整 16:9 游戏画布（4800×2700），不再受原怪兽骨骼范围限制；调整占比会立即在左侧按全画布比例预览。\n\n“自动高清”会先快速探测实际帧数，再选择图集能容纳的最高画质。勾选“自动去绿幕”后，纯绿色背景会变为透明并柔化边缘。\n\n工具会写入原卡的全部动画名，兼容 animation_USP。仅能替换原本已有召唤动画的卡。",
            Padding = new Padding(0, 10, 0, 0)
        };
        var side = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18) };
        side.Controls.Add(note); side.Controls.Add(_sourceStatus); side.Controls.Add(options); side.Controls.Add(buttons);

        var timelineRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 2, Padding = new Padding(8, 5, 8, 5), BackColor = UiTheme.SurfaceAlt };
        timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timelineRow.Controls.Add(_timeline, 0, 0); timelineRow.Controls.Add(_frameLabel, 1, 0);
        var previewPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(1) };
        previewPanel.Controls.Add(_preview); previewPanel.Controls.Add(timelineRow);

        var body = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8, FixedPanel = FixedPanel.Panel2, BackColor = UiTheme.Window };
        body.Panel1.Padding = new Padding(14, 14, 7, 14); body.Panel2.Padding = new Padding(7, 14, 14, 14);
        body.Panel1.Controls.Add(previewPanel); body.Panel2.Controls.Add(side);

        var banner = new GradientBanner { Dock = DockStyle.Fill, Padding = new Padding(22, 10, 22, 8) };
        banner.Controls.Add(new Label { Text = "MONSTER ANIMATION LAB", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 16F), ForeColor = UiTheme.Text, BackColor = Color.Transparent });
        banner.Controls.Add(new Label { Text = "GIF / VIDEO  →  SPINE SEQUENCE  →  MASTER DUEL", Dock = DockStyle.Bottom, Height = 22, ForeColor = UiTheme.Primary, BackColor = Color.Transparent });
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = UiTheme.Window };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(banner, 0, 0); root.Controls.Add(cardRow, 0, 1); root.Controls.Add(body, 0, 2); Controls.Add(root);

        FormClosed += (_, _) => DisposeMedia();
        Shown += async (_, _) =>
        {
            const int panel1Minimum = 500;
            const int panel2Minimum = 350;
            var maximum = body.Width - panel2Minimum - body.SplitterWidth;
            if (maximum >= panel1Minimum)
            {
                body.SplitterDistance = Math.Clamp(body.Width - 380, panel1Minimum, maximum);
                body.Panel1MinSize = panel1Minimum;
                body.Panel2MinSize = panel2Minimum;
            }
            if (_cardId.Text.Length > 0) await LocateAsync();
        };
    }

    static Label Label(string text, Color color) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = color, Padding = new Padding(0, 7, 8, 0) };

    static void AddOption(TableLayoutPanel panel, int row, string title, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Text }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(control, 1, row);
    }

    async Task LocateAsync()
    {
        var cardId = _cardId.Text.Trim();
        if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0) { MessageBox.Show(this, "请输入纯数字卡号。", Text); return; }
        try
        {
            SetBusy(true, "正在按卡号计算 SD / highend_hd 的 6 个资源路径…");
            _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
            _resourceStatus.Text = _set.Assets.Count == 0 ? "本机没有定位到这张卡的召唤动画" : _set.CountSummary + (_set.IsComplete ? "  · 可替换" : "  · 资源不完整");
            _resourceStatus.ForeColor = _set.IsComplete ? UiTheme.Primary : Color.OrangeRed;
            _apply.Enabled = _set.IsComplete && _media is not null;
            if (_set.IsComplete)
            {
                var template = await Task.Run(() => _service.ReadTemplate(_gameRoot, _set));
                _resourceStatus.Text += $"  · 动画名 {string.Join(" / ", template.EffectiveAnimationNames)}";
                if (_media is null) await LoadCurrentAnimationPreviewAsync(_set);
            }
            else if (_media is null)
            {
                DisposeMedia();
                _sourceStatus.Text = "这张卡没有定位到完整的两套动画资源";
                _preview.StatusText = "NO COMPLETE ANIMATION\n\n未定位到可预览的完整动画资源";
                _preview.Invalidate();
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "定位动画失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task RebuildIndexAsync()
    {
        if (MessageBox.Show(this, "正常情况下会按卡号直接计算 SD / highend_hd 的资源路径，不需要扫描。\n\n这个高级修复操作会扫描 LocalData 与 StreamingAssets 的全部 Bundle，可能需要数分钟；只有游戏更新后路径规则变化时才需要。继续？", "重建动画映射", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在扫描动画资源名…");
            await Task.Run(() => MonsterAnimationIndexService.Rebuild(_gameRoot, (done, total, found) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(() => _resourceStatus.Text = $"扫描 {done:N0}/{total:N0} · 已找到 {found:N0} 个动画资源");
            }));
            await LocateAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "重建动画映射失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task ChooseMediaAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 GIF 或视频",
            Filter = "动画与视频|*.gif;*.mp4;*.webm;*.mov;*.avi;*.mkv;*.m4v;*.apng|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) await LoadMediaAsync(dialog.FileName);
    }

    async Task LoadMediaAsync(string path)
    {
        ExtractedAnimation? loadedMedia = null;
        List<Bitmap>? loadedFrames = null;
        var resetToFullGameCanvas = _media is null;
        var automaticQuality = _frameEdge.Text == AutomaticQuality;
        var removeGreenScreen = _removeGreenScreen.Checked;
        var resolvedFrameEdge = 0;
        var mediaWidth = 0;
        var mediaHeight = 0;
        try
        {
            if (automaticQuality)
            {
                SetBusy(true, "正在低清探测实际帧数与画面比例…");
                using var probe = await MonsterAnimationMedia.ExtractAsync(path, (int)_fps.Value, (int)_maxFrames.Value, 128, (double)_startSeconds.Value);
                using var probeFrame = probe.LoadFrame(0);
                resolvedFrameEdge = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(probe.FramePaths.Count, probeFrame.Width, probeFrame.Height, int.Parse(_atlasEdge.Text));
                SetBusy(true, $"检测到 {probe.FramePaths.Count:N0} 帧，正在按 {resolvedFrameEdge} px 自动高清抽帧…");
            }
            else
            {
                resolvedFrameEdge = int.Parse(_frameEdge.Text);
                SetBusy(true, $"正在按 {resolvedFrameEdge} px 用 FFmpeg 抽取画面…");
            }

            loadedMedia = await MonsterAnimationMedia.ExtractAsync(path, (int)_fps.Value, (int)_maxFrames.Value, resolvedFrameEdge, (double)_startSeconds.Value, removeGreenScreen);
            using (var firstFrame = loadedMedia.LoadFrame(0)) { mediaWidth = firstFrame.Width; mediaHeight = firstFrame.Height; }
            var previewEdge = Math.Min(512, resolvedFrameEdge);
            var mediaForPreview = loadedMedia;
            loadedFrames = await Task.Run(() => Enumerable.Range(0, mediaForPreview.FramePaths.Count).Select(i => mediaForPreview.LoadFrame(i, previewEdge)).ToList());
            DisposeMedia();
            _media = loadedMedia; loadedMedia = null;
            _automaticQuality = automaticQuality;
            _resolvedFrameEdge = resolvedFrameEdge;
            _mediaWidth = mediaWidth;
            _mediaHeight = mediaHeight;
            _previewFrames.AddRange(loadedFrames); loadedFrames = null;
            if (resetToFullGameCanvas) _scale.Value = 100;
            SetPreviewRate((int)_fps.Value);
            _timeline.Maximum = Math.Max(0, _previewFrames.Count - 1); _timeline.Value = 0; _timeline.Enabled = _previewFrames.Count > 1;
            ShowFrame(0); UpdateSourceStatus();
            _apply.Enabled = _set?.IsComplete == true;
            if (!_playing) TogglePlay();
        }
        catch (Exception ex)
        {
            loadedMedia?.Dispose();
            if (loadedFrames is not null) foreach (var frame in loadedFrames) frame.Dispose();
            MessageBox.Show(this, ex.Message, "无法读取动画源", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    void UpdateSourceStatus()
    {
        if (_media is null) { _sourceStatus.Text = "拖入或选择 GIF / 视频后在左侧预览"; return; }
        var quality = _automaticQuality ? $"自动高清，上限 {_resolvedFrameEdge} px" : $"固定上限 {_resolvedFrameEdge} px";
        var transparency = _media.GreenScreenRemoved ? " · 绿幕已透明" : "";
        _sourceStatus.Text = $"{Path.GetFileName(_media.SourcePath)}\n{_media.FramePaths.Count:N0} 帧 · 当前 {(int)_fps.Value} FPS · {_media.FramePaths.Count / (double)_fps.Value:0.00} 秒 · {_mediaWidth}×{_mediaHeight}（{quality}）{transparency}";
    }

    async Task LoadCurrentAnimationPreviewAsync(MonsterAnimationSet set)
    {
        _resourceStatus.Text += "  · 正在读取当前动画预览…";
        var current = await Task.Run(() => MonsterAnimationCurrentPreview.TryLoad(set));
        if (current is null)
        {
            DisposeMedia();
            _sourceStatus.Text = "当前为原版多骨骼 / 网格 Spine 动画\n不会把拆散的关节图集冒充预览；导入 GIF / 视频后可实时查看替换效果";
            _preview.StatusText = "CURRENT SPINE ANIMATION\n\n原版复杂骨骼动画已定位\n拖入 GIF / 视频查看待替换效果";
            _preview.Invalidate();
            _resourceStatus.Text = _resourceStatus.Text.Replace("  · 正在读取当前动画预览…", "  · 原版骨骼动画");
            return;
        }
        var frames = current.Frames.ToList(); current.Frames.Clear();
        var fps = current.FramesPerSecond; var animationName = current.AnimationName; var scalePercent = current.ScalePercent;
        current.Dispose();
        DisposeMedia();
        _previewFrames.AddRange(frames);
        _scale.Value = scalePercent;
        SetPreviewRate(fps);
        _timeline.Maximum = Math.Max(0, _previewFrames.Count - 1); _timeline.Value = 0; _timeline.Enabled = _previewFrames.Count > 1;
        _preview.StatusText = "";
        ShowFrame(0);
        _sourceStatus.Text = $"当前游戏动画 · {animationName}\n{_previewFrames.Count:N0} 帧 · {fps} FPS · {_previewFrames.Count / (double)fps:0.00} 秒 · 全画布 {scalePercent}%";
        _resourceStatus.Text = _resourceStatus.Text.Replace("  · 正在读取当前动画预览…", "  · 正在预览当前动画");
        if (!_playing) TogglePlay();
    }

    void SetPreviewRate(int framesPerSecond)
    {
        _previewFramesPerSecond = Math.Clamp(framesPerSecond, 1, 60);
        _timer.Interval = Math.Max(15, 1000 / _previewFramesPerSecond);
    }

    void TogglePlay()
    {
        if (_previewFrames.Count < 2) return;
        _playing = !_playing; _play.Text = _playing ? "暂停" : "播放"; _timer.Enabled = _playing;
    }

    void AdvanceFrame()
    {
        if (_previewFrames.Count == 0) return;
        _busy = true; _timeline.Value = (_timeline.Value + 1) % _previewFrames.Count; _busy = false; ShowFrame(_timeline.Value);
    }

    void ShowFrame(int index)
    {
        if (index < 0 || index >= _previewFrames.Count) return;
        _preview.Frame = _previewFrames[index]; _preview.Invalidate();
        _frameLabel.Text = $"{index + 1} / {_previewFrames.Count}";
    }

    async Task ApplyAsync()
    {
        if (_set?.IsComplete != true || _media is null) { MessageBox.Show(this, "先定位完整的 6 个动画资源，并导入 GIF 或视频。", Text); return; }
        if (!EnsureGameClosed()) return;
        if (MessageBox.Show(this, $"将同时修改卡号 {_set.CardId} 的两套 Texture2D、Atlas 与 JS，共 6 个资源。\n\n所有 Bundle 会先备份并纳入 Mod 管理；任一步失败会自动回滚。继续？", "确认替换召唤动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在生成单张 Spine 图集…");
            var template = await Task.Run(() => _service.ReadTemplate(_gameRoot, _set));
            using var built = await Task.Run(() => MonsterAnimationBuilder.Build(_media.FramePaths, _set.CardId, (int)_fps.Value, (int)_scale.Value, template, int.Parse(_atlasEdge.Text)));
            _resourceStatus.Text = $"图集 {built.AtlasWidth}×{built.AtlasHeight} · 正在 DXT5 压缩并写入 6 个 Bundle…";
            await Task.Run(() => _service.Apply(_gameRoot, _set, built));
            _resourceStatus.ForeColor = UiTheme.Primary;
            _resourceStatus.Text = $"替换完成 · {built.FrameCount} 帧 / {built.FramesPerSecond} FPS · 全画布 {(int)_scale.Value}% · 图集 {built.AtlasWidth}×{built.AtlasHeight}";
            MessageBox.Show(this, "两套召唤动画资源已经全部替换并备份。\n\n请完全退出并重新启动 Master Duel 后测试召唤演出与商店预览。", "动画替换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "动画替换失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task RestoreAsync()
    {
        if (_set is null || _set.Assets.Count == 0) { MessageBox.Show(this, "先输入卡号并定位动画资源。", Text); return; }
        if (!EnsureGameClosed()) return;
        if (MessageBox.Show(this, $"确认把卡号 {_set.CardId} 的动画 Bundle 全部还原为首次替换前的版本？", "还原动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在还原动画 Bundle…");
            var count = await Task.Run(() => _service.Restore(_gameRoot, _set));
            _resourceStatus.Text = count == 0 ? "没有找到该卡的动画备份" : $"已还原 {count} 个动画 Bundle";
            MessageBox.Show(this, count == 0 ? "该卡尚未由本工具替换，或备份目录不存在。" : $"已还原 {count} 个 Bundle。", "动画还原", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        if (message is not null) _resourceStatus.Text = message;
        _cardId.Enabled = !busy;
        _apply.Enabled = !busy && _set?.IsComplete == true && _media is not null;
    }

    bool EnsureGameClosed()
    {
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("masterduel").Length == 0) return true;
        }
        catch { return true; }
        MessageBox.Show(this, "Master Duel 仍在运行。请先完全退出游戏，再替换或还原动画 Bundle，避免文件占用或更新丢失。", "请先退出游戏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    void DisposeMedia()
    {
        _timer.Stop(); _playing = false; _play.Text = "播放"; _preview.Frame = null;
        foreach (var frame in _previewFrames) frame.Dispose(); _previewFrames.Clear();
        _media?.Dispose(); _media = null;
        _timeline.Value = 0; _timeline.Maximum = 0; _timeline.Enabled = false; _frameLabel.Text = "";
    }
}

public sealed class AnimationPreviewCanvas : Control
{
    public Bitmap? Frame { get; set; }
    public float AnimationScale { get; set; } = 1f;
    public int ScalePercent { get; set; } = 100;
    public string StatusText { get; set; } = "DROP GIF / VIDEO HERE\n\n拖入 GIF 或视频开始预览";

    public AnimationPreviewCanvas()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.SurfaceAlt;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        const int cell = 18;
        using var dark = new SolidBrush(Color.FromArgb(24, 32, 46));
        using var light = new SolidBrush(Color.FromArgb(36, 48, 66));
        for (var y = 0; y < Height; y += cell)
            for (var x = 0; x < Width; x += cell)
                g.FillRectangle(((x / cell + y / cell) & 1) == 0 ? dark : light, x, y, cell, cell);
        if (Frame is null)
        {
            TextRenderer.DrawText(g, StatusText, Font, ClientRectangle, UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        var available = new RectangleF(16, 16, Math.Max(1, ClientSize.Width - 32), Math.Max(1, ClientSize.Height - 32));
        var gameAspect = (float)(MonsterAnimationBuilder.GameCanvasWidth / MonsterAnimationBuilder.GameCanvasHeight);
        var viewportWidth = available.Width;
        var viewportHeight = viewportWidth / gameAspect;
        if (viewportHeight > available.Height) { viewportHeight = available.Height; viewportWidth = viewportHeight * gameAspect; }
        var viewport = new RectangleF(available.X + (available.Width - viewportWidth) / 2f, available.Y + (available.Height - viewportHeight) / 2f, viewportWidth, viewportHeight);
        var fit = Math.Min(viewport.Width / Frame.Width, viewport.Height / Frame.Height) * AnimationScale;
        var width = Frame.Width * fit;
        var height = Frame.Height * fit;
        var target = new RectangleF(viewport.X + (viewport.Width - width) / 2f, viewport.Y + (viewport.Height - height) / 2f, width, height);
        var state = g.Save();
        g.SetClip(viewport);
        g.DrawImage(Frame, target);
        g.Restore(state);
        using var border = new Pen(Color.FromArgb(130, UiTheme.Primary), 1f);
        g.DrawRectangle(border, viewport.X, viewport.Y, viewport.Width, viewport.Height);
        TextRenderer.DrawText(g, $"全游戏画布 16:9 · {ScalePercent}%", Font, Rectangle.Round(viewport), UiTheme.Primary, TextFormatFlags.Top | TextFormatFlags.Right | TextFormatFlags.NoPadding);
    }
}
