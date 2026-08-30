using System.Text.Json.Serialization;

namespace MdCardModTool;

public sealed class PortableTextureEntry
{
	[JsonPropertyName("r")]
	public string RelativeBundlePath { get; init; } = "";

	[JsonPropertyName("p")]
	public long PathId { get; init; }

	[JsonPropertyName("f")]
	public string AssetFileName { get; init; } = "";

	[JsonPropertyName("n")]
	public string Name { get; init; } = "";

	[JsonPropertyName("w")]
	public int Width { get; init; }

	[JsonPropertyName("h")]
	public int Height { get; init; }

	[JsonPropertyName("c")]
	public string Category { get; init; } = "其他贴图";

	[JsonPropertyName("a")]
	public bool IsAlternateArt { get; init; }

	[JsonPropertyName("m")]
	public bool IsTokenOrMisc { get; init; }

	[JsonPropertyName("s")]
	public string SourceKind { get; init; } = "";

	[JsonPropertyName("k")]
	public string CardKey { get; init; } = "";
}
