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
		using LinearGradientBrush gradient = new LinearGradientBrush(base.ClientRectangle, Color.FromArgb(15, 40, 70), UiTheme.Window, 0f);
		e.Graphics.FillRectangle(gradient, base.ClientRectangle);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using Pen cyan = new Pen(Color.FromArgb(38, UiTheme.Primary), 1f);
		using Pen gold = new Pen(Color.FromArgb(30, UiTheme.Gold), 1f);
		for (int x = base.Width - 440; x < base.Width + 80; x += 52)
		{
			e.Graphics.DrawLine(cyan, x, 0, x - 92, base.Height);
			e.Graphics.DrawEllipse(gold, x - 35, 12, 52, 52);
		}
	}
}
