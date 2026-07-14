using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MdCardModTool;

public static class UiTheme
{
    public static readonly Color Window = Color.FromArgb(7, 12, 23);
    public static readonly Color Surface = Color.FromArgb(14, 23, 38);
    public static readonly Color SurfaceAlt = Color.FromArgb(18, 30, 49);
    public static readonly Color Elevated = Color.FromArgb(23, 39, 63);
    public static readonly Color Border = Color.FromArgb(43, 64, 93);
    public static readonly Color Primary = Color.FromArgb(82, 209, 244);
    public static readonly Color PrimaryDark = Color.FromArgb(29, 122, 179);
    public static readonly Color Gold = Color.FromArgb(239, 194, 104);
    public static readonly Color Text = Color.FromArgb(239, 245, 252);
    public static readonly Color Muted = Color.FromArgb(148, 166, 191);
    public static readonly Color Selection = Color.FromArgb(31, 91, 133);
    public static readonly Color Danger = Color.FromArgb(193, 78, 97);

    public static void ApplyDarkTitleBar(Form form)
    {
        form.HandleCreated += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = SurfaceAlt; box.ForeColor = Text; box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Microsoft YaHei UI", 9.5F); box.Margin = new Padding(6);
    }

    public static void StyleComboBox(ComboBox box)
    {
        box.BackColor = SurfaceAlt; box.ForeColor = Text; box.FlatStyle = FlatStyle.Flat;
        box.DrawMode = DrawMode.OwnerDrawFixed; box.ItemHeight = 24; box.Font = new Font("Microsoft YaHei UI", 9F);
        box.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Selection : SurfaceAlt);
            using var foreground = new SolidBrush(selected ? Color.White : Text);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawString(box.Items[e.Index]?.ToString() ?? "", box.Font, foreground, e.Bounds.X + 8, e.Bounds.Y + 3);
            e.DrawFocusRectangle();
        };
    }

    public static void StyleTree(TreeView tree)
    {
        tree.BackColor = Surface; tree.ForeColor = Text; tree.BorderStyle = BorderStyle.None;
        tree.LineColor = Border; tree.FullRowSelect = true; tree.ShowLines = false; tree.ShowPlusMinus = true;
        tree.ItemHeight = 28; tree.Indent = 18; tree.Font = new Font("Microsoft YaHei UI", 9F); tree.DrawMode = TreeViewDrawMode.Normal;
    }

    public static void StyleList(ListView list)
    {
        list.BackColor = Surface; list.ForeColor = Text; list.BorderStyle = BorderStyle.None;
        list.GridLines = false; list.OwnerDraw = true; list.Font = new Font("Microsoft YaHei UI", 9F);
        list.SmallImageList = new ImageList { ImageSize = new Size(1, 30), ColorDepth = ColorDepth.Depth32Bit };
        list.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(Elevated);
            using var border = new Pen(Border);
            using var foreground = new SolidBrush(Muted);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using var bold = new Font(list.Font, FontStyle.Bold);
            e.Graphics.DrawString(e.Header?.Text ?? "", bold, foreground, e.Bounds.X + 10, e.Bounds.Y + 8);
        };
        list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
        list.DrawSubItem += (_, e) =>
        {
            var selected = e.Item?.Selected == true;
            var alternate = e.ItemIndex % 2 == 1;
            var backgroundColor = selected ? Selection : alternate ? Color.FromArgb(16, 27, 44) : Surface;
            using var background = new SolidBrush(backgroundColor);
            using var foreground = new SolidBrush(selected ? Color.White : (e.ColumnIndex == 0 ? Text : Muted));
            e.Graphics.FillRectangle(background, e.Bounds);
            var text = e.SubItem?.Text ?? "";
            TextRenderer.DrawText(e.Graphics, text, list.Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 6, e.Bounds.Width - 14, e.Bounds.Height - 8), foreground.Color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
    }

    public static Button Button(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral)
    {
        var normal = tone switch
        {
            ButtonTone.Primary => PrimaryDark,
            ButtonTone.Gold => Color.FromArgb(132, 100, 42),
            ButtonTone.Danger => Color.FromArgb(102, 45, 59),
            _ => Elevated
        };
        var hover = tone switch
        {
            ButtonTone.Primary => Color.FromArgb(38, 148, 203),
            ButtonTone.Gold => Color.FromArgb(160, 121, 49),
            ButtonTone.Danger => Danger,
            _ => Color.FromArgb(34, 53, 81)
        };
        var border = tone switch
        {
            ButtonTone.Primary => Primary,
            ButtonTone.Gold => Gold,
            ButtonTone.Danger => Color.FromArgb(226, 112, 128),
            _ => Border
        };
        var button = new Button
        {
            Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(0, 34), Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(5, 4, 0, 4), FlatStyle = FlatStyle.Flat, BackColor = normal, ForeColor = Text,
            Cursor = Cursors.Hand, Font = new Font("Microsoft YaHei UI", 9F, tone is ButtonTone.Primary or ButtonTone.Gold ? FontStyle.Bold : FontStyle.Regular)
        };
        button.FlatAppearance.BorderColor = border; button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(Math.Max(0, hover.R - 18), Math.Max(0, hover.G - 18), Math.Max(0, hover.B - 18));
        button.MouseEnter += (_, _) => button.BackColor = hover;
        button.MouseLeave += (_, _) => button.BackColor = normal;
        button.Click += click;
        return button;
    }
}

public enum ButtonTone { Neutral, Primary, Gold, Danger }

public sealed class BufferedListView : ListView
{
    public BufferedListView() => DoubleBuffered = true;
}

public sealed class GradientBanner : Panel
{
    public GradientBanner() { DoubleBuffered = true; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(15, 40, 70), UiTheme.Window, 0F);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var cyan = new Pen(Color.FromArgb(38, UiTheme.Primary), 1F);
        using var gold = new Pen(Color.FromArgb(30, UiTheme.Gold), 1F);
        for (var x = Width - 440; x < Width + 80; x += 52)
        {
            e.Graphics.DrawLine(cyan, x, 0, x - 92, Height);
            e.Graphics.DrawEllipse(gold, x - 35, 12, 52, 52);
        }
    }
}

public sealed class BorderPanel : Panel
{
    public BorderPanel() { DoubleBuffered = true; Padding = new Padding(1); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
