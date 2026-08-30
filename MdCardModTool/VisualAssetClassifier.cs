using System;

namespace MdCardModTool;

public static class VisualAssetClassifier
{
	private static readonly string[] WallpaperExclusions = new string[7]
	{
		"wallpapericon",
		"wallpaperthumb",
		"gui_wallpaperbg",
		"productthumbbgwallpaperprofile",
		"sactx-0-2048x1024-bc7",
		"wallpapersale",
		"wallpapertopicsthumb"
	};

	public static string? CategoryFor(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string normalized = name.ToLowerInvariant();
		if (normalized.StartsWith("shopbgbase", StringComparison.Ordinal))
		{
			return "大厅背景";
		}
		if (normalized.Contains("basecolor", StringComparison.Ordinal) && normalized.Contains("mat_", StringComparison.Ordinal))
		{
			return "决斗场地";
		}
		if (normalized.Contains("coin01tex", StringComparison.Ordinal) || normalized.Contains("cointossicon", StringComparison.Ordinal))
		{
			return "硬币";
		}
		if (normalized.Contains("deckcase", StringComparison.Ordinal))
		{
			return "卡盒";
		}
		if (normalized.Contains("profileframe", StringComparison.Ordinal))
		{
			return "头像框";
		}
		if (normalized.Contains("profileicon", StringComparison.Ordinal))
		{
			return "头像";
		}
		if (normalized.Contains("protectoricon", StringComparison.Ordinal))
		{
			return "卡套";
		}
		if (normalized.Contains("wallpaper", StringComparison.Ordinal))
		{
			foreach (string excluded in WallpaperExclusions)
			{
				if (normalized.Contains(excluded, StringComparison.Ordinal))
				{
					return null;
				}
			}
			return "大厅壁纸";
		}
		return null;
	}
}
