using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MdCardModTool;

public static class MonsterAnimationAssetPairing
{
	private sealed record Candidate(MonsterAnimationAssetRef Asset, string Region, string Tier, string Scale);

	private sealed class ScaleGroup
	{
		public required string Region { get; init; }

		public required string Tier { get; init; }

		public required string Scale { get; init; }

		public List<MonsterAnimationAssetRef> Textures { get; } = new List<MonsterAnimationAssetRef>();

		public List<MonsterAnimationAssetRef> Atlases { get; } = new List<MonsterAnimationAssetRef>();
	}

	public static IReadOnlyList<MonsterAnimationAssetTriplet> FindComplete(MonsterAnimationSet set)
	{
		if (string.IsNullOrWhiteSpace(set.CardId))
		{
			return Array.Empty<MonsterAnimationAssetTriplet>();
		}
		Regex pathPattern = new Regex("(?:^|/)monstercutin/(?<region>tcg|ocg)/p" + Regex.Escape(set.CardId) + "/(?<tier>highend_hd|sd)(?:/(?<scale>[^/]+))?/[^/]+?(?:\\.png|\\.atlas\\.txt|js\\.json)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		List<Candidate> list = new List<Candidate>();
		ModEngine engine = new ModEngine();
		foreach (MonsterAnimationAssetRef item in set.Assets.Where((MonsterAnimationAssetRef asset) => File.Exists(asset.BundlePath)))
		{
			try
			{
				Match match = (from path in engine.ReadAssetBundleContainerPaths(item.BundlePath)
					select pathPattern.Match(path.Replace('\\', '/'))).FirstOrDefault((Match candidate) => candidate.Success);
				if (match != null && match.Success)
				{
					string region = match.Groups["region"].Value.ToLowerInvariant();
					string tier = (match.Groups["tier"].Value.Equals("sd", StringComparison.OrdinalIgnoreCase) ? "SD" : "HighEnd_HD");
					list.Add(new Candidate(item, region, tier, match.Groups["scale"].Value));
				}
			}
			catch
			{
			}
		}
		Dictionary<string, List<MonsterAnimationAssetRef>> skeletons = list.Where((Candidate candidate) => candidate.Asset.Kind == MonsterAnimationAssetKind.Skeleton).GroupBy<Candidate, string>((Candidate candidate) => candidate.Region + "|" + candidate.Tier, StringComparer.OrdinalIgnoreCase).ToDictionary<IGrouping<string, Candidate>, string, List<MonsterAnimationAssetRef>>((IGrouping<string, Candidate> group) => group.Key, (IGrouping<string, Candidate> group) => group.Select((Candidate candidate) => candidate.Asset).ToList(), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ScaleGroup> dictionary = new Dictionary<string, ScaleGroup>(StringComparer.OrdinalIgnoreCase);
		foreach (Candidate item2 in list.Where((Candidate candidate) => candidate.Asset.Kind != MonsterAnimationAssetKind.Skeleton))
		{
			string key = item2.Region + "|" + item2.Tier + "|" + item2.Scale;
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = (dictionary[key] = new ScaleGroup
				{
					Region = item2.Region,
					Tier = item2.Tier,
					Scale = item2.Scale
				});
			}
			if (item2.Asset.Kind == MonsterAnimationAssetKind.Texture)
			{
				value.Textures.Add(item2.Asset);
			}
			else if (item2.Asset.Kind == MonsterAnimationAssetKind.Atlas)
			{
				value.Atlases.Add(item2.Asset);
			}
		}
		return (from @group in dictionary.Values
			where @group.Textures.Count > 0 && @group.Atlases.Count > 0 && skeletons.ContainsKey(@group.Region + "|" + @group.Tier)
			select new MonsterAnimationAssetTriplet
			{
				Region = @group.Region,
				Tier = @group.Tier,
				Scale = @group.Scale,
				Texture = PrimaryTexture(@group, engine),
				Textures = @group.Textures,
				Atlas = Best(@group.Atlases),
				Skeleton = Best(skeletons[@group.Region + "|" + @group.Tier])
			} into pair
			orderby (!(pair.Tier == "HighEnd_HD")) ? 1 : 0, ParseScale(pair.Scale) descending
			select pair).ThenBy<MonsterAnimationAssetTriplet, string>((MonsterAnimationAssetTriplet pair) => pair.Region, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static MonsterAnimationAssetRef PrimaryTexture(ScaleGroup group, ModEngine engine)
	{
		string page = Encoding.UTF8.GetString(engine.ReadTextAsset(Best(group.Atlases)).Data).TrimStart().Split('\n')[0].Trim();
		return group.Textures.FirstOrDefault((MonsterAnimationAssetRef t) => (t.Name + ".png").Equals(page, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Atlas 首页面没有对应纹理：" + page);
	}

	public static MonsterAnimationAssetTriplet SelectPreview(MonsterAnimationSet set)
	{
		return FindComplete(set).FirstOrDefault() ?? throw new InvalidDataException("动画六资源无法按同一地区与 HD/SD 层级配对；已停止预览以避免混用资源。");
	}

	private static MonsterAnimationAssetRef Best(IEnumerable<MonsterAnimationAssetRef> assets)
	{
		return assets.OrderBy((MonsterAnimationAssetRef asset) => (!asset.StorageKind.Equals("LocalData", StringComparison.OrdinalIgnoreCase)) ? 1 : 0).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef asset) => asset.RelativeBundlePath, StringComparer.OrdinalIgnoreCase).ThenBy((MonsterAnimationAssetRef asset) => asset.PathId)
			.First();
	}

	private static double ParseScale(string value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}
}
