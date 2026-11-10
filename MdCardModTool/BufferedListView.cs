using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class BufferedListView : ListView
{
	private const int WmThemeChanged = 794;
	private const int WmLeftButtonDown = 0x0201;
	private const int WmLeftButtonUp = 0x0202;
	private const int MouseControl = 8;
	private const int MouseShift = 4;
	private const int LvmSetItemState = 0x102b;
	private const uint LvisSelected = 2;
	private bool _toggleSelectionOnClick;
	private HeaderBackground? _headerBackground;

	// Only Mod Manager opts into checklist-style selection. Other resource
	// lists retain the usual single-selection behavior.
	public bool ToggleSelectionOnClick
	{
		get => _toggleSelectionOnClick;
		set
		{
			if (_toggleSelectionOnClick == value) return;
			_toggleSelectionOnClick = value;
			Invalidate();
		}
	}

	public bool DarkThemeRequested { get; private set; }

	public BufferedListView()
	{
		DoubleBuffered = true;
	}

	protected override void WndProc(ref Message message)
	{
		if (ToggleSelectionOnClick && MultiSelect && Enabled &&
			message.Msg is WmLeftButtonDown or WmLeftButtonUp && ((long)message.WParam & MouseShift) == 0)
		{
			// Retain native hit testing, focus and drag notifications; a plain
			// row click toggles one row, rather than discarding other choices.
			message.WParam = (nint)((long)message.WParam | MouseControl);
		}
		base.WndProc(ref message);
	}

	public void SelectAllRows()
	{
		int count = VirtualMode ? VirtualListSize : Items.Count;
		if (!MultiSelect || count == 0) return;
		ListItemState state = new() { State = LvisSelected, StateMask = LvisSelected };
		SendMessage(Handle, LvmSetItemState, -1, ref state);
		Invalidate();
	}

	protected override bool ProcessCmdKey(ref Message message, Keys keyData)
	{
		if (ToggleSelectionOnClick && MultiSelect)
		{
			if (keyData == (Keys.Control | Keys.A))
			{
				SelectAllRows();
				return true;
			}
			if (keyData == Keys.Escape && SelectedIndices.Count > 0)
			{
				SelectedIndices.Clear();
				return true;
			}
			if (keyData == Keys.Space && FocusedItem is ListViewItem item && item.Index >= 0)
			{
				if (SelectedIndices.Contains(item.Index)) SelectedIndices.Remove(item.Index);
				else SelectedIndices.Add(item.Index);
				return true;
			}
		}
		return base.ProcessCmdKey(ref message, keyData);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (OperatingSystem.IsWindows())
		{
			DarkThemeRequested = SetWindowTheme(base.Handle, "DarkMode_Explorer", null) == 0;
			SendMessage(base.Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
			nint header = SendMessage(Handle, 0x101f, 0, 0); // LVM_GETHEADER
			if (header != 0) _headerBackground = new HeaderBackground(this, header);
		}
	}

	protected override void OnHandleDestroyed(EventArgs e)
	{
		_headerBackground?.ReleaseHandle();
		_headerBackground = null;
		base.OnHandleDestroyed(e);
	}

	// OwnerDraw only paints actual columns. The native header otherwise leaves
	// an unpainted white block after the last column/at the scrollbar junction.
	private sealed class HeaderBackground : NativeWindow
	{
		private readonly BufferedListView _owner;
		internal HeaderBackground(BufferedListView owner, nint handle) { _owner = owner; AssignHandle(handle); }
		protected override void WndProc(ref Message message)
		{
			base.WndProc(ref message);
			if (message.Msg is not (0x000f or 0x0318) || _owner.IsDisposed || !_owner.OwnerDraw || Handle == 0) return;
			if (!GetClientRect(Handle, out NativeRect bounds)) return;
			int end = _owner.Columns.Cast<ColumnHeader>().Sum(c => c.Width) - GetScrollPos(_owner.Handle, 0);
			end = Math.Clamp(end, 0, bounds.Right);
			if (end >= bounds.Right) return;
			using Graphics graphics = message.Msg == 0x0318 && message.WParam != 0 ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
			using var brush = new SolidBrush(UiTheme.Elevated);
			graphics.FillRectangle(brush, end, 0, bounds.Right - end, bounds.Bottom);
			using var border = new Pen(UiTheme.Border);
			graphics.DrawLine(border, end, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
		}
	}
	[StructLayout(LayoutKind.Sequential)]
	private struct NativeRect { public int Left, Top, Right, Bottom; }
	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint handle, out NativeRect rectangle);
	[DllImport("user32.dll")]
	private static extern int GetScrollPos(nint handle, int bar);

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint handle, string? subAppName, string? subIdList);

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct ListItemState
	{
		public uint Mask;
		public int Item;
		public int SubItem;
		public uint State;
		public uint StateMask;
		public nint Text;
		public int TextLength;
		public int Image;
		public nint Parameter;
		public int Indent;
		public int GroupId;
		public uint Columns;
		public nint ColumnIndices;
		public nint ColumnFormats;
		public int Group;
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint SendMessage(nint handle, int message, nint wParam, ref ListItemState state);
}
