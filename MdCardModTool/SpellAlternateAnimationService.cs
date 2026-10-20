using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace MdCardModTool;

public sealed class SpellAlternateAnimationService
{
	internal const string SettingPath = "Duel/ScriptableObject/CardRunEffectSetting";
	private readonly string? _isolatedTestRoot;
	private readonly CardCatalogService _catalog;
	public SpellAlternateAnimationService() { _catalog = CardCatalogService.LoadBestAvailable(); }
	internal SpellAlternateAnimationService(string isolatedTestRoot) : this()
	{
		_isolatedTestRoot = Path.GetFullPath(isolatedTestRoot);
		if (!Path.GetFileName(_isolatedTestRoot).StartsWith("mirror-", StringComparison.Ordinal)
			|| File.Exists(Path.Combine(_isolatedTestRoot, "masterduel.exe")))
			throw new InvalidOperationException("Test writes require an isolated mirror.");
	}
	public sealed record Candidate(int TargetId, int SourceId, string Name, string Availability)
	{
		public override string ToString() => $"{Name}  {TargetId} → {SourceId}";
	}
	public sealed record Inspection(string LocalRoot, string BundlePath, string BackupPath, string State,
		bool CanApply, bool CanRestore, IReadOnlyList<string> Dependencies, string Diagnostic);
	public sealed record Record
	{
		public int Version { get; init; } = 1;
		public int TargetId { get; init; }
		public string BuildId { get; init; } = "";
		public string BackupHash { get; init; } = "";
		public List<string[]> AcceptedRows { get; init; } = new();
	}
	internal sealed class Settings : IDisposable
	{
		internal readonly AssetsManager Manager = new();
		internal readonly BundleFileInstance Bundle;
		internal readonly AssetsFileInstance Assets;
		internal readonly AssetFileInfo Info;
		internal readonly AssetTypeValueField Field;
		internal List<AssetTypeValueField> AllRows => Field["infoList"]["Array"].Children;
		internal List<AssetTypeValueField> Rows(int id) => AllRows.Where(x => x["mrk"].AsInt == id).ToList();
		internal Settings(string path)
		{
			try
			{
				ValidateHeader(path);
				Bundle = Manager.LoadBundleFile(path);
				var candidates = new List<(AssetsFileInstance, AssetFileInfo, AssetTypeValueField)>();
				foreach (string entry in Bundle.file.GetAllFileNames())
				{
					var assets = Manager.LoadAssetsFileFromBundle(Bundle, entry);
					if (assets == null) continue;
					foreach (var info in assets.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
					{
						var field = Manager.GetBaseField(assets, info);
						if (field["m_Name"].AsString == "CardRunEffectSetting") candidates.Add((assets, info, field));
					}
				}
				if (candidates.Count != 1) throw new InvalidDataException("未找到唯一的 CardRunEffectSetting 配置。");
				(Assets, Info, Field) = candidates.Single();
				if (AllRows.Count == 0 || AllRows.Count > 10000) throw new InvalidDataException("演出表数量异常。");
			}
			catch { Manager.UnloadAll(); throw; }
		}
		internal void ReplaceRows(int targetId, IEnumerable<AssetTypeValueField> rows)
		{
			int at = AllRows.FindIndex(x => x["mrk"].AsInt == targetId);
			AllRows.RemoveAll(x => x["mrk"].AsInt == targetId);
			AllRows.InsertRange(at < 0 ? AllRows.Count : at, rows.Select(Clone));
			Field["infoList"]["Array"].AsArray = new AssetTypeArrayInfo(AllRows.Count);
		}
		internal void Save(string path)
		{
			byte[] data;
			using (var memory = new MemoryStream())
			{
				using var writer = new AssetsFileWriter(memory);
				Assets.file.Write(writer, 0, new List<AssetsReplacer> { new AssetsReplacerFromMemory(Assets.file, Info, Field) });
				data = memory.ToArray();
			}
			using var output = new AssetsFileWriter(path);
			Bundle.file.Write(output, new List<BundleReplacer> { new BundleReplacerFromMemory(Assets.name, Assets.name, true, data, -1) });
		}
		public void Dispose() => Manager.UnloadAll();
	}
	private static void ValidateHeader(string path)
	{
		using var input = File.OpenRead(path);
		var magic = new byte[8];
		if (input.Length < 64 || input.Read(magic) != 8 || Encoding.ASCII.GetString(magic) != "UnityFS\0")
			throw new InvalidDataException("不是有效的 UnityFS Bundle：" + path);
	}
	internal static AssetTypeValueField Clone(AssetTypeValueField field)
	{
		using var reader = new AssetsFileReader(new MemoryStream(field.WriteToByteArray()));
		return field.TemplateField.MakeValue(reader, 0, null);
	}
	internal static string[] Encoded(IEnumerable<AssetTypeValueField> rows) => rows.Select(x => Convert.ToBase64String(x.WriteToByteArray())).ToArray();
	internal static string FileHash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
	private static bool IsSpell(CardCatalogEntry card) => !card.IsMonster && (card.Type.Contains("魔法", StringComparison.Ordinal) || card.Type.Contains("spell", StringComparison.OrdinalIgnoreCase));
	private void ValidatePair(int targetId, int sourceId)
	{
		var target = _catalog.Find(targetId);
		var source = _catalog.Find(sourceId);
		if (target == null || source == null || targetId == sourceId || !IsSpell(target) || !IsSpell(source)
			|| !_catalog.FindEquivalentCards(target).Any(x => x.CardId == sourceId))
			throw new InvalidOperationException("仅支持卡表确认的同名魔法卡版本之间替换。");
	}
	private static (string Root, string Path) LocateSettings(string gameRoot)
	{
		if (ResourceSource.IsMobile(gameRoot)) throw new NotSupportedException("此功能仅支持 Windows 游戏资源。");
		string root = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到当前账号 LocalData。");
		string path = Path.Combine(root, IndexService.ResourceBundleRelativePath(SettingPath));
		if (!File.Exists(path)) throw new FileNotFoundException("当前账号未下载魔法卡演出配置。", path);
		return (root, path);
	}
	public IReadOnlyList<Candidate> Discover(string gameRoot)
	{
		var (_, path) = LocateSettings(gameRoot);
		using var settings = new Settings(path);
		var ids = settings.AllRows.Select(x => x["mrk"].AsInt).ToHashSet();
		var result = new List<Candidate>();
		foreach (var target in _catalog.Entries.Where(IsSpell))
			foreach (var source in _catalog.FindEquivalentCards(target).Where(x => x.CardId != target.CardId && IsSpell(x) && ids.Contains(x.CardId)))
			{
				string diagnostic = "";
				try { ValidateSource(settings.Rows(source.CardId)); } catch (Exception ex) { diagnostic = ex.Message; }
				result.Add(new(target.CardId, source.CardId, target.Name(Localizer.Language), diagnostic));
			}
		return result.OrderBy(x => x.TargetId).ThenBy(x => x.SourceId).ToArray();
	}
	private static void ValidateSource(List<AssetTypeValueField> rows)
	{
		if (rows.Count == 0 || rows.Count > 32) throw new InvalidDataException("来源没有官方演出登记。");
		if (rows.Any(x => x["reserveCardMoveEffect"].AsBool || x["cardMoveEffectLabel"].AsString.Length > 0))
			throw new NotSupportedException("此来源使用卡片移动专用脚本，尚不支持移植。");
		if (!rows.Any(x => Paths(x).Any())) throw new NotSupportedException("此来源没有直接 Timeline 路径，暂不支持内部枚举演出。");
		if (rows.Any(x => x["enable"].AsBool == false)) throw new NotSupportedException("来源含停用登记。");
	}
	private static IEnumerable<string> Paths(AssetTypeValueField row) => new[] { "effectPath", "centerEffectPath", "expandPath" }
		.Select(name => row[name].AsString).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal);
	private static string RecordPath(string gameRoot, string root, int targetId)
	{
		string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PortableIndexService.GetGameBuildId(gameRoot))));
		string account = Path.GetFileName(Path.GetDirectoryName(root)) ?? throw new InvalidDataException("账号路径异常。");
		return Path.Combine(gameRoot, "_MD卡图备份", "魔法卡异画动画", account, key, targetId + ".json");
	}
	private static Record? ReadRecord(string gameRoot, string root, int id)
	{
		string path = RecordPath(gameRoot, root, id);
		if (!File.Exists(path))
		{
			if (File.Exists(path + ".bundle")) throw new InvalidDataException("发现未完成的备份，请检查：" + path);
			return null;
		}
		var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path)) ?? throw new InvalidDataException("备份记录损坏。");
		if (record.Version != 1 || record.TargetId != id || record.BuildId != PortableIndexService.GetGameBuildId(gameRoot)
			|| FileHash(path + ".bundle") != record.BackupHash) throw new InvalidDataException("备份版本或校验不匹配。");
		return record;
	}
	public Inspection Inspect(string gameRoot, int targetId, int sourceId, bool requireDependencies = true)
	{
		ValidatePair(targetId, sourceId);
		var (root, path) = LocateSettings(gameRoot);
		using var settings = new Settings(path);
		string recordPath = RecordPath(gameRoot, root, targetId);
		var record = ReadRecord(gameRoot, root, targetId);
		string state = "original";
		if (record != null)
		{
			using var backup = new Settings(recordPath + ".bundle");
			var current = Encoded(settings.Rows(targetId));
			if (!current.SequenceEqual(Encoded(backup.Rows(targetId))))
				state = record.AcceptedRows.Any(x => x.SequenceEqual(current)) ? "applied" : "unknown";
		}
		var dependencies = new List<string>();
		string diagnostic = "";
		try
		{
			ValidateSource(settings.Rows(sourceId));
			if (requireDependencies) dependencies = ValidateDependencies(gameRoot, root, settings.Rows(sourceId).SelectMany(Paths));
		}
		catch (Exception ex) { diagnostic = ex.Message; }
		return new(root, path, recordPath + ".bundle", state, state != "unknown" && diagnostic.Length == 0,
			record != null && state != "unknown", dependencies, diagnostic);
	}
	internal static List<string> ValidateDependencies(string gameRoot, string localRoot, IEnumerable<string> logicalPaths)
	{
		string? Locate(string relative)
		{
			if (!Regex.IsMatch(relative.Replace('\\', '/'), @"\A[0-9a-f]{2}/[0-9a-f]{8}\z")) throw new InvalidDataException("异常依赖：" + relative);
			return new[] { localRoot, IndexService.StreamingRoot(gameRoot) }.Select(r => Path.Combine(r, relative)).FirstOrDefault(File.Exists);
		}
		var queue = new Queue<string>();
		foreach (string path in logicalPaths.Distinct())
		{
			if (!Regex.IsMatch(path, @"\ADuel/Timeline/Card/(?:<_CARD_ILLUST_>|tcg|ocg)/[A-Za-z0-9_/]+\z")) throw new NotSupportedException("尚不支持的演出逻辑路径：" + path);
			bool found = false;
			foreach (string logical in path.Contains("<_CARD_ILLUST_>") ? new[] { path.Replace("<_CARD_ILLUST_>", "tcg"), path.Replace("<_CARD_ILLUST_>", "ocg") } : new[] { path })
			{
				string? entry = Locate(IndexService.ResourceBundleRelativePath(logical));
				if (entry == null) continue;
				using var reader = new BundleReader(entry);
				if (!reader.Containers.Any(x => x.EndsWith("/" + logical + ".prefab", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("来源 Prefab 容器与路径不符：" + logical);
				queue.Enqueue(entry);
				found = true;
			}
			if (!found) throw new FileNotFoundException("请先在游戏内下载来源演出：" + path);
		}
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool timeline = false;
		while (queue.TryDequeue(out var path))
		{
			if (!visited.Add(path)) continue;
			if (visited.Count > 512) throw new InvalidDataException("依赖数量异常。");
			using var reader = new BundleReader(path);
			timeline |= reader.Containers.Any(x => x.EndsWith(".playable", StringComparison.OrdinalIgnoreCase));
			foreach (string dependency in reader.Dependencies) queue.Enqueue(Locate(dependency) ?? throw new FileNotFoundException("缺少来源演出依赖：" + dependency));
		}
		if (!timeline) throw new InvalidDataException("来源依赖中未找到 Timeline。");
		return visited.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
	}
	private sealed class BundleReader : IDisposable
	{
		private readonly AssetsManager _manager = new();
		internal readonly List<string> Containers = new(), Dependencies = new();
		internal BundleReader(string path)
		{
			try
			{
				ValidateHeader(path);
				_manager.LoadClassPackage(AppPaths.ResolveFile("classdata.tpk"));
				var bundle = _manager.LoadBundleFile(path);
				foreach (string name in bundle.file.GetAllFileNames())
				{
					var assets = _manager.LoadAssetsFileFromBundle(bundle, name);
					if (assets == null) continue;
					_manager.LoadClassDatabaseFromPackage(assets.file.Metadata.UnityVersion);
					foreach (var info in assets.file.GetAssetsOfType(AssetClassID.AssetBundle))
					{
						var field = _manager.GetBaseField(assets, info);
						Containers.AddRange(field["m_Container"]["Array"].Children.Select(x => x["first"].AsString));
						Dependencies.AddRange(field["m_Dependencies"]["Array"].Children.Select(x => x.AsString));
					}
				}
				if (Containers.Count == 0) throw new InvalidDataException("Bundle 容器为空：" + path);
			}
			catch { _manager.UnloadAll(); throw; }
		}
		public void Dispose() => _manager.UnloadAll();
	}
	public void Apply(string gameRoot, int targetId, int sourceId, bool sourceSound = false) => Change(gameRoot, targetId, sourceId, false, sourceSound);
	public void Restore(string gameRoot, int targetId, int sourceId) => Change(gameRoot, targetId, sourceId, true, false);
	internal void Change(string gameRoot, int targetId, int sourceId, bool restore, bool sound, Action? beforeCommit = null)
	{
		if (_isolatedTestRoot == null) DirectModArchive.EnsureGameClosed();
		else if (!Path.GetFullPath(gameRoot).Equals(_isolatedTestRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test write escaped mirror.");
		var inspection = Inspect(gameRoot, targetId, sourceId, !restore);
		if (restore ? !inspection.CanRestore : !inspection.CanApply) throw new InvalidOperationException("当前配置不支持操作：" + inspection.State + "。" + inspection.Diagnostic);
		string recordPath = inspection.BackupPath[..^7];
		string originalHash = FileHash(inspection.BundlePath);
		string temporary = inspection.BundlePath + ".md-spell-" + Guid.NewGuid().ToString("N");
		try
		{
			Record? record = ReadRecord(gameRoot, inspection.LocalRoot, targetId);
			using (var settings = new Settings(inspection.BundlePath))
			{
				if (record == null)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(inspection.BackupPath)!);
					File.Copy(inspection.BundlePath, inspection.BackupPath, false);
					record = new() { TargetId = targetId, BuildId = PortableIndexService.GetGameBuildId(gameRoot), BackupHash = FileHash(inspection.BackupPath) };
					SaveRecord(recordPath, record);
				}
				using var backup = new Settings(inspection.BackupPath);
				var originals = backup.Rows(targetId);
				var replacement = restore ? originals.Select(Clone).ToList() : settings.Rows(sourceId).Select(Clone).ToList();
				foreach (var row in replacement)
				{
					row["mrk"].AsInt = targetId;
					if (!restore && !sound)
					{
						var original = originals.FirstOrDefault(x => x["player"].AsInt == row["player"].AsInt && x["runTiming"].AsInt == row["runTiming"].AsInt) ?? (originals.Count == 1 ? originals[0] : null);
						foreach (string name in new[] { "seLabel", "is3Dse", "isNormalSe" })
						{
							int at = row.Children.FindIndex(x => x.FieldName == name);
							if (original != null) row.Children[at] = Clone(original[name]);
							else if (name == "seLabel") row[name].AsString = "";
						}
					}
				}
				settings.ReplaceRows(targetId, replacement);
				settings.Save(temporary);
				using var check = new Settings(temporary);
				if (!check.Field.WriteToByteArray().SequenceEqual(settings.Field.WriteToByteArray())) throw new InvalidDataException("写回核验失败。");
				if (!restore)
				{
					record.AcceptedRows.Add(Encoded(replacement));
					// Keep previous accepted states so a failed atomic replace remains restorable.
					SaveRecord(recordPath, record);
				}
			}
			using (var check = new Settings(temporary))
			using (var previous = new Settings(inspection.BundlePath))
			{
				check.ReplaceRows(targetId, Array.Empty<AssetTypeValueField>());
				previous.ReplaceRows(targetId, Array.Empty<AssetTypeValueField>());
				if (!check.Field.WriteToByteArray().SequenceEqual(previous.Field.WriteToByteArray())) throw new InvalidDataException("其他卡演出配置发生改变，拒绝提交。");
			}
			beforeCommit?.Invoke();
			using var lease = AnimationWriteLease.Acquire(new[] { inspection.BundlePath }, _isolatedTestRoot == null);
			if (Convert.ToHexString(SHA256.HashData(lease.Streams[inspection.BundlePath])) != originalHash) throw new IOException("制作期间配置已改变，未提交。");
			File.Replace(temporary, inspection.BundlePath, null);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
	private static void SaveRecord(string path, Record record)
	{
		string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try { File.WriteAllText(temp, JsonSerializer.Serialize(record)); File.Move(temp, path, true); }
		finally { if (File.Exists(temp)) File.Delete(temp); }
	}
}
