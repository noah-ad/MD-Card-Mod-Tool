using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpRectangle = SixLabors.ImageSharp.Rectangle;
using SharpSize = SixLabors.ImageSharp.Size;

namespace MdCardModTool;

/// <summary>卡片实装裁剪器：按卡框实际插图区构图，再反算为 Bundle 需要的存储尺寸。</summary>
public sealed class ImageCropForm : Form
{
    readonly string _sourcePath;
    readonly int _targetWidth;
    readonly int _targetHeight;
    readonly CropCanvas _canvas;
    readonly ModEngine _engine = new();
    readonly ComboBox _frames = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Height = 34 };
    readonly Label _mapping = new() { Dock = DockStyle.Top, Height = 52, ForeColor = UiTheme.Gold, Padding = new Padding(0, 5, 0, 5) };
    readonly TrackBar _zoom = new() { Minimum = 1, Maximum = 2000, Value = 100, TickFrequency = 100, Dock = DockStyle.Top, Height = 48 };
    readonly Label _zoomValue = new() { Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Primary, TextAlign = ContentAlignment.MiddleLeft };
    readonly bool _fullCardOverlay;
    int _frameGeneration;

    sealed class FrameChoice
    {
        public FrameChoice(TexRef texture) => Texture = texture;
        public TexRef Texture { get; }
        public override string ToString() => $"{Texture.Name} · {CardFrameCatalog.FriendlyName(Texture.Name)}";
    }

    public byte[]? OutputPng { get; private set; }
    public string SelectedFrameKey { get; private set; } = "";

    public ImageCropForm(
        string sourcePath,
        int targetWidth,
        int targetHeight,
        string purpose = "替换图片",
        IEnumerable<TexRef>? cardFrames = null,
        string? preferredFrameKey = null,
        bool fullCardOverlay = false)
    {
        if (targetWidth <= 0 || targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth), "目标尺寸必须大于 0。");
        _sourcePath = sourcePath; _targetWidth = targetWidth; _targetHeight = targetHeight; _fullCardOverlay = fullCardOverlay;
        var preview = ImageCropService.LoadPreview(sourcePath);
        _canvas = new CropCanvas(preview, targetWidth, targetHeight, fullCardOverlay) { Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = true };

        var choices = cardFrames is null
            ? []
            : (fullCardOverlay
                ? cardFrames.Where(x => x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == FrameComposer.Width && x.Height == FrameComposer.Height).OrderBy(x => x.Name)
                : CardFrameCatalog.CompatibleFrames(cardFrames, targetWidth, targetHeight))
                .Select(x => new FrameChoice(x)).ToArray();
        _frames.Items.AddRange(choices);
        if (choices.Length > 0)
        {
            var wanted = string.IsNullOrWhiteSpace(preferredFrameKey) ? CardFrameCatalog.DefaultKey(targetWidth, targetHeight) : preferredFrameKey;
            var selected = Array.FindIndex(choices, x => x.Texture.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (selected < 0) selected = Array.FindIndex(choices, x => x.Texture.Name.Equals(CardFrameCatalog.DefaultKey(targetWidth, targetHeight), StringComparison.OrdinalIgnoreCase));
            _frames.SelectedIndex = selected >= 0 ? selected : 0;
        }

        UiTheme.ApplyDarkTitleBar(this);
        Text = $"卡片实装裁剪 · {purpose}"; StartPosition = FormStartPosition.CenterParent; Size = new Size(1180, 850); MinimumSize = new Size(900, 660);
        BackColor = UiTheme.Window; ForeColor = UiTheme.Text; Font = new Font("Microsoft YaHei UI", 9F); KeyPreview = true;

        var title = new Label { Text = fullCardOverlay ? "超框实装构图" : choices.Length > 0 ? "卡片实装构图" : "固定比例裁剪", Dock = DockStyle.Top, Height = 30, ForeColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold) };
        var subtitle = new Label
        {
            Text = choices.Length > 0
                ? $"{purpose} · 先按卡框实际效果构图，确认后自动转换为 {_targetWidth}×{_targetHeight} 存储纹理"
                : $"{purpose} · 输出 {_targetWidth}×{_targetHeight}",
            Dock = DockStyle.Fill, ForeColor = UiTheme.Gold, Font = new Font("Microsoft YaHei UI", 9F)
        };
        var header = new GradientBanner { Dock = DockStyle.Top, Height = 72, Padding = new Padding(20, 10, 20, 8) };
        header.Controls.Add(subtitle); header.Controls.Add(title);

        var help = new Label
        {
            Text = fullCardOverlay
                ? "操作\n\n• 卡框直接显示最终游戏效果，可随时切换\n• 卡框只作底层预览，不会烘焙进透明原画\n• 左键拖动图片，滚轮／滑杆缩放\n• 可缩到卡片以内，也可放大到 2000%\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n确认构图后会进入卡框编辑器完成应用。"
                : choices.Length > 0
                ? "操作\n\n• 卡框下拉只控制实装预览与构图比例\n• 左键拖动图片，滚轮／滑杆缩放\n• 可缩到画框以内，透明处按游戏白色底板显示\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n灵摆卡会按正常宽画面预览，保存时再自动压回 512×1024。"
                : "操作\n\n• 左键拖动图片\n• 滚轮／滑杆可自由放大和缩小\n• 方向键微调位置\n• 双击恢复铺满\n\n确认后自动转换为目标尺寸，透明 PNG 的 Alpha 会保留。",
            Dock = DockStyle.Top, Height = choices.Length > 0 ? 260 : 210, ForeColor = UiTheme.Text, Padding = new Padding(2, 8, 2, 8), Font = new Font("Microsoft YaHei UI", 9F)
        };
        var frameTitle = new Label { Text = "预览卡框", Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Muted, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Visible = choices.Length > 0 };
        _frames.Visible = choices.Length > 0; _mapping.Visible = choices.Length > 0;
        var zoomTitle = new Label { Text = "缩放（1%–2000%）", Dock = DockStyle.Top, Height = 30, ForeColor = UiTheme.Muted, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
        var fill = UiTheme.Button("铺满画框", (_, _) => _canvas.ResetView(), ButtonTone.Neutral);
        var whole = UiTheme.Button("显示整图", (_, _) => _canvas.ShowWholeImage(), ButtonTone.Neutral);
        var cancel = UiTheme.Button("取消", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, ButtonTone.Neutral);
        var apply = UiTheme.Button(fullCardOverlay ? "保存构图并继续" : "按预览效果替换", (_, _) => ConfirmCrop(), ButtonTone.Primary);
        cancel.DialogResult = DialogResult.Cancel;

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 5), BackColor = UiTheme.Surface };
        actionRow.Controls.Add(apply); actionRow.Controls.Add(cancel);
        var resetRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 4), BackColor = UiTheme.Surface };
        resetRow.Controls.Add(fill); resetRow.Controls.Add(whole);
        var side = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18) };
        side.Controls.Add(actionRow); side.Controls.Add(help); side.Controls.Add(resetRow); side.Controls.Add(_zoomValue); side.Controls.Add(_zoom); side.Controls.Add(zoomTitle); side.Controls.Add(_mapping); side.Controls.Add(_frames); side.Controls.Add(frameTitle);

        var canvasFrame = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.SurfaceAlt, Padding = new Padding(1), Margin = new Padding(14, 14, 7, 14) };
        canvasFrame.Controls.Add(_canvas);
        var sideFrame = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(1), Margin = new Padding(7, 14, 14, 14) };
        sideFrame.Controls.Add(side);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.Window };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 315));
        body.Controls.Add(canvasFrame, 0, 0); body.Controls.Add(sideFrame, 1, 0);

        Controls.Add(body); Controls.Add(header);
        AcceptButton = apply; CancelButton = cancel;
        _zoom.Scroll += (_, _) => _canvas.SetZoom(_zoom.Value / 100f, null);
        _canvas.ZoomChanged += value =>
        {
            var slider = Math.Clamp((int)Math.Round(value * 100), _zoom.Minimum, _zoom.Maximum);
            if (_zoom.Value != slider) _zoom.Value = slider;
            _zoomValue.Text = $"{value * 100:0}%";
        };
        _frames.SelectedIndexChanged += async (_, _) => await LoadSelectedFrameAsync();
        _zoomValue.Text = "100%";
        Shown += async (_, _) =>
        {
            _canvas.Focus();
            if (_frames.SelectedItem is FrameChoice) await LoadSelectedFrameAsync();
            else _canvas.ResetView();
        };
        FormClosed += (_, _) => _canvas.DisposeFrame();
    }

    async Task LoadSelectedFrameAsync()
    {
        if (_frames.SelectedItem is not FrameChoice choice) return;
        var generation = ++_frameGeneration; _mapping.Text = "正在读取卡框与实际插图区…";
        try
        {
            var bytes = await Task.Run(() => _engine.DecodePng(choice.Texture));
            var frame = FrameComposer.BitmapFrom(bytes);
            if (generation != _frameGeneration) { frame.Dispose(); return; }
            _canvas.SetFrame(frame);
            SelectedFrameKey = choice.Texture.Name;
            var window = _canvas.VisualArtSize;
            _mapping.Text = _fullCardOverlay
                ? $"超框画布 {window.Width:0}×{window.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}"
                : _targetWidth == 512 && _targetHeight == 1024
                ? $"实际插图区 {window.Width:0}×{window.Height:0}\n显示区 → 512×{CardFrameCatalog.PendulumVisibleStorageHeight} · 完整纹理 512×1024"
                : $"实际插图区 {window.Width:0}×{window.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}";
        }
        catch (Exception ex) { _mapping.Text = "卡框预览载入失败：" + ex.Message; }
    }

    void ConfirmCrop()
    {
        if (_frames.Items.Count > 0 && !_canvas.HasFrame)
        {
            MessageBox.Show(this, "卡框预览还没有载入完成，请稍候。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            UseWaitCursor = true;
            var visibleTargetHeight = !_fullCardOverlay && _targetWidth == 512 && _targetHeight == 1024
                ? CardFrameCatalog.PendulumVisibleStorageHeight
                : _targetHeight;
            OutputPng = ImageCropService.RenderToTarget(_sourcePath, _canvas.RenderSpec, _targetWidth, _targetHeight, visibleTargetHeight);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "裁剪失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
}

public readonly record struct ImageRenderSpec(float VisualWidth, float VisualHeight, float ImageScale, float OffsetX, float OffsetY);

public static class ImageCropService
{
    public static Bitmap LoadPreview(string sourcePath)
    {
        using var image = SharpImage.Load<Rgba32>(sourcePath);
        image.Mutate(context => context.AutoOrient());
        using var stream = new MemoryStream(); image.Save(stream, new PngEncoder()); stream.Position = 0;
        using var drawingImage = System.Drawing.Image.FromStream(stream);
        return new Bitmap(drawingImage);
    }

    /// <summary>按用户看到的卡框插图区布局作图，再非等比映射为游戏要求的存储纹理。</summary>
    public static byte[] RenderToTarget(string sourcePath, ImageRenderSpec spec, int targetWidth, int targetHeight, int? visibleTargetHeight = null)
    {
        if (spec.VisualWidth <= 0 || spec.VisualHeight <= 0 || spec.ImageScale <= 0) throw new ArgumentException("裁剪布局无效。", nameof(spec));
        var mappedHeight = visibleTargetHeight ?? targetHeight;
        if (mappedHeight <= 0 || mappedHeight > targetHeight) throw new ArgumentOutOfRangeException(nameof(visibleTargetHeight), "显示区高度必须位于输出纹理范围内。");
        using var source = LoadPreview(sourcePath);
        using var output = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            CardFrameRenderer.Configure(graphics);
            var scaleX = targetWidth / spec.VisualWidth;
            var scaleY = mappedHeight / spec.VisualHeight;
            var visualWidth = source.Width * spec.ImageScale;
            var visualHeight = source.Height * spec.ImageScale;
            var left = spec.VisualWidth / 2f + spec.OffsetX - visualWidth / 2f;
            var top = spec.VisualHeight / 2f + spec.OffsetY - visualHeight / 2f;
            var destination = new RectangleF(left * scaleX, top * scaleY, visualWidth * scaleX, visualHeight * scaleY);
            graphics.DrawImage(source, destination);
        }
        using var stream = new MemoryStream(); output.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    /// <summary>保留给命令行批处理使用的传统铺满裁剪。</summary>
    public static byte[] CropAndResize(string sourcePath, RectangleF sourceCrop, int targetWidth, int targetHeight)
    {
        using var image = SharpImage.Load<Rgba32>(sourcePath);
        image.Mutate(context => context.AutoOrient());
        var aspect = targetWidth / (double)targetHeight;
        var cropWidth = Math.Clamp((int)Math.Round(sourceCrop.Width), 1, image.Width);
        var cropHeight = Math.Max(1, (int)Math.Round(cropWidth / aspect));
        if (cropHeight > image.Height)
        {
            cropHeight = image.Height;
            cropWidth = Math.Clamp((int)Math.Round(cropHeight * aspect), 1, image.Width);
        }
        var centerX = Math.Clamp(sourceCrop.Left + sourceCrop.Width / 2f, 0, image.Width);
        var centerY = Math.Clamp(sourceCrop.Top + sourceCrop.Height / 2f, 0, image.Height);
        var x = Math.Clamp((int)Math.Round(centerX - cropWidth / 2f), 0, image.Width - cropWidth);
        var y = Math.Clamp((int)Math.Round(centerY - cropHeight / 2f), 0, image.Height - cropHeight);
        image.Mutate(context => context
            .Crop(new SharpRectangle(x, y, cropWidth, cropHeight))
            .Resize(new ResizeOptions { Size = new SharpSize(targetWidth, targetHeight), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
        using var output = new MemoryStream(); image.Save(output, new PngEncoder()); return output.ToArray();
    }
}

public sealed class CropCanvas : Control
{
    readonly int _targetWidth;
    readonly int _targetHeight;
    readonly bool _fullCardOverlay;
    Bitmap? _source;
    Bitmap? _frame;
    RectangleF _artWindow;
    float _zoom = 1f;
    float _offsetX;
    float _offsetY;
    bool _dragging;
    Point _lastMouse;

    public event Action<float>? ZoomChanged;
    public float Zoom => _zoom;
    public bool HasFrame => _frame is not null;
    public SizeF VisualArtSize => HasFrame && !_fullCardOverlay ? _artWindow.Size : new SizeF(_targetWidth, _targetHeight);

    public CropCanvas(Bitmap source, int targetWidth, int targetHeight, bool fullCardOverlay = false)
    {
        _source = source; _targetWidth = targetWidth; _targetHeight = targetHeight; _fullCardOverlay = fullCardOverlay;
        DoubleBuffered = true; BackColor = Color.FromArgb(7, 11, 19); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
    }

    RectangleF CardRectangle
    {
        get
        {
            const float margin = 30f;
            var availableWidth = Math.Max(1, ClientSize.Width - margin * 2);
            var availableHeight = Math.Max(1, ClientSize.Height - margin * 2);
            var aspect = FrameComposer.Width / (float)FrameComposer.Height;
            float width, height;
            if (availableWidth / availableHeight > aspect) { height = availableHeight; width = height * aspect; }
            else { width = availableWidth; height = width / aspect; }
            return new RectangleF((ClientSize.Width - width) / 2f, (ClientSize.Height - height) / 2f, width, height);
        }
    }

    RectangleF WorkRectangle
    {
        get
        {
            if (_frame is not null && !_fullCardOverlay)
            {
                var card = CardRectangle; var scale = card.Width / FrameComposer.Width;
                return new RectangleF(card.Left + _artWindow.Left * scale, card.Top + _artWindow.Top * scale, _artWindow.Width * scale, _artWindow.Height * scale);
            }
            const float margin = 46f;
            var availableWidth = Math.Max(1, ClientSize.Width - margin * 2);
            var availableHeight = Math.Max(1, ClientSize.Height - margin * 2);
            var aspect = _targetWidth / (float)_targetHeight;
            float width, height;
            if (availableWidth / availableHeight > aspect) { height = availableHeight; width = height * aspect; }
            else { width = availableWidth; height = width / aspect; }
            return new RectangleF((ClientSize.Width - width) / 2f, (ClientSize.Height - height) / 2f, width, height);
        }
    }

    float FitScale
    {
        get
        {
            if (_source is null) return 1;
            var work = WorkRectangle;
            return Math.Max(work.Width / _source.Width, work.Height / _source.Height);
        }
    }

    float DisplayScale => FitScale * _zoom;

    RectangleF ImageRectangle
    {
        get
        {
            if (_source is null) return RectangleF.Empty;
            var work = WorkRectangle; var scale = DisplayScale;
            var width = _source.Width * scale; var height = _source.Height * scale;
            return new RectangleF(work.Left + work.Width / 2f + _offsetX - width / 2f, work.Top + work.Height / 2f + _offsetY - height / 2f, width, height);
        }
    }

    public ImageRenderSpec RenderSpec
    {
        get
        {
            var work = WorkRectangle; var visual = VisualArtSize;
            var logicalX = visual.Width / work.Width; var logicalY = visual.Height / work.Height;
            return new ImageRenderSpec(visual.Width, visual.Height, DisplayScale * logicalX, _offsetX * logicalX, _offsetY * logicalY);
        }
    }

    public void SetFrame(Bitmap frame)
    {
        _frame?.Dispose(); _frame = frame; _artWindow = CardFrameRenderer.FindArtWindow(frame); ResetView(); Invalidate();
    }

    public void DisposeFrame() { _frame?.Dispose(); _frame = null; }

    public void ResetView()
    {
        _zoom = 1f; _offsetX = 0; _offsetY = 0; ClampOffset(); Invalidate(); ZoomChanged?.Invoke(_zoom);
    }

    public void ShowWholeImage()
    {
        if (_source is null) return;
        var work = WorkRectangle;
        var contain = Math.Min(work.Width / _source.Width, work.Height / _source.Height);
        _zoom = Math.Clamp(contain / FitScale, 0.01f, 20f); _offsetX = 0; _offsetY = 0;
        ClampOffset(); Invalidate(); ZoomChanged?.Invoke(_zoom);
    }

    public void SetZoom(float value, PointF? anchor)
    {
        value = Math.Clamp(value, 0.01f, 20f);
        if (_source is null || Math.Abs(value - _zoom) < 0.0001f) return;
        var point = anchor ?? new PointF(WorkRectangle.Left + WorkRectangle.Width / 2f, WorkRectangle.Top + WorkRectangle.Height / 2f);
        var oldImage = ImageRectangle; var oldScale = DisplayScale;
        var sourceX = (point.X - oldImage.Left) / oldScale; var sourceY = (point.Y - oldImage.Top) / oldScale;
        _zoom = value;
        var work = WorkRectangle; var newScale = DisplayScale;
        _offsetX = point.X - (work.Left + work.Width / 2f) - (sourceX - _source.Width / 2f) * newScale;
        _offsetY = point.Y - (work.Top + work.Height / 2f) - (sourceY - _source.Height / 2f) * newScale;
        ClampOffset(); Invalidate(); ZoomChanged?.Invoke(_zoom);
    }

    void ClampOffset()
    {
        if (_source is null) return;
        var work = WorkRectangle; var image = ImageRectangle;
        var visibleX = Math.Min(28f, image.Width / 2f);
        var visibleY = Math.Min(28f, image.Height / 2f);
        var maxX = Math.Max(0, work.Width / 2f + image.Width / 2f - visibleX);
        var maxY = Math.Max(0, work.Height / 2f + image.Height / 2f - visibleY);
        _offsetX = Math.Clamp(_offsetX, -maxX, maxX); _offsetY = Math.Clamp(_offsetY, -maxY, maxY);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); ClampOffset(); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left || !WorkRectangle.Contains(e.Location)) return; Focus(); _dragging = true; _lastMouse = e.Location; Cursor = Cursors.SizeAll; Capture = true; }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!_dragging) return;
        _offsetX += e.X - _lastMouse.X; _offsetY += e.Y - _lastMouse.Y; _lastMouse = e.Location; ClampOffset(); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button != MouseButtons.Left) return; _dragging = false; Cursor = Cursors.Hand; Capture = false; }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); SetZoom(_zoom * (e.Delta > 0 ? 1.12f : 1 / 1.12f), e.Location); }
    protected override void OnDoubleClick(EventArgs e) { base.OnDoubleClick(e); ResetView(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); var step = e.Shift ? 10 : 2; var changed = true;
        switch (e.KeyCode) { case Keys.Left: _offsetX -= step; break; case Keys.Right: _offsetX += step; break; case Keys.Up: _offsetY -= step; break; case Keys.Down: _offsetY += step; break; case Keys.Add: case Keys.Oemplus: SetZoom(_zoom * 1.1f, null); return; case Keys.Subtract: case Keys.OemMinus: SetZoom(_zoom / 1.1f, null); return; default: changed = false; break; }
        if (changed) { ClampOffset(); Invalidate(); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var graphics = e.Graphics; CardFrameRenderer.Configure(graphics);
        var work = WorkRectangle;
        if (_frame is not null)
        {
            var card = CardRectangle;
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) graphics.FillRectangle(shadow, card.Left + 9, card.Top + 10, card.Width, card.Height);
            if (_fullCardOverlay)
            {
                graphics.FillRectangle(Brushes.White, card);
                graphics.DrawImage(_frame, card);
                var state = graphics.Save(); graphics.SetClip(card);
                if (_source is not null) graphics.DrawImage(_source, ImageRectangle);
                graphics.Restore(state);
            }
            else
            {
                graphics.FillRectangle(Brushes.White, card);
                var state = graphics.Save(); graphics.SetClip(work); graphics.FillRectangle(Brushes.White, work);
                if (_source is not null) graphics.DrawImage(_source, ImageRectangle);
                graphics.Restore(state);
                graphics.DrawImage(_frame, card);
            }
        }
        else
        {
            DrawCheckerboard(graphics, work);
            if (_source is not null) graphics.DrawImage(_source, ImageRectangle);
            using var shade = new SolidBrush(Color.FromArgb(185, 3, 7, 14));
            graphics.FillRectangle(shade, 0, 0, Width, Math.Max(0, work.Top));
            graphics.FillRectangle(shade, 0, work.Bottom, Width, Math.Max(0, Height - work.Bottom));
            graphics.FillRectangle(shade, 0, work.Top, Math.Max(0, work.Left), work.Height);
            graphics.FillRectangle(shade, work.Right, work.Top, Math.Max(0, Width - work.Right), work.Height);
        }
        using var grid = new Pen(Color.FromArgb(80, 235, 244, 255), 1f);
        graphics.DrawLine(grid, work.Left + work.Width / 3f, work.Top, work.Left + work.Width / 3f, work.Bottom);
        graphics.DrawLine(grid, work.Left + work.Width * 2f / 3f, work.Top, work.Left + work.Width * 2f / 3f, work.Bottom);
        graphics.DrawLine(grid, work.Left, work.Top + work.Height / 3f, work.Right, work.Top + work.Height / 3f);
        graphics.DrawLine(grid, work.Left, work.Top + work.Height * 2f / 3f, work.Right, work.Top + work.Height * 2f / 3f);
        using var border = new Pen(UiTheme.Primary, 2f); graphics.DrawRectangle(border, work.X, work.Y, work.Width, work.Height);
        DrawCorners(graphics, work);
    }

    static void DrawCheckerboard(Graphics graphics, RectangleF area)
    {
        graphics.FillRectangle(Brushes.White, area); const int size = 14;
        using var gray = new SolidBrush(Color.FromArgb(205, 210, 218));
        for (var y = (int)area.Top; y < area.Bottom; y += size)
            for (var x = (int)area.Left; x < area.Right; x += size)
                if (((x - (int)area.Left) / size + (y - (int)area.Top) / size) % 2 == 0) graphics.FillRectangle(gray, x, y, Math.Min(size, (int)area.Right - x), Math.Min(size, (int)area.Bottom - y));
    }

    static void DrawCorners(Graphics graphics, RectangleF crop)
    {
        const float length = 22f; using var pen = new Pen(UiTheme.Gold, 4f);
        graphics.DrawLines(pen, new PointF[] { new(crop.Left, crop.Top + length), new(crop.Left, crop.Top), new(crop.Left + length, crop.Top) });
        graphics.DrawLines(pen, new PointF[] { new(crop.Right - length, crop.Top), new(crop.Right, crop.Top), new(crop.Right, crop.Top + length) });
        graphics.DrawLines(pen, new PointF[] { new(crop.Left, crop.Bottom - length), new(crop.Left, crop.Bottom), new(crop.Left + length, crop.Bottom) });
        graphics.DrawLines(pen, new PointF[] { new(crop.Right - length, crop.Bottom), new(crop.Right, crop.Bottom), new(crop.Right, crop.Bottom - length) });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _source?.Dispose(); _source = null; _frame?.Dispose(); _frame = null; }
        base.Dispose(disposing);
    }
}
