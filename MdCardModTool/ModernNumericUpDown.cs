using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// Keeps the native NumericUpDown editing, keyboard and accessibility support,
/// while painting its otherwise bright Win32 spinner in the MD dark palette.
/// </summary>
public sealed class ModernNumericUpDown : NumericUpDown
{
	private const int WmPaint = 0x000F;
	private const int WmNcPaint = 0x0085;
	private const int WmEnable = 0x000A;

	public ModernNumericUpDown()
	{
		BackColor = UiTheme.SurfaceAlt;
		ForeColor = UiTheme.Text;
		BorderStyle = BorderStyle.FixedSingle;
		Font = new Font("Microsoft YaHei UI", 9f);
		SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
		ControlAdded += (_, e) =>
		{
			if (e.Control != null)
			{
				StyleChild(e.Control);
			}
		};
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		foreach (Control child in Controls)
		{
			StyleChild(child);
		}
	}

	protected override void WndProc(ref Message message)
	{
		base.WndProc(ref message);
		if (message.Msg is WmPaint or WmNcPaint or WmEnable && IsHandleCreated && !IsDisposed)
		{
			using Graphics graphics = CreateGraphics();
			DrawChrome(graphics);
		}
	}

	private void StyleChild(Control child)
	{
		child.BackColor = UiTheme.SurfaceAlt;
		child.ForeColor = UiTheme.Text;
	}

	private void DrawChrome(Graphics graphics)
	{
		float scale = Math.Max(1f, DeviceDpi / 96f);
		int border = Math.Max(1, (int)MathF.Ceiling(scale));
		int buttonWidth = Math.Max(UiTheme.Scale(this, 22), SystemInformation.VerticalScrollBarWidth);
		Rectangle button = new(Math.Max(border, ClientSize.Width - buttonWidth - border), border,
			Math.Max(1, Math.Min(buttonWidth, ClientSize.Width - border * 2)), Math.Max(1, ClientSize.Height - border * 2));
		using SolidBrush fill = new(Enabled ? UiTheme.Elevated : UiTheme.SurfaceAlt);
		graphics.FillRectangle(fill, button);
		using Pen divider = new(UiTheme.Border, scale);
		graphics.DrawLine(divider, button.Left, button.Top, button.Left, button.Bottom);

		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		Color arrowColor = Enabled ? (Focused ? UiTheme.Primary : UiTheme.Muted) : UiTheme.Border;
		using Pen arrow = new(arrowColor, 1.35f * scale)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		float centerX = button.Left + button.Width / 2f;
		float half = 2.6f * scale;
		float upperY = button.Top + button.Height * 0.29f;
		float lowerY = button.Top + button.Height * 0.71f;
		graphics.DrawLines(arrow,
		[
			new PointF(centerX - half, upperY + half / 2f),
			new PointF(centerX, upperY - half / 2f),
			new PointF(centerX + half, upperY + half / 2f)
		]);
		graphics.DrawLines(arrow,
		[
			new PointF(centerX - half, lowerY - half / 2f),
			new PointF(centerX, lowerY + half / 2f),
			new PointF(centerX + half, lowerY - half / 2f)
		]);
	}

}
