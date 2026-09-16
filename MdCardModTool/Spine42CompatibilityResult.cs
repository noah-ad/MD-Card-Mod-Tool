using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MdCardModTool;

public sealed record Spine42CompatibilityResult
{
	public required string CardId { get; init; }

	public bool Success { get; init; }

	public string PairKey { get; init; } = "";

	public string AnimationName { get; init; } = "";

	public int FrameWidth { get; init; }

	public int FrameHeight { get; init; }

	public int OpaquePixels { get; init; }

	public IReadOnlyList<string> UnsupportedFeatures { get; init; } = Array.Empty<string>();

	public string Message { get; init; } = "";

	[CompilerGenerated]
	[SetsRequiredMembers]
	private Spine42CompatibilityResult(Spine42CompatibilityResult original)
	{
		CardId = original.CardId;
		Success = original.Success;
		PairKey = original.PairKey;
		AnimationName = original.AnimationName;
		FrameWidth = original.FrameWidth;
		FrameHeight = original.FrameHeight;
		OpaquePixels = original.OpaquePixels;
		UnsupportedFeatures = original.UnsupportedFeatures;
		Message = original.Message;
	}

	public Spine42CompatibilityResult()
	{
	}
}
