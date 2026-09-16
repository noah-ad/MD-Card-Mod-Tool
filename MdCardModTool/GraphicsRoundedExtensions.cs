using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MdCardModTool;

internal static class GraphicsRoundedExtensions
{
	public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
	{
		using GraphicsPath path = Rounded(bounds, radius);
		graphics.FillPath(brush, path);
	}

	public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
	{
		using GraphicsPath path = Rounded(bounds, radius);
		graphics.DrawPath(pen, path);
	}

	private static GraphicsPath Rounded(RectangleF bounds, float radius)
	{
		float num = Math.Max(1f, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2f));
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(bounds.Left, bounds.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
