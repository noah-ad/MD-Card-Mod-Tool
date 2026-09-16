using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class AnimationPreviewCanvas : Control
{
	private bool _panning;

	private Point _lastPointer;

	private readonly Bitmap _checkerTile;

	private readonly TextureBrush _checkerBrush;

	public Bitmap? Frame { get; set; }

	public float AnimationScale { get; set; } = 1f;

	public int ScalePercent { get; set; } = 100;

	public float PanX { get; set; }

	public float PanY { get; set; }

	public string StatusText { get; set; } = Localizer.T("animation.preview.drop");

	public event EventHandler? ViewChanged;

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_checkerBrush.Dispose();
			_checkerTile.Dispose();
		}
		base.Dispose(disposing);
	}

	public AnimationPreviewCanvas()
	{
		DoubleBuffered = true;
		BackColor = UiTheme.SurfaceAlt;
		base.ResizeRedraw = true;
		base.TabStop = true;
		_checkerTile = new Bitmap(36, 36);
		using (Graphics graphics = Graphics.FromImage(_checkerTile))
		{
			graphics.Clear(Color.FromArgb(24, 32, 46));
			using SolidBrush brush = new SolidBrush(Color.FromArgb(36, 48, 66));
			graphics.FillRectangle(brush, 18, 0, 18, 18);
			graphics.FillRectangle(brush, 0, 18, 18, 18);
		}
		_checkerBrush = new TextureBrush(_checkerTile, WrapMode.Tile);
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
		if (_panning)
		{
			PanX += e.X - _lastPointer.X;
			PanY += e.Y - _lastPointer.Y;
			_lastPointer = e.Location;
			Invalidate();
			this.ViewChanged?.Invoke(this, EventArgs.Empty);
		}
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
		float num = ((e.Delta > 0) ? 1.1f : 0.9090909f);
		AnimationScale = Math.Clamp(AnimationScale * num, 0.1f, 5f);
		ScalePercent = (int)Math.Round(AnimationScale * 100f);
		Invalidate();
		this.ViewChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnDoubleClick(EventArgs e)
	{
		base.OnDoubleClick(e);
		float panX = (PanY = 0f);
		PanX = panX;
		Invalidate();
		this.ViewChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		graphics.FillRectangle(_checkerBrush, base.ClientRectangle);
		if (Frame == null)
		{
			TextRenderer.DrawText(graphics, StatusText, Font, base.ClientRectangle, UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
			return;
		}
		graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		RectangleF rectangleF = new RectangleF(16f, 16f, Math.Max(1, base.ClientSize.Width - 32), Math.Max(1, base.ClientSize.Height - 32));
		float num = 1.7777778f;
		float num2 = rectangleF.Width;
		float num3 = num2 / num;
		if (num3 > rectangleF.Height)
		{
			num3 = rectangleF.Height;
			num2 = num3 * num;
		}
		RectangleF rectangleF2 = new RectangleF(rectangleF.X + (rectangleF.Width - num2) / 2f, rectangleF.Y + (rectangleF.Height - num3) / 2f, num2, num3);
		float num4 = Math.Min(rectangleF2.Width / (float)Frame.Width, rectangleF2.Height / (float)Frame.Height) * AnimationScale;
		float num5 = (float)Frame.Width * num4;
		float num6 = (float)Frame.Height * num4;
		RectangleF rect = new RectangleF(rectangleF2.X + (rectangleF2.Width - num5) / 2f + PanX, rectangleF2.Y + (rectangleF2.Height - num6) / 2f + PanY, num5, num6);
		GraphicsState gstate = graphics.Save();
		graphics.SetClip(rectangleF2);
		graphics.DrawImage(Frame, rect);
		graphics.Restore(gstate);
		using Pen pen = new Pen(Color.FromArgb(130, UiTheme.Primary), 1f);
		graphics.DrawRectangle(pen, rectangleF2.X, rectangleF2.Y, rectangleF2.Width, rectangleF2.Height);
		TextRenderer.DrawText(graphics, string.Format(Localizer.T("animation.preview.canvas"), ScalePercent), Font, Rectangle.Round(rectangleF2), UiTheme.Primary, TextFormatFlags.Right | TextFormatFlags.NoPadding);
	}
}
