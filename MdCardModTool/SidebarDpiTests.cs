using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class SidebarDpiTests
{
	public static void Run(string output)
	{
		Directory.CreateDirectory(output);
		FieldInfo dpiField = typeof(Control).GetField("_deviceDpi", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("WinForms DPI test seam changed");
		int[] array = new int[3] { 96, 144, 192 };
		foreach (int dpi in array)
		{
			using MainForm control = new MainForm(delegate(MainForm form)
			{
				foreach (Control item in Walk(form).Prepend(form))
				{
					dpiField.SetValue(item, dpi);
				}
			});
			NavigationButton navigationButton = Walk(control).OfType<NavigationButton>().First();
			Control parent = navigationButton.Parent.Parent;
			float num = (float)dpi / 96f;
			if (Math.Abs((float)parent.Width - (228f * num - (float)parent.Margin.Horizontal)) > 3f || (float)navigationButton.Height < 42f * num || (float)navigationButton.Padding.Left < 42f * num || (float)navigationButton.Padding.Left <= 37f * num)
			{
				throw new InvalidDataException($"Initial {dpi} DPI: unscaled layout or icon overlaps text ({parent.Width}/{navigationButton.Height}/{navigationButton.Padding.Left})");
			}
			Console.WriteLine($"initialInjectedDpi={dpi}; sidebar={parent.Width}; buttonHeight={navigationButton.Height}; textInset={navigationButton.Padding.Left}; ready=True");
		}
		float[] array2 = new float[3] { 1f, 1.5f, 2f };
		foreach (float num2 in array2)
		{
			using MainForm mainForm = new MainForm
			{
				ShowInTaskbar = false,
				Opacity = 0.0
			};
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(mainForm, null);
			mainForm.Show();
			Application.DoEvents();
			NavigationButton navigationButton2 = Walk(mainForm).OfType<NavigationButton>().First();
			TableLayoutPanel tableLayoutPanel = (TableLayoutPanel)((TableLayoutPanel)navigationButton2.Parent).Parent;
			float num3 = (float)mainForm.DeviceDpi / 96f;
			if (Math.Abs((float)tableLayoutPanel.Width - (228f * num3 - (float)tableLayoutPanel.Margin.Horizontal)) > 3f)
			{
				throw new InvalidDataException("Initial sidebar missed system DPI scaling");
			}
			mainForm.AutoScaleDimensions = new SizeF((float)mainForm.DeviceDpi / num2, (float)mainForm.DeviceDpi / num2);
			mainForm.PerformAutoScale();
			mainForm.PerformLayout();
			Application.DoEvents();
			NavigationButton[] array3 = Walk(tableLayoutPanel).OfType<NavigationButton>().ToArray();
			foreach (NavigationButton navigationButton3 in array3)
			{
				if (!navigationButton3.Parent.ClientRectangle.Contains(navigationButton3.Bounds) || (float)navigationButton3.Height < 42f * num2 * num3 || (float)navigationButton3.Padding.Left < 42f * num2 * num3)
				{
					throw new InvalidDataException("Navigation geometry clipped after DPI scaling");
				}
			}
			using Bitmap bitmap = new Bitmap(tableLayoutPanel.Width, tableLayoutPanel.Height);
			bitmap.SetResolution(mainForm.DeviceDpi, mainForm.DeviceDpi);
			tableLayoutPanel.DrawToBitmap(bitmap, tableLayoutPanel.ClientRectangle);
			bitmap.Save(Path.Combine(output, $"sidebar-{num2 * 100f:0}.png"));
			Console.WriteLine($"layoutScale={num2 * 100f:0}%; systemDpi={mainForm.DeviceDpi}; sidebar={tableLayoutPanel.Width}; buttonHeight={navigationButton2.Height}; textInset={navigationButton2.Padding.Left}; ready=True");
			mainForm.Close();
		}
	}

	private static IEnumerable<Control> Walk(Control control)
	{
		foreach (Control child in control.Controls)
		{
			yield return child;
			foreach (Control item in Walk(child))
			{
				yield return item;
			}
		}
	}
}
