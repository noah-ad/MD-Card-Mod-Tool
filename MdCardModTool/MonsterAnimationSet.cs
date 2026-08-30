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
			if (Textures.Count >= 2 && Atlases.Count >= 2)
			{
				return Skeletons.Count >= 2;
			}
			return false;
		}
	}

	public string CountSummary => $"Texture2D {Textures.Count}/2 · Atlas {Atlases.Count}/2 · JS {Skeletons.Count}/2";
}
