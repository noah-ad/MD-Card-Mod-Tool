using System;
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
        // Unlike PerformAutoScale on an already-open 96-DPI form, this exercises
        // the constructor's None -> Dpi transition with an initial 144/192 DPI.
        // Inject only WinForms' cached DPI; do not alter the user's display settings.
        var dpiField = typeof(Control).GetField("_deviceDpi", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("WinForms DPI test seam changed");
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using MainForm initial = new(form =>
            {
                foreach (var control in Walk(form).Prepend(form)) dpiField.SetValue(control, dpi);
            });
            var nav = Walk(initial).OfType<NavigationButton>().First();
            var side = nav.Parent!.Parent!;
            float scale = dpi / 96f;
            if (Math.Abs(side.Width - (228 * scale - side.Margin.Horizontal)) > 3
                || nav.Height < 42 * scale || nav.Padding.Left < 42 * scale
                || nav.Padding.Left <= 37 * scale)
                throw new InvalidDataException($"Initial {dpi} DPI: unscaled layout or icon overlaps text ({side.Width}/{nav.Height}/{nav.Padding.Left})");
            Console.WriteLine($"initialInjectedDpi={dpi}; sidebar={side.Width}; buttonHeight={nav.Height}; textInset={nav.Padding.Left}; ready=True");
        }
        foreach (float factor in new[] { 1f, 1.5f, 2f })
        {
            using MainForm form = new() { ShowInTaskbar = false, Opacity = 0 };
            typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, null);
            form.Show();
            Application.DoEvents();
            var button = Walk(form).OfType<NavigationButton>().First();
            var navigation = (TableLayoutPanel)button.Parent!;
            var sidebar = (TableLayoutPanel)navigation.Parent!;
            float actualFactor = form.DeviceDpi / 96f;
            if (Math.Abs(sidebar.Width - (228 * actualFactor - sidebar.Margin.Horizontal)) > 3)
                throw new InvalidDataException("Initial sidebar missed system DPI scaling");
            // Exercise WinForms' real layout scaling path without changing the
            // user's Windows settings. This is a layout simulation, not WM_DPICHANGED.
            form.AutoScaleDimensions = new SizeF(form.DeviceDpi / factor, form.DeviceDpi / factor);
            form.PerformAutoScale();
            form.PerformLayout();
            Application.DoEvents();
            var buttons = Walk(sidebar).OfType<NavigationButton>().ToArray();
            foreach (var b in buttons)
            {
                if (!b.Parent!.ClientRectangle.Contains(b.Bounds)
                    || b.Height < 42 * factor * actualFactor
                    || b.Padding.Left < 42 * factor * actualFactor)
                    throw new InvalidDataException("Navigation geometry clipped after DPI scaling");
            }
            using Bitmap picture = new(sidebar.Width, sidebar.Height);
            picture.SetResolution(form.DeviceDpi, form.DeviceDpi);
            sidebar.DrawToBitmap(picture, sidebar.ClientRectangle);
            picture.Save(Path.Combine(output, $"sidebar-{factor * 100:0}.png"));
            Console.WriteLine($"layoutScale={factor * 100:0}%; systemDpi={form.DeviceDpi}; sidebar={sidebar.Width}; buttonHeight={button.Height}; textInset={button.Padding.Left}; ready=True");
            form.Close();
        }
    }
    private static System.Collections.Generic.IEnumerable<Control> Walk(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var nested in Walk(child)) yield return nested;
        }
    }
}
