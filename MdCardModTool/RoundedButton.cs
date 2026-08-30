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

	public int CornerRadius { get; set; } = 8;

	public Color BorderColor { get; set; } = UiTheme.Border;

	public int TransitionMilliseconds { get; set; } = 160;

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
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
		SetStyle(ControlStyles.SupportsTransparentBackColor, false);
		DoubleBuffered = true;
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
		ParentChanged += (_, _) => Invalidate();
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		// WinForms still prepares a rectangular button background even when the
		// foreground is custom drawn. Clearing it with the actual parent surface
		// removes the dark square fringe around anti-aliased corners.
		e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Window);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// Do not call Button.OnPaint here.  Native button focus/default painting can
		// run after an owner-drawn pass and leave clipped text/focus fragments below
		// controls hosted in a DPI-scaled TableLayoutPanel.
		using Region oldClip = e.Graphics.Clip;
		e.Graphics.SetClip(ClientRectangle, CombineMode.Replace);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		float scale = Math.Max(1f, DeviceDpi / 96f);
		int inset = Math.Max(1, (int)MathF.Ceiling(scale));
		Rectangle bounds = new(inset, inset, Math.Max(1, Width - inset * 2 - 1), Math.Max(1, Height - inset * 2 - 1));
		using GraphicsPath path = UiTheme.RoundedPath(bounds, UiTheme.Scale(this, CornerRadius));
		Color fill = Enabled ? BackColor : Color.FromArgb(90, BackColor);
		using SolidBrush background = new(fill);
		using Pen border = new(ContainsFocus ? UiTheme.Primary : BorderColor, (ContainsFocus ? 1.6f : 1f) * scale);
		e.Graphics.FillPath(background, path);
		e.Graphics.DrawPath(border, path);
		Rectangle textBounds = Rectangle.FromLTRB(
			bounds.Left + Padding.Left,
			bounds.Top + Padding.Top,
			Math.Max(bounds.Left + Padding.Left + 1, bounds.Right - Padding.Right),
			Math.Max(bounds.Top + Padding.Top + 1, bounds.Bottom - Padding.Bottom));
		TextFormatFlags alignment = TextAlign switch
		{
			ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
			ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
			_ => TextFormatFlags.HorizontalCenter
		};
		TextFormatFlags vertical = TextAlign switch
		{
			ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
			ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
			_ => TextFormatFlags.VerticalCenter
		};
		TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : UiTheme.Muted,
			alignment | vertical | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
			| TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping
			| TextFormatFlags.PreserveGraphicsTranslateTransform);
		e.Graphics.Clip = oldClip;
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
			Parent.Invalidate(dirty, true);
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
		BeginInvoke((MethodInvoker)delegate
		{
			if (!IsDisposed && Parent != null)
			{
				Rectangle dirty = Bounds;
				dirty.Inflate(UiTheme.Scale(this, 3), UiTheme.Scale(this, 3));
				Parent.Invalidate(dirty, true);
			}
		});
	}

	protected override void OnSizeChanged(EventArgs e)
	{
		base.OnSizeChanged(e);
		Invalidate();
		Parent?.Invalidate(Bounds, true);
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
