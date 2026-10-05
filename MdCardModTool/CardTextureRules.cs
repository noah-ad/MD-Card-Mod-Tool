using System;

namespace MdCardModTool;

internal static class CardTextureRules
{
    internal static bool IsCardSource(string sourceKind) => sourceKind is "本地卡图" or "游戏内图片";

    // StreamingAssets cards have the same illustration/overframe contract as downloaded cards.
    // Keep their source kind intact: it controls the root, backup, restore and Mod package paths.
    internal static bool IsCardArt(TexRef texture) => IsCardSource(texture.SourceKind)
        && ushort.TryParse(texture.CardKey, out ushort id) && id > 0
        && ((texture.Width == 512 && texture.Height is 512 or 1024)
            || (texture.Width == 704 && texture.Height == 1024));
}
