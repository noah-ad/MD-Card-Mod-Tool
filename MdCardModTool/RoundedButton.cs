using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MdCardModTool;

public class RoundedButton : Button
{
	private readonly Timer _animation = new Timer
	{
		Interval = 15
	};

	private readonly Stopwatch _clock = new Stopwatch();

	private Bitmap? _paintSurface;

	private Color _normalColor = UiTheme.Elevated;

	private Color _hoverColor = UiTheme.SurfaceAlt;

	private Color _fromColor;

	private Color _targetColor;

	private float _progress = 1f;

	private Rectangle _pendingParentDirty = Rectangle.Empty;

	private Control? _pendingRepaintParent;

	private bool _parentRepaintQueued;

	public int CornerRadius { get; set; } = 8;

	public Color BorderColor { get; set; } = UiTheme.Border;

	public int TransitionMilliseconds { get; set; } = 160;

	internal bool UsesSharedOptimizedBuffer => GetStyle(ControlStyles.OptimizedDoubleBuffer);

	public Color NormalColor
	{
		get
		{
			return _normalColor;
		}
		set
		{
			_normalColor = value;
			if (!PointerIsInside())
			{
				SnapTo(value);
			}
		}
	}

	public Color HoverColor
	{
		get
		{
			return _hoverColor;
		}
		set
		{
			_hoverColor = value;
			if (PointerIsInside())
			{
				SnapTo(value);
			}
		}
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.Style |= 67108864;
			return createParams;
		}
	}

	public RoundedButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint, value: true);
		SetStyle(ControlStyles.OptimizedDoubleBuffer, value: false);
		SetStyle(ControlStyles.SupportsTransparentBackColor, value: false);
		DoubleBuffered = false;
		base.FlatStyle = FlatStyle.Flat;
		base.FlatAppearance.BorderSize = 0;
		base.FlatAppearance.MouseDownBackColor = Color.Transparent;
		base.FlatAppearance.MouseOverBackColor = Color.Transparent;
		base.UseVisualStyleBackColor = false;
		base.UseCompatibleTextRendering = false;
		base.AutoEllipsis = false;
		Cursor = Cursors.Hand;
		_animation.Tick += delegate
		{
			Animate();
		};
		base.MouseEnter += delegate
		{
			BeginTransition(HoverColor);
		};
		base.MouseLeave += delegate
		{
			BeginTransition(NormalColor);
		};
		base.EnabledChanged += delegate
		{
			Invalidate();
		};
		base.ParentChanged += delegate
		{
			Invalidate();
			QueueParentRepaint(base.Bounds);
		};
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		if (base.ClientSize.Width <= 0 || base.ClientSize.Height <= 0)
		{
			return;
		}
		if (_paintSurface == null || _paintSurface.Size != base.ClientSize || Math.Abs(_paintSurface.HorizontalResolution - (float)base.DeviceDpi) > 0.1f)
		{
			_paintSurface?.Dispose();
			_paintSurface = new Bitmap(base.ClientSize.Width, base.ClientSize.Height, PixelFormat.Format32bppPArgb);
			_paintSurface.SetResolution(base.DeviceDpi, base.DeviceDpi);
		}
		Bitmap paintSurface = _paintSurface;
		using Graphics graphics = Graphics.FromImage(paintSurface);
		using (SolidBrush brush = new SolidBrush(base.Parent?.BackColor ?? UiTheme.Window))
		{
			graphics.FillRectangle(brush, new Rectangle(Point.Empty, base.ClientSize));
		}
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float num = Math.Max(1f, (float)base.DeviceDpi / 96f);
		int num2 = Math.Max(1, (int)MathF.Ceiling(num));
		Rectangle bounds = new Rectangle(num2, num2, Math.Max(1, base.Width - num2 * 2 - 1), Math.Max(1, base.Height - num2 * 2 - 1));
		using GraphicsPath path = UiTheme.RoundedPath(bounds, UiTheme.Scale(this, CornerRadius));
		using SolidBrush brush2 = new SolidBrush(base.Enabled ? BackColor : Color.FromArgb(90, BackColor));
		using Pen pen = new Pen(base.ContainsFocus ? UiTheme.Primary : BorderColor, (base.ContainsFocus ? 1.6f : 1f) * num);
		graphics.FillPath(brush2, path);
		graphics.DrawPath(pen, path);
		Rectangle rectangle = Rectangle.FromLTRB(bounds.Left + base.Padding.Left, bounds.Top + base.Padding.Top, Math.Max(bounds.Left + base.Padding.Left + 1, bounds.Right - base.Padding.Right), Math.Max(bounds.Top + base.Padding.Top + 1, bounds.Bottom - base.Padding.Bottom));
		StringAlignment stringAlignment;
		switch (TextAlign)
		{
		case ContentAlignment.TopLeft:
		case ContentAlignment.MiddleLeft:
		case ContentAlignment.BottomLeft:
			stringAlignment = StringAlignment.Near;
			break;
		case ContentAlignment.TopRight:
		case ContentAlignment.MiddleRight:
		case ContentAlignment.BottomRight:
			stringAlignment = StringAlignment.Far;
			break;
		default:
			stringAlignment = StringAlignment.Center;
			break;
		}
		StringAlignment alignment = stringAlignment;
		switch (TextAlign)
		{
		case ContentAlignment.TopLeft:
		case ContentAlignment.TopCenter:
		case ContentAlignment.TopRight:
			stringAlignment = StringAlignment.Near;
			break;
		case ContentAlignment.BottomLeft:
		case ContentAlignment.BottomCenter:
		case ContentAlignment.BottomRight:
			stringAlignment = StringAlignment.Far;
			break;
		default:
			stringAlignment = StringAlignment.Center;
			break;
		}
		StringAlignment lineAlignment = stringAlignment;
		using SolidBrush brush3 = new SolidBrush(base.Enabled ? ForeColor : UiTheme.Muted);
		using StringFormat format = new StringFormat
		{
			Alignment = alignment,
			LineAlignment = lineAlignment,
			Trimming = StringTrimming.EllipsisCharacter,
			FormatFlags = StringFormatFlags.NoWrap,
			HotkeyPrefix = HotkeyPrefix.None
		};
		graphics.DrawString(Text, Font, brush3, rectangle, format);
		e.Graphics.DrawImageUnscaled(paintSurface, Point.Empty);
	}

	public override void NotifyDefault(bool value)
	{
	}

	protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
	{
		Rectangle bounds = base.Bounds;
		base.SetBoundsCore(x, y, width, height, specified);
		if (base.Parent != null && bounds != base.Bounds)
		{
			Rectangle dirty = Rectangle.Union(bounds, base.Bounds);
			dirty.Inflate(UiTheme.Scale(this, 3), UiTheme.Scale(this, 3));
			QueueParentRepaint(dirty);
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		base.Region = null;
		Rectangle bounds = base.Bounds;
		bounds.Inflate(UiTheme.Scale(this, 3), UiTheme.Scale(this, 3));
		QueueParentRepaint(bounds);
	}

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		Invalidate();
		QueueParentRepaint(base.Bounds);
	}

	private void QueueParentRepaint(Rectangle dirty)
	{
		Control parent = base.Parent;
		if (parent == null || parent.IsDisposed || dirty.Width <= 0 || dirty.Height <= 0)
		{
			return;
		}
		parent.Invalidate(dirty, invalidateChildren: true);
		if (_pendingRepaintParent != parent)
		{
			_pendingRepaintParent = parent;
			_pendingParentDirty = dirty;
		}
		else
		{
			_pendingParentDirty = (_pendingParentDirty.IsEmpty ? dirty : Rectangle.Union(_pendingParentDirty, dirty));
		}
		if (_parentRepaintQueued || !parent.IsHandleCreated)
		{
			return;
		}
		_parentRepaintQueued = true;
		try
		{
			parent.BeginInvoke((MethodInvoker)delegate
			{
				Control pendingRepaintParent = _pendingRepaintParent;
				Rectangle pendingParentDirty = _pendingParentDirty;
				_parentRepaintQueued = false;
				_pendingRepaintParent = null;
				_pendingParentDirty = Rectangle.Empty;
				if (!base.IsDisposed && pendingRepaintParent != null && !pendingRepaintParent.IsDisposed && pendingRepaintParent.IsHandleCreated && !pendingParentDirty.IsEmpty)
				{
					pendingRepaintParent.Invalidate(pendingParentDirty, invalidateChildren: true);
				}
			});
		}
		catch (InvalidOperationException)
		{
			_parentRepaintQueued = false;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_animation.Dispose();
			_paintSurface?.Dispose();
			_paintSurface = null;
		}
		base.Dispose(disposing);
	}

	private void BeginTransition(Color target)
	{
		if (MotionPreferences.ReduceMotion || TransitionMilliseconds <= 0)
		{
			_animation.Stop();
			BackColor = target;
			Invalidate();
		}
		else
		{
			_fromColor = BackColor;
			_targetColor = target;
			_progress = 0f;
			_clock.Restart();
			_animation.Start();
		}
	}

	private bool PointerIsInside()
	{
		if (base.IsHandleCreated)
		{
			return base.ClientRectangle.Contains(PointToClient(Control.MousePosition));
		}
		return false;
	}

	private void SnapTo(Color target)
	{
		_animation.Stop();
		_clock.Reset();
		_progress = 1f;
		_fromColor = target;
		_targetColor = target;
		BackColor = target;
		Invalidate();
	}

	private void Animate()
	{
		_progress = Math.Clamp((float)_clock.Elapsed.TotalMilliseconds / (float)TransitionMilliseconds, 0f, 1f);
		float amount = 1f - MathF.Pow(1f - _progress, 3f);
		BackColor = Blend(_fromColor, _targetColor, amount);
		Invalidate();
		if (_progress >= 1f)
		{
			_animation.Stop();
			_clock.Stop();
		}
	}

	private static Color Blend(Color from, Color to, float amount)
	{
		return Color.FromArgb((int)MathF.Round((float)(int)from.A + (float)(to.A - from.A) * amount), (int)MathF.Round((float)(int)from.R + (float)(to.R - from.R) * amount), (int)MathF.Round((float)(int)from.G + (float)(to.G - from.G) * amount), (int)MathF.Round((float)(int)from.B + (float)(to.B - from.B) * amount));
	}
}
