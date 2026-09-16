using System;
using System.Collections.Generic;

namespace MdCardModTool;

public sealed class AppSettings
{
	public int FormatVersion { get; set; } = 1;

	public AppLanguage Language { get; set; }

	public string LastGameRoot { get; set; } = "";

	public Dictionary<string, string> LastProfileByGame { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public bool ReduceMotion { get; set; }
}
