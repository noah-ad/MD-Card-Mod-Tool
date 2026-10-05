using System;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class CardTextureRules
{
    internal static bool IsCardSource(string sourceKind) => sourceKind is "本地卡图" or "游戏内图片";

    // Identity comes from the card bundle/container or numeric illustration name,
    // never from a translated category or a texture's dimensions alone.
    internal static bool HasCardIdentity(TexRef texture)
    {
        if (!IsCardSource(texture.SourceKind) || !texture.CardKey.All(char.IsAsciiDigit)
            || !ushort.TryParse(texture.CardKey, out ushort id) || id == 0) return false;

        // P<id> textures in LocalData normally belong to summon-animation atlases.
        // An illustration Mod can use that name too, but only at the card's actual
        // illustration bundle path. Higher resolution must not make atlases cards.
        if (texture.Name.Length > 1 && char.ToUpperInvariant(texture.Name[0]) == 'P' && char.IsAsciiDigit(texture.Name[1]))
        {
            string bundle = Path.GetFileName(texture.BundlePath);
            return new[] { "tcg", "ocg" }.Any(region => bundle.Equals(
                Path.GetFileName(IndexService.CardIllustrationRelativePath(texture.CardKey, region)), StringComparison.OrdinalIgnoreCase));
        }
        return true;
    }

    // A Mod may retain the illustration's layout at a higher resolution. Match
    // the storage contract, not the original 512/704-pixel export resolution.
    internal static bool IsSquareCanvas(int width, int height) => width > 0 && width == height;

    internal static bool IsPendulumCanvas(int width, int height) => width > 0 && (long)width * 2 == height;

    internal static bool IsFullCardCanvas(int width, int height) => width > 0 && height > 0
        && Math.Abs((long)width * 16 - (long)height * 11) <= 16; // Allow one rounded pixel.

    internal static bool IsNativeIllustration(TexRef texture) => HasCardIdentity(texture)
        && (IsSquareCanvas(texture.Width, texture.Height) || IsPendulumCanvas(texture.Width, texture.Height));

    internal static bool IsFullCardArt(TexRef texture) => HasCardIdentity(texture)
        && IsFullCardCanvas(texture.Width, texture.Height);

    internal static bool IsCardArt(TexRef texture) => IsNativeIllustration(texture) || IsFullCardArt(texture);
}
