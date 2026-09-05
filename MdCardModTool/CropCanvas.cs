using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
	private float BackgroundFit => _background == null ? 1f : Math.Max((float)_targetWidth / _background.Width, (float)_targetHeight / _background.Height);
	public ImageRenderSpec BackgroundRenderSpec => _backgroundSpec;
	public ImageRenderSpec ActiveRenderSpec => EditingBackground ? _backgroundSpec : RenderSpec;
	public float Zoom => EditingBackground ? _backgroundSpec.ImageScale / BackgroundFit : _zoom;

	public void EditBackground(bool enabled)
	{
		EditingBackground = enabled && _background != null;
		Invalidate();
		RaiseViewChanged();
	}

	public void SetBackgroundRenderSpec(ImageRenderSpec spec)
	{
		if (!float.IsFinite(spec.ImageScale) || spec.ImageScale <= 0 || !float.IsFinite(spec.OffsetX) || !float.IsFinite(spec.OffsetY)) return;
		_backgroundSpec = spec with { VisualWidth = _targetWidth, VisualHeight = _targetHeight };
		Invalidate();
		RaiseViewChanged();
	}

	public byte[]? RenderBackgroundToTarget() => _background == null ? null :
		ImageCropService.RenderToTarget(_background, _backgroundSpec, _targetWidth, _targetHeight);

	private void MoveActive(float dx, float dy)
	{
		if (EditingBackground)
		{
			float scale = CardRectangle.Width / _targetWidth;
			_backgroundSpec = _backgroundSpec with { OffsetX = _backgroundSpec.OffsetX + dx / scale, OffsetY = _backgroundSpec.OffsetY + dy / scale };
		}
		else { _offsetX += dx; _offsetY += dy; ClampOffset(); }
	}

	public bool HasFrame => _frame != null;

	public bool IsOverFrameEditing => _overFrameEditing;

	public bool ShowingRenderedPreview => _showRenderedPreview && _renderedPreview != null;

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
			float availableWidth = Math.Max(1f, (float)base.ClientSize.Width - 60f);
			float availableHeight = Math.Max(1f, (float)base.ClientSize.Height - 60f);
			// A card frame is always authored in full-card coordinates (normally
			// 704×1024). The editable Texture2D may instead be a 512×683 logical
			// Pendulum image. Using the texture aspect here visibly squeezed the
			// frame and made the editor preview impossible to match in game.
			float visualWidth = _frame?.Width ?? _targetWidth;
			float visualHeight = _frame?.Height ?? _targetHeight;
			float aspect = visualWidth / visualHeight;
			float height;
			float width;
			if (availableWidth / availableHeight > aspect)
			{
				height = availableHeight;
				width = height * aspect;
			}
			else
			{
				width = availableWidth;
				height = width / aspect;
			}
			return new RectangleF(((float)base.ClientSize.Width - width) / 2f, ((float)base.ClientSize.Height - height) / 2f, width, height);
		}
	}

	private RectangleF WorkRectangle
	{
		get
		{
			if (_frame != null && (_overFrameEditing || !_fullCardOverlay))
			{
				RectangleF card = CardRectangle;
				float scaleX = card.Width / _frame.Width;
				float scaleY = card.Height / _frame.Height;
				return new RectangleF(card.Left + _artWindow.Left * scaleX,
					card.Top + _artWindow.Top * scaleY,
					_artWindow.Width * scaleX, _artWindow.Height * scaleY);
			}
			float availableWidth = Math.Max(1f, (float)base.ClientSize.Width - 92f);
			float availableHeight = Math.Max(1f, (float)base.ClientSize.Height - 92f);
			float aspect = (float)_targetWidth / (float)_targetHeight;
			float height;
			float width;
			if (availableWidth / availableHeight > aspect)
			{
				height = availableHeight;
				width = height * aspect;
			}
			else
			{
				width = availableWidth;
				height = width / aspect;
			}
			return new RectangleF(((float)base.ClientSize.Width - width) / 2f, ((float)base.ClientSize.Height - height) / 2f, width, height);
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
			RectangleF work = WorkRectangle;
			return Math.Max(work.Width / (float)_source.Width, work.Height / (float)_source.Height);
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
			RectangleF work = WorkRectangle;
			float scale = DisplayScale;
			float width = (float)_source.Width * scale;
			float height = (float)_source.Height * scale;
			return new RectangleF(work.Left + work.Width / 2f + _offsetX - width / 2f, work.Top + work.Height / 2f + _offsetY - height / 2f, width, height);
		}
	}

	public ImageRenderSpec RenderSpec
	{
		get
		{
			if (_overFrameEditing)
			{
				RectangleF card = CardRectangle;
				RectangleF overFrameWork = WorkRectangle;
				float cardScale = card.Width / _targetWidth;
				return new ImageRenderSpec(_targetWidth, _targetHeight,
					DisplayScale / cardScale,
					(overFrameWork.Left + overFrameWork.Width / 2f + _offsetX - (card.Left + card.Width / 2f)) / cardScale,
					(overFrameWork.Top + overFrameWork.Height / 2f + _offsetY - (card.Top + card.Height / 2f)) / cardScale);
			}
			RectangleF work = WorkRectangle;
			SizeF visual = VisualArtSize;
			float logicalX = visual.Width / work.Width;
			float logicalY = visual.Height / work.Height;
			return new ImageRenderSpec(visual.Width, visual.Height, DisplayScale * logicalX, _offsetX * logicalX, _offsetY * logicalY);
		}
	}

	public event Action<float>? ZoomChanged;

	public event Action<ImageRenderSpec>? ViewChanged;

	public CropCanvas(Bitmap source, int targetWidth, int targetHeight, bool fullCardOverlay = false,
		bool overFrameEditing = false)
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
		ImageRenderSpec? saved = preserveView && _source != null && _frame != null
			? RenderSpec
			: null;
		_frame?.Dispose();
		_frame = frame;
		_artWindow = artWindow ?? CardFrameRenderer.FindArtWindow(frame);
		if (saved.HasValue)
		{
			SetRenderSpec(saved.Value);
		}
		else
		{
			ResetArtView();
		}
	}

	public void SetSource(Bitmap source, bool resetView = true)
	{
		ArgumentNullException.ThrowIfNull(source);
		_source?.Dispose();
		_source = source;
		if (resetView)
		{
			ResetArtView();
		}
		else
		{
			ClampOffset();
			Invalidate();
			RaiseViewChanged();
		}
	}

	public void SetBackground(Bitmap? background)
	{
		_background?.Dispose();
		_background = background;
		_backgroundSpec = new(_targetWidth, _targetHeight, BackgroundFit, 0, 0);
		if (background == null) EditingBackground = false;
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
		Cursor = ShowingRenderedPreview ? Cursors.Default : Cursors.Hand;
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
			SetBackgroundRenderSpec(new(_targetWidth, _targetHeight, BackgroundFit, 0, 0));
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
			SetBackgroundRenderSpec(new(_targetWidth, _targetHeight, Math.Min((float)_targetWidth / _background.Width, (float)_targetHeight / _background.Height), 0, 0));
			return;
		}
		if (_source != null)
		{
			RectangleF work = WorkRectangle;
			float contain = Math.Min(work.Width / (float)_source.Width, work.Height / (float)_source.Height);
			_zoom = Math.Clamp(contain / FitScale, 0.01f, 20f);
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
			RectangleF card = CardRectangle;
			float screenScale = card.Width / _targetWidth;
			PointF point = anchor ?? new PointF(card.Left + card.Width / 2, card.Top + card.Height / 2);
			float x = (point.X - card.Left - card.Width / 2) / screenScale;
			float y = (point.Y - card.Top - card.Height / 2) / screenScale;
			float ratio = value * BackgroundFit / _backgroundSpec.ImageScale;
			SetBackgroundRenderSpec(_backgroundSpec with { ImageScale = value * BackgroundFit,
				OffsetX = x + (_backgroundSpec.OffsetX - x) * ratio, OffsetY = y + (_backgroundSpec.OffsetY - y) * ratio });
			return;
		}
		if (_source != null && !(Math.Abs(value - _zoom) < 0.0001f))
		{
			PointF point = anchor ?? new PointF(WorkRectangle.Left + WorkRectangle.Width / 2f, WorkRectangle.Top + WorkRectangle.Height / 2f);
			RectangleF oldImage = ImageRectangle;
			float oldScale = DisplayScale;
			float sourceX = (point.X - oldImage.Left) / oldScale;
			float sourceY = (point.Y - oldImage.Top) / oldScale;
			_zoom = value;
			RectangleF work = WorkRectangle;
			float newScale = DisplayScale;
			_offsetX = point.X - (work.Left + work.Width / 2f) - (sourceX - (float)_source.Width / 2f) * newScale;
			_offsetY = point.Y - (work.Top + work.Height / 2f) - (sourceY - (float)_source.Height / 2f) * newScale;
			ClampOffset();
			Invalidate();
			RaiseViewChanged();
		}
	}

	public void SetRenderSpec(ImageRenderSpec spec, bool notify = true)
	{
		if (_source == null || spec.ImageScale <= 0f || spec.VisualWidth <= 0f || spec.VisualHeight <= 0f)
		{
			return;
		}
		RectangleF work = WorkRectangle;
		if (_overFrameEditing)
		{
			RectangleF card = CardRectangle;
			float cardScale = card.Width / _targetWidth;
			_zoom = Math.Clamp(spec.ImageScale * cardScale / FitScale, 0.01f, 20f);
			_offsetX = card.Left + card.Width / 2f + spec.OffsetX * cardScale
				- (work.Left + work.Width / 2f);
			_offsetY = card.Top + card.Height / 2f + spec.OffsetY * cardScale
				- (work.Top + work.Height / 2f);
		}
		else
		{
			SizeF visual = VisualArtSize;
			float logicalX = visual.Width / work.Width;
			float logicalY = visual.Height / work.Height;
			_zoom = Math.Clamp(spec.ImageScale / logicalX / FitScale, 0.01f, 20f);
			_offsetX = spec.OffsetX / logicalX;
			_offsetY = spec.OffsetY / logicalY;
		}
		ClampOffset();
		Invalidate();
		if (notify) RaiseViewChanged();
	}

	private void ResetArtView()
	{
		bool background = EditingBackground;
		EditingBackground = false;
		ResetView();
		EditingBackground = background;
		RaiseViewChanged();
	}

	public System.Threading.Tasks.Task<(byte[] Art, byte[]? Background)> RenderLayersAsync()
	{
		Bitmap source = new(_source ?? throw new InvalidOperationException("尚未载入卡图。"));
		Bitmap? background = _background == null ? null : new Bitmap(_background);
		ImageRenderSpec artSpec = RenderSpec, backgroundSpec = _backgroundSpec;
		return System.Threading.Tasks.Task.Run(() =>
		{
			using (source) using (background)
				return (ImageCropService.RenderToTarget(source, artSpec, _targetWidth, _targetHeight),
					background == null ? null : ImageCropService.RenderToTarget(background, backgroundSpec, _targetWidth, _targetHeight));
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
		ZoomChanged?.Invoke(Zoom);
		if (_source != null)
		{
			ViewChanged?.Invoke(ActiveRenderSpec);
		}
	}

	private void ClampOffset()
	{
		if (_source != null)
		{
			RectangleF work = WorkRectangle;
			RectangleF image = ImageRectangle;
			float visibleX = Math.Min(28f, image.Width / 2f);
			float visibleY = Math.Min(28f, image.Height / 2f);
			float maxX = Math.Max(0f, work.Width / 2f + image.Width / 2f - visibleX);
			float maxY = Math.Max(0f, work.Height / 2f + image.Height / 2f - visibleY);
			_offsetX = Math.Clamp(_offsetX, 0f - maxX, maxX);
			_offsetY = Math.Clamp(_offsetY, 0f - maxY, maxY);
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
		ImageRenderSpec? previous = _overFrameEditing && _source != null && Width > 100 && Height > 100
			&& (width != Width || height != Height) ? RenderSpec : null;
		base.SetBoundsCore(x, y, width, height, specified);
		if (previous.HasValue && Width > 100 && Height > 100)
		{
			// Viewport resizing preserves logical composition; do not discard a valid
			// preview and enqueue expensive PNG composition for every layout wave.
			SetRenderSpec(previous.Value, notify: false);
			ImageRenderSpec current = RenderSpec;
			if (Math.Abs(current.ImageScale - previous.Value.ImageScale) > .0001f
				|| Math.Abs(current.OffsetX - previous.Value.OffsetX) > .01f
				|| Math.Abs(current.OffsetY - previous.Value.OffsetY) > .01f) RaiseViewChanged();
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (ShowingRenderedPreview) return;
		RectangleF hitArea = _overFrameEditing ? CardRectangle : WorkRectangle;
		if (e.Button == MouseButtons.Left && hitArea.Contains(e.Location))
		{
			Focus();
			_dragging = true;
			_lastMouse = e.Location;
			Cursor = Cursors.SizeAll;
			base.Capture = true;
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (ShowingRenderedPreview) return;
		if (_dragging)
		{
			MoveActive(e.X - _lastMouse.X, e.Y - _lastMouse.Y);
			_lastMouse = e.Location;
			ClampOffset();
			Invalidate();
			ViewChanged?.Invoke(ActiveRenderSpec);
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
		if (ShowingRenderedPreview) return;
		SetZoom(Zoom * ((e.Delta > 0) ? 1.12f : (25f / 28f)), e.Location);
	}

	protected override void OnDoubleClick(EventArgs e)
	{
		base.OnDoubleClick(e);
		if (ShowingRenderedPreview) return;
		ResetView();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (ShowingRenderedPreview) return;
		int step = (e.Shift ? 10 : 2);
		bool changed = true;
		switch (e.KeyCode)
		{
		case Keys.Left:
			MoveActive(-step, 0);
			break;
		case Keys.Right:
			MoveActive(step, 0);
			break;
		case Keys.Up:
			MoveActive(0, -step);
			break;
		case Keys.Down:
			MoveActive(0, step);
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
			changed = false;
			break;
		}
		if (changed)
		{
			ClampOffset();
			Invalidate();
			ViewChanged?.Invoke(ActiveRenderSpec);
			e.Handled = true;
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics graphics = e.Graphics;
		CardFrameRenderer.Configure(graphics);
		if (ShowingRenderedPreview)
		{
			RectangleF card = CardRectangle;
			using (SolidBrush shadow = new(Color.FromArgb(90, 0, 0, 0)))
			{
				graphics.FillRectangle(shadow, card.Left + 9f, card.Top + 10f, card.Width, card.Height);
			}
			DrawCheckerboard(graphics, card);
			graphics.DrawImage(_renderedPreview!, card);
			using Pen previewBorder = new(UiTheme.Primary, 2f);
			graphics.DrawRectangle(previewBorder, card.X, card.Y, card.Width, card.Height);
			return;
		}
		RectangleF work = WorkRectangle;
		if (_frame != null)
		{
			RectangleF card = CardRectangle;
			using (SolidBrush shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
			{
				graphics.FillRectangle(shadow, card.Left + 9f, card.Top + 10f, card.Width, card.Height);
			}
			if (_overFrameEditing)
			{
				DrawCheckerboard(graphics, card);
				DrawBackground(graphics, card);
				graphics.DrawImage(_frame, card);
				GraphicsState state = graphics.Save();
				graphics.SetClip(card);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(state);
			}
			else if (_fullCardOverlay)
			{
				graphics.FillRectangle(Brushes.White, card);
				DrawBackground(graphics, card);
				graphics.DrawImage(_frame, card);
				GraphicsState state = graphics.Save();
				graphics.SetClip(card);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(state);
			}
			else
			{
				graphics.FillRectangle(Brushes.White, card);
				GraphicsState state2 = graphics.Save();
				graphics.SetClip(work);
				graphics.FillRectangle(Brushes.White, work);
				if (_source != null)
				{
					graphics.DrawImage(_source, ImageRectangle);
				}
				graphics.Restore(state2);
				graphics.DrawImage(_frame, card);
			}
		}
		else
		{
			DrawCheckerboard(graphics, work);
			if (_source != null)
			{
				graphics.DrawImage(_source, ImageRectangle);
			}
			using SolidBrush shade = new SolidBrush(Color.FromArgb(185, 3, 7, 14));
			graphics.FillRectangle(shade, 0f, 0f, base.Width, Math.Max(0f, work.Top));
			graphics.FillRectangle(shade, 0f, work.Bottom, base.Width, Math.Max(0f, (float)base.Height - work.Bottom));
			graphics.FillRectangle(shade, 0f, work.Top, Math.Max(0f, work.Left), work.Height);
			graphics.FillRectangle(shade, work.Right, work.Top, Math.Max(0f, (float)base.Width - work.Right), work.Height);
		}
		using Pen grid = new Pen(Color.FromArgb(80, 235, 244, 255), 1f);
		graphics.DrawLine(grid, work.Left + work.Width / 3f, work.Top, work.Left + work.Width / 3f, work.Bottom);
		graphics.DrawLine(grid, work.Left + work.Width * 2f / 3f, work.Top, work.Left + work.Width * 2f / 3f, work.Bottom);
		graphics.DrawLine(grid, work.Left, work.Top + work.Height / 3f, work.Right, work.Top + work.Height / 3f);
		graphics.DrawLine(grid, work.Left, work.Top + work.Height * 2f / 3f, work.Right, work.Top + work.Height * 2f / 3f);
		using Pen border = new Pen(UiTheme.Primary, 2f);
		graphics.DrawRectangle(border, work.X, work.Y, work.Width, work.Height);
		DrawCorners(graphics, work);
	}

	private void DrawBackground(Graphics graphics, RectangleF area)
	{
		Bitmap? image = _background;
		if (image == null || area.Width <= 0f || area.Height <= 0f) return;
		float logicalScale = area.Width / _targetWidth;
		float scale = _overFrameEditing ? _backgroundSpec.ImageScale * logicalScale
			: Math.Max(area.Width / image.Width, area.Height / image.Height);
		float width = image.Width * scale;
		float height = image.Height * scale;
		RectangleF destination = new(area.Left + (area.Width - width) / 2f + (_overFrameEditing ? _backgroundSpec.OffsetX * logicalScale : 0),
			area.Top + (area.Height - height) / 2f + (_overFrameEditing ? _backgroundSpec.OffsetY * logicalScale : 0), width, height);
		GraphicsState state = graphics.Save();
		graphics.SetClip(area);
		graphics.DrawImage(image, destination);
		graphics.Restore(state);
	}

	private static void DrawCheckerboard(Graphics graphics, RectangleF area)
	{
		graphics.FillRectangle(Brushes.White, area);
		using SolidBrush gray = new SolidBrush(Color.FromArgb(205, 210, 218));
		for (int y = (int)area.Top; (float)y < area.Bottom; y += 14)
		{
			for (int x = (int)area.Left; (float)x < area.Right; x += 14)
			{
				if (((x - (int)area.Left) / 14 + (y - (int)area.Top) / 14) % 2 == 0)
				{
					graphics.FillRectangle(gray, x, y, Math.Min(14, (int)area.Right - x), Math.Min(14, (int)area.Bottom - y));
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
