using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MdCardModTool;

internal static class CardResourceSearch
{
	// Search the catalogue, not just the subset which already has a Texture2D
	// cache row. Missing artwork is an availability state, not "no such card".
	internal static IEnumerable<TexRef> IncludeCatalogMatches(IEnumerable<TexRef> installedMatches, CardCatalogService catalog, string query, AppLanguage language)
	{
		var installed = installedMatches.ToList();
		var seen = installed.Where(t => t.CardKey.Length > 0).Select(t => t.CardKey).ToHashSet(StringComparer.Ordinal);
		var matches = catalog.Search(query, 250);
		var rank = matches.Select((entry, index) => (entry.CardId, index)).ToDictionary(x => x.CardId, x => x.index);
		foreach (CardCatalogEntry entry in matches)
		{
			string id = entry.CardId.ToString(CultureInfo.InvariantCulture);
			if (!seen.Add(id)) continue;
			installed.Add(new TexRef
			{
				BundlePath = "", RelativeBundlePath = "", Name = entry.Name(language), CardKey = id,
				SourceKind = "卡片目录", Category = "卡图待定位"
			});
		}
		return installed.OrderBy(t => int.TryParse(t.CardKey, out int id) && rank.TryGetValue(id, out int score) ? score : int.MaxValue)
			.ThenBy(t => t.IsCatalogOnly).ThenByDescending(t => (long)t.Width * t.Height);
	}
}
