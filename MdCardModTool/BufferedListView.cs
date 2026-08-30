using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class BufferedListView : ListView
{
	public bool DarkThemeRequested { get; private set; }

	public BufferedListView()
	{
		DoubleBuffered = true;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		// Owner drawing does not include Win32's non-client scrollbars. Asking the
		// common-control theme for its dark Explorer variant keeps any unavoidable
		// vertical indicator consistent with the workspace.
		DarkThemeRequested = SetWindowTheme(Handle, "DarkMode_Explorer", null) == 0;
		SendMessage(Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
	}

	private const int WmThemeChanged = 0x031A;

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(IntPtr handle, string? subAppName, string? subIdList);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}
