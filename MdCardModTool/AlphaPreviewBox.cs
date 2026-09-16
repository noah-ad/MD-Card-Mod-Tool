using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class AlphaPreviewBox : PictureBox
{
	public AlphaPreviewBox()
	{
		SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Rectangle clientRectangle = base.ClientRectangle;
		using SolidBrush brush = new SolidBrush(Color.FromArgb(12, 20, 33));
		e.Graphics.FillRectangle(brush, base.ClientRectangle);
		if (clientRectangle.Width <= 0 || clientRectangle.Height <= 0)
		{
			return;
		}
		int num = Math.Max(8, UiTheme.Scale(this, 12));
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(25, 39, 59));
		for (int i = 0; i < clientRectangle.Height; i += num)
		{
			for (int j = 0; j < clientRectangle.Width; j += num)
			{
				if ((j / num + i / num) % 2 == 0)
				{
					e.Graphics.FillRectangle(brush2, j, i, Math.Min(num, clientRectangle.Width - j), Math.Min(num, clientRectangle.Height - i));
				}
			}
		}
	}
}
