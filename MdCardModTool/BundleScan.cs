using System.Collections.Generic;

namespace MdCardModTool;

public sealed class BundleScan
{
	public List<TexRef> Textures { get; } = new List<TexRef>();

	public List<AssetDependency> Dependencies { get; } = new List<AssetDependency>();
}
