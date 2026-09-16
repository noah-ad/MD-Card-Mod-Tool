namespace MdCardModTool;

public sealed record MonsterAnimationValidationResult(string CardId, string Region, int BundleCount, int HdAtlasWidth, int HdAtlasHeight, int SdAtlasWidth, int SdAtlasHeight, string RenderPair);
