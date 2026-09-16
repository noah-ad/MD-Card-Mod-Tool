using System;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class ImeAwareTextBox : TextBox
{
	private const int WmImeStartComposition = 269;

	private const int WmImeEndComposition = 270;

	public bool IsImeComposing { get; private set; }

	public event EventHandler? ImeCompositionStarted;

	public event EventHandler? ImeCompositionEnded;

	protected override void WndProc(ref Message message)
	{
		if (message.Msg == 269 && !IsImeComposing)
		{
			IsImeComposing = true;
			this.ImeCompositionStarted?.Invoke(this, EventArgs.Empty);
		}
		base.WndProc(ref message);
		if (message.Msg == 270 && IsImeComposing)
		{
			IsImeComposing = false;
			this.ImeCompositionEnded?.Invoke(this, EventArgs.Empty);
		}
	}
}
