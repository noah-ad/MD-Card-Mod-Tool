using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class NavigationButton : RoundedButton
{
	private bool _selected;

	public required WorkspacePage Page { get; init; }

	public bool Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			if (_selected != value)
			{
				_selected = value;
				ApplyPalette();
				Invalidate();
			}
		}
	}

	public NavigationButton()
	{
		TextAlign = ContentAlignment.MiddleLeft;
		base.Padding = new Padding(43, 0, 12, 0);
		base.CornerRadius = 8;
		base.Height = 44;
		MinimumSize = new Size(0, 44);
		base.Margin = new Padding(8, 3, 8, 3);
		ApplyPalette();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num = Math.Max(1f, (float)base.DeviceDpi / 96f);
		if (Selected)
		{
			using SolidBrush brush = new SolidBrush(UiTheme.Primary);
			e.Graphics.FillRoundedRectangle(brush, new RectangleF(5f * num, 11f * num, 3f * num, Math.Max(3f * num, (float)base.Height - 22f * num)), 1.5f * num);
			using Pen pen = new Pen(UiTheme.Gold, num);
			e.Graphics.DrawLines(pen, new PointF[3]
			{
				new PointF((float)base.Width - 22f * num, (float)base.Height - 8f * num),
				new PointF((float)base.Width - 10f * num, (float)base.Height - 8f * num),
				new PointF((float)base.Width - 6f * num, (float)base.Height - 12f * num)
			});
		}
		using Pen pen2 = new Pen(Selected ? UiTheme.Gold : UiTheme.Muted, 1.7f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		GraphicsState gstate = e.Graphics.Save();
		e.Graphics.ScaleTransform(num, num);
		DrawGlyph(e.Graphics, pen2, new Rectangle(17, (int)Math.Round((float)base.Height / num / 2f - 10f), 20, 20));
		e.Graphics.Restore(gstate);
	}

	private void ApplyPalette()
	{
		base.NormalColor = (Selected ? Color.FromArgb(27, 62, 88) : UiTheme.Surface);
		base.HoverColor = (Selected ? Color.FromArgb(31, 72, 101) : UiTheme.SurfaceAlt);
		base.BorderColor = (Selected ? Color.FromArgb(120, UiTheme.Primary) : Color.Transparent);
		ForeColor = (Selected ? Color.White : UiTheme.Muted);
		BackColor = base.NormalColor;
	}

	private void DrawGlyph(Graphics graphics, Pen pen, Rectangle r)
	{
		switch (Page)
		{
		case WorkspacePage.Cards:
			graphics.DrawRoundedRectangle(pen, new RectangleF(r.X + 3, r.Y + 1, 14f, 18f), 2f);
			graphics.DrawLine(pen, r.X + 6, r.Y + 14, r.X + 10, r.Y + 9);
			graphics.DrawLine(pen, r.X + 10, r.Y + 9, r.X + 15, r.Y + 14);
			break;
		case WorkspacePage.Visuals:
			graphics.DrawEllipse(pen, r.X + 1, r.Y + 1, 18, 18);
			graphics.DrawEllipse(pen, r.X + 5, r.Y + 6, 3, 3);
			graphics.DrawArc(pen, r.X + 5, r.Y + 5, 11, 11, 20, 135);
			break;
		case WorkspacePage.Animation:
			graphics.DrawPolygon(pen, new Point[3]
			{
				new Point(r.X + 5, r.Y + 2),
				new Point(r.X + 17, r.Y + 10),
				new Point(r.X + 5, r.Y + 18)
			});
			break;
		case WorkspacePage.Frames:
			graphics.DrawRectangle(pen, r.X + 2, r.Y + 2, 16, 16);
			graphics.DrawRectangle(pen, r.X + 6, r.Y + 6, 8, 8);
			break;
		case WorkspacePage.Mods:
			graphics.DrawRectangle(pen, r.X + 2, r.Y + 3, 16, 14);
			graphics.DrawLine(pen, r.X + 6, r.Y + 3, r.X + 6, r.Y + 17);
			graphics.DrawLine(pen, r.X + 2, r.Y + 8, r.X + 18, r.Y + 8);
			break;
		case WorkspacePage.Settings:
		{
			graphics.DrawEllipse(pen, r.X + 3, r.Y + 3, 14, 14);
			graphics.DrawEllipse(pen, r.X + 8, r.Y + 8, 4, 4);
			for (int i = 0; i < 8; i++)
			{
				double num = (double)i * Math.PI / 4.0;
				graphics.DrawLine(pen, (float)(r.X + 10) + (float)Math.Cos(num) * 7f, (float)(r.Y + 10) + (float)Math.Sin(num) * 7f, (float)(r.X + 10) + (float)Math.Cos(num) * 9f, (float)(r.Y + 10) + (float)Math.Sin(num) * 9f);
			}
			break;
		}
		}
	}
}
