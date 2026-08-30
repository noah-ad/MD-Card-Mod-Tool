using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class AnimationPreviewCanvas : Control
{
	private bool _panning;

	private Point _lastPointer;

	public Bitmap? Frame { get; set; }

	public float AnimationScale { get; set; } = 1f;

	public int ScalePercent { get; set; } = 100;

	public float PanX { get; set; }

	public float PanY { get; set; }

	public event EventHandler? ViewChanged;

	public string StatusText { get; set; } = Localizer.T("animation.preview.drop");

	public AnimationPreviewCanvas()
	{
		DoubleBuffered = true;
		BackColor = UiTheme.SurfaceAlt;
		base.ResizeRedraw = true;
		TabStop = true;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button == MouseButtons.Left)
		{
			Focus();
			_panning = true;
			_lastPointer = e.Location;
			Cursor = Cursors.SizeAll;
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (!_panning) return;
		PanX += e.X - _lastPointer.X;
		PanY += e.Y - _lastPointer.Y;
		_lastPointer = e.Location;
		Invalidate();
		ViewChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left)
		{
			_panning = false;
			Cursor = Cursors.Default;
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		float factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
		AnimationScale = Math.Clamp(AnimationScale * factor, 0.1f, 5f);
		ScalePercent = (int)Math.Round(AnimationScale * 100);
		Invalidate();
		ViewChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnDoubleClick(EventArgs e)
	{
		base.OnDoubleClick(e);
		PanX = PanY = 0;
		Invalidate();
		ViewChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics g = e.Graphics;
		using SolidBrush dark = new SolidBrush(Color.FromArgb(24, 32, 46));
		using SolidBrush light = new SolidBrush(Color.FromArgb(36, 48, 66));
		for (int y = 0; y < base.Height; y += 18)
		{
			for (int x = 0; x < base.Width; x += 18)
			{
				g.FillRectangle((((x / 18 + y / 18) & 1) == 0) ? dark : light, x, y, 18, 18);
			}
		}
		if (Frame == null)
		{
			TextRenderer.DrawText(g, StatusText, Font, base.ClientRectangle, UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
			return;
		}
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		RectangleF available = new RectangleF(16f, 16f, Math.Max(1, base.ClientSize.Width - 32), Math.Max(1, base.ClientSize.Height - 32));
		float gameAspect = 1.7777778f;
		float viewportWidth = available.Width;
		float viewportHeight = viewportWidth / gameAspect;
		if (viewportHeight > available.Height)
		{
			viewportHeight = available.Height;
			viewportWidth = viewportHeight * gameAspect;
		}
		RectangleF viewport = new RectangleF(available.X + (available.Width - viewportWidth) / 2f, available.Y + (available.Height - viewportHeight) / 2f, viewportWidth, viewportHeight);
		float fit = Math.Min(viewport.Width / (float)Frame.Width, viewport.Height / (float)Frame.Height) * AnimationScale;
		float width = (float)Frame.Width * fit;
		float height = (float)Frame.Height * fit;
		RectangleF target = new RectangleF(viewport.X + (viewport.Width - width) / 2f + PanX, viewport.Y + (viewport.Height - height) / 2f + PanY, width, height);
		GraphicsState state = g.Save();
		g.SetClip(viewport);
		g.DrawImage(Frame, target);
		g.Restore(state);
		using Pen border = new Pen(Color.FromArgb(130, UiTheme.Primary), 1f);
		g.DrawRectangle(border, viewport.X, viewport.Y, viewport.Width, viewport.Height);
		TextRenderer.DrawText(g, string.Format(Localizer.T("animation.preview.canvas"), ScalePercent), Font,
			Rectangle.Round(viewport), UiTheme.Primary, TextFormatFlags.Right | TextFormatFlags.NoPadding);
	}
}
