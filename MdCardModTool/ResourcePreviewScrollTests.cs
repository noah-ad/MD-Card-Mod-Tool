using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class ResourcePreviewScrollTests
{
	public static void Run(string output)
	{
		Directory.CreateDirectory(output);
		int[] array = new int[3] { 96, 144, 192 };
		foreach (int dpi in array)
		{
			using MainForm root = new MainForm(delegate(MainForm f)
			{
				FieldInfo field = typeof(Control).GetField("_deviceDpi", BindingFlags.Instance | BindingFlags.NonPublic);
				foreach (Control item in Walk(f).Prepend(f))
				{
					field.SetValue(item, dpi);
				}
			});
			DarkScrollPanel darkScrollPanel = Walk(root).OfType<DarkScrollPanel>().Single((DarkScrollPanel c) => c.Name == "ResourcePreviewScroll");
			TableLayoutPanel tableLayoutPanel = (TableLayoutPanel)darkScrollPanel.ContentPanel.Controls[0];
			PictureBox pictureBox = Walk(tableLayoutPanel).OfType<PictureBox>().Single();
			using Bitmap image = new Bitmap(280, 400);
			using (Graphics graphics = Graphics.FromImage(image))
			{
				graphics.Clear(Color.DarkSlateBlue);
				graphics.FillEllipse(Brushes.Gold, 40, 100, 200, 200);
			}
			pictureBox.Image = image;
			foreach (Label item2 in pictureBox.Parent.Controls.OfType<Label>())
			{
				item2.Visible = false;
			}
			DarkVerticalScrollBar darkVerticalScrollBar = Walk(darkScrollPanel).OfType<DarkVerticalScrollBar>().Single();
			using Form form = new Form
			{
				ShowInTaskbar = false,
				Opacity = 0.0,
				AutoScaleMode = AutoScaleMode.None
			};
			darkScrollPanel.Parent.Controls.Remove(darkScrollPanel);
			form.Controls.Add(darkScrollPanel);
			form.ClientSize = new Size(480, 480);
			form.Show();
			int[] array2 = new int[4] { 320, 480, 1100, 360 };
			foreach (int num2 in array2)
			{
				form.ClientSize = new Size(480, num2);
				form.PerformLayout();
				darkScrollPanel.PerformLayout();
				tableLayoutPanel.PerformLayout();
				Application.DoEvents();
				if ((float)pictureBox.Height < (float)(260 * dpi) / 96f || pictureBox.SizeMode != PictureBoxSizeMode.Zoom)
				{
					throw new Exception($"Collapsed or stretched preview: dpi={dpi}, viewport={num2}, image={pictureBox.Size}");
				}
				darkVerticalScrollBar.Value = 0;
				if (darkScrollPanel.ContentHeight > num2)
				{
					if (!darkVerticalScrollBar.Visible || darkVerticalScrollBar.Maximum <= 0)
					{
						throw new Exception("Missing overflow scrollbar");
					}
					typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pictureBox, new object[1]
					{
						new MouseEventArgs(MouseButtons.None, 0, 10, 10, -120)
					});
					if (darkVerticalScrollBar.Value <= 0)
					{
						throw new Exception("Wheel on image did not scroll");
					}
					darkVerticalScrollBar.Value = darkVerticalScrollBar.Maximum;
					if (darkScrollPanel.ContentPanel.Bottom > darkScrollPanel.Height + 1)
					{
						throw new Exception("Cannot reach bottom actions");
					}
				}
				else if (darkVerticalScrollBar.Visible || darkVerticalScrollBar.Value != 0)
				{
					throw new Exception("Scrollbar failed to reset after growing");
				}
				darkVerticalScrollBar.Value = 0;
				using Bitmap bitmap = new Bitmap(darkScrollPanel.Width, darkScrollPanel.Height);
				darkScrollPanel.DrawToBitmap(bitmap, darkScrollPanel.ClientRectangle);
				bitmap.Save(Path.Combine(output, $"preview-{dpi}-{num2}.png"));
				Console.WriteLine($"injectedDpi={dpi}; viewport={num2}; preview={pictureBox.Width}x{pictureBox.Height}; content={darkScrollPanel.ContentHeight}; scrollMax={darkVerticalScrollBar.Maximum}; ready=True");
			}
		}
	}

	private static IEnumerable<Control> Walk(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control item in Walk(child))
			{
				yield return item;
			}
		}
	}
}
