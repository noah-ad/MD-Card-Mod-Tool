using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class OverFrameMappingTable : Control
{
	private readonly List<OverFrameMapping> _mappings = new List<OverFrameMapping>();

	private int _selectedIndex = -1;

	private int _scrollOffset;

	private bool _draggingThumb;

	private int _thumbDragOffset;

	public OverFrameMapping? SelectedMapping
	{
		get
		{
			if (_selectedIndex < 0 || _selectedIndex >= _mappings.Count)
			{
				return null;
			}
			return _mappings[_selectedIndex];
		}
	}

	public int MappingCount => _mappings.Count;

	public bool HasHorizontalScrollBar => false;

	public bool HasVerticalScrollIndicator => MaximumOffset > 0;

	private int HeaderHeight => UiTheme.Scale(this, 34);

	private int RowHeight => UiTheme.Scale(this, 31);

	private int ScrollAreaWidth => UiTheme.Scale(this, 15);

	private int ViewportHeight => Math.Max(1, base.ClientSize.Height - HeaderHeight);

	private int MaximumOffset => Math.Max(0, _mappings.Count * RowHeight - ViewportHeight);

	public event EventHandler? SelectedMappingChanged;

	public OverFrameMappingTable()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = UiTheme.Surface;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		base.TabStop = true;
		Cursor = Cursors.Default;
		base.AccessibleRole = AccessibleRole.Table;
		base.AccessibleName = "超框映射表";
	}

	public void SetMappings(IEnumerable<OverFrameMapping> mappings)
	{
		ushort? selectedCard = SelectedMapping?.CardId;
		_mappings.Clear();
		_mappings.AddRange(mappings);
		_selectedIndex = (selectedCard.HasValue ? _mappings.FindIndex((OverFrameMapping mapping) => mapping.CardId == selectedCard.Value) : (-1));
		ClampScroll();
		EnsureSelectionVisible();
		Invalidate();
	}

	protected override bool IsInputKey(Keys keyData)
	{
		bool flag;
		switch (keyData & Keys.KeyCode)
		{
		case Keys.Prior:
		case Keys.Next:
		case Keys.End:
		case Keys.Home:
		case Keys.Up:
		case Keys.Down:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return base.IsInputKey(keyData);
		}
		return true;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using SolidBrush brush = new SolidBrush(UiTheme.Surface);
		e.Graphics.FillRectangle(brush, base.ClientRectangle);
		int headerHeight = HeaderHeight;
		int rowHeight = RowHeight;
		bool flag = MaximumOffset > 0;
		int num = (flag ? ScrollAreaWidth : 0);
		int num2 = Math.Max(1, base.ClientSize.Width - num);
		var (num3, num4, width) = ColumnWidths(num2);
		using SolidBrush brush2 = new SolidBrush(UiTheme.Elevated);
		using SolidBrush brush3 = new SolidBrush(UiTheme.Selection);
		using SolidBrush brush4 = new SolidBrush(Color.FromArgb(16, 27, 44));
		using Pen pen = new Pen(UiTheme.Border);
		using Font font = new Font(Font, FontStyle.Bold);
		e.Graphics.FillRectangle(brush2, 0, 0, num2, headerHeight);
		DrawCell(e.Graphics, "显示卡号", font, UiTheme.Muted, new Rectangle(0, 0, num3, headerHeight));
		DrawCell(e.Graphics, "高图卡号", font, UiTheme.Muted, new Rectangle(num3, 0, num4, headerHeight));
		DrawCell(e.Graphics, "模式", font, UiTheme.Muted, new Rectangle(num3 + num4, 0, width, headerHeight));
		e.Graphics.DrawLine(pen, 0, headerHeight - 1, num2, headerHeight - 1);
		int num5 = ((rowHeight != 0) ? (_scrollOffset / rowHeight) : 0);
		int num6 = headerHeight - ((rowHeight != 0) ? (_scrollOffset % rowHeight) : 0);
		int num7 = num5;
		while (num7 < _mappings.Count && num6 < base.ClientSize.Height)
		{
			Rectangle rect = new Rectangle(0, num6, num2, rowHeight);
			if (num7 == _selectedIndex)
			{
				e.Graphics.FillRectangle(brush3, rect);
				using SolidBrush brush5 = new SolidBrush(UiTheme.Primary);
				e.Graphics.FillRectangle(brush5, 0, num6, UiTheme.Scale(this, 3), rowHeight);
			}
			else if ((num7 & 1) == 1)
			{
				e.Graphics.FillRectangle(brush4, rect);
			}
			OverFrameMapping overFrameMapping = _mappings[num7];
			Color color = ((num7 == _selectedIndex) ? Color.White : UiTheme.Text);
			Color color2 = ((num7 == _selectedIndex) ? Color.White : UiTheme.Muted);
			DrawCell(e.Graphics, overFrameMapping.CardId.ToString(), Font, color, new Rectangle(0, num6, num3, rowHeight));
			DrawCell(e.Graphics, overFrameMapping.ArtId.ToString(), Font, color2, new Rectangle(num3, num6, num4, rowHeight));
			DrawCell(e.Graphics, overFrameMapping.UsesOwnArt ? "使用本卡高图" : "复用其他卡高图", Font, color2, new Rectangle(num3 + num4, num6, width, rowHeight));
			e.Graphics.DrawLine(pen, 0, num6 + rowHeight - 1, num2, num6 + rowHeight - 1);
			num7++;
			num6 += rowHeight;
		}
		if (_mappings.Count == 0)
		{
			TextRenderer.DrawText(e.Graphics, "暂无超框映射", Font, new Rectangle(0, headerHeight, num2, Math.Max(1, base.ClientSize.Height - headerHeight)), UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		if (flag)
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
		if (HasVerticalScrollIndicator && e.X >= base.ClientSize.Width - ScrollAreaWidth)
		{
			Rectangle rectangle = ThumbBounds();
			if (rectangle.Contains(e.Location))
			{
				_draggingThumb = true;
				_thumbDragOffset = e.Y - rectangle.Top;
				base.Capture = true;
			}
			else
			{
				ScrollBy((e.Y < rectangle.Top) ? (-ViewportHeight) : ViewportHeight);
			}
			Invalidate();
		}
		else
		{
			int num = RowAt(e.Y);
			if (num >= 0)
			{
				SelectIndex(num);
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		if (_draggingThumb && MaximumOffset > 0)
		{
			Rectangle rectangle = TrackBounds();
			Rectangle rectangle2 = ThumbBounds();
			int num = Math.Max(1, rectangle.Height - rectangle2.Height);
			int num2 = Math.Clamp(e.Y - _thumbDragOffset - rectangle.Top, 0, num);
			_scrollOffset = (int)Math.Round((double)num2 / (double)num * (double)MaximumOffset);
			Invalidate();
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left)
		{
			_draggingThumb = false;
			base.Capture = false;
			Invalidate();
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (e.Delta != 0)
		{
			int num = Math.Max(1, Math.Abs(e.Delta) / SystemInformation.MouseWheelScrollDelta);
			ScrollBy(Math.Sign(-e.Delta) * num * RowHeight * 3);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (_mappings.Count != 0)
		{
			int num = Math.Max(1, ViewportHeight / RowHeight);
			int num2 = ((_selectedIndex >= 0) ? _selectedIndex : 0);
			switch (e.KeyCode)
			{
			default:
				return;
			case Keys.Up:
				num2--;
				break;
			case Keys.Down:
				num2++;
				break;
			case Keys.Prior:
				num2 -= num;
				break;
			case Keys.Next:
				num2 += num;
				break;
			case Keys.Home:
				num2 = 0;
				break;
			case Keys.End:
				num2 = _mappings.Count - 1;
				break;
			case Keys.Left:
			case Keys.Right:
				return;
			}
			SelectIndex(Math.Clamp(num2, 0, _mappings.Count - 1));
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
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

	private void SelectIndex(int index)
	{
		if (index >= 0 && index < _mappings.Count && index != _selectedIndex)
		{
			_selectedIndex = index;
			EnsureSelectionVisible();
			Invalidate();
			this.SelectedMappingChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private int RowAt(int y)
	{
		if (y < HeaderHeight)
		{
			return -1;
		}
		int num = (_scrollOffset + y - HeaderHeight) / RowHeight;
		if (num < 0 || num >= _mappings.Count)
		{
			return -1;
		}
		return num;
	}

	private void ScrollBy(int pixels)
	{
		int num = Math.Clamp(_scrollOffset + pixels, 0, MaximumOffset);
		if (num != _scrollOffset)
		{
			_scrollOffset = num;
			Invalidate();
		}
	}

	private void EnsureSelectionVisible()
	{
		if (_selectedIndex >= 0)
		{
			int num = _selectedIndex * RowHeight;
			int num2 = num + RowHeight;
			if (num < _scrollOffset)
			{
				_scrollOffset = num;
			}
			else if (num2 > _scrollOffset + ViewportHeight)
			{
				_scrollOffset = num2 - ViewportHeight;
			}
			ClampScroll();
		}
	}

	private void ClampScroll()
	{
		_scrollOffset = Math.Clamp(_scrollOffset, 0, MaximumOffset);
	}

	private (int First, int Second, int Third) ColumnWidths(int availableWidth)
	{
		int num = Math.Clamp((int)Math.Round((double)availableWidth * 0.22), UiTheme.Scale(this, 110), UiTheme.Scale(this, 170));
		int num2 = Math.Clamp((int)Math.Round((double)availableWidth * 0.22), UiTheme.Scale(this, 110), UiTheme.Scale(this, 170));
		int item = Math.Max(1, availableWidth - num - num2);
		return (First: num, Second: num2, Third: item);
	}

	private void DrawCell(Graphics graphics, string text, Font font, Color color, Rectangle bounds)
	{
		int num = UiTheme.Scale(this, 10);
		Rectangle bounds2 = new Rectangle(bounds.X + num, bounds.Y, Math.Max(1, bounds.Width - num - UiTheme.Scale(this, 5)), bounds.Height);
		TextRenderer.DrawText(graphics, text, font, bounds2, color, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
	}

	private Rectangle TrackBounds()
	{
		int num = Math.Max(UiTheme.Scale(this, 4), 3);
		int x = base.ClientSize.Width - ScrollAreaWidth + (ScrollAreaWidth - num) / 2;
		int num2 = HeaderHeight + UiTheme.Scale(this, 5);
		return new Rectangle(x, num2, num, Math.Max(1, base.ClientSize.Height - num2 - UiTheme.Scale(this, 5)));
	}

	private Rectangle ThumbBounds()
	{
		Rectangle rectangle = TrackBounds();
		int num = Math.Max(1, _mappings.Count * RowHeight);
		int min = Math.Min(rectangle.Height, UiTheme.Scale(this, 32));
		int num2 = Math.Clamp((int)Math.Round((double)ViewportHeight / (double)num * (double)rectangle.Height), min, rectangle.Height);
		int num3 = Math.Max(0, rectangle.Height - num2);
		int y = rectangle.Top + ((MaximumOffset != 0) ? ((int)Math.Round((double)_scrollOffset / (double)MaximumOffset * (double)num3)) : 0);
		return new Rectangle(rectangle.Left, y, rectangle.Width, num2);
	}

	private void DrawScrollIndicator(Graphics graphics)
	{
		Rectangle rect = new Rectangle(base.ClientSize.Width - ScrollAreaWidth, 0, ScrollAreaWidth, base.ClientSize.Height);
		using SolidBrush brush = new SolidBrush(UiTheme.Surface);
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(72, UiTheme.Border));
		using SolidBrush brush3 = new SolidBrush(_draggingThumb ? UiTheme.Primary : Color.FromArgb(122, 148, 180));
		graphics.FillRectangle(brush, rect);
		graphics.FillRoundedRectangle(brush2, TrackBounds(), Math.Max(2f, (float)TrackBounds().Width / 2f));
		graphics.FillRoundedRectangle(brush3, ThumbBounds(), Math.Max(2f, (float)ThumbBounds().Width / 2f));
	}
}
