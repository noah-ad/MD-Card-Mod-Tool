using System;
using System.Collections.Generic;

namespace MdCardModTool;

public sealed class ModPackageManifest
{
	public int FormatVersion { get; init; } = 1;

	public string Name { get; init; } = "Master Duel Mod";

	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

	public List<ModPackageEntry> Entries { get; init; } = new List<ModPackageEntry>();
}
