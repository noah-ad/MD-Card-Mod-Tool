using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class NewCardDiscovery
{
	internal static string DiscoveredCatalogPath => Path.Combine(AppSettingsStore.AppDataRoot, "card-catalog-discovered-v1.json.br");
	private static readonly object Gate = new();

	internal static int Merge(GameIndex index, List<TexRef> visible, MissingCardScanResult result)
	{
		var oldIds = index.Textures.Where(CardTextureRules.HasCardIdentity).Select(x => x.CardKey).ToHashSet(StringComparer.Ordinal);
		var old = index.Textures.Where(CardTextureRules.HasCardIdentity).GroupBy(x => x.BundlePath, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
		foreach (var texture in result.Textures)
		{
			TexRef? previous = old.TryGetValue(texture.BundlePath, out var group) ? group.FirstOrDefault(x => x.Name == texture.Name || x.CardKey == texture.CardKey) : null;
			if (previous != null)
			{
				texture.PreviewFrameKey = previous.PreviewFrameKey;
				texture.IsAlternateArt = previous.IsAlternateArt;
				texture.IsTokenOrMisc = previous.IsTokenOrMisc;
				texture.HasMonsterAnimation = previous.HasMonsterAnimation;
				texture.IsModded = previous.IsModded;
				texture.ModModifiedAtUtc = previous.ModModifiedAtUtc;
			}
			IndexService.NormalizeLocalCardCategory(texture);
		}
		bool Replace(TexRef x) => CardTextureRules.HasCardIdentity(x) && result.ScanStamps.ContainsKey(Path.GetFullPath(x.BundlePath));
		index.Textures.RemoveAll(x => Replace(x));
		if (!ReferenceEquals(index.Textures, visible)) visible.RemoveAll(x => Replace(x));
		var keys = index.Textures.Select(x => x.BundlePath + "\0" + x.AssetFileName + "\0" + x.PathId).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var additions = result.Textures.Where(x => keys.Add(x.BundlePath + "\0" + x.AssetFileName + "\0" + x.PathId)).ToArray();
		index.Textures.AddRange(additions);
		if (!ReferenceEquals(index.Textures, visible)) visible.AddRange(additions);
		foreach (var stamp in result.ScanStamps) index.CardBundleScanStamps[stamp.Key] = stamp.Value;
		index.BuiltInCardIndexVersion = 2;
		return result.Textures.Select(x => x.CardKey).Distinct(StringComparer.Ordinal).Count(x => !oldIds.Contains(x));
	}

	// IDs with downloaded illustrations stay searchable while dictionaries are
	// unavailable. Do not invent names, types or summon eligibility.
	internal static void SaveObservedIds(IEnumerable<TexRef> textures, string? path = null)
	{
		path ??= DiscoveredCatalogPath;
		lock (Gate)
		{
			var prior = File.Exists(path) ? CardCatalogService.Read(path) : new List<CardCatalogEntry>();
			var ids = prior.Select(x => x.CardId).ToHashSet();
			var additions = textures.Where(CardTextureRules.HasCardIdentity).Select(x => int.Parse(x.CardKey))
				.Where(x => ids.Add(x)).Select(x => new CardCatalogEntry { CardId = x }).ToArray();
			if (additions.Length > 0) CardCatalogService.Write(path, prior.Concat(additions));
		}
	}
}
