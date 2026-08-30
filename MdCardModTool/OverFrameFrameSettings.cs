namespace MdCardModTool;

public sealed record OverFrameFrameSettings(
	string FrameKey = "card_frame01",
	bool UsesCustomFrame = false,
	bool UserSelected = false,
	string CompositionMode = "AstellarTransparent",
	float ArtImageScale = 0f,
	float ArtOffsetX = 0f,
	float ArtOffsetY = 0f);
