using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public class RoundedButton : Button
{
	private readonly Timer _animation = new() { Interval = 15 };
	private readonly Stopwatch _clock = new();
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
		get => _normalColor;
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
		get => _hoverColor;
		set
		{
			_hoverColor = value;
			if (PointerIsInside())
			{
				SnapTo(value);
			}
		}
	}

	public RoundedButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
			| ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.Opaque, true);
		SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
		SetStyle(ControlStyles.SupportsTransparentBackColor, false);
		DoubleBuffered = false;
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		FlatAppearance.MouseDownBackColor = Color.Transparent;
		FlatAppearance.MouseOverBackColor = Color.Transparent;
		UseVisualStyleBackColor = false;
		UseCompatibleTextRendering = false;
		AutoEllipsis = false;
		Cursor = Cursors.Hand;
		_animation.Tick += (_, _) => Animate();
		MouseEnter += (_, _) => BeginTransition(HoverColor);
		MouseLeave += (_, _) => BeginTransition(NormalColor);
		EnabledChanged += (_, _) => Invalidate();
		ParentChanged += (_, _) =>
		{
			Invalidate();
			QueueParentRepaint(Bounds);
		};
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		// The complete rectangular surface is painted in one local-buffer blit from
		// OnPaint.  Leaving WM_ERASEBKGND out of the pass prevents a half-erased
		// rounded button from becoming visible while a TableLayoutPanel is settling.
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// Do not call Button.OnPaint here.  Native button focus/default painting can
		// run after an owner-drawn pass and leave clipped text/focus fragments below
		// controls hosted in a DPI-scaled TableLayoutPanel.
		// Keep the native child-window clip exactly as WinForms supplied it. Even an
		// IntersectClip call can recreate the region in translated parent coordinates
		// on a per-monitor-DPI TableLayoutPanel and expose a strip of sibling cells.
		// Every primitive below is already bounded by ClientRectangle.
		if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
		{
			return;
		}

		// WinForms' OptimizedDoubleBuffer uses a process-wide BufferedGraphicsContext.
		// When several owner-drawn Button windows repaint during a DPI/layout wave,
		// that shared surface can briefly retain another button's origin/clip. Render
		// into a control-sized bitmap instead and commit the whole client rectangle in
		// one clipped blit. This keeps hover/playback paints inside this HWND even when
		// siblings are moving.
		using Bitmap surface = new(ClientSize.Width, ClientSize.Height,
			System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
		using Graphics graphics = Graphics.FromImage(surface);
		using (SolidBrush parentSurface = new(Parent?.BackColor ?? UiTheme.Window))
		{
			graphics.FillRectangle(parentSurface, new Rectangle(Point.Empty, ClientSize));
		}
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float scale = Math.Max(1f, DeviceDpi / 96f);
		int inset = Math.Max(1, (int)MathF.Ceiling(scale));
		Rectangle bounds = new(inset, inset, Math.Max(1, Width - inset * 2 - 1), Math.Max(1, Height - inset * 2 - 1));
		using GraphicsPath path = UiTheme.RoundedPath(bounds, UiTheme.Scale(this, CornerRadius));
		Color fill = Enabled ? BackColor : Color.FromArgb(90, BackColor);
		using SolidBrush background = new(fill);
		using Pen border = new(ContainsFocus ? UiTheme.Primary : BorderColor, (ContainsFocus ? 1.6f : 1f) * scale);
		graphics.FillPath(background, path);
		graphics.DrawPath(border, path);
		Rectangle textBounds = Rectangle.FromLTRB(
			bounds.Left + Padding.Left,
			bounds.Top + Padding.Top,
			Math.Max(bounds.Left + Padding.Left + 1, bounds.Right - Padding.Right),
			Math.Max(bounds.Top + Padding.Top + 1, bounds.Bottom - Padding.Bottom));
		StringAlignment alignment = TextAlign switch
		{
			ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => StringAlignment.Near,
			ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => StringAlignment.Far,
			_ => StringAlignment.Center
		};
		StringAlignment vertical = TextAlign switch
		{
			ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => StringAlignment.Near,
			ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => StringAlignment.Far,
			_ => StringAlignment.Center
		};
		using SolidBrush textBrush = new(Enabled ? ForeColor : UiTheme.Muted);
		using StringFormat format = new()
		{
			Alignment = alignment,
			LineAlignment = vertical,
			Trimming = StringTrimming.EllipsisCharacter,
			FormatFlags = StringFormatFlags.NoWrap,
			HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None
		};
		// TextRenderer temporarily acquires a native HDC. On a DPI-scaled nested
		// TableLayoutPanel that HDC can lose the inherited sibling-cell clip even
		// when PreserveGraphicsClipping is requested, producing caption fragments
		// below and to the right of the button. GDI+ stays on the already-clipped
		// Graphics surface for the whole pass.
		graphics.DrawString(Text, Font, textBrush, textBounds, format);
		e.Graphics.DrawImageUnscaled(surface, Point.Empty);
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams parameters = base.CreateParams;
			// Child controls in FlowLayoutPanel/TableLayoutPanel are native sibling
			// windows. Explicit sibling clipping is a final guard against a delayed
			// hover paint crossing into the neighbouring cell after a bounds change.
			parameters.Style |= 0x04000000; // WS_CLIPSIBLINGS
			return parameters;
		}
	}

	public override void NotifyDefault(bool value)
	{
		// The default-button state is represented by the explicit focus border.
		// Suppressing the native default frame keeps the rounded bounds deterministic.
	}

	protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
	{
		Rectangle previous = Bounds;
		base.SetBoundsCore(x, y, width, height, specified);
		if (Parent != null && previous != Bounds)
		{
			// TableLayoutPanel can move an auto-sized Button by a few device pixels
			// during the final per-monitor DPI layout.  The old child rectangle is not
			// always repainted by WinForms, leaving a strip of the previous text/border
			// beneath the rounded control.  Explicitly invalidate both rectangles.
			Rectangle dirty = Rectangle.Union(previous, Bounds);
			dirty.Inflate(UiTheme.Scale(this, 3), UiTheme.Scale(this, 3));
			QueueParentRepaint(dirty);
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		// Keep the native child window rectangular.  A rounded WinForms Region is
		// recalculated asynchronously by TableLayoutPanel during per-monitor DPI
		// changes; the old clipped window can then leave text/border pixels behind at
		// its former bounds.  The control is already fully owner-drawn, so clipping the
		// paint path is sufficient and gives the parent a stable invalidation surface.
		Region = null;
		Rectangle dirty = Bounds;
		dirty.Inflate(UiTheme.Scale(this, 3), UiTheme.Scale(this, 3));
		QueueParentRepaint(dirty);
	}

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		Invalidate();
		QueueParentRepaint(Bounds);
	}

	private void QueueParentRepaint(Rectangle dirty)
	{
		Control? parent = Parent;
		if (parent == null || parent.IsDisposed || dirty.Width <= 0 || dirty.Height <= 0)
		{
			return;
		}

		// Immediate invalidation keeps ordinary resize feedback responsive.  The
		// deferred pass is the important part: TableLayoutPanel can issue several
		// bounds changes in one DPI/layout wave, and an invalidation performed in the
		// middle of that wave may be discarded before the old child rectangle is
		// erased.  Coalesce those rectangles and repaint once after layout settles.
		parent.Invalidate(dirty, true);
		if (_pendingRepaintParent != parent)
		{
			_pendingRepaintParent = parent;
			_pendingParentDirty = dirty;
		}
		else
		{
			_pendingParentDirty = _pendingParentDirty.IsEmpty
				? dirty
				: Rectangle.Union(_pendingParentDirty, dirty);
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
				Control? repaintParent = _pendingRepaintParent;
				Rectangle repaintArea = _pendingParentDirty;
				_parentRepaintQueued = false;
				_pendingRepaintParent = null;
				_pendingParentDirty = Rectangle.Empty;
				if (IsDisposed || repaintParent == null || repaintParent.IsDisposed
					|| !repaintParent.IsHandleCreated || repaintArea.IsEmpty)
				{
					return;
				}
				repaintParent.Invalidate(repaintArea, true);
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
			return;
		}
		_fromColor = BackColor;
		_targetColor = target;
		_progress = 0f;
		_clock.Restart();
		_animation.Start();
	}

	private bool PointerIsInside()
	{
		return IsHandleCreated && ClientRectangle.Contains(PointToClient(MousePosition));
	}

	private void SnapTo(Color target)
	{
		// Navigation selection can change while an earlier hover/leave transition is
		// still running.  Letting that timer finish would repaint the previous page's
		// palette over the new selected state and leave a convincing stale highlight.
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
		_progress = Math.Clamp((float)_clock.Elapsed.TotalMilliseconds / TransitionMilliseconds, 0f, 1f);
		float eased = 1f - MathF.Pow(1f - _progress, 3f);
		BackColor = Blend(_fromColor, _targetColor, eased);
		Invalidate();
		if (_progress >= 1f)
		{
			_animation.Stop();
			_clock.Stop();
		}
	}

	private static Color Blend(Color from, Color to, float amount)
	{
		return Color.FromArgb(
			(int)MathF.Round(from.A + (to.A - from.A) * amount),
			(int)MathF.Round(from.R + (to.R - from.R) * amount),
			(int)MathF.Round(from.G + (to.G - from.G) * amount),
			(int)MathF.Round(from.B + (to.B - from.B) * amount));
	}

}
