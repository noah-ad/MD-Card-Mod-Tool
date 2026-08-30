using System.Collections.Generic;

namespace MdCardModTool;

public sealed class AppSettings
{
	public int FormatVersion { get; set; } = 1;

	public AppLanguage Language { get; set; } = AppLanguage.SimplifiedChinese;

	public string LastGameRoot { get; set; } = "";

	public Dictionary<string, string> LastProfileByGame { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

	public bool ReduceMotion { get; set; }
}
