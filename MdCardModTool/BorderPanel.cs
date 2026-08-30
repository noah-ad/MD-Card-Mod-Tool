using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class BorderPanel : Panel
{
	public int CornerRadius { get; set; } = 12;

	public BorderPanel()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
		DoubleBuffered = true;
		base.Padding = new Padding(2);
		ParentChanged += (_, _) => Invalidate();
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Window);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), UiTheme.Scale(this, CornerRadius));
		using SolidBrush fill = new(BackColor);
		e.Graphics.FillPath(fill, path);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float scale = Math.Max(1f, DeviceDpi / 96f);
		using Pen pen = new Pen(UiTheme.Border, scale);
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, base.Width - 3), Math.Max(1, base.Height - 3)), UiTheme.Scale(this, CornerRadius));
		e.Graphics.DrawPath(pen, path);
	}
}
