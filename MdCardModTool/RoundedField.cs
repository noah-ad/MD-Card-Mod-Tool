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
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		BackColor = UiTheme.SurfaceAlt;
		base.Padding = new Padding(10, 7, 10, 6);
		base.Margin = new Padding(6, 4, 6, 4);
		MinimumSize = new Size(100, 36);
		control.Dock = DockStyle.Fill;
		control.Margin = Padding.Empty;
		if (control is TextBox textBox)
		{
			textBox.BorderStyle = BorderStyle.None;
		}
		base.Controls.Add(control);
		control.GotFocus += delegate
		{
			Invalidate();
		};
		control.LostFocus += delegate
		{
			Invalidate();
		};
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
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float num = Math.Max(1f, (float)base.DeviceDpi / 96f);
		using GraphicsPath path = UiTheme.RoundedPath(new Rectangle(1, 1, Math.Max(1, base.Width - 3), Math.Max(1, base.Height - 3)), UiTheme.Scale(this, CornerRadius));
		using Pen pen = new Pen(_control.ContainsFocus ? UiTheme.Primary : UiTheme.Border, (_control.ContainsFocus ? 1.5f : 1f) * num);
		e.Graphics.DrawPath(pen, path);
	}
}
