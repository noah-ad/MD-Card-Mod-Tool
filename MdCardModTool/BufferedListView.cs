using System;
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
		}
	}

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
