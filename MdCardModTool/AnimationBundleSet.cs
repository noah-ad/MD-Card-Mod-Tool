using System.Collections.Generic;

namespace MdCardModTool;

public sealed record AnimationBundleSet
{
	public required string CardId { get; init; }

	public required IReadOnlyList<MonsterAnimationAssetRef> Assets { get; init; }

	public bool IsComplete => new MonsterAnimationSet { CardId = CardId, Assets = [.. Assets] }.IsComplete;
}
