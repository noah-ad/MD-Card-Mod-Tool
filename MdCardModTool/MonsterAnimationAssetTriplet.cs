using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record MonsterAnimationAssetTriplet
{
	public required string Region { get; init; }

	public required string Tier { get; init; }

	public string Scale { get; init; } = "";

	public required MonsterAnimationAssetRef Texture { get; init; }

	public IReadOnlyList<MonsterAnimationAssetRef> Textures { get; init; } = Array.Empty<MonsterAnimationAssetRef>();

	public required MonsterAnimationAssetRef Atlas { get; init; }

	public required MonsterAnimationAssetRef Skeleton { get; init; }

	public string Key => Region + "/" + Tier + ((Scale.Length > 0) ? ("/" + Scale) : "");

	[CompilerGenerated]
	[SetsRequiredMembers]
	private MonsterAnimationAssetTriplet(MonsterAnimationAssetTriplet original)
	{
		Region = original.Region;
		Tier = original.Tier;
		Scale = original.Scale;
		Texture = original.Texture;
		Textures = original.Textures;
		Atlas = original.Atlas;
		Skeleton = original.Skeleton;
	}

	public MonsterAnimationAssetTriplet()
	{
	}
}
