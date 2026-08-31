using System;
using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public static class CardFrameCatalog
{
	public const int PendulumVisibleStorageHeight = 596;

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
		string baseKey = BaseKey(key);
		if (!FriendlyNames.TryGetValue(baseKey, out string value))
		{
			return key;
		}
		return value;
	}

	public static string BaseKey(string key)
	{
		if (key.StartsWith("transparent_gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return key["transparent_gradient_".Length..];
		}
		if (key.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase))
		{
			return key["transparent_".Length..];
		}
		if (key.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return key["gradient_".Length..];
		}
		return key;
	}

	public static bool IsPendulum(string key)
	{
		return PendulumFrames.Contains(BaseKey(key));
	}

	public static string DefaultKey(int storedWidth, int storedHeight)
	{
		// Texture dimensions describe storage layout, not card rules.  Inferring a
		// Pendulum frame from 512x1024 caused ordinary cards to acquire a phantom
		// Pendulum frame whenever catalog data was temporarily unavailable.
		return "card_frame01";
	}

	/// <summary>
	/// Resolves the game's frame from the card catalog instead of guessing from
	/// Texture2D dimensions.  A 704x1024 over-frame image has no useful size hint,
	/// and a stale preview selection must not turn a Link monster into a Pendulum
	/// Xyz card.
	/// </summary>
	public static string RecommendedKey(CardCatalogEntry? card, int storedWidth, int storedHeight)
	{
		if (card == null)
		{
			return DefaultKey(storedWidth, storedHeight);
		}

		string type = card.Type.Trim();
		string subType = card.SubType.Trim();
		if (ContainsAny(type, "魔法", "spell", "magic"))
		{
			return "card_frame07";
		}
		if (ContainsAny(type, "陷阱", "trap"))
		{
			return "card_frame08";
		}
		if (ContainsAny(subType, "衍生物", "token") || ContainsAny(type, "衍生物", "token"))
		{
			return "card_frame09";
		}

		bool pendulum = ContainsAny(subType, "靈擺", "灵摆", "pendulum");
		if (ContainsAny(subType, "連結", "连接", "链接", "link"))
		{
			return "card_frame18";
		}
		if (ContainsAny(subType, "超量", "xyz"))
		{
			return pendulum ? "card_frame15" : "card_frame12";
		}
		if (ContainsAny(subType, "同步", "同調", "同调", "synchro"))
		{
			return pendulum ? "card_frame16" : "card_frame10";
		}
		if (ContainsAny(subType, "融合", "fusion"))
		{
			return pendulum ? "card_frame17" : "card_frame03";
		}
		if (ContainsAny(subType, "儀式", "仪式", "ritual"))
		{
			return pendulum ? "card_frame19" : "card_frame02";
		}
		if (ContainsAny(subType, "通常", "normal"))
		{
			return pendulum ? "card_frame13" : "card_frame00";
		}
		return pendulum ? "card_frame14" : "card_frame01";
	}

	public static IEnumerable<TexRef> CompatibleFrames(IEnumerable<TexRef> frames, int storedWidth, int storedHeight)
	{
		return (from x in frames
			where BuiltInCardFrameCatalog.IsNormalFrame(x) && x.Width == 704 && x.Height == 1024
			select x).OrderBy<TexRef, string>((TexRef x) => x.Name, StringComparer.OrdinalIgnoreCase);
	}

	private static bool ContainsAny(string value, params string[] needles)
	{
		return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
	}
}
