namespace MdCardModTool;

public sealed record OverFrameMapping(ushort CardId, ushort ArtId)
{
	public bool UsesOwnArt => CardId == ArtId;
}
