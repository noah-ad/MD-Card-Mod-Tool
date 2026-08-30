using System;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// Exposes IME composition state so expensive filtering and popups do not
/// interrupt an unfinished Chinese or Japanese composition string.
/// </summary>
public sealed class ImeAwareTextBox : TextBox
{
	private const int WmImeStartComposition = 0x010D;
	private const int WmImeEndComposition = 0x010E;

	public bool IsImeComposing { get; private set; }

	public event EventHandler? ImeCompositionStarted;

	public event EventHandler? ImeCompositionEnded;

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == WmImeStartComposition && !IsImeComposing)
		{
			IsImeComposing = true;
			ImeCompositionStarted?.Invoke(this, EventArgs.Empty);
		}

		base.WndProc(ref message);

		if (message.Msg == WmImeEndComposition && IsImeComposing)
		{
			IsImeComposing = false;
			ImeCompositionEnded?.Invoke(this, EventArgs.Empty);
		}
	}
}
