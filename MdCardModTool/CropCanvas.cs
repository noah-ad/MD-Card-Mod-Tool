using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class CropCanvas : Control
{
	private readonly int _targetWidth;

	private readonly int _targetHeight;

	private readonly bool _fullCardOverlay;

	private readonly bool _overFrameEditing;

	private Bitmap? _source;

	private Bitmap? _background;

	private Bitmap? _frame;

	private Bitmap? _renderedPreview;

	private bool _showRenderedPreview;

	private RectangleF _artWindow;

	private float _zoom = 1f;

	private float _offsetX;

	private float _offsetY;

	private bool _dragging;

	private Point _lastMouse;

	private ImageRenderSpec _backgroundSpec;

	public bool EditingBackground { get; private set; }

	private float BackgroundFit
	{
		get
		{
			if (_background != null)
			{
				return Math.Max((float)_targetWidth / (float)_background.Width, (float)_targetHeight / (float)_background.Height);
			}
			return 1f;
		}
	}

	public ImageRenderSpec BackgroundRenderSpec => _backgroundSpec;

	public ImageRenderSpec ActiveRenderSpec
	{
		get
		{
			if (!EditingBackground)
			{
				return RenderSpec;
			}
			return _backgroundSpec;
		}
	}

	public float Zoom
	{
		get
		{
			if (!EditingBackground)
			{
				return _zoom;
			}
			return _backgroundSpec.ImageScale / BackgroundFit;
		}
	}

	public bool HasFrame => _frame != null;

	public bool IsOverFrameEditing => _overFrameEditing;

	public bool ShowingRenderedPreview
	{
		get
		{
			if (_showRenderedPreview)
			{
				return _renderedPreview != null;
			}
			return false;
		}
	}

	public SizeF VisualArtSize
	{
		get
		{
			if (_overFrameEditing || !HasFrame || _fullCardOverlay)
			{
				return new SizeF(_targetWidth, _targetHeight);
			}
			return _artWindow.Size;
		}
	}

	private RectangleF CardRectangle
	{
		get
		{
			float num = Math.Max(1f, (float)base.ClientSize.Width - 60f);
			float num2 = Math.Max(1f, (float)base.ClientSize.Height - 60f);
			float num3 = _frame?.Width ?? _targetWidth;
			float num4 = _frame?.Height ?? _targetHeight;
			float num5 = num3 / num4;
			float num6;
			float num7;
			if (num / num2 > num5)
			{
				num6 = num2;
				num7 = num6 * num5;
			}
			else
			{
				num7 = num;
				num6 = num7 / num5;
			}
			return new RectangleF(((float)base.ClientSize.Width - num7) / 2f, ((float)base.ClientSize.Height - num6) / 2f, num7, num6);
		}
	}

	private RectangleF WorkRectangle
	{
		get
		{
			if (_frame != null && (_overFrameEditing || !_fullCardOverlay))
			{
				RectangleF cardRectangle = CardRectangle;
				float num = cardRectangle.Width / (float)_frame.Width;
				float num2 = cardRectangle.Height / (float)_frame.Height;
				return new RectangleF(cardRectangle.Left + _artWindow.Left * num, cardRectangle.Top + _artWindow.Top * num2, _artWindow.Width * num, _artWindow.Height * num2);
			}
			float num3 = Math.Max(1f, (float)base.ClientSize.Width - 92f);
			float num4 = Math.Max(1f, (float)base.ClientSize.Height - 92f);
			float num5 = (float)_targetWidth / (float)_targetHeight;
			float num6;
			float num7;
			if (num3 / num4 > num5)
			{
				num6 = num4;
				num7 = num6 * num5;
			}
			else
			{
				num7 = num3;
				num6 = num7 / num5;
			}
			return new RectangleF(((float)base.ClientSize.Width - num7) / 2f, ((float)base.ClientSize.Height - num6) / 2f, num7, num6);
		}
	}

	private float FitScale
	{
		get
		{
			if (_source == null)
			{
				return 1f;
			}
			RectangleF workRectangle = WorkRectangle;
			return Math.Max(workRectangle.Width / (float)_source.Width, workRectangle.Height / (float)_source.Height);
		}
	}

	private float DisplayScale => FitScale * _zoom;

	private RectangleF ImageRectangle
	{
		get
		{
			if (_source == null)
			{
				return RectangleF.Empty;
			}
			RectangleF workRectangle = WorkRectangle;
			float displayScale = DisplayScale;
			float num = (float)_source.Width * displayScale;
			float num2 = (float)_source.Height * displayScale;
			return new RectangleF(workRectangle.Left + workRectangle.Width / 2f + _offsetX - num / 2f, workRectangle.Top + workRectangle.Height / 2f + _offsetY - num2 / 2f, num, num2);
		}
	}

	public ImageRenderSpec RenderSpec
	{
		get
		{
			if (_overFrameEditing)
			{
				RectangleF cardRectangle = CardRectangle;
				RectangleF workRectangle = WorkRectangle;
				float num = cardRectangle.Width / (float)_targetWidth;
				return new ImageRenderSpec(_targetWidth, _targetHeight, DisplayScale / num, (workRectangle.Left + workRectangle.Width / 2f + _offsetX - (cardRectangle.Left + cardRectangle.Width / 2f)) / num, (workRectangle.Top + workRectangle.Height / 2f + _offsetY - (cardRectangle.Top + cardRectangle.Height / 2f)) / num);
			}
			RectangleF workRectangle2 = WorkRectangle;
			SizeF visualArtSize = VisualArtSize;
			float num2 = visualArtSize.Width / workRectangle2.Width;
			float num3 = visualArtSize.Height / workRectangle2.Height;
			return new ImageRenderSpec(visualArtSize.Width, visualArtSize.Height, DisplayScale * num2, _offsetX * num2, _offsetY * num3);
		}
	}

	public event Action<float>? ZoomChanged;

	public event Action<ImageRenderSpec>? ViewChanged;

	public void EditBackground(bool enabled)
	{
		EditingBackground = enabled && _background != null;
		Invalidate();
		RaiseViewChanged();
	}

	public void SetBackgroundRenderSpec(ImageRenderSpec spec)
	{
		if (float.IsFinite(spec.ImageScale) && !(spec.ImageScale <= 0f) && float.IsFinite(spec.OffsetX) && float.IsFinite(spec.OffsetY))
		{
			_backgroundSpec = spec with
			{
				VisualWidth = _targetWidth,
				VisualHeight = _targetHeight
			};
			Invalidate();
			RaiseViewChanged();
		}
	}

	public byte[]? RenderBackgroundToTarget()
	{
		if (_background != null)
		{
			return ImageCropService.RenderToTarget(_background, _backgroundSpec, _targetWidth, _targetHeight);
		}
		return null;
	}

	private void MoveActive(float dx, float dy)
	{
		if (EditingBackground)
		{
			float num = CardRectangle.Width / (float)_targetWidth;
			_backgroundSpec = _backgroundSpec with
			{
				OffsetX = _backgroundSpec.OffsetX + dx / num,
				OffsetY = _backgroundSpec.OffsetY + dy / num
			};
		}
		else
		{
			_offsetX += dx;
			_offsetY += dy;
			ClampOffset();
		}
	}

	public CropCanvas(Bitmap source, int targetWidth, int targetHeight, bool fullCardOverlay = false, bool overFrameEditing = false)
	{
		_source = source;
		_targetWidth = targetWidth;
		_targetHeight = targetHeight;
		_fullCardOverlay = fullCardOverlay;
		_overFrameEditing = overFrameEditing;
		DoubleBuffered = true;
		BackColor = Color.FromArgb(7, 11, 19);
		Cursor = Cursors.Hand;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	public void SetFrame(Bitmap frame, bool preserveView = false, RectangleF? artWindow = null)
	{
		ImageRenderSpec? imageRenderSpec = ((preserveView && _source != null && _frame != null) ? new ImageRenderSpec?(RenderSpec) : ((ImageRenderSpec?)null));
		_frame?.Dispose();
		_frame = frame;
		_artWindow = artWindow ?? CardFrameRenderer.FindArtWindow(frame);
		if (imageRenderSpec.HasValue)
		{
			SetRenderSpec(imageRenderSpec.Value);
		}
		else
		{
			ResetArtView();
		}
	}

	public void SetSource(Bitmap source, bool resetView = true)
	{
		ArgumentNullException.ThrowIfNull(source, "source");
		_source?.Dispose();
		_source = source;
		if (resetView)
		{
			ResetArtView();
			return;
		}
		ClampOffset();
		Invalidate();
		RaiseViewChanged();
	}

	public void SetBackground(Bitmap? background)
	{
		_background?.Dispose();
		_background = background;
		_backgroundSpec = new ImageRenderSpec(_targetWidth, _targetHeight, BackgroundFit, 0f, 0f);
		if (background == null)
		{
			EditingBackground = false;
		}
		Invalidate();
	}

	public void SetRenderedPreview(Bitmap? preview)
	{
		_renderedPreview?.Dispose();
		_renderedPreview = preview;
		Invalidate();
	}

	public void SetRenderedPreviewVisible(bool visible)
	{
		_showRenderedPreview = visible;
		Cursor = (ShowingRenderedPreview ? Cursors.Default : Cursors.Hand);
		Invalidate();
	}

	public void DisposeFrame()
	{
		_frame?.Dispose();
		_frame = null;
	}

	public void ResetView()
	{
		if (EditingBackground)
		{
			SetBackgroundRenderSpec(new ImageRenderSpec(_targetWidth, _targetHeight, BackgroundFit, 0f, 0f));
			return;
		}
		_zoom = 1f;
		_offsetX = 0f;
		_offsetY = 0f;
		ClampOffset();
		Invalidate();
		RaiseViewChanged();
	}

	public void ShowWholeImage()
	{
		if (EditingBackground && _background != null)
		{
			SetBackgroundRenderSpec(new ImageRenderSpec(_targetWidth, _targetHeight, Math.Min((float)_targetWidth / (float)_background.Width, (float)_targetHeight / (float)_background.Height), 0f, 0f));
		}
		else if (_source != null)
		{
			RectangleF workRectangle = WorkRectangle;
			float num = Math.Min(workRectangle.Width / (float)_source.Width, workRectangle.Height / (float)_source.Height);
			_zoom = Math.Clamp(num / FitScale, 0.01f, 20f);
			_offsetX = 0f;
			_offsetY = 0f;
			ClampOffset();
			Invalidate();
			RaiseViewChanged();
		}
	}

	public void SetZoom(float value, PointF? anchor)
	{
		value = Math.Clamp(value, 0.01f, 20f);
		if (EditingBackground)
		{
			RectangleF cardRectangle = CardRectangle;
			float num = cardRectangle.Width / (float)_targetWidth;
			PointF pointF = anchor ?? new PointF(cardRectangle.Left + cardRectangle.Width / 2f, cardRectangle.Top + cardRectangle.Height / 2f);
			float num2 = (pointF.X - cardRectangle.Left - cardRectangle.Width / 2f) / num;
			float num3 = (pointF.Y - cardRectangle.Top - cardRectangle.Height / 2f) / num;
			float num4 = value * BackgroundFit / _backgroundSpec.ImageScale;
			SetBackgroundRenderSpec(_backgroundSpec with
			{
				ImageScale = value * BackgroundFit,
				OffsetX = num2 + (_backgroundSpec.OffsetX - num2) * num4,
				OffsetY = num3 + (_backgroundSpec.OffsetY - num3) * num4
			});
		}
		else if (_source != null && !(Math.Abs(value - _zoom) < 0.0001f))
		{
			PointF pointF2 = anchor ?? new PointF(WorkRectangle.Left + WorkRectangle.Width / 2f, WorkRectangle.Top + WorkRectangle.Height / 2f);
			RectangleF imageRectangle = ImageRectangle;
			float displayScale = DisplayScale;
			float num5 = (pointF2.X - imageRectangle.Left) / displayScale;
			float num6 = (pointF2.Y - imageRectangle.Top) / displayScale;
			_zoom = value;
			RectangleF workRectangle = WorkRectangle;
			float displayScale2 = DisplayScale;
			_offsetX = pointF2.X - (workRectangle.Left + workRectangle.Width / 2f) - (num5 - (float)_source.Width / 2f) * displayScale2;
			_offsetY = pointF2.Y - (workRectangle.Top + workRectangle.Height / 2f) - (num6 - (float)_source.Height / 2f) * displayScale2;
			ClampOffset();
			Invalidate();
			RaiseViewChanged();
		}
	}

	public void SetRenderSpec(ImageRenderSpec spec, bool notify = true)
	{
		if (_source != null && !(spec.ImageScale <= 0f) && !(spec.VisualWidth <= 0f) && !(spec.VisualHeight <= 0f))
		{
			RectangleF workRectangle = WorkRectangle;
			if (_overFrameEditing)
			{
				RectangleF cardRectangle = CardRectangle;
				float num = cardRectangle.Width / (float)_targetWidth;
				_zoom = Math.Clamp(spec.ImageScale * num / FitScale, 0.01f, 20f);
				_offsetX = cardRectangle.Left + cardRectangle.Width / 2f + spec.OffsetX * num - (workRectangle.Left + workRectangle.Width / 2f);
				_offsetY = cardRectangle.Top + cardRectangle.Height / 2f + spec.OffsetY * num - (workRectangle.Top + workRectangle.Height / 2f);
			}
			else
			{
				SizeF visualArtSize = VisualArtSize;
				float num2 = visualArtSize.Width / workRectangle.Width;
				float num3 = visualArtSize.Height / workRectangle.Height;
				_zoom = Math.Clamp(spec.ImageScale / num2 / FitScale, 0.01f, 20f);
				_offsetX = spec.OffsetX / num2;
				_offsetY = spec.OffsetY / num3;
			}
			ClampOffset();
			Invalidate();
			if (notify)
			{
				RaiseViewChanged();
			}
		}
	}

	private void ResetArtView()
	{
		bool editingBackground = EditingBackground;
		EditingBackground = false;
		ResetView();
		EditingBackground = editingBackground;
		RaiseViewChanged();
	}

	public Task<(byte[] Art, byte[]? Background)> RenderLayersAsync()
	{
		Bitmap source = new Bitmap(_source ?? throw new InvalidOperationException("尚未载入卡图。"));
		Bitmap background = ((_background == null) ? null : new Bitmap(_background));
		ImageRenderSpec artSpec = RenderSpec;
		ImageRenderSpec backgroundSpec = _backgroundSpec;
		return Task.Run(delegate
		{
			using (source)
			{
				using (background)
				{
					return (ImageCropService.RenderToTarget(source, artSpec, _targetWidth, _targetHeight), (background == null) ? null : ImageCropService.RenderToTarget(background, backgroundSpec, _targetWidth, _targetHeight));
				}
			}
		});
	}

	public byte[] RenderSourceToTarget()
	{
		if (_source == null)
		{
			throw new InvalidOperationException("尚未载入卡图。");
		}
		return ImageCropService.RenderToTarget(_source, RenderSpec, _targetWidth, _targetHeight);
	}

	private void RaiseViewChanged()
	{
		this.ZoomChanged?.Invoke(Zoom);
		if (_source != null)
		{
			this.ViewChanged?.Invoke(ActiveRenderSpec);
		}
	}

	private void ClampOffset()
	{
		if (_source != null)
		{
			RectangleF workRectangle = WorkRectangle;
			RectangleF imageRectangle = ImageRectangle;
			float num = Math.Min(28f, imageRectangle.Width / 2f);
			float num2 = Math.Min(28f, imageRectangle.Height / 2f);
			float num3 = Math.Max(0f, workRectangle.Width / 2f + imageRectangle.Width / 2f - num);
			float num4 = Math.Max(0f, workRectangle.Height / 2f + imageRectangle.Height / 2f - num2);
			_offsetX = Math.Clamp(_offsetX, 0f - num3, num3);
			_offsetY = Math.Clamp(_offsetY, 0f - num4, num4);
		}
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ClampOffset();
		Invalidate();
	}

	protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
	{
		ImageRenderSpec? imageRenderSpec = ((_overFrameEditing && _source != null && base.Width > 100 && base.Height > 100 && (width != base.Width || height != base.Height)) ? new ImageRenderSpec?(RenderSpec) : ((ImageRenderSpec?)null));
		base.SetBoundsCore(x, y, width, height, specified);
		if (imageRenderSpec.HasValue && base.Width > 100 && base.Height > 100)
		{
			SetRenderSpec(imageRenderSpec.Value, notify: false);
			ImageRenderSpec renderSpec = RenderSpec;
			if (Math.Abs(renderSpec.ImageScale - imageRenderSpec.Value.ImageScale) > 0.0001f || Math.Abs(renderSpec.OffsetX - imageRenderSpec.Value.OffsetX) > 0.01f || Math.Abs(renderSpec.OffsetY - imageRenderSpec.Value.OffsetY) > 0.01f)
			{
				RaiseViewChanged();
			}
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (!ShowingRenderedPreview)
		{
			RectangleF rectangleF = (_overFrameEditing ? CardRectangle : WorkRectangle);
			if (e.Button == MouseButtons.Left && rectangleF.Contains(e.Location))
			{
				Focus();
				_dragging = true;
				_lastMouse = e.Location;
				Cursor = Cursors.SizeAll;
				base.Capture = true;
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (!ShowingRenderedPreview && _dragging)
		{
			MoveActive(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
			_lastMouse = e.Location;
			ClampOffset();
			Invalidate();
			this.ViewChanged?.Invoke(ActiveRenderSpec);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left)
		{
			_dragging = false;
			Cursor = Cursors.Hand;
			base.Capture = false;
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (!ShowingRenderedPreview)
		{
			SetZoom(Zoom * ((e.Delta > 0) ? 1.12f : (25f / 28f)), e.Location);
		}
	}

	protected override void OnDoubleClick(EventArgs e)
	{
		base.OnDoubleClick(e);
		if (!ShowingRenderedPreview)
		{
			ResetView();
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!ShowingRenderedPreview)
		{
			int num = (e.Shift ? 10 : 2);
			bool flag = true;
			switch (e.KeyCode)
			{
			case Keys.Left:
				MoveActive(-num, 0f);
				break;
			case Keys.Right:
				MoveActive(num, 0f);
				break;
			case Keys.Up:
				MoveActive(0f, -num);
				break;
			case Keys.Down:
				MoveActive(0f, num);
				break;
			case Keys.Add:
			case Keys.Oemplus:
				SetZoom(Zoom * 1.1f, null);
				return;
			case Keys.Subtract:
			case Keys.OemMinus:
				SetZoom(Zoom / 1.1f, null);
				return;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				ClampOffset();
				Invalidate();
				this.ViewChanged?.Invoke(ActiveRenderSpec);
				e.Handled = true;
			}
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		CardFrameRenderer.Configure(graphics);
		if (ShowingRenderedPreview)
		{
			RectangleF cardRectangle = CardRectangle;
			using (SolidBrush brush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
			{
				graphics.FillRectangle(brush, cardRectangle.Left + 9f, cardRectangle.Top + 10f, cardRectangle.Width, cardRectangle.Height);
			}
			DrawCheckerboard(graphics, cardRectangle);
			graphics.DrawImage(_renderedPreview, cardRectangle);
			using Pen pen = new Pen(UiTheme.Primary, 2f);
			graphics.DrawRectangle(pen, cardRectangle.X, cardRectangle.Y, cardRectangle.Width, cardRectangle.Height);
			return;
		}
		RectangleF workRectangle = WorkRectangle;
		if (_frame != null)
		{
			RectangleF cardRectangle2 = CardRectangle;
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
			{
				graphics.FillRectangle(brush2, cardRectangle2.Left + 9f, cardRectangle2.Top + 10f, cardRectangle2.Width, cardRectangle2.Height);
			}
			if (_overFrameEditing)
			{
				DrawCheckerboard(graphics, cardRectangle2);
				DrawBackground(graphics, cardRectangle2);
				graphics.DrawImage(_frame, cardRectangle2);
				GraphicsState gstate = graphics.Save();
				graphics.SetClip(cardRectangle2);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(gstate);
			}
			else if (_fullCardOverlay)
			{
				graphics.FillRectangle(Brushes.White, cardRectangle2);
				DrawBackground(graphics, cardRectangle2);
				graphics.DrawImage(_frame, cardRectangle2);
				GraphicsState gstate2 = graphics.Save();
				graphics.SetClip(cardRectangle2);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(gstate2);
			}
			else
			{
				graphics.FillRectangle(Brushes.White, cardRectangle2);
				GraphicsState gstate3 = graphics.Save();
				graphics.SetClip(workRectangle);
				graphics.FillRectangle(Brushes.White, workRectangle);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(gstate3);
				graphics.DrawImage(_frame, cardRectangle2);
			}
		}
		else
		{
			DrawCheckerboard(graphics, workRectangle);
			if (_source != null)
			{
				graphics.DrawImage(_source, ImageRectangle);
			}
			using SolidBrush brush3 = new SolidBrush(Color.FromArgb(185, 3, 7, 14));
			graphics.FillRectangle(brush3, 0f, 0f, base.Width, Math.Max(0f, workRectangle.Top));
			graphics.FillRectangle(brush3, 0f, workRectangle.Bottom, base.Width, Math.Max(0f, (float)base.Height - workRectangle.Bottom));
			graphics.FillRectangle(brush3, 0f, workRectangle.Top, Math.Max(0f, workRectangle.Left), workRectangle.Height);
			graphics.FillRectangle(brush3, workRectangle.Right, workRectangle.Top, Math.Max(0f, (float)base.Width - workRectangle.Right), workRectangle.Height);
		}
		using Pen pen2 = new Pen(Color.FromArgb(80, 235, 244, 255), 1f);
		graphics.DrawLine(pen2, workRectangle.Left + workRectangle.Width / 3f, workRectangle.Top, workRectangle.Left + workRectangle.Width / 3f, workRectangle.Bottom);
		graphics.DrawLine(pen2, workRectangle.Left + workRectangle.Width * 2f / 3f, workRectangle.Top, workRectangle.Left + workRectangle.Width * 2f / 3f, workRectangle.Bottom);
		graphics.DrawLine(pen2, workRectangle.Left, workRectangle.Top + workRectangle.Height / 3f, workRectangle.Right, workRectangle.Top + workRectangle.Height / 3f);
		graphics.DrawLine(pen2, workRectangle.Left, workRectangle.Top + workRectangle.Height * 2f / 3f, workRectangle.Right, workRectangle.Top + workRectangle.Height * 2f / 3f);
		using Pen pen3 = new Pen(UiTheme.Primary, 2f);
		graphics.DrawRectangle(pen3, workRectangle.X, workRectangle.Y, workRectangle.Width, workRectangle.Height);
		DrawCorners(graphics, workRectangle);
	}

	private void DrawBackground(Graphics graphics, RectangleF area)
	{
		Bitmap background = _background;
		if (background != null && !(area.Width <= 0f) && !(area.Height <= 0f))
		{
			float num = area.Width / (float)_targetWidth;
			float num2 = (_overFrameEditing ? (_backgroundSpec.ImageScale * num) : Math.Max(area.Width / (float)background.Width, area.Height / (float)background.Height));
			float num3 = (float)background.Width * num2;
			float num4 = (float)background.Height * num2;
			RectangleF rect = new RectangleF(area.Left + (area.Width - num3) / 2f + (_overFrameEditing ? (_backgroundSpec.OffsetX * num) : 0f), area.Top + (area.Height - num4) / 2f + (_overFrameEditing ? (_backgroundSpec.OffsetY * num) : 0f), num3, num4);
			GraphicsState gstate = graphics.Save();
			graphics.SetClip(area);
			graphics.DrawImage(background, rect);
			graphics.Restore(gstate);
		}
	}

	private static void DrawCheckerboard(Graphics graphics, RectangleF area)
	{
		graphics.FillRectangle(Brushes.White, area);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(205, 210, 218));
		for (int i = (int)area.Top; (float)i < area.Bottom; i += 14)
		{
			for (int j = (int)area.Left; (float)j < area.Right; j += 14)
			{
				if (((j - (int)area.Left) / 14 + (i - (int)area.Top) / 14) % 2 == 0)
				{
					graphics.FillRectangle(brush, j, i, Math.Min(14, (int)area.Right - j), Math.Min(14, (int)area.Bottom - i));
				}
			}
		}
	}

	private static void DrawCorners(Graphics graphics, RectangleF crop)
	{
		using Pen pen = new Pen(UiTheme.Gold, 4f);
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(crop.Left, crop.Top + 22f),
			new PointF(crop.Left, crop.Top),
			new PointF(crop.Left + 22f, crop.Top)
		});
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(crop.Right - 22f, crop.Top),
			new PointF(crop.Right, crop.Top),
			new PointF(crop.Right, crop.Top + 22f)
		});
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(crop.Left, crop.Bottom - 22f),
			new PointF(crop.Left, crop.Bottom),
			new PointF(crop.Left + 22f, crop.Bottom)
		});
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(crop.Right - 22f, crop.Bottom),
			new PointF(crop.Right, crop.Bottom),
			new PointF(crop.Right, crop.Bottom - 22f)
		});
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_source?.Dispose();
			_source = null;
			_background?.Dispose();
			_background = null;
			_frame?.Dispose();
			_frame = null;
			_renderedPreview?.Dispose();
			_renderedPreview = null;
		}
		base.Dispose(disposing);
	}
}
