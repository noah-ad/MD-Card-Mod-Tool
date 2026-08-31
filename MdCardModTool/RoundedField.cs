using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class RoundedField : Panel
{
	private readonly Control _control;

	public int CornerRadius { get; set; } = 9;

	public RoundedField(Control control)
	{
		_control = control;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
		DoubleBuffered = true;
		BackColor = UiTheme.SurfaceAlt;
		Padding = new Padding(10, 7, 10, 6);
		Margin = new Padding(6, 4, 6, 4);
		MinimumSize = new Size(100, 36);
		control.Dock = DockStyle.Fill;
		control.Margin = Padding.Empty;
		if (control is TextBox text)
		{
			text.BorderStyle = BorderStyle.None;
		}
		Controls.Add(control);
		control.GotFocus += (_, _) => Invalidate();
		control.LostFocus += (_, _) => Invalidate();
		ParentChanged += (_, _) => Invalidate();
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		using SolidBrush parentSurface = new(Parent?.BackColor ?? UiTheme.Window);
		e.Graphics.FillRectangle(parentSurface, ClientRectangle);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		Rectangle bounds = new(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
		using GraphicsPath path = UiTheme.RoundedPath(bounds, UiTheme.Scale(this, CornerRadius));
		using SolidBrush fill = new(BackColor);
		e.Graphics.FillPath(fill, path);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float scale = Math.Max(1f, DeviceDpi / 96f);
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3)), UiTheme.Scale(this, CornerRadius));
		using Pen pen = new(_control.ContainsFocus ? UiTheme.Primary : UiTheme.Border, (_control.ContainsFocus ? 1.5f : 1f) * scale);
		e.Graphics.DrawPath(pen, path);
	}
}
