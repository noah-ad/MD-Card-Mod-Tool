namespace MdCardModTool;

public sealed class ModPackageEntry
{
	public string ArchivePath { get; init; } = "";

	public string TargetKind { get; init; } = "";

	public string RelativePath { get; init; } = "";

	public string SourceKind { get; init; } = "";

	public string DisplayName { get; init; } = "";

	public string Sha256 { get; init; } = "";

	public long Size { get; init; }
}
