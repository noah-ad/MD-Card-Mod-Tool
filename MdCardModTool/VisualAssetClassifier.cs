using System;

namespace MdCardModTool;

public static class VisualAssetClassifier
{
	private static readonly string[] WallpaperExclusions = new string[7] { "wallpapericon", "wallpaperthumb", "gui_wallpaperbg", "productthumbbgwallpaperprofile", "sactx-0-2048x1024-bc7", "wallpapersale", "wallpapertopicsthumb" };

	public static string? CategoryFor(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string text = name.ToLowerInvariant();
		if (text.StartsWith("shopbgbase", StringComparison.Ordinal))
		{
			return "大厅背景";
		}
		if (text.Contains("basecolor", StringComparison.Ordinal) && text.Contains("mat_", StringComparison.Ordinal))
		{
			return "决斗场地";
		}
		if (text.Contains("coin01tex", StringComparison.Ordinal) || text.Contains("cointossicon", StringComparison.Ordinal))
		{
			return "硬币";
		}
		if (text.Contains("deckcase", StringComparison.Ordinal))
		{
			return "卡盒";
		}
		if (text.Contains("profileframe", StringComparison.Ordinal))
		{
			return "头像框";
		}
		if (text.Contains("profileicon", StringComparison.Ordinal))
		{
			return "头像";
		}
		if (text.Contains("protectoricon", StringComparison.Ordinal))
		{
			return "卡套";
		}
		if (text.Contains("wallpaper", StringComparison.Ordinal))
		{
			string[] wallpaperExclusions = WallpaperExclusions;
			foreach (string value in wallpaperExclusions)
			{
				if (text.Contains(value, StringComparison.Ordinal))
				{
					return null;
				}
			}
			return "大厅壁纸";
		}
		return null;
	}
}
