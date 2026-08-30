using System.Collections.Generic;

namespace MdCardModTool;

public sealed class GameIndex
{
	public List<TexRef> Textures { get; init; } = new List<TexRef>();

	public List<AssetDependency> Dependencies { get; init; } = new List<AssetDependency>();

	public List<string> CheckedLocalBundlePaths { get; init; } = new List<string>();

	public int AlternateArtIndexVersion { get; set; }
}
