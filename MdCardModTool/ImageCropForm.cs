using System.Drawing.Drawing2D;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpRectangle = SixLabors.ImageSharp.Rectangle;
using SharpSize = SixLabors.ImageSharp.Size;

namespace MdCardModTool;

/// <summary>固定目标比例的交互式裁剪器：拖动平移，滚轮／滑杆缩放，输出严格目标尺寸。</summary>
public sealed class ImageCropForm : Form
{
    readonly string _sourcePath;
    readonly int _targetWidth;
    readonly int _targetHeight;
    readonly CropCanvas _canvas;
    readonly TrackBar _zoom = new() { Minimum = 100, Maximum = 600, Value = 100, TickFrequency = 50, Dock = DockStyle.Top, Height = 48 };
    readonly Label _zoomValue = new() { Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Primary, TextAlign = ContentAlignment.MiddleLeft };

    public byte[]? OutputPng { get; private set; }

    public ImageCropForm(string sourcePath, int targetWidth, int targetHeight, string purpose = "替换图片")
    {
        if (targetWidth <= 0 || targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetWidth), "目标尺寸必须大于 0。");
        _sourcePath = sourcePath; _targetWidth = targetWidth; _targetHeight = targetHeight;
        var preview = ImageCropService.LoadPreview(sourcePath);
        _canvas = new CropCanvas(preview, targetWidth, targetHeight) { Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = true };

        UiTheme.ApplyDarkTitleBar(this);
        Text = $"固定比例裁剪 · {purpose}"; StartPosition = FormStartPosition.CenterParent; Size = new Size(1120, 790); MinimumSize = new Size(860, 620);
        BackColor = UiTheme.Window; ForeColor = UiTheme.Text; Font = new Font("Microsoft YaHei UI", 9F); KeyPreview = true;

