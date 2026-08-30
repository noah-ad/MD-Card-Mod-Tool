using System;

namespace MdCardModTool;

public sealed record LocalDataProfile
{
	public required string AccountId { get; init; }

	public required string RootPath { get; init; }

	public DateTime LastWriteTimeUtc { get; init; }

	public string DisplayName => $"{AccountId}  ·  {LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

	public override string ToString() => DisplayName;
}
