using System;
using System.Collections.Generic;
using System.IO;

namespace MdCardModTool;

public static class AppPaths
{
	public static string ApplicationRoot => AppContext.BaseDirectory;

	public static string DataRoot => Path.Combine(ApplicationRoot, "data");

	public static string Data(params string[] segments)
	{
		return Combine(DataRoot, segments);
	}

	public static string ResolveFile(params string[] segments)
	{
		foreach (string item in CandidatePaths(segments))
		{
			if (File.Exists(item))
			{
				return item;
			}
		}
		return Data(segments);
	}

	public static string? ResolveDirectory(params string[] segments)
	{
		foreach (string item in CandidatePaths(segments))
		{
			if (Directory.Exists(item))
			{
				return item;
			}
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
		string text = root;
		for (int i = 0; i < segments.Count; i++)
		{
			text = Path.Combine(text, segments[i]);
		}
		return text;
	}
}
