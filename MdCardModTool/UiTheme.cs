using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdCardModTool;

public static class UiTheme
{
	public static readonly Color Window = Color.FromArgb(7, 12, 23);

	public static readonly Color Surface = Color.FromArgb(14, 23, 38);

	public static readonly Color SurfaceAlt = Color.FromArgb(18, 30, 49);

	public static readonly Color Elevated = Color.FromArgb(23, 39, 63);

	public static readonly Color Border = Color.FromArgb(43, 64, 93);

	public static readonly Color Primary = Color.FromArgb(82, 209, 244);

	public static readonly Color PrimaryDark = Color.FromArgb(29, 122, 179);

	public static readonly Color Gold = Color.FromArgb(239, 194, 104);

	public static readonly Color Text = Color.FromArgb(239, 245, 252);

	public static readonly Color Muted = Color.FromArgb(148, 166, 191);

	public static readonly Color Selection = Color.FromArgb(31, 91, 133);

	public static readonly Color Danger = Color.FromArgb(193, 78, 97);

	public static void ApplyDarkTitleBar(Form form)
	{
		form.HandleCreated += delegate
		{
			if (OperatingSystem.IsWindows())
			{
				int value = 1;
				if (DwmSetWindowAttribute(form.Handle, 20, ref value, 4) != 0)
				{
					DwmSetWindowAttribute(form.Handle, 19, ref value, 4);
				}
			}
		};
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

	public static int Scale(Control control, int logicalPixels)
	{
		return Math.Max(1, (int)Math.Round((double)(logicalPixels * Math.Max(96, control.DeviceDpi)) / 96.0));
	}

	public static void QueueStableRepaint(Control root)
	{
		if (root.IsDisposed || !root.IsHandleCreated)
		{
			return;
		}
		try
		{
			root.BeginInvoke((MethodInvoker)delegate
			{
				if (!root.IsDisposed && root.IsHandleCreated)
				{
					root.PerformLayout();
					root.Invalidate(invalidateChildren: true);
				}
			});
		}
		catch (InvalidOperationException)
		{
		}
	}

	public static void StyleTextBox(TextBox box)
	{
		box.BackColor = SurfaceAlt;
		box.ForeColor = Text;
		box.BorderStyle = BorderStyle.None;
		box.Font = new Font("Microsoft YaHei UI", 9.5f);
		box.Margin = new Padding(6);
	}

	public static void StyleComboBox(ComboBox box)
	{
		box.BackColor = SurfaceAlt;
		box.ForeColor = Text;
		box.FlatStyle = FlatStyle.Flat;
		box.DrawMode = DrawMode.OwnerDrawFixed;
		box.ItemHeight = 24;
		box.IntegralHeight = false;
		box.DropDownHeight = 216;
		box.Font = new Font("Microsoft YaHei UI", 9f);
		box.DrawItem += delegate(object? _, DrawItemEventArgs e)
		{
			if (e.Index < 0)
			{
				return;
			}
			bool flag = (e.State & DrawItemState.Selected) != 0;
			using SolidBrush brush = new SolidBrush(flag ? Selection : SurfaceAlt);
			using SolidBrush solidBrush = new SolidBrush(flag ? Color.White : Text);
			e.Graphics.FillRectangle(brush, e.Bounds);
			TextRenderer.DrawText(e.Graphics, box.Items[e.Index]?.ToString() ?? "", box.Font, new Rectangle(e.Bounds.X + 9, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 38), e.Bounds.Height), solidBrush.Color, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		};
	}

	public static void StyleTree(TreeView tree)
	{
		tree.BackColor = Surface;
		tree.ForeColor = Text;
		tree.BorderStyle = BorderStyle.None;
		tree.LineColor = Border;
		tree.FullRowSelect = true;
		tree.ShowLines = false;
		tree.ShowPlusMinus = true;
		tree.ItemHeight = 28;
		tree.Indent = 18;
		tree.Font = new Font("Microsoft YaHei UI", 9f);
		tree.DrawMode = TreeViewDrawMode.Normal;
	}

	public static void StyleList(ListView list)
	{
		list.BackColor = Surface;
		list.ForeColor = Text;
		list.BorderStyle = BorderStyle.None;
		list.GridLines = false;
		list.OwnerDraw = true;
		list.Font = new Font("Microsoft YaHei UI", 9f);
		list.SmallImageList = new ImageList
		{
			ImageSize = new Size(1, 30),
			ColorDepth = ColorDepth.Depth32Bit
		};
		list.DrawColumnHeader += delegate(object? _, DrawListViewColumnHeaderEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(Elevated);
			using Pen pen = new Pen(Border);
			using SolidBrush brush2 = new SolidBrush(Muted);
			e.Graphics.FillRectangle(brush, e.Bounds);
			e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
			using Font font = new Font(list.Font, FontStyle.Bold);
			e.Graphics.DrawString(e.Header?.Text ?? "", font, brush2, e.Bounds.X + 10, e.Bounds.Y + 8);
		};
		list.DrawItem += delegate(object? _, DrawListViewItemEventArgs e)
		{
			if (list.View != View.Details)
			{
				e.DrawDefault = true;
			}
		};
		list.DrawSubItem += delegate(object? _, DrawListViewSubItemEventArgs e)
		{
			bool flag = e.Item?.Selected ?? false;
			bool flag2 = e.Item?.Tag is TexRef texRef && texRef.IsModded;
			bool flag3 = e.ItemIndex % 2 == 1;
			using SolidBrush brush = new SolidBrush(flag ? Selection : (flag3 ? Color.FromArgb(16, 27, 44) : Surface));
			using SolidBrush solidBrush = new SolidBrush(flag ? Color.White : ((flag2 && e.ColumnIndex == 0) ? Gold : ((e.ColumnIndex == 0) ? Text : Muted)));
			e.Graphics.FillRectangle(brush, e.Bounds);
			string text = e.SubItem?.Text ?? "";
			TextRenderer.DrawText(e.Graphics, text, list.Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 6, e.Bounds.Width - 14, e.Bounds.Height - 8), solidBrush.Color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
		};
	}

	public static Button Button(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral)
	{
		Color color = tone switch
		{
			ButtonTone.Primary => PrimaryDark,
			ButtonTone.Gold => Color.FromArgb(132, 100, 42),
			ButtonTone.Danger => Color.FromArgb(102, 45, 59),
			_ => Elevated,
		};
		Color hoverColor = tone switch
		{
			ButtonTone.Primary => Color.FromArgb(38, 148, 203),
			ButtonTone.Gold => Color.FromArgb(160, 121, 49),
			ButtonTone.Danger => Danger,
			_ => Color.FromArgb(34, 53, 81),
		};
		Color borderColor = tone switch
		{
			ButtonTone.Primary => Primary,
			ButtonTone.Gold => Gold,
			ButtonTone.Danger => Color.FromArgb(226, 112, 128),
			_ => Border,
		};
		RoundedButton obj = new RoundedButton
		{
			Text = text,
			AutoSize = true,
			Height = 34,
			MinimumSize = new Size(0, 34),
			Padding = new Padding(12, 0, 12, 0),
			Margin = new Padding(5, 4, 0, 4),
			NormalColor = color,
			HoverColor = hoverColor,
			BackColor = color,
			ForeColor = Text,
			Cursor = Cursors.Hand
		};
		bool flag = (uint)(tone - 1) <= 1u;
		obj.Font = new Font("Microsoft YaHei UI", 9f, flag ? FontStyle.Bold : FontStyle.Regular);
		obj.BorderColor = borderColor;
		obj.Click += click;
		return obj;
	}

	public static RoundedField Field(Control control)
	{
		return new RoundedField(control)
		{
			Dock = DockStyle.Fill
		};
	}

	public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
	{
		int num = Math.Max(1, Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2));
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(bounds.Left, bounds.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(bounds.Right - num, bounds.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(bounds.Left, bounds.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
