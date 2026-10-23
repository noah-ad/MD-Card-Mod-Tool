using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MdCardModTool;

internal sealed record DuelBundleSlot(string Key, string LogicalPath, string BundlePath)
{
	internal string Container => "assets/resourcesassetbundle/" + LogicalPath.ToLowerInvariant() + (Key == "music" ? ".wav" : ".prefab");
}

internal sealed record DuelField(int Id, string Names, IReadOnlyList<DuelBundleSlot> Models, TexRef? Preview, int ItemId = 0)
{
	public override string ToString() => $"{Id:000} · {Names} · {Models.Count} " + Localizer.T("duel.variants");
	internal bool Matches(string query) => $"{Id:000} {ItemId} {Names} Mat_{Id:000}".Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal sealed record DuelMusic(string Name, DuelBundleSlot Slot, string Annotation = "", string Group = "", string Phase = "")
{
	public override string ToString() => string.IsNullOrEmpty(Annotation) ? Name : $"{Name}（{Annotation}）";
	internal bool Matches(string query) => ToString().Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal static class DuelResourceCatalog
{
	private static IReadOnlyDictionary<int, string> _fieldNames = new Dictionary<int, string>();
	private static IReadOnlyDictionary<int, int> _modelItems = new Dictionary<int, int>();
	internal static int ModelItemId(int model) => _modelItems.GetValueOrDefault(model);
	internal static string ModelName(int model) => _fieldNames.TryGetValue(ModelItemId(model) - 1090000, out string? name) ? name : $"Mat_{model:000}";
	internal static DuelMusic DescribeMusic(DuelBundleSlot slot) => DuelMusicMetadata.Describe(Path.GetFileName(slot.LogicalPath), slot, _fieldNames);
	internal static string? Locate(string gameRoot, string logical)
	{
		string relative = IndexService.ResourceBundleRelativePath(logical);
		return new[] { IndexService.FindLocalRoot(gameRoot), IndexService.StreamingRoot(gameRoot) }
			.Where(x => x != null).Select(x => Path.Combine(x!, relative)).FirstOrDefault(File.Exists);
	}
	internal static int FieldId(string name)
	{
		var match = Regex.Match(name, @"^(?:Mat_(?<id>\d{3})(?:_|$)|FieldIcon109(?<id>\d{4})$)", RegexOptions.IgnoreCase);
		return match.Success && int.TryParse(match.Groups["id"].Value, out int id) ? id : -1;
	}
	internal static string SearchNames(TexRef texture)
	{
		int id = FieldId(texture.Name);
		if (texture.Category == "决斗场地") id = _modelItems.TryGetValue(id, out int item) ? item - 1090000 : -1;
		return texture.Category is "决斗场地" or "场地预览" && _fieldNames.TryGetValue(id, out var value) ? value : "";
	}
	internal static bool MatchesFieldId(TexRef texture, int number)
	{
		int id = FieldId(texture.Name);
		if (id < 0) return false;
		int item = texture.Category == "场地预览" ? 1090000 + id
			: _modelItems.TryGetValue(id, out int mapped) ? mapped : 0;
		return number >= 1090000 ? number == item : number == id || item != 0 && number == item - 1090000;
	}

	internal static void LoadNames(string gameRoot)
	{
		var names = new Dictionary<int, List<string>>();
		foreach (string language in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
		{
			string? bundle = Locate(gameRoot, "Texts/" + language + "/IDS_ITEM");
			if (bundle == null) continue;
			try
			{
				var dictionary = ReadStringMap(GameCardCatalogUpdater.ReadDictionary(bundle, "IDS_ITEM"));
				for (int id = 1; id <= 5999; id++)
				{
					string key = Path.GetFileName(IndexService.ResourceBundleRelativePath("ID" + (1090000 + id)));
					if (!dictionary.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value)) continue;
					if (!names.TryGetValue(id, out var list)) names[id] = list = new();
					if (!list.Contains(value)) list.Add(value);
				}
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
		}
		_fieldNames = names.ToDictionary(x => x.Key, x => string.Join(" / ", x.Value));
		var models = new Dictionary<int, int>();
		if (Locate(gameRoot, "Duel/ScriptableObject/Bg/BgMatModelSetting") is { } setting)
		{
			using var document = new DuelBundleDocument(setting);
			var root = document.Field(document.Assets.file.GetAssetInfo(document.RootId));
			foreach (var row in root["infoList"]["Array"].Children)
				if (int.TryParse(row["modelName"].AsString, out int model)) models.Add(model, row["id"].AsInt);
		}
		_modelItems = models;
	}

	// The game's item dictionary is a MessagePack map of UTF-8 strings.
	internal static Dictionary<string, string> ReadStringMap(byte[] data)
	{
		using var input = new MemoryStream(data, false);
		int Byte() { int b = input.ReadByte(); return b < 0 ? throw new EndOfStreamException() : b; }
		int UInt(int count) { int value = 0; for (int i = 0; i < count; i++) value = checked(value * 256 + Byte()); return value; }
		string String()
		{
			int marker = Byte();
			int length = (marker & 0xe0) == 0xa0 ? marker & 31 : marker switch
			{ 0xd9 => UInt(1), 0xda => UInt(2), 0xdb => UInt(4), _ => throw new InvalidDataException("未知的名称字典格式。") };
			if (length > 1024 * 1024 || length > input.Length - input.Position) throw new InvalidDataException("名称字典长度异常。");
			byte[] bytes = new byte[length]; input.ReadExactly(bytes);
			return new UTF8Encoding(false, true).GetString(bytes);
		}
		int head = Byte();
		int count = (head & 0xf0) == 0x80 ? head & 15 : head switch
		{ 0xde => UInt(2), 0xdf => UInt(4), _ => throw new InvalidDataException("名称字典不是映射。") };
		if (count > 100000) throw new InvalidDataException("名称字典条目过多。");
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < count; i++) { string key = String(); result.Add(key, String()); }
		if (input.Position != input.Length) throw new InvalidDataException("名称字典存在多余数据。");
		return result;
	}

	internal static List<DuelField> Fields(string gameRoot, CancellationToken token = default)
	{
		LoadNames(gameRoot);
		var result = new List<DuelField>();
		for (int id = 0; id <= 999; id++)
		{
			token.ThrowIfCancellationRequested();
			var models = new List<DuelBundleSlot>();
			foreach (string quality in new[] { "HighEnd_HD", "HD", "SD" })
			foreach (string side in new[] { "near", "far" })
			{
				string logical = $"Duel/BG/Mat/Mat_{id:000}/{quality}/Mat_{id:000}_{side}";
				if (Locate(gameRoot, logical) is { } path) models.Add(new(quality + "/" + side, logical, path));
			}
			TexRef? preview = null;
			int itemId = _modelItems.TryGetValue(id, out int mappedItem) ? mappedItem : 0;
			foreach (string quality in new[] { "HighEnd_HD", "SD" })
			{
				if (itemId == 0) break;
				string logical = $"Images/Field/{quality}/FieldIcon{itemId}";
				if (Locate(gameRoot, logical) is not { } path) continue;
				string? local = IndexService.FindLocalRoot(gameRoot);
				bool isLocal = local != null && StandaloneResourceService.IsInside(path, local);
				preview = new ModEngine().ScanBundle(path, isLocal ? local! : gameRoot, isLocal ? "视觉资源" : "基础视觉资源", false).Textures
					.FirstOrDefault(x => x.Name.Equals("FieldIcon" + itemId, StringComparison.OrdinalIgnoreCase));
				if (preview != null) { preview.Category = "场地预览"; break; }
			}
			if (models.Count > 0 || preview != null) result.Add(new(id,
				_fieldNames.TryGetValue(itemId - 1090000, out string? name) ? name : $"Mat_{id:000}", models, preview, itemId));
		}
		return result;
	}

	internal static List<DuelMusic> Music(string gameRoot)
	{
		LoadNames(gameRoot);
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (string name in File.ReadAllLines(AppPaths.ResolveFile("bgm-catalog-v1.txt")))
			if (Regex.IsMatch(name, @"\ABGM_[A-Z0-9_]+\z")) names.Add(name);
		for (int i = 1; i <= 199; i++)
		{
			foreach (string kind in new[] { "NORMAL", "KEYCARD", "CLIMAX" }) names.Add($"BGM_DUEL_{kind}_{i:00}");
			names.Add($"BGM_SOLO_{i:00}");
			foreach (string phase in new[] { "NORMAL", "KEYCARD", "CLIMAX", "PHASE_A", "PHASE_B" })
			{
				names.Add($"BGM_DUEL_EX_{i:00}_{phase}");
				names.Add($"BGM_DUEL_F{i:00}_{phase}");
			}
		}
		return names.OrderBy(x => x).Select(name => (Name: name, Logical: "Sound/AudioClip/BGM/" + name))
			.Select(x => (x.Name, x.Logical, Path: Locate(gameRoot, x.Logical))).Where(x => x.Path != null)
			.Select(x => DuelMusicMetadata.Describe(x.Name, new("music", x.Logical, x.Path!), _fieldNames)).ToList();
	}
}
