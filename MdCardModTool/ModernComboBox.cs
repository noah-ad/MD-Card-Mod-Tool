using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class ModernComboBox : ComboBox
{
	private const int WmPaint = 15;

	private const int WmNcPaint = 133;

	public ModernComboBox()
	{
		SetStyle(ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		if (OperatingSystem.IsWindows())
		{
			SetWindowTheme(base.Handle, "", "");
		}
	}

	protected override void WndProc(ref Message message)
	{
		base.WndProc(ref message);
		if ((message.Msg == 15 || message.Msg == 133) && base.IsHandleCreated && !base.IsDisposed)
		{
			using (Graphics graphics = CreateGraphics())
			{
				DrawDropDownChrome(graphics);
			}
		}
	}

	private void DrawDropDownChrome(Graphics graphics)
	{
		float num = Math.Max(1f, (float)base.DeviceDpi / 96f);
		int num2 = Math.Max(UiTheme.Scale(this, 30), SystemInformation.VerticalScrollBarWidth);
		if (base.DropDownStyle == ComboBoxStyle.DropDownList)
		{
			using SolidBrush brush = new SolidBrush(BackColor);
			graphics.FillRectangle(brush, base.ClientRectangle);
			string text = base.SelectedItem?.ToString() ?? Text;
			TextRenderer.DrawText(graphics, text, Font, new Rectangle(UiTheme.Scale(this, 7), 0, Math.Max(1, base.ClientSize.Width - num2 - UiTheme.Scale(this, 10)), base.ClientSize.Height), base.Enabled ? ForeColor : UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		Rectangle rect = new Rectangle(Math.Max(0, base.ClientSize.Width - num2), 0, Math.Min(num2, base.ClientSize.Width), base.ClientSize.Height);
		using SolidBrush brush2 = new SolidBrush(BackColor);
		graphics.FillRectangle(brush2, rect);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using Pen pen = new Pen((!base.Enabled) ? UiTheme.Border : ((Focused || base.DroppedDown) ? UiTheme.Primary : UiTheme.Muted), 1.7f * num)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		float num3 = (float)rect.Left + (float)rect.Width / 2f;
		float num4 = (float)rect.Top + (float)rect.Height / 2f;
		float num5 = 3.5f * num;
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(num3 - num5, num4 - 1.5f * num),
			new PointF(num3, num4 + 2f * num),
			new PointF(num3 + num5, num4 - 1.5f * num)
		});
	}

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	private static extern int SetWindowTheme(nint handle, string? subAppName, string? subIdList);
}
