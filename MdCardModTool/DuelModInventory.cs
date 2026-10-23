using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MdCardModTool;

internal sealed record DuelManagedMod(string Name, string Kind, int ModelId, IReadOnlyList<DuelBundleSlot> Slots, bool Verified)
{
	internal TexRef Row(string root)
	{
		string? local = IndexService.FindLocalRoot(root);
		bool isLocal = local != null && StandaloneResourceService.IsInside(Slots[0].BundlePath, local);
		return new TexRef
		{
			BundlePath = Slots[0].BundlePath, RelativeBundlePath = Path.GetRelativePath(isLocal ? local! : root, Slots[0].BundlePath),
			SourceKind = isLocal ? "视觉资源" : "基础视觉资源", Category = Kind,
			Name = Name + (Verified ? "" : "（校验异常，禁止自动覆盖）"), IsModded = true,
			ModModifiedAtUtc = Slots.Max(x => File.GetLastWriteTimeUtc(x.BundlePath)), ManagedDuel = this
		};
	}
	internal bool Contains(TexRef texture) => Slots.Any(x => x.BundlePath.Equals(texture.BundlePath, StringComparison.OrdinalIgnoreCase))
		|| Kind == "场地模型" && texture.Category == "场地预览" && DuelResourceCatalog.FieldId(texture.Name) + 1090000 == DuelResourceCatalog.ModelItemId(ModelId);
}

internal static class DuelModInventory
{
	internal static IReadOnlyList<DuelManagedMod> Read(string root)
	{
		string directory = Path.Combine(root, "_MD卡图备份", "场地与音乐");
		if (!Directory.Exists(directory)) return Array.Empty<DuelManagedMod>();
		DuelResourceCatalog.LoadNames(root);
		var changed = new List<(DuelBundleSlot Slot, bool Verified)>();
		foreach (string file in Directory.EnumerateFiles(directory, "*.bundle.json"))
		{
			var record = JsonSerializer.Deserialize<DuelResourceService.Record>(File.ReadAllText(file)) ?? throw new InvalidDataException("场地／音乐记录为空：" + file);
			DuelBundleSlot slot;
			try { slot = DuelResourceService.ManagedSlot(root, record.Container); }
			catch (FileNotFoundException) { continue; }
			// Only current account records may become actionable rows.
			if (!Path.GetFullPath(record.Target).Equals(Path.GetFullPath(slot.BundlePath), StringComparison.OrdinalIgnoreCase)
				|| !Path.GetFullPath(file).Equals(Path.GetFullPath(DuelResourceService.BackupPath(root, slot) + ".json"), StringComparison.OrdinalIgnoreCase)) continue;
			string state = DuelResourceService.State(root, slot);
			if (state != "original") changed.Add((slot, state == "applied"));
		}
		return changed.GroupBy(x => x.Slot.Key == "music" ? x.Slot.LogicalPath : x.Slot.LogicalPath.Split('/')[3])
			.Select(group =>
			{
				var slots = group.Select(x => x.Slot).OrderBy(x => x.Key).ToArray();
				bool music = slots[0].Key == "music";
				int model = music ? 0 : DuelResourceCatalog.FieldId(group.Key);
				return new DuelManagedMod(music ? DuelResourceCatalog.DescribeMusic(slots[0]).ToString() : $"Mat_{model:000} · {DuelResourceCatalog.ModelName(model)}",
					music ? "背景音乐" : "场地模型", model, slots, group.All(x => x.Verified));
			}).ToArray();
	}
}