        var title = new Label { Text = "固定比例裁剪", Dock = DockStyle.Top, Height = 30, ForeColor = UiTheme.Text, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold) };
        var subtitle = new Label { Text = $"{purpose}  ·  输出 {_targetWidth}×{_targetHeight}  ·  比例 {_targetWidth}:{_targetHeight}", Dock = DockStyle.Fill, ForeColor = UiTheme.Gold, Font = new Font("Microsoft YaHei UI", 9F) };
        var header = new GradientBanner { Dock = DockStyle.Top, Height = 72, Padding = new Padding(20, 10, 20, 8) };
        header.Controls.Add(subtitle); header.Controls.Add(title);

        var help = new Label
        {
            Text = "操作\n\n• 按住鼠标左键拖动图片\n• 滚轮或滑杆放大／缩小\n• 方向键微调位置\n• 双击裁剪区恢复居中\n\n图片会始终铺满裁剪框，确认后自动转换为目标尺寸。透明 PNG 的 Alpha 会保留。",
            Dock = DockStyle.Top, Height = 210, ForeColor = UiTheme.Text, Padding = new Padding(2, 8, 2, 8), Font = new Font("Microsoft YaHei UI", 9F)
        };
        var zoomTitle = new Label { Text = "缩放", Dock = DockStyle.Top, Height = 30, ForeColor = UiTheme.Muted, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold) };
        var reset = UiTheme.Button("适应并居中", (_, _) => { _canvas.ResetView(); _zoom.Value = 100; }, ButtonTone.Neutral);
        var cancel = UiTheme.Button("取消", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, ButtonTone.Neutral);
        var apply = UiTheme.Button("裁剪并继续", (_, _) => ConfirmCrop(), ButtonTone.Primary);
        cancel.DialogResult = DialogResult.Cancel;

        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 5), BackColor = UiTheme.Surface };
        actionRow.Controls.Add(apply); actionRow.Controls.Add(cancel);
        var resetRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 4, 0, 4), BackColor = UiTheme.Surface };
        resetRow.Controls.Add(reset);
        var side = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(18) };
        side.Controls.Add(actionRow); side.Controls.Add(help); side.Controls.Add(resetRow); side.Controls.Add(_zoom); side.Controls.Add(_zoomValue); side.Controls.Add(zoomTitle);

        var canvasFrame = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.SurfaceAlt, Padding = new Padding(1), Margin = new Padding(14, 14, 7, 14) };
        canvasFrame.Controls.Add(_canvas);
        var sideFrame = new BorderPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(1), Margin = new Padding(7, 14, 14, 14) };
        sideFrame.Controls.Add(side);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.Window };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
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
        _zoomValue.Text = "100%";
        Shown += (_, _) => { _canvas.Focus(); _canvas.ResetView(); };
    }

    void ConfirmCrop()
    {
        try
        {
            UseWaitCursor = true;
            OutputPng = ImageCropService.CropAndResize(_sourcePath, _canvas.SourceCropRectangle, _targetWidth, _targetHeight);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "裁剪失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
}

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
    Bitmap? _source;
    float _zoom = 1f;
    float _offsetX;
    float _offsetY;
    bool _dragging;
    Point _lastMouse;

    public event Action<float>? ZoomChanged;
    public float Zoom => _zoom;

    public CropCanvas(Bitmap source, int targetWidth, int targetHeight)
    {
        _source = source; _targetWidth = targetWidth; _targetHeight = targetHeight;
        DoubleBuffered = true; BackColor = Color.FromArgb(7, 11, 19); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
    }

    RectangleF CropRectangle
    {
        get
        {
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

    float DisplayScale
    {
        get
        {
            if (_source is null) return 1;
            var crop = CropRectangle;
            return Math.Max(crop.Width / _source.Width, crop.Height / _source.Height) * _zoom;
        }
    }

    RectangleF ImageRectangle
    {
        get
        {
            if (_source is null) return RectangleF.Empty;
            var crop = CropRectangle; var scale = DisplayScale;
            var width = _source.Width * scale; var height = _source.Height * scale;
            return new RectangleF(crop.Left + crop.Width / 2f + _offsetX - width / 2f, crop.Top + crop.Height / 2f + _offsetY - height / 2f, width, height);
        }
    }

    public RectangleF SourceCropRectangle
    {
        get
        {
            var crop = CropRectangle; var image = ImageRectangle; var scale = DisplayScale;
            return new RectangleF((crop.Left - image.Left) / scale, (crop.Top - image.Top) / scale, crop.Width / scale, crop.Height / scale);
        }
    }

    public void ResetView()
    {
        _zoom = 1f; _offsetX = 0; _offsetY = 0; ClampOffset(); Invalidate(); ZoomChanged?.Invoke(_zoom);
    }

    public void SetZoom(float value, PointF? anchor)
    {
        value = Math.Clamp(value, 1f, 6f);
        if (_source is null || Math.Abs(value - _zoom) < 0.0001f) return;
        var point = anchor ?? new PointF(CropRectangle.Left + CropRectangle.Width / 2f, CropRectangle.Top + CropRectangle.Height / 2f);
        var oldImage = ImageRectangle; var oldScale = DisplayScale;
        var sourceX = (point.X - oldImage.Left) / oldScale; var sourceY = (point.Y - oldImage.Top) / oldScale;
        _zoom = value;
        var crop = CropRectangle; var newScale = DisplayScale;
        _offsetX = point.X - (crop.Left + crop.Width / 2f) - (sourceX - _source.Width / 2f) * newScale;
        _offsetY = point.Y - (crop.Top + crop.Height / 2f) - (sourceY - _source.Height / 2f) * newScale;
        ClampOffset(); Invalidate(); ZoomChanged?.Invoke(_zoom);
    }

    void ClampOffset()
    {
        if (_source is null) return;
        var crop = CropRectangle; var scale = DisplayScale;
        var extraX = Math.Max(0, _source.Width * scale - crop.Width) / 2f;
        var extraY = Math.Max(0, _source.Height * scale - crop.Height) / 2f;
        _offsetX = Math.Clamp(_offsetX, -extraX, extraX); _offsetY = Math.Clamp(_offsetY, -extraY, extraY);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); ClampOffset(); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); _dragging = true; _lastMouse = e.Location; Cursor = Cursors.SizeAll; Capture = true; }
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
        base.OnPaint(e); var graphics = e.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var crop = CropRectangle; DrawCheckerboard(graphics, crop);
        if (_source is not null) graphics.DrawImage(_source, ImageRectangle);
        using var shade = new SolidBrush(Color.FromArgb(185, 3, 7, 14));
        graphics.FillRectangle(shade, 0, 0, Width, Math.Max(0, crop.Top));
        graphics.FillRectangle(shade, 0, crop.Bottom, Width, Math.Max(0, Height - crop.Bottom));
        graphics.FillRectangle(shade, 0, crop.Top, Math.Max(0, crop.Left), crop.Height);
        graphics.FillRectangle(shade, crop.Right, crop.Top, Math.Max(0, Width - crop.Right), crop.Height);
        using var grid = new Pen(Color.FromArgb(115, 235, 244, 255), 1f);
        graphics.DrawLine(grid, crop.Left + crop.Width / 3f, crop.Top, crop.Left + crop.Width / 3f, crop.Bottom);
        graphics.DrawLine(grid, crop.Left + crop.Width * 2f / 3f, crop.Top, crop.Left + crop.Width * 2f / 3f, crop.Bottom);
        graphics.DrawLine(grid, crop.Left, crop.Top + crop.Height / 3f, crop.Right, crop.Top + crop.Height / 3f);
        graphics.DrawLine(grid, crop.Left, crop.Top + crop.Height * 2f / 3f, crop.Right, crop.Top + crop.Height * 2f / 3f);
        using var border = new Pen(UiTheme.Primary, 2f); graphics.DrawRectangle(border, crop.X, crop.Y, crop.Width, crop.Height);
        DrawCorners(graphics, crop);
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

    protected override void Dispose(bool disposing) { if (disposing) { _source?.Dispose(); _source = null; } base.Dispose(disposing); }
}
