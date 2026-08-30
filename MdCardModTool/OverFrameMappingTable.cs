using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// Small virtualized table for the over-frame mapping list. Win32 ListView
/// scrollbars are rendered by the non-client theme and can remain bright white
/// even when every row is owner drawn. Drawing the three-column table here keeps
/// the whole surface deterministic, DPI aware and free of horizontal overflow.
/// </summary>
public sealed class OverFrameMappingTable : Control
{
	private readonly List<OverFrameMapping> _mappings = [];
	private int _selectedIndex = -1;
	private int _scrollOffset;
	private bool _draggingThumb;
	private int _thumbDragOffset;

	public event EventHandler? SelectedMappingChanged;

	public OverFrameMapping? SelectedMapping =>
		_selectedIndex >= 0 && _selectedIndex < _mappings.Count ? _mappings[_selectedIndex] : null;

	public int MappingCount => _mappings.Count;

	public bool HasHorizontalScrollBar => false;

	public bool HasVerticalScrollIndicator => MaximumOffset > 0;

	public OverFrameMappingTable()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
			| ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
			| ControlStyles.Selectable, true);
		BackColor = UiTheme.Surface;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		TabStop = true;
		Cursor = Cursors.Default;
		AccessibleRole = AccessibleRole.Table;
		AccessibleName = "超框映射表";
	}

	public void SetMappings(IEnumerable<OverFrameMapping> mappings)
	{
		ushort? selectedCard = SelectedMapping?.CardId;
		_mappings.Clear();
		_mappings.AddRange(mappings);
		_selectedIndex = selectedCard.HasValue
			? _mappings.FindIndex(mapping => mapping.CardId == selectedCard.Value)
			: -1;
		ClampScroll();
		EnsureSelectionVisible();
		Invalidate();
	}

	protected override bool IsInputKey(Keys keyData)
	{
		Keys key = keyData & Keys.KeyCode;
		return key is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
			|| base.IsInputKey(keyData);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		e.Graphics.Clear(UiTheme.Surface);

		int headerHeight = HeaderHeight;
		int rowHeight = RowHeight;
		bool hasScroll = MaximumOffset > 0;
		int scrollWidth = hasScroll ? ScrollAreaWidth : 0;
		int tableWidth = Math.Max(1, ClientSize.Width - scrollWidth);
		(int first, int second, int third) = ColumnWidths(tableWidth);

		using SolidBrush headerBrush = new(UiTheme.Elevated);
		using SolidBrush selectedBrush = new(UiTheme.Selection);
		using SolidBrush oddBrush = new(Color.FromArgb(16, 27, 44));
		using Pen borderPen = new(UiTheme.Border);
		using Font headerFont = new(Font, FontStyle.Bold);
		e.Graphics.FillRectangle(headerBrush, 0, 0, tableWidth, headerHeight);
		DrawCell(e.Graphics, "显示卡号", headerFont, UiTheme.Muted, new Rectangle(0, 0, first, headerHeight));
		DrawCell(e.Graphics, "高图卡号", headerFont, UiTheme.Muted, new Rectangle(first, 0, second, headerHeight));
		DrawCell(e.Graphics, "模式", headerFont, UiTheme.Muted, new Rectangle(first + second, 0, third, headerHeight));
		e.Graphics.DrawLine(borderPen, 0, headerHeight - 1, tableWidth, headerHeight - 1);

		int firstVisible = rowHeight == 0 ? 0 : _scrollOffset / rowHeight;
		int y = headerHeight - (rowHeight == 0 ? 0 : _scrollOffset % rowHeight);
		for (int index = firstVisible; index < _mappings.Count && y < ClientSize.Height; index++, y += rowHeight)
		{
			Rectangle rowBounds = new(0, y, tableWidth, rowHeight);
			if (index == _selectedIndex)
			{
				e.Graphics.FillRectangle(selectedBrush, rowBounds);
				using SolidBrush accent = new(UiTheme.Primary);
				e.Graphics.FillRectangle(accent, 0, y, UiTheme.Scale(this, 3), rowHeight);
			}
			else if ((index & 1) == 1)
			{
				e.Graphics.FillRectangle(oddBrush, rowBounds);
			}

			OverFrameMapping mapping = _mappings[index];
			Color primary = index == _selectedIndex ? Color.White : UiTheme.Text;
			Color secondary = index == _selectedIndex ? Color.White : UiTheme.Muted;
			DrawCell(e.Graphics, mapping.CardId.ToString(), Font, primary, new Rectangle(0, y, first, rowHeight));
			DrawCell(e.Graphics, mapping.ArtId.ToString(), Font, secondary, new Rectangle(first, y, second, rowHeight));
			DrawCell(e.Graphics, mapping.UsesOwnArt ? "使用本卡高图" : "复用其他卡高图", Font, secondary,
				new Rectangle(first + second, y, third, rowHeight));
			e.Graphics.DrawLine(borderPen, 0, y + rowHeight - 1, tableWidth, y + rowHeight - 1);
		}

		if (_mappings.Count == 0)
		{
			TextRenderer.DrawText(e.Graphics, "暂无超框映射", Font,
				new Rectangle(0, headerHeight, tableWidth, Math.Max(1, ClientSize.Height - headerHeight)), UiTheme.Muted,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
		}

		if (hasScroll)
		{
			DrawScrollIndicator(e.Graphics);
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		Focus();
		if (HasVerticalScrollIndicator && e.X >= ClientSize.Width - ScrollAreaWidth)
		{
			Rectangle thumb = ThumbBounds();
			if (thumb.Contains(e.Location))
			{
				_draggingThumb = true;
				_thumbDragOffset = e.Y - thumb.Top;
				Capture = true;
			}
			else
			{
				ScrollBy(e.Y < thumb.Top ? -ViewportHeight : ViewportHeight);
			}
			Invalidate();
			return;
		}

		int index = RowAt(e.Y);
		if (index >= 0)
		{
			SelectIndex(index);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (!_draggingThumb || MaximumOffset <= 0)
		{
			return;
		}
		Rectangle track = TrackBounds();
		Rectangle thumb = ThumbBounds();
		int travel = Math.Max(1, track.Height - thumb.Height);
		int position = Math.Clamp(e.Y - _thumbDragOffset - track.Top, 0, travel);
		_scrollOffset = (int)Math.Round((double)position / travel * MaximumOffset);
		Invalidate();
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		_draggingThumb = false;
		Capture = false;
		Invalidate();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (e.Delta == 0)
		{
			return;
		}
		int steps = Math.Max(1, Math.Abs(e.Delta) / SystemInformation.MouseWheelScrollDelta);
		ScrollBy(Math.Sign(-e.Delta) * steps * RowHeight * 3);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (_mappings.Count == 0)
		{
			return;
		}
		int page = Math.Max(1, ViewportHeight / RowHeight);
		int next = _selectedIndex < 0 ? 0 : _selectedIndex;
		switch (e.KeyCode)
		{
			case Keys.Up:
				next--;
				break;
			case Keys.Down:
				next++;
				break;
			case Keys.PageUp:
				next -= page;
				break;
			case Keys.PageDown:
				next += page;
				break;
			case Keys.Home:
				next = 0;
				break;
			case Keys.End:
				next = _mappings.Count - 1;
				break;
			default:
				return;
		}
		SelectIndex(Math.Clamp(next, 0, _mappings.Count - 1));
		e.Handled = true;
		e.SuppressKeyPress = true;
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		ClampScroll();
		EnsureSelectionVisible();
		Invalidate();
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		Invalidate();
	}

	private int HeaderHeight => UiTheme.Scale(this, 34);

	private int RowHeight => UiTheme.Scale(this, 31);

	private int ScrollAreaWidth => UiTheme.Scale(this, 15);

	private int ViewportHeight => Math.Max(1, ClientSize.Height - HeaderHeight);

	private int MaximumOffset => Math.Max(0, _mappings.Count * RowHeight - ViewportHeight);

	private void SelectIndex(int index)
	{
		if (index < 0 || index >= _mappings.Count || index == _selectedIndex)
		{
			return;
		}
		_selectedIndex = index;
		EnsureSelectionVisible();
		Invalidate();
		SelectedMappingChanged?.Invoke(this, EventArgs.Empty);
	}

	private int RowAt(int y)
	{
		if (y < HeaderHeight)
		{
			return -1;
		}
		int index = (_scrollOffset + y - HeaderHeight) / RowHeight;
		return index >= 0 && index < _mappings.Count ? index : -1;
	}

	private void ScrollBy(int pixels)
	{
		int next = Math.Clamp(_scrollOffset + pixels, 0, MaximumOffset);
		if (next == _scrollOffset)
		{
			return;
		}
		_scrollOffset = next;
		Invalidate();
	}

	private void EnsureSelectionVisible()
	{
		if (_selectedIndex < 0)
		{
			return;
		}
		int top = _selectedIndex * RowHeight;
		int bottom = top + RowHeight;
		if (top < _scrollOffset)
		{
			_scrollOffset = top;
		}
		else if (bottom > _scrollOffset + ViewportHeight)
		{
			_scrollOffset = bottom - ViewportHeight;
		}
		ClampScroll();
	}

	private void ClampScroll()
	{
		_scrollOffset = Math.Clamp(_scrollOffset, 0, MaximumOffset);
	}

	private (int First, int Second, int Third) ColumnWidths(int availableWidth)
	{
		int first = Math.Clamp((int)Math.Round(availableWidth * 0.22), UiTheme.Scale(this, 110), UiTheme.Scale(this, 170));
		int second = Math.Clamp((int)Math.Round(availableWidth * 0.22), UiTheme.Scale(this, 110), UiTheme.Scale(this, 170));
		int third = Math.Max(1, availableWidth - first - second);
		return (first, second, third);
	}

	private void DrawCell(Graphics graphics, string text, Font font, Color color, Rectangle bounds)
	{
		int left = UiTheme.Scale(this, 10);
		Rectangle textBounds = new(bounds.X + left, bounds.Y,
			Math.Max(1, bounds.Width - left - UiTheme.Scale(this, 5)), bounds.Height);
		TextRenderer.DrawText(graphics, text, font, textBounds, color,
			TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
			| TextFormatFlags.SingleLine);
	}

	private Rectangle TrackBounds()
	{
		int width = Math.Max(UiTheme.Scale(this, 4), 3);
		int left = ClientSize.Width - ScrollAreaWidth + (ScrollAreaWidth - width) / 2;
		int top = HeaderHeight + UiTheme.Scale(this, 5);
		return new Rectangle(left, top, width, Math.Max(1, ClientSize.Height - top - UiTheme.Scale(this, 5)));
	}

	private Rectangle ThumbBounds()
	{
		Rectangle track = TrackBounds();
		int contentHeight = Math.Max(1, _mappings.Count * RowHeight);
		int minimum = Math.Min(track.Height, UiTheme.Scale(this, 32));
		int height = Math.Clamp((int)Math.Round((double)ViewportHeight / contentHeight * track.Height), minimum, track.Height);
		int travel = Math.Max(0, track.Height - height);
		int top = track.Top + (MaximumOffset == 0 ? 0 : (int)Math.Round((double)_scrollOffset / MaximumOffset * travel));
		return new Rectangle(track.Left, top, track.Width, height);
	}

	private void DrawScrollIndicator(Graphics graphics)
	{
		Rectangle strip = new(ClientSize.Width - ScrollAreaWidth, 0, ScrollAreaWidth, ClientSize.Height);
		using SolidBrush stripBrush = new(UiTheme.Surface);
		using SolidBrush trackBrush = new(Color.FromArgb(72, UiTheme.Border));
		using SolidBrush thumbBrush = new(_draggingThumb ? UiTheme.Primary : Color.FromArgb(122, 148, 180));
		graphics.FillRectangle(stripBrush, strip);
		graphics.FillRoundedRectangle(trackBrush, TrackBounds(), Math.Max(2, TrackBounds().Width / 2f));
		graphics.FillRoundedRectangle(thumbBrush, ThumbBounds(), Math.Max(2, ThumbBounds().Width / 2f));
	}
}
