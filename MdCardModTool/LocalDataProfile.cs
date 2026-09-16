using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record LocalDataProfile
{
	public required string AccountId { get; init; }

	public required string RootPath { get; init; }

	public DateTime LastWriteTimeUtc { get; init; }

	public string DisplayName => $"{AccountId}  ·  {LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

	public override string ToString()
	{
		return DisplayName;
	}

	[CompilerGenerated]
	[SetsRequiredMembers]
	private LocalDataProfile(LocalDataProfile original)
	{
		AccountId = original.AccountId;
		RootPath = original.RootPath;
		LastWriteTimeUtc = original.LastWriteTimeUtc;
	}

	public LocalDataProfile()
	{
	}
}
