using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class DarkScrollPanel : UserControl
{
	private readonly Panel _content = new Panel
	{
		Location = Point.Empty,
		Margin = Padding.Empty,
		Padding = Padding.Empty,
		BackColor = UiTheme.Surface
	};

	private readonly DarkVerticalScrollBar _scroll = new DarkVerticalScrollBar
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
		get
		{
			return _contentHeight;
		}
		set
		{
			if (_contentHeight != Math.Max(0, value))
			{
				_contentHeight = Math.Max(0, value);
				PerformLayout();
			}
		}
	}

	public DarkScrollPanel()
	{
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = UiTheme.Surface;
		base.Controls.Add(_content);
		base.Controls.Add(_scroll);
		_scroll.ValueChanged += delegate
		{
			PositionContent();
		};
		base.MouseWheel += OnWheel;
		_content.MouseWheel += OnWheel;
		_content.ControlAdded += delegate(object? _, ControlEventArgs e)
		{
			AttachWheel(e.Control);
		};
	}

	protected override void OnLayout(LayoutEventArgs e)
	{
		base.OnLayout(e);
		int num = Math.Max(0, base.ClientSize.Height);
		int num2 = (UseExplicitContentHeight ? _contentHeight : Math.Max(_contentHeight, MeasureContentHeight()));
		bool flag = num2 > num;
		_scroll.Visible = flag;
		_scroll.ViewportSize = num;
		_scroll.Maximum = Math.Max(0, num2 - num);
		int width = Math.Max(0, base.ClientSize.Width - (flag ? (_scroll.Width + 4) : 0));
		_content.Size = new Size(width, Math.Max(num, num2));
		PositionContent();
	}

	private int MeasureContentHeight()
	{
		int num = 0;
		foreach (Control control in _content.Controls)
		{
			num = Math.Max(num, control.Bottom + control.Margin.Bottom);
		}
		return num;
	}

	private void PositionContent()
	{
		_content.Location = new Point(0, -_scroll.Value);
	}

	private void OnWheel(object? sender, MouseEventArgs e)
	{
		if (_scroll.Visible && e.Delta != 0)
		{
			int num = Math.Max(1, Math.Abs(e.Delta) / SystemInformation.MouseWheelScrollDelta);
			_scroll.Value += Math.Sign(-e.Delta) * num * UiTheme.Scale(this, 38);
		}
	}

	private void AttachWheel(Control control)
	{
		control.MouseWheel += OnWheel;
		control.Enter += delegate
		{
			Reveal(control);
		};
		control.ControlAdded += delegate(object? _, ControlEventArgs e)
		{
			AttachWheel(e.Control);
		};
		foreach (Control control2 in control.Controls)
		{
			AttachWheel(control2);
		}
	}

	private void Reveal(Control control)
	{
		if (_scroll.Visible && control.IsHandleCreated)
		{
			Rectangle rectangle = _content.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
			if (rectangle.Top < _scroll.Value)
			{
				_scroll.Value = rectangle.Top;
			}
			else if (rectangle.Bottom > _scroll.Value + base.ClientSize.Height)
			{
				_scroll.Value = rectangle.Bottom - base.ClientSize.Height;
			}
		}
	}
}
