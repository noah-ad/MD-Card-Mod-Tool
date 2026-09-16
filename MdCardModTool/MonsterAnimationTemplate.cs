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
				return new string[1] { string.IsNullOrWhiteSpace(AnimationName) ? "animation" : AnimationName };
			}
			return AnimationNames;
		}
	}

	public static MonsterAnimationTemplate Parse(byte[] data)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement value;
		JsonElement element = (rootElement.TryGetProperty("skeleton", out value) ? value : default(JsonElement));
		string spineVersion = String(element, "spine", "3.8.75");
		double num = Number(element, "width", 0.0);
		double num2 = Number(element, "height", 0.0);
		double x = Number(element, "x", (0.0 - num) / 2.0);
		double y = Number(element, "y", (0.0 - num2) / 2.0);
		IReadOnlyList<string> readOnlyList = new string[1] { "animation" };
		if (rootElement.TryGetProperty("animations", out var value2) && value2.ValueKind == JsonValueKind.Object)
		{
			string[] array = (from value3 in value2.EnumerateObject().Select(delegate(JsonProperty p)
				{
					JsonProperty jsonProperty = p;
					return jsonProperty.Name;
				})
				where !string.IsNullOrWhiteSpace(value3)
				select value3).Distinct<string>(StringComparer.Ordinal).ToArray();
			if (array.Length != 0)
			{
				readOnlyList = array;
			}
		}
		return new MonsterAnimationTemplate(spineVersion, readOnlyList[0], x, y, num, num2, readOnlyList);
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
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var value2))
		{
			return fallback;
		}
		return value2;
	}
}
