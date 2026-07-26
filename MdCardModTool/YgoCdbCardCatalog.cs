using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace MdCardModTool;

/// <summary>
/// 百鸽 cards.zip 的本地 CID 名单。20567–22747 范围中不在名单的资源号是异画；其余未命中项视为 Token／杂图。
/// 名单只在第一次使用时下载，随后完全离线读取，避免对网页逐张查询。
/// </summary>
public static class YgoCdbCardCatalog
{
    public const int ClassificationVersion = 4;
    const int CatalogVersion = 1;
    const int AlternateArtFirstId = 20567;
    const int AlternateArtLastId = 22747;
    const string DownloadUrl = "https://ygocdb.com/api/v0/cards.zip";
    static readonly HttpClient Http = CreateClient();

    sealed class Cache
    {
        public int Version { get; init; } = CatalogVersion;
        public HashSet<string> CardIds { get; init; } = new(StringComparer.Ordinal);
    }

    public static async Task ClassifyAlternateArtsAsync(GameIndex index)
    {
        if (index.AlternateArtIndexVersion >= ClassificationVersion) return;
        // v3 已完成百鸽全量分类；v4 只增加用户确认过的本地强制覆盖，可完全离线迁移。
        if (index.AlternateArtIndexVersion == 3)
        {
            ApplyForcedOverrides(index.Textures);
            index.AlternateArtIndexVersion = ClassificationVersion;
            return;
        }
        var normalCardIds = await LoadCardIdsAsync();
        ClassifyTextures(index.Textures, normalCardIds);
        index.AlternateArtIndexVersion = ClassificationVersion;
    }

    /// <summary>为按需补查加入的新卡图套用已有的本地 CID 名单；名单缺失时才会下载一次。</summary>
    public static async Task ClassifyTexturesAsync(IEnumerable<TexRef> textures)
        => ClassifyTextures(textures, await LoadCardIdsAsync());

    static void ClassifyTextures(IEnumerable<TexRef> textures, HashSet<string> normalCardIds)
    {
        foreach (var texture in textures.Where(IsLocalCardTexture))
        {
            var isNormalCard = normalCardIds.Contains(texture.CardKey);
            var isInAlternateArtRange = int.TryParse(texture.CardKey, out var cardId) && cardId >= AlternateArtFirstId && cardId <= AlternateArtLastId;
            // 百鸽可查到的仍是正常卡；只有号段内且未命中的资源号才是异画。
            texture.IsAlternateArt = !isNormalCard && isInAlternateArtRange;
            texture.IsTokenOrMisc = !isNormalCard && !isInAlternateArtRange;
            if (texture.Width == 512 && texture.Height == 512)
                texture.Category = texture.IsAlternateArt ? "异画卡图" : texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图";
        }
        ApplyForcedOverrides(textures);
    }

    /// <summary>
    /// 用户实机确认的资源号覆盖规则，优先级高于百鸽 CID 名单和默认异画号段。
    /// </summary>
    public static int ApplyForcedOverrides(IEnumerable<TexRef> textures)
    {
        var changed = 0;
        foreach (var texture in textures.Where(IsLocalCardTexture))
        {
            if (!int.TryParse(texture.CardKey, out var cardId)) continue;
            var forceNormal = cardId is >= 30000 and <= 30064;
            var forceAlternate = cardId is >= 3401 and <= 3899 or 19736 or 20040;
            if (!forceNormal && !forceAlternate) continue;

            var category = forceAlternate ? "异画卡图" : "卡图缩略图";
            if (texture.IsAlternateArt != forceAlternate || texture.IsTokenOrMisc || texture.Category != category) changed++;
            texture.IsAlternateArt = forceAlternate;
            texture.IsTokenOrMisc = false;
            if (texture.Width == 512 && texture.Height == 512) texture.Category = category;
        }
        return changed;
    }

    static bool IsLocalCardTexture(TexRef texture) =>
        texture.SourceKind == "本地卡图" && texture.CardKey.Length > 0;

    static async Task<HashSet<string>> LoadCardIdsAsync()
    {
        var path = CachePath();
        if (File.Exists(path))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(path));
                if (cached?.Version == CatalogVersion && cached.CardIds.Count > 0) return cached.CardIds;
            }
            catch { }
        }

        using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        var entry = zip.GetEntry("cards.json") ?? throw new InvalidDataException("百鸽 cards.zip 中未找到 cards.json。");
        await using var json = entry.Open();
        using var document = await JsonDocument.ParseAsync(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("百鸽 cards.json 格式不正确。");
        var ids = new HashSet<string>(document.RootElement.EnumerateObject().Select(x => x.Name), StringComparer.Ordinal);
        if (ids.Count == 0) throw new InvalidDataException("百鸽 cards.json 未包含卡号。");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Cache { CardIds = ids }));
        return ids;
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MDCardModTool/1.0");
        return client;
    }

    static string CachePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MDCardModTool",
        "ygocdb_card_cids_v1.json");
}
