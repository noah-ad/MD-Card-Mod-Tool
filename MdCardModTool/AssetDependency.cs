namespace MdCardModTool;

public sealed class AssetDependency
{
    public string Name { get; init; } = "";
    public string RelativeBundlePath { get; init; } = "";
    public string BundlePath { get; init; } = "";
    public string Kind { get; init; } = "";
    public string CardKey { get; init; } = "";
}

public sealed class BundleScan
{
    public List<TexRef> Textures { get; } = [];
    public List<AssetDependency> Dependencies { get; } = [];
}
