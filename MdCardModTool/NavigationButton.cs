using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class NavigationButton : RoundedButton
{
	public required WorkspacePage Page { get; init; }

	private bool _selected;

	public bool Selected
	{
		get => _selected;
		set
		{
			if (_selected == value)
			{
				return;
			}
			_selected = value;
			ApplyPalette();
			Invalidate();
		}
	}

	public NavigationButton()
	{
		TextAlign = ContentAlignment.MiddleLeft;
		Padding = new Padding(43, 0, 12, 0);
		CornerRadius = 8;
		Height = 44;
		MinimumSize = new Size(0, 44);
		Margin = new Padding(8, 3, 8, 3);
		ApplyPalette();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float scale = Math.Max(1f, DeviceDpi / 96f);
		if (Selected)
		{
			using SolidBrush accent = new(UiTheme.Primary);
			e.Graphics.FillRoundedRectangle(accent, new RectangleF(5 * scale, 11 * scale, 3 * scale, Math.Max(3 * scale, Height - 22 * scale)), 1.5f * scale);
			using Pen detail = new(UiTheme.Gold, scale);
			e.Graphics.DrawLines(detail, [new PointF(Width-22*scale, Height-8*scale),
				new PointF(Width-10*scale, Height-8*scale), new PointF(Width-6*scale, Height-12*scale)]);
		}
		using Pen icon = new(Selected ? UiTheme.Gold : UiTheme.Muted, 1.7f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		GraphicsState state = e.Graphics.Save();
		e.Graphics.ScaleTransform(scale, scale);
		DrawGlyph(e.Graphics, icon, new Rectangle(17, 12, 20, 20));
		e.Graphics.Restore(state);
	}

	private void ApplyPalette()
	{
		NormalColor = Selected ? Color.FromArgb(27, 62, 88) : UiTheme.Surface;
		HoverColor = Selected ? Color.FromArgb(31, 72, 101) : UiTheme.SurfaceAlt;
		BorderColor = Selected ? Color.FromArgb(120, UiTheme.Primary) : Color.Transparent;
		ForeColor = Selected ? Color.White : UiTheme.Muted;
		BackColor = NormalColor;
	}

	private void DrawGlyph(Graphics graphics, Pen pen, Rectangle r)
	{
		switch (Page)
		{
			case WorkspacePage.Cards:
				graphics.DrawRoundedRectangle(pen, new RectangleF(r.X + 3, r.Y + 1, 14, 18), 2);
				graphics.DrawLine(pen, r.X + 6, r.Y + 14, r.X + 10, r.Y + 9);
				graphics.DrawLine(pen, r.X + 10, r.Y + 9, r.X + 15, r.Y + 14);
				break;
			case WorkspacePage.Visuals:
				graphics.DrawEllipse(pen, r.X + 1, r.Y + 1, 18, 18);
				graphics.DrawEllipse(pen, r.X + 5, r.Y + 6, 3, 3);
				graphics.DrawArc(pen, r.X + 5, r.Y + 5, 11, 11, 20, 135);
				break;
			case WorkspacePage.Animation:
				graphics.DrawPolygon(pen, new Point[] { new(r.X + 5, r.Y + 2), new(r.X + 17, r.Y + 10), new(r.X + 5, r.Y + 18) });
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
				graphics.DrawEllipse(pen, r.X + 3, r.Y + 3, 14, 14);
				graphics.DrawEllipse(pen, r.X + 8, r.Y + 8, 4, 4);
				for (int i = 0; i < 8; i++)
				{
					double angle = i * Math.PI / 4;
					graphics.DrawLine(pen, r.X + 10 + (float)Math.Cos(angle) * 7, r.Y + 10 + (float)Math.Sin(angle) * 7,
						r.X + 10 + (float)Math.Cos(angle) * 9, r.Y + 10 + (float)Math.Sin(angle) * 9);
				}
				break;
		}
	}
}
