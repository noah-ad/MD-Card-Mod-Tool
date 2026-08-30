namespace MdCardModTool;

public sealed record CardCatalogEntry
{
	public required int CardId { get; init; }

	/// <summary>
	/// Record position observed in CARD_Indx/CARD_Prop.  This is useful for
	/// diagnostics, but it is not the P-number used by MonsterCutIn resources.
	/// </summary>
	public int Mrk { get; init; }

	public string TraditionalChineseName { get; init; } = "";

	public string SimplifiedChineseName { get; init; } = "";

	public string JapaneseName { get; init; } = "";

	public string EnglishName { get; init; } = "";

	public string Type { get; init; } = "";

	public string SubType { get; init; } = "";

	public bool IsMonster => Type.Contains("怪", System.StringComparison.OrdinalIgnoreCase)
		|| Type.Contains("monster", System.StringComparison.OrdinalIgnoreCase);

	public int AnimationId => CardId;

	public string Name(AppLanguage language)
	{
		string preferred = language switch
		{
			AppLanguage.TraditionalChinese => TraditionalChineseName,
			AppLanguage.Japanese => JapaneseName,
			AppLanguage.English => EnglishName,
			_ => SimplifiedChineseName
		};
		return First(preferred, SimplifiedChineseName, TraditionalChineseName, JapaneseName, EnglishName, CardId.ToString());
	}

	private static string First(params string[] values)
	{
		foreach (string value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}
		return "";
	}
}
