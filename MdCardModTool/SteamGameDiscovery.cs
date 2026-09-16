using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MdCardModTool;

public static class SteamGameDiscovery
{
	public const string AppId = "1449850";

	private static readonly Regex QuotedPair = new Regex("\\\"(?<key>[^\\\"]+)\\\"\\s+\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Compiled);

	public static IReadOnlyList<GameInstallation> Discover(string? manualFallback = null)
	{
		Dictionary<string, GameInstallation> dictionary = new Dictionary<string, GameInstallation>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in SteamRoots())
		{
			foreach (string item2 in LibraryRoots(item))
			{
				string path = Path.Combine(item2, "steamapps", "appmanifest_1449850.acf");
				if (!File.Exists(path))
				{
					continue;
				}
				Dictionary<string, string> dictionary2 = ParsePairs(File.ReadAllText(path));
				if (dictionary2.TryGetValue("installdir", out var value) && !string.IsNullOrWhiteSpace(value))
				{
					string fullPath = Path.GetFullPath(Path.Combine(item2, "steamapps", "common", value));
					if (IsGameRoot(fullPath))
					{
						dictionary[fullPath] = Build(fullPath, item, item2, dictionary2.GetValueOrDefault("buildid", ""));
					}
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(manualFallback))
		{
			string fullPath2 = Path.GetFullPath(manualFallback);
			if (IsGameRoot(fullPath2) && !dictionary.ContainsKey(fullPath2))
			{
				dictionary[fullPath2] = Build(fullPath2, "", "", ReadBuildId(fullPath2));
			}
		}
		return dictionary.Values.OrderByDescending((GameInstallation x) => x.Profiles.FirstOrDefault()?.LastWriteTimeUtc ?? DateTime.MinValue).ToArray();
	}

	public static GameInstallation FromPath(string gameRoot)
	{
		string full = Path.GetFullPath(gameRoot);
		string text = StandaloneResourceService.ResolveRoot(full);
		if (text != null)
		{
			return ResourceSource.Mobile(text);
		}
		if (!IsGameRoot(full))
		{
			throw new DirectoryNotFoundException("所选目录不是有效的 Master Duel 安装目录。需要 masterduel.exe 与 LocalData。\n" + full);
		}
		return Discover(full).FirstOrDefault((GameInstallation x) => string.Equals(x.GameRoot, full, StringComparison.OrdinalIgnoreCase)) ?? Build(full, "", "", ReadBuildId(full));
	}

	public static IReadOnlyList<LocalDataProfile> EnumerateProfiles(string gameRoot)
	{
		string path = Path.Combine(gameRoot, "LocalData");
		if (!Directory.Exists(path))
		{
			return Array.Empty<LocalDataProfile>();
		}
		return (from account in Directory.EnumerateDirectories(path)
			select new
			{
				Account = account,
				Data = Path.Combine(account, "0000")
			} into x
			where Directory.Exists(x.Data)
			select new LocalDataProfile
			{
				AccountId = Path.GetFileName(x.Account),
				RootPath = Path.GetFullPath(x.Data),
				LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(x.Data)
			} into x
			orderby x.LastWriteTimeUtc descending
			select x).ToArray();
	}

	public static bool IsGameRoot(string path)
	{
		if (Directory.Exists(path) && File.Exists(Path.Combine(path, "masterduel.exe")))
		{
			return Directory.Exists(Path.Combine(path, "LocalData"));
		}
		return false;
	}

	private static GameInstallation Build(string gameRoot, string steamRoot, string libraryRoot, string buildId)
	{
		return new GameInstallation
		{
			GameRoot = gameRoot,
			SteamRoot = steamRoot,
			LibraryRoot = libraryRoot,
			BuildId = buildId,
			Profiles = EnumerateProfiles(gameRoot)
		};
	}

	private static IEnumerable<string> SteamRoots()
	{
		HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		(RegistryHive, RegistryView, string, string)[] array = new(RegistryHive, RegistryView, string, string)[4]
		{
			(RegistryHive.CurrentUser, RegistryView.Registry64, "Software\\Valve\\Steam", "SteamPath"),
			(RegistryHive.CurrentUser, RegistryView.Registry32, "Software\\Valve\\Steam", "SteamPath"),
			(RegistryHive.LocalMachine, RegistryView.Registry64, "Software\\WOW6432Node\\Valve\\Steam", "InstallPath"),
			(RegistryHive.LocalMachine, RegistryView.Registry32, "Software\\Valve\\Steam", "InstallPath")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (hKey, view, name, name2) = array[i];
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey(hKey, view);
				using RegistryKey registryKey2 = registryKey.OpenSubKey(name);
				Add(registryKey2?.GetValue(name2) as string);
			}
			catch
			{
			}
		}
		return roots;
		void Add(string? value)
		{
			if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
			{
				roots.Add(Path.GetFullPath(value));
			}
		}
	}

	private static IEnumerable<string> LibraryRoots(string steamRoot)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
		string path = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
		if (File.Exists(path))
		{
			foreach (Match item in QuotedPair.Matches(File.ReadAllText(path)))
			{
				if (string.Equals(item.Groups["key"].Value, "path", StringComparison.OrdinalIgnoreCase))
				{
					string path2 = Unescape(item.Groups["value"].Value);
					if (Directory.Exists(path2))
					{
						hashSet.Add(Path.GetFullPath(path2));
					}
				}
			}
		}
		return hashSet;
	}

	private static Dictionary<string, string> ParsePairs(string text)
	{
		return QuotedPair.Matches(text).Cast<Match>().GroupBy<Match, string>((Match x) => x.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
			.ToDictionary<IGrouping<string, Match>, string, string>((IGrouping<string, Match> x) => x.Key, (IGrouping<string, Match> x) => Unescape(x.Last().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
	}

	private static string Unescape(string value)
	{
		return value.Replace("\\\\", "\\").Replace("\\\"", "\"");
	}

	private static string ReadBuildId(string gameRoot)
	{
		for (DirectoryInfo directoryInfo = new DirectoryInfo(gameRoot); directoryInfo != null; directoryInfo = directoryInfo.Parent)
		{
			string path = Path.Combine(directoryInfo.FullName, "steamapps", "appmanifest_1449850.acf");
			if (File.Exists(path))
			{
				return ParsePairs(File.ReadAllText(path)).GetValueOrDefault("buildid", "");
			}
		}
		return "";
	}
}
