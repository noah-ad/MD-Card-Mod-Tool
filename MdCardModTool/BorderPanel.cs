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
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		base.Padding = new Padding(2);
		base.ParentChanged += delegate
		{
			Invalidate();
		};
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(base.Parent?.BackColor ?? UiTheme.Window);
		e.Graphics.FillRectangle(brush, base.ClientRectangle);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, base.Width - 3), Math.Max(1, base.Height - 3)), UiTheme.Scale(this, CornerRadius));
		using SolidBrush brush2 = new SolidBrush(BackColor);
		e.Graphics.FillPath(brush2, path);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float width = Math.Max(1f, (float)base.DeviceDpi / 96f);
		using Pen pen = new Pen(UiTheme.Border, width);
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, base.Width - 3), Math.Max(1, base.Height - 3)), UiTheme.Scale(this, CornerRadius));
		e.Graphics.DrawPath(pen, path);
	}
}
