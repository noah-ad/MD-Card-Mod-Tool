using System;
using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public static class CardFrameCatalog
{
	private static readonly IReadOnlyDictionary<string, string> FriendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["card_frame00"] = "通常怪兽",
		["card_frame01"] = "效果怪兽",
		["card_frame02"] = "仪式怪兽",
		["card_frame03"] = "融合怪兽",
		["card_frame07"] = "魔法卡",
		["card_frame08"] = "陷阱卡",
		["card_frame09"] = "衍生物／灰色",
		["card_frame10"] = "同调怪兽",
		["card_frame12"] = "超量怪兽",
		["card_frame13"] = "灵摆通常怪兽",
		["card_frame14"] = "灵摆效果怪兽",
		["card_frame15"] = "灵摆超量怪兽",
		["card_frame16"] = "灵摆同调怪兽",
		["card_frame17"] = "灵摆融合怪兽",
		["card_frame18"] = "连接怪兽",
		["card_frame19"] = "灵摆仪式怪兽"
	};

	private static readonly HashSet<string> PendulumFrames = new HashSet<string> { "card_frame13", "card_frame14", "card_frame15", "card_frame16", "card_frame17", "card_frame19" };

	public static string FriendlyName(string key)
	{
		string key2 = BaseKey(key);
		if (!FriendlyNames.TryGetValue(key2, out string value))
		{
			return key;
		}
		return value;
	}

	public static string BaseKey(string key)
	{
		if (key.StartsWith("transparent_gradient_", StringComparison.OrdinalIgnoreCase))
		{
			string text = key;
			int length = "transparent_gradient_".Length;
			return text.Substring(length, text.Length - length);
		}
		if (key.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase))
		{
			string text = key;
			int length = "transparent_".Length;
			return text.Substring(length, text.Length - length);
		}
		if (key.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase))
		{
			string text = key;
			int length = "gradient_".Length;
			return text.Substring(length, text.Length - length);
		}
		return key;
	}

	public static bool IsPendulum(string key)
	{
		return PendulumFrames.Contains(BaseKey(key));
	}

	public static string DefaultKey(int storedWidth, int storedHeight)
	{
		return "card_frame01";
	}

	public static string RecommendedKey(CardCatalogEntry? card, int storedWidth, int storedHeight)
	{
		if (card == null)
		{
			return DefaultKey(storedWidth, storedHeight);
		}
		string value = card.Type.Trim();
		string value2 = card.SubType.Trim();
		if (ContainsAny(value, "魔法", "spell", "magic"))
		{
			return "card_frame07";
		}
		if (ContainsAny(value, "陷阱", "trap"))
		{
			return "card_frame08";
		}
		if (ContainsAny(value2, "衍生物", "token") || ContainsAny(value, "衍生物", "token"))
		{
			return "card_frame09";
		}
		bool flag = ContainsAny(value2, "靈擺", "灵摆", "pendulum");
		if (ContainsAny(value2, "連結", "连接", "链接", "link"))
		{
			return "card_frame18";
		}
		if (ContainsAny(value2, "超量", "xyz"))
		{
			if (!flag)
			{
				return "card_frame12";
			}
			return "card_frame15";
		}
		if (ContainsAny(value2, "同步", "同調", "同调", "synchro"))
		{
			if (!flag)
			{
				return "card_frame10";
			}
			return "card_frame16";
		}
		if (ContainsAny(value2, "融合", "fusion"))
		{
			if (!flag)
			{
				return "card_frame03";
			}
			return "card_frame17";
		}
		if (ContainsAny(value2, "儀式", "仪式", "ritual"))
		{
			if (!flag)
			{
				return "card_frame02";
			}
			return "card_frame19";
		}
		if (ContainsAny(value2, "通常", "normal"))
		{
			if (!flag)
			{
				return "card_frame00";
			}
			return "card_frame13";
		}
		if (!flag)
		{
			return "card_frame01";
		}
		return "card_frame14";
	}

	public static IEnumerable<TexRef> CompatibleFrames(IEnumerable<TexRef> frames, int storedWidth, int storedHeight)
	{
		return frames.Where((TexRef x) => BuiltInCardFrameCatalog.IsNormalFrame(x) && x.Width == 704 && x.Height == 1024).OrderBy<TexRef, string>((TexRef x) => x.Name, StringComparer.OrdinalIgnoreCase);
	}

	private static bool ContainsAny(string value, params string[] needles)
	{
		return needles.Any((string needle) => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
	}
}
