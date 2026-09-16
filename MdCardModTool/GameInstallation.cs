using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record GameInstallation
{
	public required string GameRoot { get; init; }

	public string SteamRoot { get; init; } = "";

	public string LibraryRoot { get; init; } = "";

	public string BuildId { get; init; } = "";

	public IReadOnlyList<LocalDataProfile> Profiles { get; init; } = Array.Empty<LocalDataProfile>();

	[CompilerGenerated]
	[SetsRequiredMembers]
	private GameInstallation(GameInstallation original)
	{
		GameRoot = original.GameRoot;
		SteamRoot = original.SteamRoot;
		LibraryRoot = original.LibraryRoot;
		BuildId = original.BuildId;
		Profiles = original.Profiles;
	}

	public GameInstallation()
	{
	}
}
