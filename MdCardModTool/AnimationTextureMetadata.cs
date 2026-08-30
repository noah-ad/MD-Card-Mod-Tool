namespace MdCardModTool;

public sealed record AnimationTextureMetadata(
	int Width,
	int Height,
	int TextureFormat,
	int ColorSpace,
	int MipCount,
	long CompleteImageSize,
	long StreamSize,
	string StreamPath,
	int InlineDataSize);
