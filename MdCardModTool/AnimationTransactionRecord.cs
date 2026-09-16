using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record AnimationTransactionRecord
{
	public int FormatVersion { get; init; } = 1;

	public required string CardId { get; init; }

	public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

	public List<AnimationTransactionFile> Files { get; init; } = new List<AnimationTransactionFile>();

	[CompilerGenerated]
	[SetsRequiredMembers]
	private AnimationTransactionRecord(AnimationTransactionRecord original)
	{
		FormatVersion = original.FormatVersion;
		CardId = original.CardId;
		CreatedUtc = original.CreatedUtc;
		Files = original.Files;
	}

	public AnimationTransactionRecord()
	{
	}
}
