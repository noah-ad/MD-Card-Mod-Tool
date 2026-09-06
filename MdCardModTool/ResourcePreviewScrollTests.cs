using System;
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
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            using MainForm form = new(f =>
            {
                var field = typeof(Control).GetField("_deviceDpi", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (var c in Walk(f).Prepend(f)) field.SetValue(c, dpi);
            });
            var scroll = Walk(form).OfType<DarkScrollPanel>().Single(c => c.Name == "ResourcePreviewScroll");
            var layout = (TableLayoutPanel)scroll.ContentPanel.Controls[0];
            var preview = Walk(layout).OfType<PictureBox>().Single();
            using Bitmap art = new(280,400);
            using (var g = Graphics.FromImage(art))
            {
                g.Clear(Color.DarkSlateBlue);
                g.FillEllipse(Brushes.Gold,40,100,200,200);
            }
            preview.Image = art;
            foreach(var label in preview.Parent!.Controls.OfType<Label>()) label.Visible = false;
            var bar = Walk(scroll).OfType<DarkVerticalScrollBar>().Single();
            // Isolate the real right-side tree, avoiding game scans and desktop changes.
            using Form host = new() { ShowInTaskbar = false, Opacity = 0, AutoScaleMode = AutoScaleMode.None };
            scroll.Parent!.Controls.Remove(scroll);
            host.Controls.Add(scroll);
            host.ClientSize = new Size(480, 480);
            host.Show();
            foreach (int height in new[] { 320, 480, 1100, 360 })
            {
                host.ClientSize = new Size(480, height);
                host.PerformLayout(); scroll.PerformLayout(); layout.PerformLayout(); Application.DoEvents();
                if (preview.Height < 260 * dpi / 96f || preview.SizeMode != PictureBoxSizeMode.Zoom)
                    throw new Exception($"Collapsed or stretched preview: dpi={dpi}, viewport={height}, image={preview.Size}");
                bar.Value = 0;
                if (scroll.ContentHeight > height)
                {
                    if (!bar.Visible || bar.Maximum <= 0) throw new Exception("Missing overflow scrollbar");
                    typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(preview, new object[] { new MouseEventArgs(MouseButtons.None,0,10,10,-120) });
                    if (bar.Value <= 0) throw new Exception("Wheel on image did not scroll");
                    bar.Value = bar.Maximum;
                    if (scroll.ContentPanel.Bottom > scroll.Height + 1) throw new Exception("Cannot reach bottom actions");
                }
                else if (bar.Visible || bar.Value != 0) throw new Exception("Scrollbar failed to reset after growing");
                bar.Value = 0;
                using Bitmap bitmap = new(scroll.Width,scroll.Height);
                scroll.DrawToBitmap(bitmap,scroll.ClientRectangle);
                bitmap.Save(Path.Combine(output,$"preview-{dpi}-{height}.png"));
                Console.WriteLine($"injectedDpi={dpi}; viewport={height}; preview={preview.Width}x{preview.Height}; content={scroll.ContentHeight}; scrollMax={bar.Maximum}; ready=True");
            }
        }
    }
    static System.Collections.Generic.IEnumerable<Control> Walk(Control root)
    {
        foreach(Control child in root.Controls) { yield return child; foreach(var c in Walk(child)) yield return c; }
    }
}
