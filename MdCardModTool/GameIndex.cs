using System.Collections.Generic;

namespace MdCardModTool;

public sealed class GameIndex
{
	public List<TexRef> Textures { get; init; } = new List<TexRef>();

	public List<AssetDependency> Dependencies { get; init; } = new List<AssetDependency>();

	public List<string> CheckedLocalBundlePaths { get; init; } = new List<string>();

	public int AlternateArtIndexVersion { get; set; }

	public int BuiltInCardIndexVersion { get; set; }

	// The game can replace an illustration Bundle in-place while retaining its
	// file metadata. Remember the Build for which the complete card pass ran so
	// the next game update forces a real re-read instead of trusting stale PathID
	// and Texture2D metadata.
	public string LastScannedBuildId { get; set; } = "";

	public Dictionary<string, string> CardBundleScanStamps { get; init; } = new(System.StringComparer.OrdinalIgnoreCase);
}
