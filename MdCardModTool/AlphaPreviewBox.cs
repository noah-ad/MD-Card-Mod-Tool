using System;
using System.Drawing;
using System.Windows.Forms;

namespace MdCardModTool;

/// <summary>
/// PictureBox with a dark checkerboard that makes real image transparency
/// visible without clashing with the Master Duel theme.
/// </summary>
public sealed class AlphaPreviewBox : PictureBox
{
	public AlphaPreviewBox()
	{
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
			| ControlStyles.ResizeRedraw, true);
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
		Rectangle bounds = ClientRectangle;
		using SolidBrush background = new(Color.FromArgb(12, 20, 33));
		e.Graphics.FillRectangle(background, ClientRectangle);
		if (bounds.Width <= 0 || bounds.Height <= 0) return;
		int cell = Math.Max(8, UiTheme.Scale(this, 12));
		using SolidBrush alternate = new(Color.FromArgb(25, 39, 59));
		for (int y = 0; y < bounds.Height; y += cell)
		for (int x = 0; x < bounds.Width; x += cell)
		{
			if ((x / cell + y / cell) % 2 == 0)
			{
				e.Graphics.FillRectangle(alternate, x, y,
					Math.Min(cell, bounds.Width - x), Math.Min(cell, bounds.Height - y));
			}
		}
	}
}
