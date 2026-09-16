using System.Drawing;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class DpiLayout
{
	internal static void Initialize(ContainerControl control)
	{
		control.AutoScaleMode = AutoScaleMode.Dpi;
		control.AutoScaleDimensions = new SizeF(96f, 96f);
	}
}
