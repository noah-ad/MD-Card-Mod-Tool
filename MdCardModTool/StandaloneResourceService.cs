using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

internal static class StandaloneResourceService
{
	internal const string SourceKind = "手机 / 独立 0000";

	internal static string? ResolveRoot(string path)
	{
		if (!Directory.Exists(path))
		{
			return null;
		}
		string text = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		string text2 = ((Path.GetFileName(text) == "0000") ? text : Path.Combine(text, "0000"));
		if (!Directory.Exists(text2))
		{
			return null;
		}
		if (!Directory.EnumerateDirectories(text2).Any((string d) => IsHex(Path.GetFileName(d), 2)))
		{
			return null;
		}
		return text2;
	}

	internal static IEnumerable<string> EnumerateBundles(string root)
	{
		return from f in (from d in Directory.EnumerateDirectories(root)
				where IsHex(Path.GetFileName(d), 2) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0
				select d).SelectMany((string d) => Directory.EnumerateFiles(d))
			where IsHex(Path.GetFileName(f), 8) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0
			select f;
	}

	private static bool IsHex(string value, int count)
	{
		if (value.Length == count)
		{
			return value.All(char.IsAsciiHexDigit);
		}
		return false;
	}

	internal static List<TexRef> ReadBundle(string file, string root)
	{
		return new ModEngine().ScanBundle(file, root, "手机 / 独立 0000", includeDependencies: false).Textures;
	}

	internal static void Scan(string root, Action<IReadOnlyList<TexRef>, int, int, string?> report, CancellationToken token)
	{
		string[] files = EnumerateBundles(root).ToArray();
		int done = 0;
		object gate = new object();
		Parallel.ForEach(files, new ParallelOptions
		{
			MaxDegreeOfParallelism = 2,
			CancellationToken = token
		}, delegate(string file)
		{
			token.ThrowIfCancellationRequested();
			List<TexRef> arg = new List<TexRef>();
			string arg2 = null;
			try
			{
				using (FileStream fileStream = File.OpenRead(file))
				{
					Span<byte> buffer = stackalloc byte[8];
					int num = fileStream.Read(buffer);
					if (num < 7 || !Encoding.ASCII.GetString(buffer.Slice(0, num)).StartsWith("Unity", StringComparison.Ordinal))
					{
						lock (gate)
						{
							report(arg, ++done, files.Length, null);
							return;
						}
					}
				}
				arg = ReadBundle(file, root);
			}
			catch (Exception ex)
			{
				arg2 = Path.GetRelativePath(root, file) + ": " + ex.Message;
			}
			lock (gate)
			{
				report(arg, ++done, files.Length, arg2);
			}
		});
	}

	internal static byte[] Decode(TexRef texture, int maxSize = 0)
	{
		return new ModEngine().DecodePng(texture, maxSize);
	}

	internal static bool IsInside(string file, string root)
	{
		string relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
		if (!Path.IsPathRooted(relativePath) && relativePath != "..")
		{
			return !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
		}
		return false;
	}
}
