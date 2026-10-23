using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MdCardModTool;

internal sealed class DuelResourceService
{
	private static readonly object Gate = new();
	private readonly string? _testRoot;
	private readonly Action<int>? _afterCommitForTest;
	internal DuelResourceService() { }
	internal DuelResourceService(string isolatedRoot, Action<int>? afterCommitForTest = null)
	{ _testRoot = Path.GetFullPath(isolatedRoot); _afterCommitForTest = afterCommitForTest; }
	internal sealed class Record
	{
		public int Version { get; set; } = 1;
		public string Build { get; set; } = "";
		public string Target { get; set; } = "";
		public string Container { get; set; } = "";
		public string Original { get; set; } = "";
		public List<string> Accepted { get; set; } = new();
	}
	internal static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
	private void EnsureClosed(string root)
	{
		if (_testRoot == null) DirectModArchive.EnsureGameClosed();
		else if (!Path.GetFullPath(root).Equals(_testRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("测试写入超出镜像范围。");
	}
	private static void ValidateSlot(string root, DuelBundleSlot slot)
	{
		string? expected = DuelResourceCatalog.Locate(root, slot.LogicalPath);
		if (expected == null || !Path.GetFullPath(expected).Equals(Path.GetFullPath(slot.BundlePath), StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("资源路径与当前游戏／账号不匹配。");
		if (!(slot.Key == "music" ? Regex.IsMatch(slot.LogicalPath, @"\ASound/AudioClip/BGM/BGM_[A-Z0-9_]+\z")
			: Regex.IsMatch(slot.LogicalPath, @"\ADuel/BG/Mat/Mat_\d{3}/(?:SD|HD|HighEnd_HD)/Mat_\d{3}_(?:near|far)\z")))
			throw new InvalidDataException("不支持的资源路径。");
		for (string? path = Path.GetFullPath(slot.BundlePath); path != null; path = Path.GetDirectoryName(path))
			if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不支持链接目录中的资源。");
	}
	internal static string BackupPath(string root, DuelBundleSlot slot)
	{
		string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(slot.BundlePath).ToUpperInvariant())))[..24];
		return Path.Combine(root, "_MD卡图备份", "场地与音乐", key + ".bundle");
	}
	private static Record? ReadRecord(string root, DuelBundleSlot slot)
	{
		string path = BackupPath(root, slot);
		if (!File.Exists(path + ".json"))
		{
			if (File.Exists(path)) throw new IOException("备份缺少校验记录：" + path);
			return null;
		}
		var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path + ".json")) ?? throw new InvalidDataException("备份记录为空。");
		if (record.Version != 1 || record.Target != Path.GetFullPath(slot.BundlePath) || record.Container != slot.Container
			|| record.Build != PortableIndexService.GetGameBuildId(root) || record.Original != Hash(path))
			throw new InvalidDataException("备份版本、目标或校验不符，请勿覆盖旧备份。");
		return record;
	}
	internal static string State(string root, DuelBundleSlot slot)
	{
		try
		{
			var record = ReadRecord(root, slot);
			if (record == null) return "original";
			string current = Hash(slot.BundlePath);
			return current == record.Original ? "original" : record.Accepted.Contains(current) ? "applied" : "unknown";
		}
		catch { return "unknown"; }
	}
	private static void SaveRecord(string path, Record record)
	{
		string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try { File.WriteAllText(temp, JsonSerializer.Serialize(record)); File.Move(temp, path, true); }
		finally { if (File.Exists(temp)) File.Delete(temp); }
	}
	internal static void ValidateDependencies(string root, DuelBundleSlot slot)
	{
		ValidateSlot(root, slot);
		var queue = new Queue<string>(); queue.Enqueue(slot.BundlePath);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		while (queue.TryDequeue(out string? path))
		{
			if (!seen.Add(path)) continue;
			if (seen.Count > 1024) throw new InvalidDataException("场地依赖数量异常。");
			string[] dependencies;
			if (path == slot.BundlePath)
			{
				using var document = new DuelBundleDocument(path, slot.Container);
				if (slot.Key == "music") _ = document.AudioBytes();
				dependencies = document.Dependencies;
			}
			else
			{
				var engine = new ModEngine();
				if (engine.ReadAssetBundleContainerPaths(path).Count == 0) throw new InvalidDataException("依赖 Bundle 无法读取：" + path);
				dependencies = engine.ReadAssetBundleContainerPaths(path, true).ToArray();
			}
			foreach (string dep in dependencies)
			{
				if (!Regex.IsMatch(dep.Replace('\\', '/'), @"\A[0-9a-f]{2}/[0-9a-f]{8}\z")) throw new InvalidDataException("依赖路径异常：" + dep);
				string? resolved = new[] { IndexService.FindLocalRoot(root), IndexService.StreamingRoot(root) }.Where(x => x != null)
					.Select(x => Path.Combine(x!, dep)).FirstOrDefault(File.Exists);
				queue.Enqueue(resolved ?? throw new FileNotFoundException("请先在游戏中下载完整来源资源：" + dep));
			}
		}
	}
	internal void ReplaceField(string root, DuelField target, DuelField source, Action? beforeCommit = null)
	{
		if (target.Id == source.Id || target.Models.Count == 0) throw new InvalidOperationException("请选择两个不同且已下载模型的场地。");
		var pairs = target.Models.Select(t => (Target: t, Source: source.Models.SingleOrDefault(s => s.Key == t.Key)
			?? throw new InvalidOperationException("来源缺少 " + t.Key + "，不会部分替换。"))).ToArray();
		Change(root, pairs.Select(x => x.Target).ToArray(), (slot, output) =>
		{
			var sourceSlot = pairs.Single(x => x.Target == slot).Source;
			ValidateDependencies(root, sourceSlot);
			string sourceHash = Hash(sourceSlot.BundlePath);
			using (var donor = new DuelBundleDocument(sourceSlot.BundlePath, sourceSlot.Container))
			using (var destination = new DuelBundleDocument(slot.BundlePath, slot.Container)) donor.WriteDonor(output, destination);
			if (Hash(sourceSlot.BundlePath) != sourceHash) throw new IOException("制作期间来源已改变。");
		}, false, beforeCommit);
	}
	internal void ReplaceMusic(string root, DuelMusic target, DuelMusic source)
	{
		if (source.Slot.BundlePath == target.Slot.BundlePath) throw new InvalidOperationException("来源与目标相同。");
		Change(root, new[] { target.Slot }, (slot, output) =>
		{
			ValidateDependencies(root, source.Slot);
			string hash = Hash(source.Slot.BundlePath);
			using (var donor = new DuelBundleDocument(source.Slot.BundlePath, source.Slot.Container))
			using (var destination = new DuelBundleDocument(slot.BundlePath, slot.Container)) donor.WriteDonor(output, destination);
			if (hash != Hash(source.Slot.BundlePath)) throw new IOException("制作期间来源已改变。");
		}, false);
	}
	internal void ImportMusic(string root, DuelMusic target, string input)
	{
		string directory = Path.Combine(Path.GetTempPath(), "MDCardModTool", "music-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			string raw = Path.Combine(directory, "audio.pcm");
			DuelMusicAudio.Convert(input, raw, true);
			byte[] pcm = File.ReadAllBytes(raw), fsb = DuelMusicAudio.BuildPcmBank(pcm);
			Change(root, new[] { target.Slot }, (slot, output) =>
			{
				using var document = new DuelBundleDocument(slot.BundlePath, slot.Container);
				document.WriteAudio(output, fsb, pcm.Length / 4);
				using var check = new DuelBundleDocument(output, slot.Container);
				if (!check.AudioBytes().SequenceEqual(fsb)) throw new InvalidDataException("音频写回核验失败。");
			}, false);
		}
		finally { Directory.Delete(directory, true); }
	}
	internal void Restore(string root, IReadOnlyList<DuelBundleSlot> slots) => Change(root, slots, (_, _) => { }, true);
	private void Change(string root, IReadOnlyList<DuelBundleSlot> slots, Action<DuelBundleSlot, string> prepare, bool restore, Action? beforeCommit = null)
	{
		lock (Gate)
		{
			EnsureClosed(root);
			if (slots.Count == 0 || slots.Select(x => x.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Count)
				throw new InvalidDataException("资源列表为空或重复。");
			string transaction = ".md-duel-" + Guid.NewGuid().ToString("N");
			var prepared = new List<(DuelBundleSlot Slot, string Temp, string Rollback, string Original, string Next, Record Record)>();
			var cleanup = new List<string>();
			try
			{
				foreach (var slot in slots)
				{
					ValidateSlot(root, slot);
					string original = Hash(slot.BundlePath), temp = slot.BundlePath + transaction, rollback = temp + ".rollback";
					cleanup.Add(temp); cleanup.Add(rollback);
					var record = ReadRecord(root, slot);
					if (record != null && original != record.Original && !record.Accepted.Contains(original)) throw new IOException("目标已被其他工具修改，拒绝覆盖：" + slot.LogicalPath);
					if (restore && record == null) throw new IOException("该目标没有场地／音乐备份。");
					if (restore) File.Copy(BackupPath(root, slot), temp);
					else prepare(slot, temp);
					using (var check = new DuelBundleDocument(temp, slot.Container))
					{
						if (slot.Key == "music") _ = check.AudioBytes();
					}
					File.Copy(slot.BundlePath, rollback);
					if (Hash(rollback) != original) throw new IOException("准备期间目标已改变。");
					prepared.Add((slot, temp, rollback, original, Hash(temp), record ?? new()
					{ Build = PortableIndexService.GetGameBuildId(root), Target = Path.GetFullPath(slot.BundlePath), Container = slot.Container, Original = original }));
				}
				beforeCommit?.Invoke();
				using var lease = AnimationWriteLease.Acquire(slots.Select(x => x.BundlePath), _testRoot == null);
				foreach (var item in prepared)
					if (Convert.ToHexString(SHA256.HashData(lease.Streams[item.Slot.BundlePath])) != item.Original) throw new IOException("提交前目标已改变，未覆盖。");
				EnsureClosed(root);
				foreach (var item in prepared)
				{
					string backup = BackupPath(root, item.Slot);
					Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
					if (!File.Exists(backup)) File.Copy(item.Rollback, backup, false);
					if (Hash(backup) != item.Record.Original) throw new IOException("首次备份校验失败。");
					if (!item.Record.Accepted.Contains(item.Next)) item.Record.Accepted.Add(item.Next);
					SaveRecord(backup + ".json", item.Record);
				}
				var committed = new List<(string Path, string Rollback)>();
				try
				{
					foreach (var item in prepared)
					{
						File.Replace(item.Temp, item.Slot.BundlePath, null);
						committed.Add((item.Slot.BundlePath, item.Rollback));
						_afterCommitForTest?.Invoke(committed.Count);
						if (Hash(item.Slot.BundlePath) != item.Next) throw new IOException("提交后核验失败。");
					}
				}
				catch (Exception failure)
				{
					var errors = new List<Exception> { failure };
					foreach (var item in committed.AsEnumerable().Reverse())
						try { File.Replace(item.Rollback, item.Path, null); }
						catch (Exception ex) { cleanup.Remove(item.Rollback); errors.Add(new IOException("回滚失败，保留恢复副本：" + item.Rollback, ex)); }
					throw new AggregateException("替换事务失败。", errors);
				}
			}
			finally { foreach (string path in cleanup) if (File.Exists(path)) File.Delete(path); }
		}
	}
}
