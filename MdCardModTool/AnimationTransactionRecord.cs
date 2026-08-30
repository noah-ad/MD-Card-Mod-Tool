using System;
using System.Collections.Generic;

namespace MdCardModTool;

public enum AnimationBundleState
{
	Created,
	BackedUp
}

public sealed record AnimationTransactionRecord
{
	public int FormatVersion { get; init; } = 1;

	public required string CardId { get; init; }

	public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

	public List<AnimationTransactionFile> Files { get; init; } = [];
}

public sealed record AnimationTransactionFile
{
	public required string RelativePath { get; init; }

	public required AnimationBundleState State { get; init; }

	public string BackupPath { get; init; } = "";
}
