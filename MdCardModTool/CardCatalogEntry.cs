using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record CardCatalogEntry
{
	public required int CardId { get; init; }

	public int Mrk { get; init; }

	public string TraditionalChineseName { get; init; } = "";

	public string SimplifiedChineseName { get; init; } = "";

	public string JapaneseName { get; init; } = "";

	public string EnglishName { get; init; } = "";

	public string Type { get; init; } = "";

	public string SubType { get; init; } = "";

	public bool IsMonster
	{
		get
		{
			if (!Type.Contains("怪", StringComparison.OrdinalIgnoreCase))
			{
				return Type.Contains("monster", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	public int AnimationId => CardId;

	public string Name(AppLanguage language)
	{
		string text = language switch
		{
			AppLanguage.TraditionalChinese => TraditionalChineseName,
			AppLanguage.Japanese => JapaneseName,
			AppLanguage.English => EnglishName,
			_ => SimplifiedChineseName,
		};
		return First(text, SimplifiedChineseName, TraditionalChineseName, JapaneseName, EnglishName, CardId.ToString());
	}

	private static string First(params string[] values)
	{
		foreach (string text in values)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return "";
	}

	[CompilerGenerated]
	[SetsRequiredMembers]
	private CardCatalogEntry(CardCatalogEntry original)
	{
		CardId = original.CardId;
		Mrk = original.Mrk;
		TraditionalChineseName = original.TraditionalChineseName;
		SimplifiedChineseName = original.SimplifiedChineseName;
		JapaneseName = original.JapaneseName;
		EnglishName = original.EnglishName;
		Type = original.Type;
		SubType = original.SubType;
	}

	public CardCatalogEntry()
	{
	}
}
