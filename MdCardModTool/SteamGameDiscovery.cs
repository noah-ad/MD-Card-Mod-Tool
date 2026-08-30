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

	private static readonly Regex QuotedPair = new("\\\"(?<key>[^\\\"]+)\\\"\\s+\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.Compiled);

	public static IReadOnlyList<GameInstallation> Discover(string? manualFallback = null)
	{
		Dictionary<string, GameInstallation> found = new(StringComparer.OrdinalIgnoreCase);
		foreach (string steamRoot in SteamRoots())
		{
			foreach (string library in LibraryRoots(steamRoot))
			{
				string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
				if (!File.Exists(manifest))
				{
					continue;
				}
				Dictionary<string, string> values = ParsePairs(File.ReadAllText(manifest));
				if (!values.TryGetValue("installdir", out string? installDir) || string.IsNullOrWhiteSpace(installDir))
				{
					continue;
				}
				string gameRoot = Path.GetFullPath(Path.Combine(library, "steamapps", "common", installDir));
				if (IsGameRoot(gameRoot))
				{
					found[gameRoot] = Build(gameRoot, steamRoot, library, values.GetValueOrDefault("buildid", ""));
				}
			}
		}
		if (!string.IsNullOrWhiteSpace(manualFallback))
		{
			string full = Path.GetFullPath(manualFallback);
			if (IsGameRoot(full) && !found.ContainsKey(full))
			{
				found[full] = Build(full, "", "", ReadBuildId(full));
			}
		}
		return found.Values.OrderByDescending(x => x.Profiles.FirstOrDefault()?.LastWriteTimeUtc ?? DateTime.MinValue).ToArray();
	}

	public static GameInstallation FromPath(string gameRoot)
	{
		string full = Path.GetFullPath(gameRoot);
		if (!IsGameRoot(full))
		{
			throw new DirectoryNotFoundException("所选目录不是有效的 Master Duel 安装目录。需要 masterduel.exe 与 LocalData。\n" + full);
		}
		return Discover(full).FirstOrDefault(x => string.Equals(x.GameRoot, full, StringComparison.OrdinalIgnoreCase))
			?? Build(full, "", "", ReadBuildId(full));
	}

	public static IReadOnlyList<LocalDataProfile> EnumerateProfiles(string gameRoot)
	{
		string root = Path.Combine(gameRoot, "LocalData");
		if (!Directory.Exists(root))
		{
			return [];
		}
		return Directory.EnumerateDirectories(root)
			.Select(account => new { Account = account, Data = Path.Combine(account, "0000") })
			.Where(x => Directory.Exists(x.Data))
			.Select(x => new LocalDataProfile
			{
				AccountId = Path.GetFileName(x.Account),
				RootPath = Path.GetFullPath(x.Data),
				LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(x.Data)
			})
			.OrderByDescending(x => x.LastWriteTimeUtc)
			.ToArray();
	}

	public static bool IsGameRoot(string path)
	{
		return Directory.Exists(path)
			&& File.Exists(Path.Combine(path, "masterduel.exe"))
			&& Directory.Exists(Path.Combine(path, "LocalData"));
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
		HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
		void Add(string? value)
		{
			if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
			{
				roots.Add(Path.GetFullPath(value));
			}
		}
		foreach ((RegistryHive hive, RegistryView view, string key, string name) in new[]
		{
			(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Valve\Steam", "SteamPath"),
			(RegistryHive.CurrentUser, RegistryView.Registry32, @"Software\Valve\Steam", "SteamPath"),
			(RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\WOW6432Node\Valve\Steam", "InstallPath"),
			(RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath")
		})
		{
			try
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
				using RegistryKey? subKey = baseKey.OpenSubKey(key);
				Add(subKey?.GetValue(name) as string);
			}
			catch
			{
			}
		}
		return roots;
	}

	private static IEnumerable<string> LibraryRoots(string steamRoot)
	{
		HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase) { steamRoot };
		string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
		if (File.Exists(vdf))
		{
			foreach (Match match in QuotedPair.Matches(File.ReadAllText(vdf)))
			{
				if (string.Equals(match.Groups["key"].Value, "path", StringComparison.OrdinalIgnoreCase))
				{
					string path = Unescape(match.Groups["value"].Value);
					if (Directory.Exists(path))
					{
						roots.Add(Path.GetFullPath(path));
					}
				}
			}
		}
		return roots;
	}

	private static Dictionary<string, string> ParsePairs(string text)
	{
		return QuotedPair.Matches(text).Cast<Match>()
			.GroupBy(x => x.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => Unescape(x.Last().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
	}

	private static string Unescape(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");

	private static string ReadBuildId(string gameRoot)
	{
		DirectoryInfo? current = new(gameRoot);
		while (current != null)
		{
			string manifest = Path.Combine(current.FullName, "steamapps", $"appmanifest_{AppId}.acf");
			if (File.Exists(manifest))
			{
				return ParsePairs(File.ReadAllText(manifest)).GetValueOrDefault("buildid", "");
			}
			current = current.Parent;
		}
		return "";
	}
}
