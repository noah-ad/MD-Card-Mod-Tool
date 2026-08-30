using System.Text.Json.Serialization;

namespace MdCardModTool;

public sealed class MonsterAnimationAssetRef
{
	[JsonIgnore]
	public string BundlePath { get; init; } = "";

	[JsonPropertyName("r")]
	public string RelativeBundlePath { get; init; } = "";

	[JsonPropertyName("f")]
	public string AssetFileName { get; init; } = "";

	[JsonPropertyName("p")]
	public long PathId { get; init; }

	[JsonPropertyName("n")]
	public string Name { get; init; } = "";

	[JsonPropertyName("c")]
	public string CardId { get; init; } = "";

	[JsonPropertyName("k")]
	public MonsterAnimationAssetKind Kind { get; init; }

	[JsonPropertyName("s")]
	public string StorageKind { get; init; } = "LocalData";

	[JsonIgnore]
	public string ModSourceKind
	{
		get
		{
			if (!(StorageKind == "StreamingAssets"))
			{
				return "召唤动画";
			}
			return "召唤动画-游戏内";
		}
	}

	public TexRef AsTexture()
	{
		return new TexRef
		{
			BundlePath = BundlePath,
			RelativeBundlePath = RelativeBundlePath,
			AssetFileName = AssetFileName,
			PathId = PathId,
			Name = Name,
			SourceKind = ModSourceKind,
			Category = "怪兽动画图集",
			CardKey = CardId
		};
	}
}
