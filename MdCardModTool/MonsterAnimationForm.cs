namespace MdCardModTool;

public sealed class MonsterAnimationForm : Form
{
    const string AutomaticQuality = "自动高清（推荐）";
    readonly string _gameRoot;
    readonly MonsterAnimationService _service = new();
    readonly MonsterAnimationBorrowService _borrowService = new();
    readonly TextBox _cardId = new() { Width = 130, PlaceholderText = "例如 4007" };
    readonly TextBox _sourceCardId = new() { Width = 82, PlaceholderText = "源卡号" };
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
    readonly CheckBox _removeGreenScreen = new() { Text = $"启用（{MonsterAnimationMedia.GreenScreenKeyHex}）", AutoSize = true, ForeColor = UiTheme.Text, BackColor = Color.Transparent };
    readonly Label _frameLabel = new() { AutoSize = true, ForeColor = UiTheme.Muted, Padding = new Padding(8, 8, 0, 0) };
    readonly Button _play;
    readonly Button _apply;
    readonly Button _copyOther;
    readonly Button _borrowOther;
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
        Size = new Size(1120, 780);
        MinimumSize = new Size(760, 580);
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        AllowDrop = true;
        UiTheme.StyleTextBox(_cardId);
        UiTheme.StyleTextBox(_sourceCardId);
        UiTheme.StyleComboBox(_frameEdge);
        UiTheme.StyleComboBox(_atlasEdge);
        _frameEdge.Items.AddRange([AutomaticQuality, "512", "768", "1024", "1280", "1600", "1920", "2048"]); _frameEdge.SelectedItem = AutomaticQuality;
        _atlasEdge.Items.AddRange(["4096", "8192", "16384"]); _atlasEdge.SelectedItem = "8192";
        _cardId.Text = initialCardId?.All(char.IsAsciiDigit) == true ? initialCardId : "";
        _cardId.TextChanged += (_, _) => UpdateApplyState();
        _cardId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LocateAsync(); } };
        _sourceCardId.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoadOtherCardAnimationAsync(); } };
        _timeline.ValueChanged += (_, _) => { if (!_busy) ShowFrame(_timeline.Value); };
        _fps.ValueChanged += (_, _) => { if (_media is not null) SetPreviewRate((int)_fps.Value); UpdateSourceStatus(); };
        _scale.ValueChanged += (_, _) => { _preview.AnimationScale = (float)_scale.Value / 100f; _preview.ScalePercent = (int)_scale.Value; _preview.Invalidate(); };
        _timer.Interval = 1000 / (int)_fps.Value;
        _timer.Tick += (_, _) => AdvanceFrame();
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await LoadMediaAsync(files[0]); };

        var locate = UiTheme.Button("定位 6 个资源", async (_, _) => await LocateAsync(), ButtonTone.Primary);
        var rebuild = UiTheme.Button("重建动画映射", async (_, _) => await RebuildIndexAsync());
        var cardRow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18, 6, 18, 5), ColumnCount = 4, RowCount = 2 };
        cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cardRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); cardRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cardRow.Controls.Add(Label("目标卡号", UiTheme.Gold), 0, 0); cardRow.Controls.Add(_cardId, 1, 0); cardRow.Controls.Add(locate, 2, 0); cardRow.Controls.Add(rebuild, 3, 0);
        cardRow.Controls.Add(_resourceStatus, 0, 1); cardRow.SetColumnSpan(_resourceStatus, 4);

        var choose = UiTheme.Button("选择 GIF / 视频", async (_, _) => await ChooseMediaAsync(), ButtonTone.Primary);
        _play = UiTheme.Button("播放", (_, _) => TogglePlay());
        _apply = UiTheme.Button("写入动画", async (_, _) => await ApplyAsync(), ButtonTone.Gold); _apply.Enabled = false;
        var restore = UiTheme.Button("还原该卡动画", async (_, _) => await RestoreAsync(), ButtonTone.Danger);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 92, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 4), BackColor = UiTheme.Surface };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var button in new[] { choose, _play, _apply, restore }) { button.AutoSize = false; button.Dock = DockStyle.Fill; button.Margin = new Padding(3); }
        buttons.Controls.Add(choose, 0, 0); buttons.Controls.Add(_play, 1, 0); buttons.Controls.Add(_apply, 0, 1); buttons.Controls.Add(restore, 1, 1);

        _copyOther = UiTheme.Button("预览并使用", async (_, _) => await LoadOtherCardAnimationAsync(), ButtonTone.Primary);
        _copyOther.AutoSize = false; _copyOther.Dock = DockStyle.Fill;
        _borrowOther = UiTheme.Button("高级：只读借用", async (_, _) => await BorrowOtherCardAnimationAsync(), ButtonTone.Neutral);
        _borrowOther.AutoSize = false; _borrowOther.Dock = DockStyle.Fill;
        var sourceRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _sourceCardId.Dock = DockStyle.Fill; sourceRow.Controls.Add(_sourceCardId, 0, 0); sourceRow.Controls.Add(_copyOther, 1, 0); sourceRow.Controls.Add(_borrowOther, 2, 0);
        var sourceTitle = new Label { Text = "使用其他卡的原版动画（可选）", Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Gold, TextAlign = ContentAlignment.BottomLeft };
        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 7, Padding = new Padding(0, 4, 0, 4), BackColor = UiTheme.Surface };
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
            AutoSize = true,
            MaximumSize = new Size(330, 0),
            Text = "直接拖入 GIF／视频即可。目标卡没有原动画时，“创建并写入动画”会自动复制一套独立模板，不需要先借用。\n\n100% 对应完整 16:9 游戏画布；调整占比会立即反映在左侧预览。高级只读借用仅用于原样复用其他卡演出。",
            Padding = new Padding(0, 10, 0, 12)
        };
        var scrollContent = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTheme.Surface, Padding = new Padding(14, 10, 14, 10) };
        scrollContent.Controls.Add(note); scrollContent.Controls.Add(sourceRow); scrollContent.Controls.Add(sourceTitle); scrollContent.Controls.Add(_sourceStatus); scrollContent.Controls.Add(options);
        var scrollBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Surface };
        scrollBody.Controls.Add(scrollContent);
        scrollContent.Width = 330;
        scrollBody.Resize += (_, _) => scrollContent.Width = Math.Max(260, scrollBody.ClientSize.Width - (scrollBody.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        var sideLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, RowCount = 2, ColumnCount = 1 };
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        sideLayout.Controls.Add(scrollBody, 0, 0); sideLayout.Controls.Add(buttons, 0, 1);
        var side = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(1) };
        side.Controls.Add(sideLayout);

        var timelineRow = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 2, Padding = new Padding(8, 5, 8, 5), BackColor = UiTheme.SurfaceAlt };
        timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        timelineRow.Controls.Add(_timeline, 0, 0); timelineRow.Controls.Add(_frameLabel, 1, 0);
        var previewPanel = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(1) };
        previewPanel.Controls.Add(_preview); previewPanel.Controls.Add(timelineRow);

        var body = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8, FixedPanel = FixedPanel.Panel2, BackColor = UiTheme.Window };
        body.Panel1.Padding = new Padding(14, 14, 7, 14); body.Panel2.Padding = new Padding(7, 14, 14, 14);
        body.Panel1.Controls.Add(previewPanel); body.Panel2.Controls.Add(side);

        var banner = new GradientBanner { Dock = DockStyle.Fill, Padding = new Padding(22, 7, 22, 6) };
        banner.Controls.Add(new Label { Text = "MONSTER ANIMATION LAB", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 16F), ForeColor = UiTheme.Text, BackColor = Color.Transparent });
        banner.Controls.Add(new Label { Text = "GIF / VIDEO  →  SPINE SEQUENCE  →  MASTER DUEL", Dock = DockStyle.Bottom, Height = 20, ForeColor = UiTheme.Primary, BackColor = Color.Transparent });
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = UiTheme.Window };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
            SetBusy(true, "正在按卡号计算 SD / highend_hd 路径并跟随 Prefab 依赖…");
            _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
            var borrowed = _borrowService.Find(_gameRoot, cardId);
            _resourceStatus.Text = _set.Assets.Count == 0 ? "本机没有定位到这张卡的召唤动画" : _set.CountSummary + (_set.IsComplete ? "  · 可替换" : "  · 资源不完整");
            if (borrowed is { IsIndependent: true }) _resourceStatus.Text += $"  · 已自动创建独立模板（来源 {borrowed.DonorCardId}）";
            else if (borrowed is not null) _resourceStatus.Text += $"  · 借用 {borrowed.DonorCardId}（只读）";
            _resourceStatus.ForeColor = borrowed is not null ? UiTheme.Gold : _set.IsComplete ? UiTheme.Primary : Color.OrangeRed;
            UpdateApplyState();
            if (_set.IsComplete)
            {
                var template = await Task.Run(() => _service.ReadTemplate(_gameRoot, _set));
                _resourceStatus.Text += $"  · 动画名 {string.Join(" / ", template.EffectiveAnimationNames)}";
                if (_media is null) await LoadCurrentAnimationPreviewAsync(_set);
            }
            else if (_media is null)
            {
                DisposeMedia();
                _sourceStatus.Text = "这张卡原本没有动画：直接拖入 GIF／视频，写入时会自动创建所需资源";
                _preview.StatusText = "DROP GIF / VIDEO HERE\n\n无原动画也可直接拖入，工具会自动创建并写入";
                _preview.Invalidate();
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "定位动画失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    async Task RebuildIndexAsync()
    {
        var cardId = _cardId.Text.Trim();
        if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0) { MessageBox.Show(this, "先输入要修复的纯数字卡号。", Text); return; }
        try
        {
            SetBusy(true, $"正在重算卡号 {cardId} 的资源路径与 Prefab 依赖…");
            // 只修复当前卡。旧版会扫描 LocalData 的数万个文件，在尾部异常 Bundle
            // 上可能长时间无响应；动画资源的哈希路径和依赖表本身已经足够定位。
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
            var targetCardId = _cardId.Text.Trim();
            if (targetCardId.Length > 0 && targetCardId.All(char.IsAsciiDigit) && (_set is null || _set.CardId != targetCardId))
                _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, targetCardId));
            UpdateApplyState();
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
        var name = string.IsNullOrWhiteSpace(_media.DisplayName) ? Path.GetFileName(_media.SourcePath) : _media.DisplayName;
        _sourceStatus.Text = $"{name}\n{_media.FramePaths.Count:N0} 帧 · 当前 {(int)_fps.Value} FPS · {_media.FramePaths.Count / (double)_fps.Value:0.00} 秒 · {_mediaWidth}×{_mediaHeight}（{quality}）{transparency}";
    }

    async Task LoadCurrentAnimationPreviewAsync(MonsterAnimationSet set)
    {
        _resourceStatus.Text += "  · 正在读取当前动画预览…";
        var current = await Task.Run(() => MonsterAnimationCurrentPreview.TryLoad(_gameRoot, set));
        if (current is null)
        {
            DisposeMedia();
            _sourceStatus.Text = "当前 Spine 资源已定位，但无法合成预览\n" + MonsterAnimationSpineRenderer.LastDiagnostic;
            _preview.StatusText = "SPINE PREVIEW UNAVAILABLE\n\n" + MonsterAnimationSpineRenderer.LastDiagnostic;
            _preview.Invalidate();
            _resourceStatus.Text = _resourceStatus.Text.Replace("  · 正在读取当前动画预览…", "  · 预览失败");
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

    async Task LoadOtherCardAnimationAsync()
    {
        var sourceCardId = _sourceCardId.Text.Trim();
        if (!sourceCardId.All(char.IsAsciiDigit) || sourceCardId.Length == 0)
        {
            MessageBox.Show(this, "请输入要复制动画的纯数字源卡号。", Text);
            return;
        }
        CurrentMonsterAnimationPreview? rendered = null;
        ExtractedAnimation? loadedMedia = null;
        try
        {
            SetBusy(true, $"正在定位源卡 {sourceCardId} 的 Spine 与多页图集…");
            var sourceSet = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, sourceCardId));
            if (!sourceSet.IsComplete) throw new InvalidDataException($"源卡 {sourceCardId} 的动画资源不完整：{sourceSet.CountSummary}");
            var requestedFps = (int)_fps.Value;
            var maximumFrames = (int)_maxFrames.Value;
            var automatic = _frameEdge.Text == AutomaticQuality;
            var edge = automatic ? 256 : int.Parse(_frameEdge.Text);
            if (automatic)
            {
                using var probe = await Task.Run(() => MonsterAnimationCurrentPreview.TryLoad(_gameRoot, sourceSet, 256, requestedFps, maximumFrames));
                if (probe is null) throw new InvalidDataException(MonsterAnimationSpineRenderer.LastDiagnostic);
                edge = Math.Min(1024, MonsterAnimationBuilder.ChooseAutomaticFrameEdge(
                    probe.Frames.Count,
                    probe.Frames[0].Width,
                    probe.Frames[0].Height,
                    int.Parse(_atlasEdge.Text)));
                SetBusy(true, $"源卡 {sourceCardId} 已定位，正在按 {edge} px 合成真实 Spine 动画…");
            }
            rendered = await Task.Run(() => MonsterAnimationCurrentPreview.TryLoad(_gameRoot, sourceSet, edge, requestedFps, maximumFrames));
            if (rendered is null) throw new InvalidDataException("无法合成源卡动画：" + MonsterAnimationSpineRenderer.LastDiagnostic);
            loadedMedia = await Task.Run(() => ExtractedAnimation.CreateFromFrames(
                $"源卡 {sourceCardId} · {rendered.AnimationName}",
                rendered.Frames,
                rendered.FramesPerSecond));
            var frames = rendered.Frames.ToList();
            rendered.Frames.Clear();
            var fps = rendered.FramesPerSecond;
            rendered.Dispose(); rendered = null;
            DisposeMedia();
            _media = loadedMedia; loadedMedia = null;
            _previewFrames.AddRange(frames);
            _automaticQuality = automatic;
            _resolvedFrameEdge = edge;
            _mediaWidth = frames[0].Width;
            _mediaHeight = frames[0].Height;
            _fps.Value = Math.Clamp(fps, (int)_fps.Minimum, (int)_fps.Maximum);
            _scale.Value = 100;
            SetPreviewRate(fps);
            _timeline.Maximum = Math.Max(0, _previewFrames.Count - 1);
            _timeline.Value = 0;
            _timeline.Enabled = _previewFrames.Count > 1;
            _preview.StatusText = "";
            ShowFrame(0);
            UpdateSourceStatus();
            UpdateApplyState();
            _resourceStatus.ForeColor = UiTheme.Primary;
            _resourceStatus.Text = $"已载入源卡 {sourceCardId} 的真实 Spine 动画 · {_previewFrames.Count} 帧 · {fps} FPS";
            if (!_playing) TogglePlay();
        }
        catch (Exception ex)
        {
            rendered?.Dispose();
            loadedMedia?.Dispose();
            MessageBox.Show(this, ex.Message, "无法复制源卡动画", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    async Task BorrowOtherCardAnimationAsync()
    {
        var targetCardId = _cardId.Text.Trim();
        var donorCardId = _sourceCardId.Text.Trim();
        var installed = false;
        if (!targetCardId.All(char.IsAsciiDigit) || targetCardId.Length == 0 ||
            !donorCardId.All(char.IsAsciiDigit) || donorCardId.Length == 0)
        {
            MessageBox.Show(this, "请同时输入纯数字目标卡号和源卡号。", Text);
            return;
        }
        if (!EnsureGameClosed()) return;
        if (MessageBox.Show(this,
                $"给原本没有召唤动画的卡号 {targetCardId} 登记动画，并借用卡号 {donorCardId} 的 SD / HighEnd_HD 演出？\n\n" +
                "会新增 4 个入口 Bundle，并修改 CardIndividualData 与本地资源目录；全部自动备份，可用“还原该卡动画”撤销。",
                "确认建立借用动画",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, $"正在让卡号 {targetCardId} 借用 {donorCardId} 的召唤动画…");
            var record = await Task.Run(() => _borrowService.Install(_gameRoot, targetCardId, donorCardId));
            installed = true;
            _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, targetCardId));
            if (!_set.IsComplete) throw new InvalidDataException("借用登记已写入，但没有重新定位到完整动画资源，已停止后续写入。");
            DisposeMedia();
            _resourceStatus.ForeColor = UiTheme.Gold;
            _resourceStatus.Text = $"借用完成 · 供体 {record.DonorCardId} · {_set.CountSummary} · 只读";
            await LoadCurrentAnimationPreviewAsync(_set);
            _apply.Enabled = false;
            MessageBox.Show(this,
                $"卡号 {targetCardId} 已借用卡号 {donorCardId} 的召唤动画。\n\n请完全退出并重新启动 Master Duel 后测试。借用资源为只读，避免修改供体卡图集。",
                "借用动画完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (installed)
            {
                try { await Task.Run(() => _borrowService.Remove(_gameRoot, targetCardId)); }
                catch (Exception rollbackEx)
                {
                    MessageBox.Show(this,
                        $"{ex.Message}\n\n自动回滚也失败：{rollbackEx.Message}\n请先不要启动游戏，并再次点击“还原该卡动画”。",
                        "建立借用动画失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }
            MessageBox.Show(this, ex.Message, "建立借用动画失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
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
        var cardId = _cardId.Text.Trim();
        if (cardId.Length == 0 || !cardId.All(char.IsAsciiDigit)) { MessageBox.Show(this, "先输入纯数字目标卡号。", Text); return; }
        if (_media is null) { MessageBox.Show(this, "先拖入或选择 GIF／视频。", Text); return; }
        _set ??= await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
        if (_set.CardId != cardId) _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
        if (_borrowService.IsReadOnlyBorrowed(_gameRoot, cardId)) { MessageBox.Show(this, "这张卡处于旧版只读借用模式。请先点“还原该卡动画”，再直接拖入视频重新创建。", Text); return; }
        if (!EnsureGameClosed()) return;
        var needsCreation = !_set.IsComplete;
        var confirm = needsCreation
            ? $"卡号 {cardId} 原本没有完整召唤动画。\n\n工具将自动选择本机已有演出作为结构模板，复制成完全独立的一套资源，再写入当前 GIF／视频；不会修改供体卡。继续？"
            : $"将修改卡号 {cardId} 的 SD／HighEnd_HD 两套动画资源。\n\n所有 Bundle 会先备份；任一步失败会自动回滚。继续？";
        if (MessageBox.Show(this, confirm, needsCreation ? "创建并写入召唤动画" : "确认替换召唤动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        var autoCreated = false;
        try
        {
            if (needsCreation)
            {
                SetBusy(true, "正在自动选择模板并复制独立动画资源…");
                var record = await Task.Run(() => _borrowService.InstallIndependent(_gameRoot, cardId));
                autoCreated = true;
                _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
                if (!_set.IsComplete) throw new InvalidDataException("独立模板已经复制，但目标动画资源仍不完整，已停止写入。");
                _resourceStatus.Text = $"已从卡号 {record.DonorCardId} 建立独立模板 · 正在生成动画图集…";
            }
            SetBusy(true, "正在生成单张 Spine 图集…");
            var template = await Task.Run(() => _service.ReadTemplate(_gameRoot, _set));
            using var built = await Task.Run(() => MonsterAnimationBuilder.Build(_media.FramePaths, _set.CardId, (int)_fps.Value, (int)_scale.Value, template, int.Parse(_atlasEdge.Text)));
            _resourceStatus.Text = $"图集 {built.AtlasWidth}×{built.AtlasHeight} · 正在 DXT5 压缩并写入 6 个 Bundle…";
            await Task.Run(() => _service.Apply(_gameRoot, _set, built));
            _resourceStatus.ForeColor = UiTheme.Primary;
            _resourceStatus.Text = $"替换完成 · {built.FrameCount} 帧 / {built.FramesPerSecond} FPS · 全画布 {(int)_scale.Value}% · 图集 {built.AtlasWidth}×{built.AtlasHeight}";
            MessageBox.Show(this, needsCreation
                ? "已为这张无原动画卡自动创建独立资源并写入动画。\n\n请完全退出并重新启动 Master Duel 后测试召唤演出与商店预览。"
                : "两套召唤动画资源已经全部替换并备份。\n\n请完全退出并重新启动 Master Duel 后测试召唤演出与商店预览。", "动画替换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (autoCreated)
            {
                try { await Task.Run(() => _borrowService.Remove(_gameRoot, cardId)); }
                catch (Exception rollbackEx)
                {
                    MessageBox.Show(this, $"{ex.Message}\n\n自动清理失败：{rollbackEx.Message}\n请保持游戏关闭并点击“还原该卡动画”。", "动画写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
            }
            MessageBox.Show(this, ex.Message, "动画替换失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    async Task RestoreAsync()
    {
        var cardId = _cardId.Text.Trim();
        var borrowed = cardId.Length > 0 ? _borrowService.Find(_gameRoot, cardId) : null;
        if (borrowed is null && (_set is null || _set.Assets.Count == 0)) { MessageBox.Show(this, "先输入卡号并定位动画资源。", Text); return; }
        if (!EnsureGameClosed()) return;
        if (borrowed is not null)
        {
            var description = borrowed.IsIndependent ? "本工具自动创建的整套独立动画资源" : $"对卡号 {borrowed.DonorCardId} 的只读动画借用";
            if (MessageBox.Show(this, $"确认移除卡号 {cardId} 的{description}，并删除 {borrowed.CreatedBundlePaths.Count} 个已创建 Bundle？", "还原动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                SetBusy(true, "正在移除借用登记与入口 Bundle…");
                var removed = await Task.Run(() => _borrowService.Remove(_gameRoot, cardId));
                DisposeMedia();
                _set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
                _resourceStatus.ForeColor = UiTheme.Primary;
                _resourceStatus.Text = removed ? "已移除自动创建的动画资源；该卡已回到原始无动画状态" : "没有找到自动创建记录";
                MessageBox.Show(this, _resourceStatus.Text, "还原完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "还原借用动画失败（已回滚）", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); }
            return;
        }
        var set = _set!;
        if (MessageBox.Show(this, $"确认把卡号 {set.CardId} 的动画 Bundle 全部还原为首次替换前的版本？", "还原动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try
        {
            SetBusy(true, "正在还原动画 Bundle…");
            var count = await Task.Run(() => _service.Restore(_gameRoot, set));
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
        _sourceCardId.Enabled = !busy;
        _copyOther.Enabled = !busy;
        _borrowOther.Enabled = !busy;
        UpdateApplyState();
    }

    void UpdateApplyState()
    {
        var cardId = _cardId.Text.Trim();
        var validTarget = cardId.Length > 0 && cardId.All(char.IsAsciiDigit);
        var readOnly = validTarget && _borrowService.IsReadOnlyBorrowed(_gameRoot, cardId);
        _apply.Text = _set?.IsComplete == true ? "写入动画" : "创建并写入动画";
        _apply.Enabled = !_busy && validTarget && _media is not null && !readOnly;
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
