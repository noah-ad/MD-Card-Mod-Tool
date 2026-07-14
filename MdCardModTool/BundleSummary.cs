namespace MdCardModTool;

public sealed class BundleSummary
{
    public required string RelativePath { get; init; }
    public int SerializedFiles { get; init; }
    public Dictionary<string, int> AssetTypes { get; init; } = [];
    public string Describe() => AssetTypes.Count == 0 ? "无 Serialized Asset（通常是资源数据或依赖 Bundle）" : string.Join("；", AssetTypes.OrderBy(x => x.Key).Select(x => $"{x.Key} × {x.Value}"));
}
