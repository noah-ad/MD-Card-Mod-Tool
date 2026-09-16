using System;
using System.Runtime.InteropServices;

namespace MdCardModTool;

public static class MotionPreferences
{
	private const uint SpiGetClientAreaAnimation = 4162u;

	public static bool UserReducesMotion { get; set; }

	public static bool ReduceMotion
	{
		get
		{
			if (!UserReducesMotion)
			{
				return WindowsReducesMotion;
			}
			return true;
		}
	}

	public static bool WindowsReducesMotion
	{
		get
		{
			if (!OperatingSystem.IsWindows())
			{
				return false;
			}
			bool value = true;
			if (SystemParametersInfo(4162u, 0u, ref value, 0u))
			{
				return !value;
			}
			return false;
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SystemParametersInfo(uint action, uint parameter, ref bool value, uint flags);
}
