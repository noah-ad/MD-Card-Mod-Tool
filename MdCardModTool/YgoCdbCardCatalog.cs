using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class YgoCdbCardCatalog
{
	private sealed class Cache
	{
		public int Version { get; init; } = 1;

		public HashSet<string> CardIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
	}

	public const int ClassificationVersion = 4;

	private const int CatalogVersion = 1;

	private const int AlternateArtFirstId = 20567;

	private const int AlternateArtLastId = 22747;

	private const string DownloadUrl = "https://ygocdb.com/api/v0/cards.zip";

	private static readonly HttpClient Http = CreateClient();

	public static async Task ClassifyAlternateArtsAsync(GameIndex index)
	{
		if (index.AlternateArtIndexVersion < 4)
		{
			if (index.AlternateArtIndexVersion == 3)
			{
				ApplyForcedOverrides(index.Textures);
				index.AlternateArtIndexVersion = 4;
			}
			else
			{
				HashSet<string> normalCardIds = await LoadCardIdsAsync();
				ClassifyTextures(index.Textures, normalCardIds);
				index.AlternateArtIndexVersion = 4;
			}
		}
	}

	public static async Task ClassifyTexturesAsync(IEnumerable<TexRef> textures)
	{
		ClassifyTextures(textures, await LoadCardIdsAsync());
	}

	private static void ClassifyTextures(IEnumerable<TexRef> textures, HashSet<string> normalCardIds)
	{
		foreach (TexRef texture in textures.Where(IsLocalCardTexture))
		{
			bool isNormalCard = normalCardIds.Contains(texture.CardKey);
			int cardId;
			bool isInAlternateArtRange = int.TryParse(texture.CardKey, out cardId) && cardId >= 20567 && cardId <= 22747;
			texture.IsAlternateArt = !isNormalCard && isInAlternateArtRange;
			texture.IsTokenOrMisc = !isNormalCard && !isInAlternateArtRange;
			if (texture.Width == 512 && texture.Height == 512)
			{
				texture.Category = (texture.IsAlternateArt ? "异画卡图" : (texture.IsTokenOrMisc ? "Token／杂图" : "卡图缩略图"));
			}
		}
		ApplyForcedOverrides(textures);
	}

	public static int ApplyForcedOverrides(IEnumerable<TexRef> textures)
	{
		int changed = 0;
		foreach (TexRef texture in textures.Where(IsLocalCardTexture))
		{
			if (!int.TryParse(texture.CardKey, out var cardId))
			{
				continue;
			}
			bool forceNormal = cardId >= 30000 && cardId <= 30064;
			bool flag = ((cardId >= 3401 && (cardId <= 3899 || cardId == 19736 || cardId == 20040)) ? true : false);
			bool forceAlternate = flag;
			if (forceNormal || forceAlternate)
			{
				string category = (forceAlternate ? "异画卡图" : "卡图缩略图");
				if (texture.IsAlternateArt != forceAlternate || texture.IsTokenOrMisc || texture.Category != category)
				{
					changed++;
				}
				texture.IsAlternateArt = forceAlternate;
				texture.IsTokenOrMisc = false;
				if (texture.Width == 512 && texture.Height == 512)
				{
					texture.Category = category;
				}
			}
		}
		return changed;
	}

	private static bool IsLocalCardTexture(TexRef texture)
	{
		if (texture.SourceKind == "本地卡图")
		{
			return texture.CardKey.Length > 0;
		}
		return false;
	}

	private static async Task<HashSet<string>> LoadCardIdsAsync()
	{
		string path = CachePath();
		if (File.Exists(path))
		{
			try
			{
				Cache cached = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(path));
				if (cached != null && cached.Version == 1 && cached.CardIds.Count > 0)
				{
					return cached.CardIds;
				}
			}
			catch
			{
			}
		}
		using HttpResponseMessage response = await Http.GetAsync("https://ygocdb.com/api/v0/cards.zip", HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		HashSet<string> result;
		await using (Stream source = await response.Content.ReadAsStreamAsync())
		{
			using ZipArchive zip = new ZipArchive(source, ZipArchiveMode.Read);
			ZipArchiveEntry entry = zip.GetEntry("cards.json") ?? throw new InvalidDataException("百鸽 cards.zip 中未找到 cards.json。");
			HashSet<string> hashSet;
			await using (Stream json = entry.Open())
			{
				using JsonDocument document = await JsonDocument.ParseAsync(json);
				if (document.RootElement.ValueKind != JsonValueKind.Object)
				{
					throw new InvalidDataException("百鸽 cards.json 格式不正确。");
				}
				HashSet<string> ids = new HashSet<string>(from x in document.RootElement.EnumerateObject()
					select x.Name, StringComparer.Ordinal);
				if (ids.Count == 0)
				{
					throw new InvalidDataException("百鸽 cards.json 未包含卡号。");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Cache
				{
					CardIds = ids
				}));
				hashSet = ids;
			}
			result = hashSet;
		}
		return result;
	}

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromSeconds(45.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MDCardModTool/1.0");
		return httpClient;
	}

	private static string CachePath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", "ygocdb_card_cids_v1.json");
	}
}
