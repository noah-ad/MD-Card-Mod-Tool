using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class ModernNumericUpDown : NumericUpDown
{
	private const int WmPaint = 15;

	private const int WmNcPaint = 133;

	private const int WmEnable = 10;

	public ModernNumericUpDown()
	{
		BackColor = UiTheme.SurfaceAlt;
		ForeColor = UiTheme.Text;
		base.BorderStyle = BorderStyle.FixedSingle;
		Font = new Font("Microsoft YaHei UI", 9f);
		SetStyle(ControlStyles.OptimizedDoubleBuffer, value: true);
		base.ControlAdded += delegate(object? _, ControlEventArgs e)
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
		foreach (Control control in base.Controls)
		{
			StyleChild(control);
		}
	}

	protected override void WndProc(ref Message message)
	{
		base.WndProc(ref message);
		int msg = message.Msg;
		bool flag = ((msg == 10 || msg == 15 || msg == 133) ? true : false);
		if (flag && base.IsHandleCreated && !base.IsDisposed)
		{
			using (Graphics graphics = CreateGraphics())
			{
				DrawChrome(graphics);
			}
		}
	}

	private void StyleChild(Control child)
	{
		child.BackColor = UiTheme.SurfaceAlt;
		child.ForeColor = UiTheme.Text;
	}

	private void DrawChrome(Graphics graphics)
	{
		float num = Math.Max(1f, (float)base.DeviceDpi / 96f);
		int num2 = Math.Max(1, (int)MathF.Ceiling(num));
		int num3 = Math.Max(UiTheme.Scale(this, 22), SystemInformation.VerticalScrollBarWidth);
		Rectangle rect = new Rectangle(Math.Max(num2, base.ClientSize.Width - num3 - num2), num2, Math.Max(1, Math.Min(num3, base.ClientSize.Width - num2 * 2)), Math.Max(1, base.ClientSize.Height - num2 * 2));
		using SolidBrush brush = new SolidBrush(base.Enabled ? UiTheme.Elevated : UiTheme.SurfaceAlt);
		graphics.FillRectangle(brush, rect);
		using Pen pen = new Pen(UiTheme.Border, num);
		graphics.DrawLine(pen, rect.Left, rect.Top, rect.Left, rect.Bottom);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
		using Pen pen2 = new Pen((!base.Enabled) ? UiTheme.Border : (Focused ? UiTheme.Primary : UiTheme.Muted), 1.35f * num)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		float num4 = (float)rect.Left + (float)rect.Width / 2f;
		float num5 = 2.6f * num;
		float num6 = (float)rect.Top + (float)rect.Height * 0.29f;
		float num7 = (float)rect.Top + (float)rect.Height * 0.71f;
		graphics.DrawLines(pen2, new PointF[3]
		{
			new PointF(num4 - num5, num6 + num5 / 2f),
			new PointF(num4, num6 - num5 / 2f),
			new PointF(num4 + num5, num6 + num5 / 2f)
		});
		graphics.DrawLines(pen2, new PointF[3]
		{
			new PointF(num4 - num5, num7 - num5 / 2f),
			new PointF(num4, num7 + num5 / 2f),
			new PointF(num4 + num5, num7 - num5 / 2f)
		});
	}
}
