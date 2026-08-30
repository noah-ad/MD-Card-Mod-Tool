namespace MdCardModTool;

public sealed class RawAnimationManifestEntry
{
	public string FileName { get; init; } = "";

	public string RelativeBundlePath { get; init; } = "";

	public string AssetFileName { get; init; } = "";

	public long PathId { get; init; }

	public MonsterAnimationAssetKind Kind { get; init; }
}
