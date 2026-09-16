using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class GradientBanner : Panel
{
	public GradientBanner()
	{
		DoubleBuffered = true;
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		if (base.Width <= 0 || base.Height <= 0)
		{
			return;
		}
		using LinearGradientBrush brush = new LinearGradientBrush(base.ClientRectangle, Color.FromArgb(10, 34, 52), UiTheme.Window, 0f);
		e.Graphics.FillRectangle(brush, base.ClientRectangle);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num = (float)base.DeviceDpi / 96f;
		using Pen pen = new Pen(Color.FromArgb(44, UiTheme.Primary), num);
		using Pen pen2 = new Pen(Color.FromArgb(78, UiTheme.Gold), num);
		for (int i = 0; i < 5; i++)
		{
			float num2 = (float)base.Width - (float)(330 - i * 75) * num;
			float num3 = (float)base.Height * 0.5f;
			float num4 = 30f * num;
			e.Graphics.DrawPolygon((i == 2) ? pen2 : pen, new PointF[6]
			{
				new PointF(num2 - num4, num3),
				new PointF(num2 - num4 / 2f, num3 - num4),
				new PointF(num2 + num4 / 2f, num3 - num4),
				new PointF(num2 + num4, num3),
				new PointF(num2 + num4 / 2f, num3 + num4),
				new PointF(num2 - num4 / 2f, num3 + num4)
			});
			e.Graphics.DrawLine(pen, num2 + num4, num3, num2 + 45f * num, num3);
		}
		e.Graphics.DrawLines(pen2, new PointF[3]
		{
			new PointF(0f, (float)base.Height - 2f * num),
			new PointF(96f * num, (float)base.Height - 2f * num),
			new PointF(112f * num, (float)base.Height - 10f * num)
		});
		e.Graphics.DrawLine(pen, 118f * num, (float)base.Height - 10f * num, base.Width, (float)base.Height - 10f * num);
	}
}
