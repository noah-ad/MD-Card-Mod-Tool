using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

internal sealed class DarkVerticalScrollBar : Control
{
	private int _maximum;

	private int _value;

	private int _viewportSize;

	private bool _dragging;

	private int _dragOffset;

	public int Maximum
	{
		get
		{
			return _maximum;
		}
		set
		{
			_maximum = Math.Max(0, value);
			Value = _value;
			Invalidate();
		}
	}

	public int ViewportSize
	{
		get
		{
			return _viewportSize;
		}
		set
		{
			_viewportSize = Math.Max(0, value);
			Invalidate();
		}
	}

	public int Value
	{
		get
		{
			return _value;
		}
		set
		{
			int num = Math.Clamp(value, 0, Maximum);
			if (_value != num)
			{
				_value = num;
				Invalidate();
				this.ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? ValueChanged;

	public DarkVerticalScrollBar()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = UiTheme.Surface;
		Cursor = Cursors.Hand;
		base.TabStop = false;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle rectangle = TrackBounds();
		using SolidBrush brush = new SolidBrush(Color.FromArgb(45, UiTheme.Border));
		using SolidBrush brush2 = new SolidBrush(_dragging ? UiTheme.Primary : Color.FromArgb(122, 148, 180));
		e.Graphics.FillRoundedRectangle(brush, rectangle, Math.Max(2f, (float)rectangle.Width / 2f));
		e.Graphics.FillRoundedRectangle(brush2, ThumbBounds(), Math.Max(2f, (float)rectangle.Width / 2f));
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			Rectangle rectangle = ThumbBounds();
			if (rectangle.Contains(e.Location))
			{
				_dragging = true;
				_dragOffset = e.Y - rectangle.Top;
				base.Capture = true;
				Invalidate();
			}
			else
			{
				Value += ((e.Y < rectangle.Top) ? (-Math.Max(32, ViewportSize - 32)) : Math.Max(32, ViewportSize - 32));
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (_dragging && Maximum > 0)
		{
			Rectangle rectangle = TrackBounds();
			Rectangle rectangle2 = ThumbBounds();
			int num = Math.Max(1, rectangle.Height - rectangle2.Height);
			int num2 = Math.Clamp(e.Y - _dragOffset - rectangle.Top, 0, num);
			Value = (int)Math.Round((double)num2 / (double)num * (double)Maximum);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left)
		{
			_dragging = false;
			base.Capture = false;
			Invalidate();
		}
	}

	private Rectangle TrackBounds()
	{
		int num = Math.Max(4, UiTheme.Scale(this, 5));
		return new Rectangle((base.ClientSize.Width - num) / 2, UiTheme.Scale(this, 4), num, Math.Max(1, base.ClientSize.Height - UiTheme.Scale(this, 8)));
	}

	private Rectangle ThumbBounds()
	{
		Rectangle rectangle = TrackBounds();
		int num = Math.Max(1, ViewportSize + Maximum);
		int min = Math.Min(UiTheme.Scale(this, 30), rectangle.Height);
		int num2 = Math.Clamp((int)Math.Round((double)ViewportSize / (double)num * (double)rectangle.Height), min, rectangle.Height);
		int num3 = Math.Max(0, rectangle.Height - num2);
		int y = rectangle.Top + ((Maximum != 0) ? ((int)Math.Round((double)Value / (double)Maximum * (double)num3)) : 0);
		return new Rectangle(rectangle.Left, y, rectangle.Width, num2);
	}
}
