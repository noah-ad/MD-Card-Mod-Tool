namespace MdCardModTool;

public sealed class TextAssetRef
{
    public string BundlePath { get; init; } = "";
    public string RelativeBundlePath { get; init; } = "";
    public string AssetFileName { get; init; } = "";
    public string Name { get; init; } = "";
    public string PathName { get; init; } = "";
    public long PathId { get; init; }
    public byte[] Data { get; init; } = [];
}
