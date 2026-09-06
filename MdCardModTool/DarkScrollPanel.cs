using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// A small owner-drawn vertical scroller used where the native Win32 white
/// scrollbar would break the dark Master Duel workspace.  Child controls live
/// in <see cref="ContentPanel"/> and retain normal keyboard/mouse behaviour.
/// </summary>
public sealed class DarkScrollPanel : UserControl
{
	private readonly Panel _content = new()
	{
		Location = Point.Empty,
		Margin = Padding.Empty,
		Padding = Padding.Empty,
		BackColor = UiTheme.Surface
	};

	private readonly DarkVerticalScrollBar _scroll = new()
	{
		Dock = DockStyle.Right,
		Width = 12,
		Visible = false
	};

	private int _contentHeight;

	public Panel ContentPanel => _content;
	public bool UseExplicitContentHeight { get; set; }

	public int ContentHeight
	{
		get => _contentHeight;
		set
		{
			if (_contentHeight == Math.Max(0, value)) return;
			_contentHeight = Math.Max(0, value);
			PerformLayout();
		}
	}

	public DarkScrollPanel()
	{
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw, true);
		BackColor = UiTheme.Surface;
		Controls.Add(_content);
		Controls.Add(_scroll);
		_scroll.ValueChanged += (_, _) => PositionContent();
		MouseWheel += OnWheel;
		_content.MouseWheel += OnWheel;
		_content.ControlAdded += (_, e) => AttachWheel(e.Control);
	}

	protected override void OnLayout(LayoutEventArgs e)
	{
		base.OnLayout(e);
		int viewportHeight = Math.Max(0, ClientSize.Height);
		int measured = UseExplicitContentHeight ? _contentHeight : Math.Max(_contentHeight, MeasureContentHeight());
		bool needsScroll = measured > viewportHeight;
		_scroll.Visible = needsScroll;
		_scroll.ViewportSize = viewportHeight;
		_scroll.Maximum = Math.Max(0, measured - viewportHeight);
		int contentWidth = Math.Max(0, ClientSize.Width - (needsScroll ? _scroll.Width + 4 : 0));
		_content.Size = new Size(contentWidth, Math.Max(viewportHeight, measured));
		PositionContent();
	}

	private int MeasureContentHeight()
	{
		int bottom = 0;
		foreach (Control child in _content.Controls)
		{
			bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom);
		}
		return bottom;
	}

	private void PositionContent()
	{
		_content.Location = new Point(0, -_scroll.Value);
	}

	private void OnWheel(object? sender, MouseEventArgs e)
	{
		if (!_scroll.Visible || e.Delta == 0) return;
		int steps = Math.Max(1, Math.Abs(e.Delta) / SystemInformation.MouseWheelScrollDelta);
		_scroll.Value += Math.Sign(-e.Delta) * steps * UiTheme.Scale(this, 38);
	}

	private void AttachWheel(Control control)
	{
		control.MouseWheel += OnWheel;
		control.Enter += (_, _) => Reveal(control);
		control.ControlAdded += (_, e) => AttachWheel(e.Control);
		foreach (Control child in control.Controls) AttachWheel(child);
	}

	private void Reveal(Control control)
	{
		if (!_scroll.Visible || !control.IsHandleCreated) return;
		Rectangle bounds = _content.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
		if (bounds.Top < _scroll.Value) _scroll.Value = bounds.Top;
		else if (bounds.Bottom > _scroll.Value + ClientSize.Height)
			_scroll.Value = bounds.Bottom - ClientSize.Height;
	}
}

internal sealed class DarkVerticalScrollBar : Control
{
	private int _maximum;
	private int _value;
	private int _viewportSize;
	private bool _dragging;
	private int _dragOffset;

	public event EventHandler? ValueChanged;

	public int Maximum
	{
		get => _maximum;
		set
		{
			_maximum = Math.Max(0, value);
			Value = _value;
			Invalidate();
		}
	}

	public int ViewportSize
	{
		get => _viewportSize;
		set
		{
			_viewportSize = Math.Max(0, value);
			Invalidate();
		}
	}

	public int Value
	{
		get => _value;
		set
		{
			int next = Math.Clamp(value, 0, Maximum);
			if (_value == next) return;
			_value = next;
			Invalidate();
			ValueChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	public DarkVerticalScrollBar()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		BackColor = UiTheme.Surface;
		Cursor = Cursors.Hand;
		TabStop = false;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle track = TrackBounds();
		using SolidBrush trackBrush = new(Color.FromArgb(45, UiTheme.Border));
		using SolidBrush thumbBrush = new(_dragging ? UiTheme.Primary : Color.FromArgb(122, 148, 180));
		e.Graphics.FillRoundedRectangle(trackBrush, track, Math.Max(2, track.Width / 2f));
		e.Graphics.FillRoundedRectangle(thumbBrush, ThumbBounds(), Math.Max(2, track.Width / 2f));
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button != MouseButtons.Left) return;
		Rectangle thumb = ThumbBounds();
		if (thumb.Contains(e.Location))
		{
			_dragging = true;
			_dragOffset = e.Y - thumb.Top;
			Capture = true;
			Invalidate();
		}
		else
		{
			Value += e.Y < thumb.Top ? -Math.Max(32, ViewportSize - 32) : Math.Max(32, ViewportSize - 32);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (!_dragging || Maximum <= 0) return;
		Rectangle track = TrackBounds();
		Rectangle thumb = ThumbBounds();
		int travel = Math.Max(1, track.Height - thumb.Height);
		int position = Math.Clamp(e.Y - _dragOffset - track.Top, 0, travel);
		Value = (int)Math.Round((double)position / travel * Maximum);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button != MouseButtons.Left) return;
		_dragging = false;
		Capture = false;
		Invalidate();
	}

	private Rectangle TrackBounds()
	{
		int width = Math.Max(4, UiTheme.Scale(this, 5));
		return new Rectangle((ClientSize.Width - width) / 2, UiTheme.Scale(this, 4), width,
			Math.Max(1, ClientSize.Height - UiTheme.Scale(this, 8)));
	}

	private Rectangle ThumbBounds()
	{
		Rectangle track = TrackBounds();
		int total = Math.Max(1, ViewportSize + Maximum);
		int minimumThumb = Math.Min(UiTheme.Scale(this, 30), track.Height);
		int height = Math.Clamp((int)Math.Round((double)ViewportSize / total * track.Height),
			minimumThumb, track.Height);
		int travel = Math.Max(0, track.Height - height);
		int top = track.Top + (Maximum == 0 ? 0 : (int)Math.Round((double)Value / Maximum * travel));
		return new Rectangle(track.Left, top, track.Width, height);
	}
}
