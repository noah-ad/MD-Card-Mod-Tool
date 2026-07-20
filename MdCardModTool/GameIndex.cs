namespace MdCardModTool;

public sealed class GameIndex
{
    public List<TexRef> Textures { get; init; } = [];
    public List<AssetDependency> Dependencies { get; init; } = [];
    /// <summary>按卡号补查时已经解析过的 LocalData Bundle 相对路径。避免同一台电脑下次又从头扫描。</summary>
    public List<string> CheckedLocalBundlePaths { get; init; } = [];
    /// <summary>异画卡分类规则的版本。0 表示旧缓存尚未建立过本地异画名单。</summary>
    public int AlternateArtIndexVersion { get; set; }
}
