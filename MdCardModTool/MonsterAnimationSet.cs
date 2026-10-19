using System;
using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public sealed class MonsterAnimationSet
{
	public string CardId { get; init; } = "";

	public List<MonsterAnimationAssetRef> Assets { get; init; } = new List<MonsterAnimationAssetRef>();

	// Prefabs, timelines and effects are evidence of an official cut-in, not
	// interchangeable Spine assets. Never pass these bundles to the six-file writer.
	public IReadOnlyList<MonsterAnimationResourceBundle> ResourceBundles { get; init; } = Array.Empty<MonsterAnimationResourceBundle>();

	public bool HasOfficialPrefab => ResourceBundles.Any(bundle => bundle.IsPrefab);

	public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

	public MonsterAnimationAvailability Availability => IsComplete ? MonsterAnimationAvailability.Spine
		: Skeletons.Count > 0 || Atlases.Count > 0 || ResourceBundles.Any(bundle => bundle.HasSpineData)
			? MonsterAnimationAvailability.IncompleteSpine
			: HasOfficialPrefab && Diagnostics.Count == 0 && ResourceBundles.Any(bundle => bundle.IsTimeline)
				? MonsterAnimationAvailability.NativeTimeline : MonsterAnimationAvailability.Unresolved;

	public IReadOnlyList<MonsterAnimationAssetRef> Textures => Assets.Where((MonsterAnimationAssetRef x) => x.Kind == MonsterAnimationAssetKind.Texture).ToArray();

	public IReadOnlyList<MonsterAnimationAssetRef> Atlases => Assets.Where((MonsterAnimationAssetRef x) => x.Kind == MonsterAnimationAssetKind.Atlas).ToArray();

	public IReadOnlyList<MonsterAnimationAssetRef> Skeletons => Assets.Where((MonsterAnimationAssetRef x) => x.Kind == MonsterAnimationAssetKind.Skeleton).ToArray();

	public bool IsComplete
	{
		get
		{
			if (IsMobile)
			{
				if (Textures.Count >= 1 && Atlases.Count >= 1 && Skeletons.Count >= 1)
				{
					return MonsterAnimationAssetPairing.FindComplete(this).Count > 0;
				}
				return false;
			}
			if (Textures.Count >= 2 && Atlases.Count >= 2)
			{
				if (Skeletons.Count < 2) return false;
				try
				{
					return MonsterAnimationAssetPairing.FindComplete(this).GroupBy(pair => pair.Region, StringComparer.OrdinalIgnoreCase)
						.Any(group => group.Any(pair => pair.Tier == "HighEnd_HD") && group.Any(pair => pair.Tier == "SD"));
				}
				catch (System.IO.IOException) { return false; }
			}
			return false;
		}
	}

	public bool IsMobile => Assets.Any((MonsterAnimationAssetRef a) => a.StorageKind == "Mobile");

	public string CountSummary => $"Texture2D ×{Textures.Count} · Atlas ×{Atlases.Count} · JS ×{Skeletons.Count}";
}
