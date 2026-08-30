using System.Collections.Generic;

namespace MdCardModTool;

public sealed class RawAnimationManifest
{
	public int FormatVersion { get; init; } = 1;

	public string CardId { get; init; } = "";

	public List<RawAnimationManifestEntry> Files { get; init; } = new List<RawAnimationManifestEntry>();
}
