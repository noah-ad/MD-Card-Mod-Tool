using System.Drawing;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class DpiLayout
{
    // Call while layout is suspended, after the full 96-DPI tree is assembled.
    internal static void Initialize(ContainerControl control)
    {
        control.AutoScaleMode = AutoScaleMode.Dpi;
        control.AutoScaleDimensions = new SizeF(96, 96);
    }
}
