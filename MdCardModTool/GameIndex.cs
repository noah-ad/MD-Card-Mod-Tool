namespace MdCardModTool;

public sealed class GameIndex
{
    public List<TexRef> Textures { get; init; } = [];
    public List<AssetDependency> Dependencies { get; init; } = [];
    /// <summary>
    /// data.unity3d 的轻量指纹。Master Duel 更新后该文件内的 PathID 会整体漂移，
    /// 不能继续复用旧缓存中的 card_frame 映射。
    /// </summary>
    public string CardFrameDataStamp { get; set; } = "";
    /// <summary>按卡号补查时已经解析过的 LocalData Bundle 相对路径。避免同一台电脑下次又从头扫描。</summary>
    public List<string> CheckedLocalBundlePaths { get; init; } = [];
    /// <summary>异画卡分类规则的版本。0 表示旧缓存尚未建立过本地异画名单。</summary>
    public int AlternateArtIndexVersion { get; set; }
}
