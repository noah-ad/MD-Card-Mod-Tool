namespace MdCardModTool;

public sealed class TexRef
{
    public required string BundlePath { get; init; }
    public required string RelativeBundlePath { get; init; }
    public long PathId { get; init; }
    public string AssetFileName { get; init; } = "";
    public string Name { get; init; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Category { get; set; } = "其他贴图";
    /// <summary>20567–22747 区间内、且不在百鸽正常卡 CID 名单中的 MD 异画资源号。</summary>
    public bool IsAlternateArt { get; set; }
    /// <summary>不在百鸽正常卡 CID 名单、且不属于异画号段的 Token 或其他杂图。</summary>
    public bool IsTokenOrMisc { get; set; }
    /// <summary>当前 Bundle 与本工具保存的原版备份不同；启动时由轻量 Mod 台账重新计算。</summary>
    public bool IsModded { get; set; }
    public string SourceKind { get; init; } = "";
    public string CardKey { get; init; } = "";
    public string? OverrideBundlePath { get; set; }
    public string ActiveBundlePath => OverrideBundlePath ?? BundlePath;
    public override string ToString() => $"{Name}  ·  {Width}×{Height}";
}
