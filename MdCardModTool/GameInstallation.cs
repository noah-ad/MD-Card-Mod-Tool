using System.Collections.Generic;

namespace MdCardModTool;

public sealed record GameInstallation
{
	public required string GameRoot { get; init; }

	public string SteamRoot { get; init; } = "";

	public string LibraryRoot { get; init; } = "";

	public string BuildId { get; init; } = "";

	public IReadOnlyList<LocalDataProfile> Profiles { get; init; } = [];
}
