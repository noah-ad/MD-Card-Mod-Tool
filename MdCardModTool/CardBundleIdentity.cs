using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class CardBundleIdentity
{
    static readonly object Gate = new();
    static string stamp = "";
    static Dictionary<string,string> ids = new(StringComparer.OrdinalIgnoreCase);

    internal static string Find(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Length != 8 || !name.All(char.IsAsciiHexDigit)) return "";
        lock (Gate)
        {
            string current = File.GetLastWriteTimeUtc(CardCatalogService.BundledPath).Ticks + ":"
                + File.GetLastWriteTimeUtc(GameCardCatalogUpdater.ExtraCatalogPath).Ticks;
            if (stamp != current)
            {
                var next = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach (var card in CardCatalogService.LoadBestAvailable().Entries)
                foreach (string region in new[] { "tcg", "ocg" })
                {
                    string key = Path.GetFileName(IndexService.CardIllustrationRelativePath(card.CardId.ToString(), region));
                    string id = card.CardId.ToString();
                    if (next.TryGetValue(key, out var other) && other != id) next[key] = "";
                    else next.TryAdd(key,id);
                }
                ids = next; stamp = current;
            }
            return ids.GetValueOrDefault(name, "");
        }
    }

    internal static void Refresh(TexRef texture, IReadOnlyList<TexRef> scanned)
    {
        var match = scanned.FirstOrDefault(x => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName)
            ?? scanned.FirstOrDefault(x => x.Name == texture.Name)
            ?? (scanned.Count == 1 ? scanned[0] : null);
        if (match == null) return; // Never invent a match for ambiguous multi-texture bundles.
        texture.PathId = match.PathId;
        texture.AssetFileName = match.AssetFileName;
        texture.Width = match.Width; texture.Height = match.Height;
        texture.OverrideBundlePath = null;
        // Keep existing card identity/category when a foreign tool renames the asset.
        if (texture.CardKey.Length == 0) texture.Category = match.Category;
        IndexService.NormalizeLocalCardCategory(texture);
    }
}
