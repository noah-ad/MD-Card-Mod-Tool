using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record AnimationTransactionFile
{
	public required string RelativePath { get; init; }

	public required AnimationBundleState State { get; init; }

	public string BackupPath { get; init; } = "";

	[CompilerGenerated]
	[SetsRequiredMembers]
	private AnimationTransactionFile(AnimationTransactionFile original)
	{
		RelativePath = original.RelativePath;
		State = original.State;
		BackupPath = original.BackupPath;
	}

	public AnimationTransactionFile()
	{
	}
}
