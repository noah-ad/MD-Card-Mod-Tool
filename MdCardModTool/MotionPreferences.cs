using System;
using System.Runtime.InteropServices;

namespace MdCardModTool;

public static class MotionPreferences
{
	private const uint SpiGetClientAreaAnimation = 0x1042;

	public static bool UserReducesMotion { get; set; }

	public static bool ReduceMotion => UserReducesMotion || WindowsReducesMotion;

	public static bool WindowsReducesMotion
	{
		get
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			bool enabled = true;
			return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) && !enabled;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SystemParametersInfo(uint action, uint parameter, ref bool value, uint flags);
}
