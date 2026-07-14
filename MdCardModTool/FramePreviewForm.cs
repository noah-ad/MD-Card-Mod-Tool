using System.Drawing.Imaging;

namespace MdCardModTool;

/// <summary>把当前卡图与 data.unity3d 的 card_frame 透明贴图合成，仅作本地预览与导出。</summary>
public sealed class FramePreviewForm : Form
{
    readonly ModEngine _engine;
    readonly TexRef _art;
    readonly ComboBox _frames = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(16, 22, 34) };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8), ForeColor = Color.Gainsboro };
    int _generation;

    sealed class FrameChoice(TexRef texture)
    {
        public TexRef Texture { get; } = texture;
        public override string ToString() => $"{Texture.Name}  ·  {Texture.Width}×{Texture.Height}";
    }

    public FramePreviewForm(ModEngine engine, TexRef art, IEnumerable<TexRef> frames)
    {
        UiTheme.ApplyDarkTitleBar(this);
        _engine = engine; _art = art;
        Text = "卡框预览模式"; StartPosition = FormStartPosition.CenterParent; Size = new Size(920, 820); MinimumSize = new Size(660, 600);
        BackColor = Color.FromArgb(15, 22, 35); ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 9F);
        var export = Button("导出预览 PNG", (_, _) => Export());
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12, 10, 12, 8), ColumnCount = 3, BackColor = Color.FromArgb(20, 28, 45) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "卡框", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(160, 195, 255) }, 0, 0); top.Controls.Add(_frames, 1, 0); top.Controls.Add(export, 2, 0);
        top.Controls.Add(new Label { Text = "按游戏超框层级预览：704×1024 卡框在下、透明高图在上；只合成显示，不写入游戏。", AutoSize = true, ForeColor = Color.Gainsboro }, 0, 1); top.SetColumnSpan(top.GetControlFromPosition(0, 1)!, 3);
        var choices = frames.Where(x => x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == 704 && x.Height == 1024).OrderBy(x => x.Name).Select(x => new FrameChoice(x)).ToArray();
        _frames.Items.AddRange(choices);
        var defaultIndex = Array.FindIndex(choices, x => x.Texture.Name == "card_frame00" && x.Texture.Width == 704);
        _frames.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        _frames.SelectedIndexChanged += async (_, _) => await RenderAsync();
        Controls.Add(_preview); Controls.Add(_status); Controls.Add(top);
        Shown += async (_, _) => await RenderAsync();
        FormClosed += (_, _) => _preview.Image?.Dispose();
    }

    static Button Button(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 50, 76), ForeColor = Color.White, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = Color.FromArgb(71, 91, 128); b.Click += click; return b;
    }

    async Task RenderAsync()
    {
        if (_frames.SelectedItem is not FrameChoice choice) return;
        var generation = ++_generation; UseWaitCursor = true; _status.Text = "正在合成卡框预览…";
        try
        {
            var sources = await Task.WhenAll(Task.Run(() => _engine.DecodePng(_art)), Task.Run(() => _engine.DecodePng(choice.Texture)));
            if (generation != _generation) return;
            var composed = await Task.Run(() => FrameComposer.Compose(sources[0], sources[1]));
            var output = FrameComposer.PreviewBitmap(composed);
            var old = _preview.Image; _preview.Image = output; old?.Dispose();
            _status.Text = $"{_art.Name}  +  {choice.Texture.Name}（{output.Width}×{output.Height}）";
        }
        catch (Exception ex) { _status.Text = "卡框预览失败：" + ex.Message; }
        finally { UseWaitCursor = false; }
    }

    void Export()
    {
        if (_preview.Image is null) return;
        using var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"{Safe(_art.Name)}_卡框预览.png" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _preview.Image.Save(dialog.FileName, ImageFormat.Png);
    }
    static string Safe(string n) => string.Concat(n.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
