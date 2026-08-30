using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// Keeps the native ComboBox keyboard and accessibility behavior while
/// replacing the light Windows drop-down button with the application chrome.
/// </summary>
public sealed class ModernComboBox : ComboBox
{
	private const int WmPaint = 0x000F;
	private const int WmNcPaint = 0x0085;

	public ModernComboBox()
	{
		SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (OperatingSystem.IsWindows())
		{
			// The themed Win32 ComboBox ignores BackColor for its closed state on
			// several Windows 10/11 builds. Disabling only this control's native
			// theme lets the owner-drawn dark palette remain DPI/accessibility safe.
			SetWindowTheme(Handle, "", "");
		}
	}

	protected override void WndProc(ref Message message)
	{
		base.WndProc(ref message);
		if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated && !IsDisposed)
		{
			using Graphics graphics = CreateGraphics();
			DrawDropDownChrome(graphics);
		}
	}

	private void DrawDropDownChrome(Graphics graphics)
	{
		float scale = Math.Max(1f, DeviceDpi / 96f);
		int buttonWidth = Math.Max(UiTheme.Scale(this, 30), SystemInformation.VerticalScrollBarWidth);
		if (DropDownStyle == ComboBoxStyle.DropDownList)
		{
			using SolidBrush background = new(BackColor);
			graphics.FillRectangle(background, ClientRectangle);
			string selectedText = SelectedItem?.ToString() ?? Text;
			TextRenderer.DrawText(graphics, selectedText, Font,
				new Rectangle(UiTheme.Scale(this, 7), 0,
					Math.Max(1, ClientSize.Width - buttonWidth - UiTheme.Scale(this, 10)), ClientSize.Height),
				Enabled ? ForeColor : UiTheme.Muted,
				TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
		}
		Rectangle button = new(Math.Max(0, ClientSize.Width - buttonWidth), 0,
			Math.Min(buttonWidth, ClientSize.Width), ClientSize.Height);
		using SolidBrush fill = new(BackColor);
		graphics.FillRectangle(fill, button);

		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		Color glyphColor = Enabled ? (Focused || DroppedDown ? UiTheme.Primary : UiTheme.Muted) : UiTheme.Border;
		using Pen glyph = new(glyphColor, 1.7f * scale)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		float centerX = button.Left + button.Width / 2f;
		float centerY = button.Top + button.Height / 2f;
		float half = 3.5f * scale;
		graphics.DrawLines(glyph,
		[
			new PointF(centerX - half, centerY - 1.5f * scale),
			new PointF(centerX, centerY + 2f * scale),
			new PointF(centerX + half, centerY - 1.5f * scale)
		]);
	}

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(IntPtr handle, string? subAppName, string? subIdList);
}
