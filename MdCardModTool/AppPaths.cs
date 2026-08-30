using System;
using System.Collections.Generic;
using System.IO;

namespace MdCardModTool;

/// <summary>
/// Stable application layout. Published builds keep the launcher at the root
/// and all external payload beside it under <c>data</c>. Legacy and source-tree
/// candidates remain read-only fallbacks for developer builds.
/// </summary>
public static class AppPaths
{
	public static string ApplicationRoot => AppContext.BaseDirectory;

	public static string DataRoot => Path.Combine(ApplicationRoot, "data");

	public static string Data(params string[] segments) => Combine(DataRoot, segments);

	public static string ResolveFile(params string[] segments)
	{
		foreach (string candidate in CandidatePaths(segments))
		{
			if (File.Exists(candidate)) return candidate;
		}
		return Data(segments);
	}

	public static string? ResolveDirectory(params string[] segments)
	{
		foreach (string candidate in CandidatePaths(segments))
		{
			if (Directory.Exists(candidate)) return candidate;
		}
		return null;
	}

	public static IEnumerable<string> CandidatePaths(params string[] segments)
	{
		yield return Data(segments);
		yield return Combine(ApplicationRoot, segments);
		yield return Combine(Path.Combine(ApplicationRoot, "Resources"), segments);
	}

	private static string Combine(string root, IReadOnlyList<string> segments)
	{
		string path = root;
		for (int index = 0; index < segments.Count; index++)
		{
			path = Path.Combine(path, segments[index]);
		}
		return path;
	}
}
