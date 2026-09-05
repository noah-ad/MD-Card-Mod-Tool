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
		if (Width <= 0 || Height <= 0) return;
		using LinearGradientBrush gradient = new LinearGradientBrush(base.ClientRectangle, Color.FromArgb(10, 34, 52), UiTheme.Window, 0f);
		e.Graphics.FillRectangle(gradient, base.ClientRectangle);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float dpi = DeviceDpi / 96f;
		using Pen cyan = new Pen(Color.FromArgb(44, UiTheme.Primary), dpi);
		using Pen gold = new Pen(Color.FromArgb(78, UiTheme.Gold), dpi);
		// Static dueling-field geometry keeps text readable without idle animation.
		for (int i = 0; i < 5; i++)
		{
			float x = Width - (330 - i * 75) * dpi;
			float y = Height * .5f;
			float r = 30 * dpi;
			e.Graphics.DrawPolygon(i == 2 ? gold : cyan,
				[new PointF(x-r,y), new PointF(x-r/2,y-r), new PointF(x+r/2,y-r),
				 new PointF(x+r,y), new PointF(x+r/2,y+r), new PointF(x-r/2,y+r)]);
			e.Graphics.DrawLine(cyan, x + r, y, x + 45 * dpi, y);
		}
		e.Graphics.DrawLines(gold, [new PointF(0, Height-2*dpi), new PointF(96*dpi, Height-2*dpi), new PointF(112*dpi, Height-10*dpi)]);
		e.Graphics.DrawLine(cyan, 118*dpi, Height-10*dpi, Width, Height-10*dpi);
	}
}
