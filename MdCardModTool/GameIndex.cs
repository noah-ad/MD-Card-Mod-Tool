namespace MdCardModTool;

public sealed class GameIndex
{
    public List<TexRef> Textures { get; init; } = [];
    public List<AssetDependency> Dependencies { get; init; } = [];
    /// <summary>异画卡分类规则的版本。0 表示旧缓存尚未建立过本地异画名单。</summary>
    public int AlternateArtIndexVersion { get; set; }
}
