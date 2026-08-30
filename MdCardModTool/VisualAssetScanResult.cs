using System.Collections.Generic;

namespace MdCardModTool;

public sealed class VisualAssetScanResult
{
	public List<TexRef> Textures { get; init; } = new List<TexRef>();

	public int CatalogEntries { get; init; }

	public int CandidateBundles { get; init; }

	public int InstalledBundles { get; init; }
}
