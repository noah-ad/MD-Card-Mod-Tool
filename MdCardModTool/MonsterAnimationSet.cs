using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public sealed class MonsterAnimationSet
{
	public string CardId { get; init; } = "";

	public List<MonsterAnimationAssetRef> Assets { get; init; } = new List<MonsterAnimationAssetRef>();

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
				return Skeletons.Count >= 2;
			}
			return false;
		}
	}

	public bool IsMobile => Assets.Any((MonsterAnimationAssetRef a) => a.StorageKind == "Mobile");

	public string CountSummary => $"Texture2D ×{Textures.Count} · Atlas ×{Atlases.Count} · JS ×{Skeletons.Count}";
}
