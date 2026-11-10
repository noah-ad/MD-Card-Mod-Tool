using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// A lightweight, virtualized card gallery.  The ListView remains the
/// selection/mod-management adapter, while this control is the visual card
/// workspace shown to users.  Only the cards near the viewport are decoded.
/// </summary>
internal sealed class CardResourceGallery : ScrollableControl
{
	private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
	private CancellationTokenSource? _loadCancellation;
	private IReadOnlyList<TexRef> _items = Array.Empty<TexRef>();
	private int _selectedIndex = -1;

	public Func<TexRef, CancellationToken, Task<Bitmap?>>? ImageLoader { get; set; }
	public Func<TexRef, string>? NameFormatter { get; set; }
	public event EventHandler? SelectionChanged;
	public int SelectedIndex => _selectedIndex;

	private int Scale(int value) => Math.Max(1, (int)Math.Round(value * Math.Max(96, DeviceDpi) / 96d));
	private int Gap => Scale(12);
	private int TileWidth => Math.Max(Scale(142), Math.Min(Scale(190), (ClientSize.Width - Gap * 2) / 4));
	private int TileHeight => Scale(250);
	private int Columns => Math.Max(1, (ClientSize.Width - Gap) / (TileWidth + Gap));

	public CardResourceGallery()
	{
		DoubleBuffered = true;
		AutoScroll = true;
		BackColor = UiTheme.Surface;
		ForeColor = UiTheme.Text;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		MouseDown += OnMouseDown;
		Scroll += (_, _) => { Invalidate(); QueueVisibleLoads(); };
		SizeChanged += (_, _) => { UpdateScrollSize(); Invalidate(); QueueVisibleLoads(); };
		DpiChangedAfterParent += (_, _) => { UpdateScrollSize(); Invalidate(); QueueVisibleLoads(); };
	}

