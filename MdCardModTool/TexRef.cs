using System.Text.Json.Serialization;

namespace MdCardModTool;

public sealed class TexRef
{
	public required string BundlePath { get; set; }

	public required string RelativeBundlePath { get; set; }

	public long PathId { get; set; }

	public string AssetFileName { get; set; } = "";

	public string Name { get; init; } = "";

	public int Width { get; set; }

	public int Height { get; set; }

	public string Category { get; set; } = "其他贴图";

	public bool IsAlternateArt { get; set; }

	public bool IsTokenOrMisc { get; set; }

	public bool IsModded { get; set; }

	[JsonIgnore]
	public bool HasMonsterAnimation { get; set; }

	public string SourceKind { get; init; } = "";

	public string CardKey { get; init; } = "";

	public string PreviewFrameKey { get; set; } = "";

	public string? OverrideBundlePath { get; set; }

	public string ActiveBundlePath => OverrideBundlePath ?? BundlePath;

	public override string ToString()
	{
		return $"{Name}  ·  {Width}×{Height}";
	}
}
