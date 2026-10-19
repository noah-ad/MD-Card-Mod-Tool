using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MdCardModTool;

public sealed class PortableMonsterAnimationIndex
{
	[JsonPropertyName("v")]
	public int FormatVersion { get; init; } = 1;

	[JsonPropertyName("b")]
	public string GameBuildId { get; init; } = "";

	[JsonPropertyName("a")]
	public List<MonsterAnimationAssetRef> Assets { get; init; } = new List<MonsterAnimationAssetRef>();

	[JsonPropertyName("o")]
	public List<string> OfficialCardIds { get; init; } = new List<string>();

	[JsonPropertyName("l")]
	public int ResolverVersion { get; init; }
}
