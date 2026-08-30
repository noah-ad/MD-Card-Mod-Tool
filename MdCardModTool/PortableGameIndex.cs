using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MdCardModTool;

public sealed class PortableGameIndex
{
	[JsonPropertyName("v")]
	public int FormatVersion { get; init; } = 1;

	[JsonPropertyName("b")]
	public string GameBuildId { get; init; } = "";

	[JsonPropertyName("a")]
	public int AlternateArtIndexVersion { get; init; }

	[JsonPropertyName("t")]
	public List<PortableTextureEntry> Textures { get; init; } = new List<PortableTextureEntry>();
}
