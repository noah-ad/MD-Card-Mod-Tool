using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

// Send input to this control's window only; never move the desktop cursor or
// send keyboard input to other applications. Testing SelectedIndices.Add alone
// does not cover the native virtual-list mouse/selection notifications.
internal static class ModSelectionInputTests
{
	internal static void ClickRow(ListView list, int index, Keys modifiers = Keys.None, int column = 0)
	{
		list.EnsureVisible(index);
		Application.DoEvents();
		Rectangle row = list.GetItemRect(index);
		int columnLeft = list.Columns.Cast<ColumnHeader>().Take(column).Sum(x => x.Width);
		Point point = new(Math.Max(row.Left + columnLeft + 12, 12), row.Top + row.Height / 2);
		int keys = ((modifiers & Keys.Control) != 0 ? 8 : 0) | ((modifiers & Keys.Shift) != 0 ? 4 : 0);
		nint position = (point.Y << 16) | (point.X & 0xffff);
		// Queue the pair before pumping: the native mouse-down handler can
		// enter a tracking loop and must be able to consume the mouse-up.
		WithModifiers(modifiers, () =>
		{
			PostMessage(list.Handle, 0x0201, keys | 1, position); // WM_LBUTTONDOWN
			PostMessage(list.Handle, 0x0202, keys, position); // WM_LBUTTONUP
			Application.DoEvents();
		});
	}

	internal static void RunClicks(ListView list, Button export)
	{
		list.SelectedIndices.Clear();
		ClickRow(list, 0);
		Check(new[] { 0 }, "first click");
		ClickRow(list, 1);
		Check(new[] { 0, 1 }, "second plain click keeps previous selection");
		ClickRow(list, 0);
		Check(new[] { 1 }, "click again deselects only that item");
		ClickRow(list, 0, Keys.Control);
		Check(new[] { 0, 1 }, "Ctrl click adds");
		ClickRow(list, 0, Keys.Control);
		Check(new[] { 1 }, "Ctrl click removes");
		ClickRow(list, 1);
		Check(Array.Empty<int>(), "deselect last item");
		ClickRow(list, 0);
		ClickRow(list, 3, Keys.Shift);
		Check(new[] { 0, 1, 2, 3 }, "Shift range and virtual selection feedback");
		PressKey(list, Keys.Control | Keys.A);
		Check(Enumerable.Range(0, list.VirtualListSize).ToArray(), "Ctrl+A selects the visible list");
		PressKey(list, Keys.Escape);
		Check(Array.Empty<int>(), "Escape clears selection");
		ClickRow(list, 1);
		PressKey(list, Keys.Space);
		Check(Array.Empty<int>(), "Space toggles focused item off");
		PressKey(list, Keys.Space);
		Check(new[] { 1 }, "Space toggles focused item on");
		PressKey(list, Keys.Escape);
		ClickRow(list, 0, column: 1);
		ClickRow(list, 2, column: 3);
		Check(new[] { 0, 2 }, "row text and timestamp clicks also toggle selection");
		ClickRow(list, 0, column: 4);
		Check(new[] { 2 }, "Bundle column click toggles only that row");
		PressKey(list, Keys.Escape);
		list.SelectedIndices.Clear();
		Console.WriteLine("inputClicks=True; plainClickMultiSelect=True; toggle=True; ctrl=True; shift=True; ctrlA=True; escape=True; space=True");

		void Check(int[] expected, string action)
		{
			int[] actual = list.SelectedIndices.Cast<int>().ToArray();
			Console.WriteLine($"input={action}; expected={string.Join(",", expected)}; actual={string.Join(",", actual)}; export={export.Text}");
			if (!actual.SequenceEqual(expected)) throw new InvalidDataException("Native selection mismatch: " + action);
			string label = expected.Length == 0 ? Localizer.T("action.mods.export.selected") : Localizer.F("action.mods.export.selectedCount", expected.Length);
			if (export.Text != label || export.Enabled != (expected.Length > 0))
				throw new InvalidDataException("Export selection feedback mismatch: " + action);
		}
	}

	internal static void PressKey(Control control, Keys key)
	{
		WithModifiers(key & Keys.Modifiers, () =>
		{
			Message message = Message.Create(control.Handle, 0x0100, (nint)(key & Keys.KeyCode), 0);
			if (!control.PreProcessMessage(ref message))
				throw new InvalidDataException("Selection shortcut was not handled: " + key);
		});
		Application.DoEvents();
	}

	private static void WithModifiers(Keys modifiers, Action action)
	{
		// This state table belongs only to the test UI thread. Always restore
		// it; SetKeyboardState does not send global keyboard input.
		byte[] previous = new byte[256];
		if (!GetKeyboardState(previous)) throw new InvalidOperationException("Cannot read test-thread keyboard state");
		byte[] state = (byte[])previous.Clone();
		foreach (int key in new[] { 0x10, 0xa0, 0xa1, 0x11, 0xa2, 0xa3, 0x12, 0xa4, 0xa5 }) state[key] = 0;
		if ((modifiers & Keys.Control) != 0) state[0x11] = state[0xa2] = 0x80;
		if ((modifiers & Keys.Shift) != 0) state[0x10] = state[0xa0] = 0x80;
		if (!SetKeyboardState(state)) throw new InvalidOperationException("Cannot set test-thread keyboard state");
		try { action(); }
		finally { SetKeyboardState(previous); }
	}

	[DllImport("user32.dll")]
	private static extern bool GetKeyboardState(byte[] state);
	[DllImport("user32.dll")]
	private static extern bool SetKeyboardState(byte[] state);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
}
