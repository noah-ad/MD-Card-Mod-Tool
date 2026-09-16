using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class BufferedListView : ListView
{
	private const int WmThemeChanged = 794;

	public bool DarkThemeRequested { get; private set; }

	public BufferedListView()
	{
		DoubleBuffered = true;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (OperatingSystem.IsWindows())
		{
			DarkThemeRequested = SetWindowTheme(base.Handle, "DarkMode_Explorer", null) == 0;
			SendMessage(base.Handle, 794, IntPtr.Zero, IntPtr.Zero);
		}
	}

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint handle, string? subAppName, string? subIdList);

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
}
