using System.Collections.Generic;

namespace MdCardModTool;

public sealed class MissingCardScanResult
{
	public List<TexRef> Textures { get; init; } = new List<TexRef>();

	public int ScannedBundles { get; init; }

	public int TotalBundles { get; init; }

	public bool Found { get; init; }
}
