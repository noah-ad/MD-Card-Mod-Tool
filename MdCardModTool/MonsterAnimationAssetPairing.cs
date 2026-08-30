using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MdCardModTool;

public sealed record MonsterAnimationAssetTriplet
{
	public required string Region { get; init; }

	public required string Tier { get; init; }

	public string Scale { get; init; } = "";

	public required MonsterAnimationAssetRef Texture { get; init; }

	public required MonsterAnimationAssetRef Atlas { get; init; }

	public required MonsterAnimationAssetRef Skeleton { get; init; }

	public string Key => Region + "/" + Tier + (Scale.Length > 0 ? "/" + Scale : "");
}

public static class MonsterAnimationAssetPairing
{
	private sealed record Candidate(MonsterAnimationAssetRef Asset, string Region, string Tier, string Scale);

	private sealed class ScaleGroup
	{
		public required string Region { get; init; }
		public required string Tier { get; init; }
		public required string Scale { get; init; }
		public List<MonsterAnimationAssetRef> Textures { get; } = [];
		public List<MonsterAnimationAssetRef> Atlases { get; } = [];
	}

	public static IReadOnlyList<MonsterAnimationAssetTriplet> FindComplete(MonsterAnimationSet set)
	{
		if (string.IsNullOrWhiteSpace(set.CardId)) return [];
		Regex pathPattern = new(
			"(?:^|/)monstercutin/(?<region>tcg|ocg)/p" + Regex.Escape(set.CardId)
			+ "/(?<tier>highend_hd|sd)(?:/(?<scale>[^/]+))?/p" + Regex.Escape(set.CardId)
			+ "(?:\\.png|\\.atlas\\.txt|js\\.json)$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		List<Candidate> candidates = [];
		ModEngine engine = new();
		foreach (MonsterAnimationAssetRef asset in set.Assets.Where(asset => File.Exists(asset.BundlePath)))
		{
			try
			{
				Match? match = engine.ReadAssetBundleContainerPaths(asset.BundlePath)
					.Select(path => pathPattern.Match(path.Replace('\\', '/')))
					.FirstOrDefault(candidate => candidate.Success);
				if (match == null || !match.Success) continue;
				string region = match.Groups["region"].Value.ToLowerInvariant();
				string tier = match.Groups["tier"].Value.Equals("sd", StringComparison.OrdinalIgnoreCase)
					? "SD" : "HighEnd_HD";
				candidates.Add(new Candidate(asset, region, tier, match.Groups["scale"].Value));
			}
			catch
			{
			}
		}
		Dictionary<string, List<MonsterAnimationAssetRef>> skeletons = candidates
			.Where(candidate => candidate.Asset.Kind == MonsterAnimationAssetKind.Skeleton)
			.GroupBy(candidate => candidate.Region + "|" + candidate.Tier, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.Select(candidate => candidate.Asset).ToList(), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ScaleGroup> groups = new(StringComparer.OrdinalIgnoreCase);
		foreach (Candidate candidate in candidates.Where(candidate => candidate.Asset.Kind != MonsterAnimationAssetKind.Skeleton))
		{
			string key = candidate.Region + "|" + candidate.Tier + "|" + candidate.Scale;
			if (!groups.TryGetValue(key, out ScaleGroup? group))
			{
				group = new ScaleGroup { Region = candidate.Region, Tier = candidate.Tier, Scale = candidate.Scale };
				groups[key] = group;
			}
			if (candidate.Asset.Kind == MonsterAnimationAssetKind.Texture) group.Textures.Add(candidate.Asset);
			else if (candidate.Asset.Kind == MonsterAnimationAssetKind.Atlas) group.Atlases.Add(candidate.Asset);
		}
		return groups.Values
			.Where(group => group.Textures.Count > 0 && group.Atlases.Count > 0
				&& skeletons.ContainsKey(group.Region + "|" + group.Tier))
			.Select(group => new MonsterAnimationAssetTriplet
			{
				Region = group.Region,
				Tier = group.Tier,
				Scale = group.Scale,
				Texture = Best(group.Textures),
				Atlas = Best(group.Atlases),
				Skeleton = Best(skeletons[group.Region + "|" + group.Tier])
			})
			.OrderBy(pair => pair.Tier == "HighEnd_HD" ? 0 : 1)
			.ThenByDescending(pair => ParseScale(pair.Scale))
			.ThenBy(pair => pair.Region, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public static MonsterAnimationAssetTriplet SelectPreview(MonsterAnimationSet set)
	{
		return FindComplete(set).FirstOrDefault()
			?? throw new InvalidDataException("动画六资源无法按同一地区与 HD/SD 层级配对；已停止预览以避免混用资源。");
	}

	private static MonsterAnimationAssetRef Best(IEnumerable<MonsterAnimationAssetRef> assets) => assets
		.OrderBy(asset => asset.StorageKind.Equals("LocalData", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
		.ThenBy(asset => asset.RelativeBundlePath, StringComparer.OrdinalIgnoreCase)
		.ThenBy(asset => asset.PathId)
		.First();

	private static double ParseScale(string value) => double.TryParse(value, NumberStyles.Float,
		CultureInfo.InvariantCulture, out double scale) ? scale : 0;
}
