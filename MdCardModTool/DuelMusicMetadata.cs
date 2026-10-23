using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MdCardModTool;

internal static class DuelMusicMetadata
{
	private static readonly IReadOnlyDictionary<string, int[]> Fields = File.ReadAllLines(AppPaths.ResolveFile("bgm-fields-v1.tsv"))
		.Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
		.Select(x => x.Split('\t')).ToDictionary(x => x[0], x => x[1].Split(',').Select(int.Parse).ToArray());

	internal static DuelMusic Describe(string name, DuelBundleSlot slot, IReadOnlyDictionary<int, string> names)
	{
		string group = "", phase = "";
		var standard = Regex.Match(name, @"\ABGM_DUEL_(NORMAL|KEYCARD|CLIMAX)_(\d+)\z");
		var special = Regex.Match(name, @"\ABGM_DUEL_((?:EX_\d+|F\d+|DC\d+|RATE\d+))_(NORMAL|KEYCARD|CLIMAX|PHASE_[A-Z]|ALL)\z");
		if (standard.Success) { group = "DUEL_" + standard.Groups[2].Value; phase = standard.Groups[1].Value; }
		else if (special.Success) { group = "DUEL_" + special.Groups[1].Value; phase = special.Groups[2].Value; }
		string stage = phase switch
		{
			"NORMAL" => "通常", "KEYCARD" => "关键卡", "CLIMAX" => "高潮", "ALL" => "全程",
			_ when phase.StartsWith("PHASE_", StringComparison.Ordinal) => "阶段 " + phase[6..], _ => ""
		};
		string annotation;
		if (Fields.TryGetValue(group, out int[]? ids))
		{
			annotation = string.Join(" / ", ids.Select(id => names.TryGetValue(id - 1090000, out string? value)
				? value.Split(" / ")[0] : "场地 " + id));
			annotation += "；" + stage + (ids.Length > 1 ? "；共用曲目" : "");
		}
		else annotation = (name.StartsWith("BGM_MENU", StringComparison.Ordinal) ? "菜单音乐"
			: name.StartsWith("BGM_SOLO", StringComparison.Ordinal) ? "单人模式音乐"
			: name.Contains("TUTORIAL", StringComparison.Ordinal) ? "教程音乐" : "未确认场地绑定")
			+ (stage.Length > 0 ? "；" + stage : "");
		return new(name, slot, annotation, group, phase);
	}

	internal static (DuelMusic Target, DuelMusic Source)[] PlanGroup(IReadOnlyList<DuelMusic> music, DuelMusic target, DuelMusic source)
	{
		if (target.Group.Length == 0 || source.Group.Length == 0 || target.Group == source.Group)
			throw new InvalidOperationException(Localizer.T("duel.groupUnavailable"));
		var targets = music.Where(x => x.Group == target.Group).OrderBy(x => x.Phase).ToArray();
		var sources = music.Where(x => x.Group == source.Group).OrderBy(x => x.Phase).ToArray();
		var known = File.ReadAllLines(AppPaths.ResolveFile("bgm-catalog-v1.txt"))
			.Select(name => Describe(name, target.Slot, new Dictionary<int, string>())).ToArray();
		if (targets.Length < 2 || targets.Length != known.Count(x => x.Group == target.Group)
			|| sources.Length != known.Count(x => x.Group == source.Group)
			|| !targets.Select(x => x.Phase).SequenceEqual(sources.Select(x => x.Phase)))
			throw new InvalidOperationException(Localizer.T("duel.groupUnavailable"));
		return targets.Zip(sources, (t, s) => (t, s)).ToArray();
	}
}