	public void SetItems(IReadOnlyList<TexRef> items)
	{
		_loadCancellation?.Cancel();
		var keep = items.Select(CacheKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (string key in _images.Keys.Where(k => !keep.Contains(k)).ToArray())
		{
			_images[key].Dispose();
			_images.Remove(key);
		}
		_items = items;
		if (_selectedIndex >= _items.Count) _selectedIndex = -1;
		UpdateScrollSize();
		Invalidate();
		QueueVisibleLoads();
	}

	public void SetSelectedIndex(int index, bool raiseEvent = false)
	{
		int next = index >= 0 && index < _items.Count ? index : -1;
		if (_selectedIndex == next)
		{
			Invalidate();
			return;
		}
		_selectedIndex = next;
		EnsureSelectedVisible();
		Invalidate();
		if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	private static string CacheKey(TexRef texture) => $"{texture.BundlePath}\0{texture.AssetFileName}\0{texture.PathId}\0{texture.CardKey}";

	private void UpdateScrollSize()
	{
		if (ClientSize.Width <= 0) return;
		int rows = (_items.Count + Columns - 1) / Columns;
		AutoScrollMinSize = new Size(0, Gap + rows * (TileHeight + Gap));
	}

	private void EnsureSelectedVisible()
	{
		if (_selectedIndex < 0 || Columns <= 0) return;
		int row = _selectedIndex / Columns;
		int y = Gap + row * (TileHeight + Gap);
		if (y < -AutoScrollPosition.Y) AutoScrollPosition = new Point(0, Math.Max(0, y - Gap));
		else if (y + TileHeight > -AutoScrollPosition.Y + ClientSize.Height) AutoScrollPosition = new Point(0, Math.Max(0, y + TileHeight - ClientSize.Height + Gap));
	}

	private void OnMouseDown(object? sender, MouseEventArgs e)
	{
		if (e.Button != MouseButtons.Left) return;
		Point p = new(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
		int column = (p.X - Gap) / (TileWidth + Gap);
		int row = (p.Y - Gap) / (TileHeight + Gap);
		int xIn = (p.X - Gap) % (TileWidth + Gap);
		int yIn = (p.Y - Gap) % (TileHeight + Gap);
		if (column < 0 || row < 0 || xIn < 0 || xIn > TileWidth || yIn < 0 || yIn > TileHeight) return;
		int index = row * Columns + column;
		if (index < 0 || index >= _items.Count) return;
		SetSelectedIndex(index, raiseEvent: true);
	}

	private void QueueVisibleLoads()
	{
		if (!IsHandleCreated || ImageLoader == null || _items.Count == 0) return;
		_loadCancellation?.Cancel();
		_loadCancellation = new CancellationTokenSource();
		CancellationToken token = _loadCancellation.Token;
		int firstRow = Math.Max(0, (-AutoScrollPosition.Y - Gap) / (TileHeight + Gap) - 1);
		int visibleRows = Math.Max(1, ClientSize.Height / (TileHeight + Gap) + 3);
		int first = Math.Min(_items.Count, firstRow * Columns);
		int last = Math.Min(_items.Count, (firstRow + visibleRows) * Columns);
		_ = LoadVisibleAsync(first, last, token);
	}

	private async Task LoadVisibleAsync(int first, int last, CancellationToken token)
	{
		for (int i = first; i < last; i++)
		{
			if (token.IsCancellationRequested) return;
			TexRef texture = _items[i];
			string key = CacheKey(texture);
			if (_images.ContainsKey(key) || !_loading.Add(key)) continue;
			try
			{
				Bitmap? image = await ImageLoader!(texture, token);
				if (image != null && !token.IsCancellationRequested && IsHandleCreated)
				{
					if (_images.TryGetValue(key, out Bitmap? old)) old.Dispose();
					_images[key] = image;
					BeginInvoke((MethodInvoker)Invalidate);
				}
				else image?.Dispose();
			}
			catch (OperationCanceledException) { return; }
			catch { /* an unresolved or corrupt card is drawn as a stable placeholder */ }
			finally { _loading.Remove(key); }
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
		e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
		int columns = Columns;
		int firstRow = Math.Max(0, (-AutoScrollPosition.Y - Gap) / (TileHeight + Gap));
		int lastRow = Math.Min((_items.Count + columns - 1) / columns, firstRow + Math.Max(1, ClientSize.Height / (TileHeight + Gap) + 2));
		for (int row = firstRow; row < lastRow; row++)
		{
			for (int column = 0; column < columns; column++)
			{
				int index = row * columns + column;
				if (index >= _items.Count) break;
				Rectangle tile = new(Gap + column * (TileWidth + Gap), Gap + row * (TileHeight + Gap), TileWidth, TileHeight);
				DrawTile(e.Graphics, tile, index, _items[index]);
			}
		}
	}

	private void DrawTile(Graphics g, Rectangle tile, int index, TexRef texture)
	{
		bool selected = index == _selectedIndex;
		using SolidBrush bg = new(selected ? Color.FromArgb(29, 66, 91) : UiTheme.SurfaceAlt);
		using Pen border = new(selected ? UiTheme.Gold : UiTheme.Border, selected ? Scale(2) : Scale(1));
		using GraphicsPath path = UiTheme.RoundedPath(tile, Scale(8));
		g.FillPath(bg, path);
		g.DrawPath(border, path);
		Rectangle imageBox = new(tile.X + Scale(9), tile.Y + Scale(9), tile.Width - Scale(18), tile.Height - Scale(58));
		using SolidBrush imageBg = new(UiTheme.Window);
		g.FillRectangle(imageBg, imageBox);
		Bitmap? image = _images.GetValueOrDefault(CacheKey(texture));
		if (image != null)
		{
			Rectangle target = Fit(image.Size, imageBox);
			g.DrawImage(image, target);
		}
		else
		{
			using SolidBrush accent = new(Color.FromArgb(38, UiTheme.Primary));
			g.FillRectangle(accent, imageBox);
			string placeholder = texture.IsCatalogOnly ? "卡图待定位" : "读取中";
			TextRenderer.DrawText(g, placeholder, Font, imageBox, UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
		}
		string name = NameFormatter?.Invoke(texture) ?? texture.Name;
		string meta = texture.CardKey.Length > 0 ? texture.CardKey : ResourceLabelFallback(texture.Category);
		using Font titleFont = new(Font, FontStyle.Bold);
		TextRenderer.DrawText(g, name, titleFont, new Rectangle(tile.X + Scale(10), tile.Bottom - Scale(43), tile.Width - Scale(20), Scale(20)), UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
		TextRenderer.DrawText(g, meta, Font, new Rectangle(tile.X + Scale(10), tile.Bottom - Scale(23), tile.Width - Scale(20), Scale(16)), selected ? UiTheme.Gold : UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
	}

	private static Rectangle Fit(Size source, Rectangle target)
	{
		if (source.Width <= 0 || source.Height <= 0) return target;
		double scale = Math.Min((double)target.Width / source.Width, (double)target.Height / source.Height);
		int width = Math.Max(1, (int)Math.Round(source.Width * scale));
		int height = Math.Max(1, (int)Math.Round(source.Height * scale));
		return new Rectangle(target.X + (target.Width - width) / 2, target.Y + (target.Height - height) / 2, width, height);
	}

	private static string ResourceLabelFallback(string value) => value switch
	{
		"卡图待定位" => "待定位",
		"其他贴图" => "资源",
		_ => value
	};

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_loadCancellation?.Cancel();
			_loadCancellation?.Dispose();
			foreach (Bitmap image in _images.Values) image.Dispose();
			_images.Clear();
		}
		base.Dispose(disposing);
	}
}
