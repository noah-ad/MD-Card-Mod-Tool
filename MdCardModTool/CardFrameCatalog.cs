namespace MdCardModTool;

/// <summary>游戏内 704×1024 卡框的显示名称、分组与默认选择。</summary>
public static class CardFrameCatalog
{
    static readonly IReadOnlyDictionary<string, string> FriendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["card_frame00"] = "通常怪兽",
        ["card_frame01"] = "效果怪兽",
        ["card_frame02"] = "仪式怪兽",
        ["card_frame03"] = "融合怪兽",
        ["card_frame07"] = "魔法卡",
        ["card_frame08"] = "陷阱卡",
        ["card_frame09"] = "衍生物／灰色",
        ["card_frame10"] = "同调怪兽",
        ["card_frame12"] = "超量怪兽",
        ["card_frame13"] = "灵摆通常怪兽",
        ["card_frame14"] = "灵摆效果怪兽",
        ["card_frame15"] = "灵摆超量怪兽",
        ["card_frame16"] = "灵摆同调怪兽",
        ["card_frame17"] = "灵摆融合怪兽",
        ["card_frame18"] = "连接怪兽",
        ["card_frame19"] = "灵摆仪式怪兽"
    };

    static readonly HashSet<string> PendulumFrames = ["card_frame13", "card_frame14", "card_frame15", "card_frame16", "card_frame17", "card_frame19"];

    public static string FriendlyName(string key) => FriendlyNames.TryGetValue(key, out var value) ? value : key;
    public static bool IsPendulum(string key) => PendulumFrames.Contains(key);
    public static string DefaultKey(int storedWidth, int storedHeight) => storedWidth == 512 && storedHeight == 1024 ? "card_frame14" : "card_frame01";

    public static IEnumerable<TexRef> CompatibleFrames(IEnumerable<TexRef> frames, int storedWidth, int storedHeight)
    {
        var pendulum = storedWidth == 512 && storedHeight == 1024;
        return frames
            .Where(x => x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == FrameComposer.Width && x.Height == FrameComposer.Height)
            .Where(x => IsPendulum(x.Name) == pendulum)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }
}
