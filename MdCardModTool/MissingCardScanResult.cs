using System.Collections.Generic;

namespace MdCardModTool;

public sealed class MissingCardScanResult
{
	public List<TexRef> Textures { get; init; } = new List<TexRef>();

	public int ScannedBundles { get; init; }

	public int TotalBundles { get; init; }

	public bool Found { get; init; }

	public Dictionary<string, string> ScanStamps { get; init; } = new(System.StringComparer.OrdinalIgnoreCase);

	// Never acknowledge an incomplete or unreadable illustration as scanned.
	// Keep the previous texture, and retry it even when Steam's build is unchanged.
	public List<string> PendingBundles { get; init; } = new();
}
