using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MdCardModTool;

public sealed record MonsterAnimationTemplate(string SpineVersion, string AnimationName, double X, double Y, double Width, double Height, IReadOnlyList<string>? AnimationNames = null)
{
	public IReadOnlyList<string> EffectiveAnimationNames
	{
		get
		{
			IReadOnlyList<string> animationNames = AnimationNames;
			if (animationNames == null || animationNames.Count <= 0)
			{
				return new[] { string.IsNullOrWhiteSpace(AnimationName) ? "animation" : AnimationName };
			}
			return AnimationNames;
		}
	}

	public static MonsterAnimationTemplate Parse(byte[] data)
	{
		using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement root = document.RootElement;
		JsonElement value;
		JsonElement element = (root.TryGetProperty("skeleton", out value) ? value : default(JsonElement));
		string spine = String(element, "spine", "3.8.75");
		double width = Number(element, "width", 0.0);
		double height = Number(element, "height", 0.0);
		double x = Number(element, "x", (0.0 - width) / 2.0);
		double y = Number(element, "y", (0.0 - height) / 2.0);
		IReadOnlyList<string> animationNames = new[] { "animation" };
		if (root.TryGetProperty("animations", out var animations) && animations.ValueKind == JsonValueKind.Object)
		{
			string[] names = (from p in animations.EnumerateObject()
				select p.Name into value2
				where !string.IsNullOrWhiteSpace(value2)
				select value2).Distinct<string>(StringComparer.Ordinal).ToArray();
			if (names.Length != 0)
			{
				animationNames = names;
			}
		}
		return new MonsterAnimationTemplate(spine, animationNames[0], x, y, width, height, animationNames);
	}

	private static string String(JsonElement element, string name, string fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return fallback;
		}
		return value.GetString() ?? fallback;
	}

	private static double Number(JsonElement element, string name, double fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var number))
		{
			return fallback;
		}
		return number;
	}
}
