using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal sealed record ModRecoveryPlan(IReadOnlyList<RecoverableTextureMod> Saved, IReadOnlyList<ushort> LegacyCards)
{
	internal int Count => Saved.Count + LegacyCards.Count;
}
internal sealed record ModRecoveryResult(int Reapplied, int AlreadyApplied, IReadOnlyList<string> Skipped, IReadOnlyList<string> ChangedPaths, string HistoryFolder);

internal sealed class ModRecoveryService
{
	private readonly ModRecoveryStore _store;
	private readonly ModEngine _engine = new();
	private readonly OverFrameService _frames = new();
	internal ModRecoveryService(ModRecoveryStore? store = null) => _store = store ?? new ModRecoveryStore();

	internal ModRecoveryPlan Plan(string gameRoot, IReadOnlyList<TexRef> textures)
	{
		var manifest = _store.Load(gameRoot);
		var saved = manifest.Textures.Where(x => x.Active).ToArray();
		var knownCards = manifest.Textures.Select(x => x.CardKey).ToHashSet(StringComparer.Ordinal);
		var legacy = new List<ushort>();
		string folder = Path.Combine(gameRoot, "_MD卡图素材", "超框");
		if (Directory.Exists(folder)) foreach (string directory in Directory.EnumerateDirectories(folder))
		{
			if (!ushort.TryParse(Path.GetFileName(directory), out ushort card) || card == 0 || knownCards.Contains(card.ToString()) || manifest.DisabledLegacyCards.Contains(card)) continue;
			if (!OverFrameArtStore.HasSettings(gameRoot, card) || !File.Exists(OverFrameArtStore.ArtPath(gameRoot, card)) || !OverFrameArtStore.ReadSettings(gameRoot, card).UserSelected) continue;
			// Drafts alone are not proof of application. Require a previous target backup,
			// and show these explicitly as legacy candidates before the user confirms.
			var candidates = textures.Where(x => x.CardKey == card.ToString() && CardTextureRules.HasCardIdentity(x));
			if (candidates.Any(x => File.Exists(Path.Combine(gameRoot, "_MD卡图备份", x.SourceKind, x.RelativeBundlePath)))) legacy.Add(card);
		}
		return new ModRecoveryPlan(saved, legacy.Order().ToArray());
	}

	private sealed class Pending
	{
		internal required string Target;
		internal required string Stage;
		internal required string Original;
		internal required string Hash;
		internal required string Backup;
		internal string? OldBackupCopy;
		internal bool HadBackup;
		internal bool BackupChanged;
		internal bool RotateBackup;
	}

	internal ModRecoveryResult Reapply(string gameRoot, ModRecoveryPlan plan, Action<int, int, string>? progress = null,
		CancellationToken cancellationToken = default, Action<int>? afterCommit = null)
	{
		DirectModArchive.EnsureGameClosed();
		string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动 LocalData 账号。");
		string history = Path.Combine(gameRoot, "_MD卡图备份", "更新恢复", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8]);
		string staging = Path.Combine(gameRoot, "_MDModRecovery-stage_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(staging);
		var pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
		var skipped = new List<string>();
		var complete = new List<(RecoverableTextureMod Entry, TexRef Texture, byte[] Png, bool Changed)>();
		bool keepStage = false;
		int already = 0;
		try
		{
			var inputs = plan.Saved.Select(x => (Entry: x, Legacy: (ushort?)null)).ToList();
			inputs.AddRange(plan.LegacyCards.Select(x => (new RecoverableTextureMod { Id = "legacy_" + x, CardKey = x.ToString(), Name = x.ToString(), OverFrameArtId = x, SourceKind = "本地卡图",
				RelativePath = IndexService.CardIllustrationRelativePath(x.ToString()).Replace('\\', '/') }, (ushort?)x)));
			for (int i = 0; i < inputs.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var (entry, legacy) = inputs[i];
				progress?.Invoke(i, inputs.Count, entry.CardKey + " " + entry.Name);
				bool startedStaging = false;
				try
				{
					byte[] png = legacy is ushort card ? OverFrameRecoveryComposer.Compose(gameRoot, card) : _store.ReadPng(gameRoot, entry);
					TexRef texture = Resolve(gameRoot, entry);
					bool changed = !PixelHash(_engine.DecodePng(texture)).Equals(PixelHash(png), StringComparison.Ordinal);
					if (changed)
					{
						Pending write = GetPending(texture.BundlePath, Path.Combine(gameRoot, "_MD卡图备份", texture.SourceKind, texture.RelativeBundlePath), rotate: true);
						TexRef stage = CloneAt(texture, write.Stage);
						startedStaging = true;
						_engine.Replace(stage, png, Path.Combine(staging, "writer-backups"));
						if (PixelHash(_engine.DecodePng(stage)) != PixelHash(png)) throw new InvalidDataException("临时 Bundle 像素验证失败。");
					}
					complete.Add((entry, texture, png, changed));
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception ex)
				{
					if (startedStaging) throw new IOException("临时资源构建失败；未向游戏提交任何文件：" + entry.CardKey, ex);
					skipped.Add((entry.CardKey.Length > 0 ? entry.CardKey : entry.Name) + ": " + ex.Message);
				}
			}
			// Merge only the requested card rows into the current game table. Never copy
			// an old entire of_card_asset bundle over an updated official catalog.
			var overframes = complete.Where(x => x.Entry.OverFrameArtId.HasValue).ToArray();
			var gateMissing = new HashSet<string>(StringComparer.Ordinal);
			if (overframes.Length > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				TextAssetRef gate = _frames.FindGate(gameRoot, (done, total) => progress?.Invoke(done, total, "of_card_asset"), cancellationToken);
				var mappings = _frames.Read(gate);
				foreach (var item in overframes)
				{
					ushort card = ushort.Parse(item.Entry.CardKey);
					if (!mappings.Any(x => x.CardId == card && x.ArtId == item.Entry.OverFrameArtId)) gateMissing.Add(item.Entry.Id);
					mappings.RemoveAll(x => x.CardId == card);
					mappings.Add(new OverFrameMapping(card, item.Entry.OverFrameArtId!.Value));
				}
				if (gateMissing.Count > 0)
				{
					Pending write = GetPending(gate.BundlePath, Path.Combine(gameRoot, "_MD卡图备份", "超框开关", gate.RelativeBundlePath), rotate: true);
					TextAssetRef stageGate = _engine.FindTextAssetFast(write.Stage, staging, OverFrameService.GateName) ?? throw new InvalidDataException("临时超框登记表缺失。");
					byte[] data = EncodeMappings(mappings);
					_engine.ReplaceTextAsset(stageGate, data, Path.Combine(staging, "writer-backups"));
					var reopened = _engine.FindTextAssetFast(write.Stage, staging, OverFrameService.GateName)!;
					if (!reopened.Data.SequenceEqual(data)) throw new InvalidDataException("临时超框登记表验证失败。");
				}
			}
			already = complete.Count(x => !x.Changed && !gateMissing.Contains(x.Entry.Id));
			cancellationToken.ThrowIfCancellationRequested();
			DirectModArchive.EnsureGameClosed(); // Preparation may outlast a game launch.
			using (AnimationWriteLease lease = AnimationWriteLease.Acquire(pending.Keys))
			{
				foreach (Pending write in pending.Values)
				{
					FileStream locked = lease.Streams[write.Target];
					locked.Position = 0;
					if (Convert.ToHexString(SHA256.HashData(locked)) != write.Hash) throw new IOException("资源在准备期间已改变，取消全部提交：" + write.Target);
					DirectModArchive.EnsureNoLinks(write.Backup);
				}
				var committed = new List<Pending>();
				try
				{
					int done = 0;
					foreach (Pending write in pending.Values)
					{
						// Preserve both the previous game baseline and the current build's
						// baseline. Default Restore Selected then uses the current baseline.
						string relative = Path.GetRelativePath(gameRoot, write.Backup);
						string currentCopy = Path.Combine(history, "before-reapply", relative);
						Directory.CreateDirectory(Path.GetDirectoryName(currentCopy)!);
						File.Copy(write.Original, currentCopy, false);
						write.HadBackup = File.Exists(write.Backup);
						if (write.HadBackup && write.RotateBackup)
						{
							write.OldBackupCopy = Path.Combine(history, "previous-baseline", relative);
							Directory.CreateDirectory(Path.GetDirectoryName(write.OldBackupCopy)!);
							File.Copy(write.Backup, write.OldBackupCopy, false);
							write.BackupChanged = true;
							ModRecoveryStore.AtomicWrite(write.Backup, File.ReadAllBytes(write.Original));
						}
						else if (!write.HadBackup)
						{
							write.BackupChanged = true;
							Directory.CreateDirectory(Path.GetDirectoryName(write.Backup)!);
							File.Copy(write.Original, write.Backup, false);
						}
						File.Replace(write.Stage, write.Target, null);
						committed.Add(write);
						progress?.Invoke(++done, pending.Count, "commit");
						afterCommit?.Invoke(done);
					}
				}
				catch (Exception commitError)
				{
					var rollbackErrors = new List<Exception>();
					foreach (Pending write in committed.AsEnumerable().Reverse()) try { File.Replace(write.Original, write.Target, null); } catch (Exception ex) { rollbackErrors.Add(ex); }
					foreach (Pending write in pending.Values.Where(x => x.BackupChanged)) try
					{
						if (write.HadBackup && write.OldBackupCopy != null) ModRecoveryStore.AtomicWrite(write.Backup, File.ReadAllBytes(write.OldBackupCopy));
						else if (!write.HadBackup && File.Exists(write.Backup)) File.Delete(write.Backup);
					} catch (Exception ex) { rollbackErrors.Add(ex); }
					if (rollbackErrors.Count > 0) { keepStage = true; throw new AggregateException("回滚未完全成功，请保留恢复目录：" + staging + " 和 " + history, rollbackErrors.Prepend(commitError)); }
					throw new IOException("本次恢复失败，所有已提交文件和原版备份已回滚。", commitError);
				}
			}
			foreach (var item in complete.Where(x => x.Entry.Id.StartsWith("legacy_", StringComparison.Ordinal)))
				try { _store.RecordTexture(gameRoot, item.Texture, item.Png, item.Entry.OverFrameArtId); } catch (Exception ex) { skipped.Add(item.Entry.CardKey + ": 恢复成功但素材库登记失败：" + ex.Message); }
			try { _store.MarkReapplied(gameRoot, complete.Select(x => x.Entry.Id)); } catch (Exception ex) { skipped.Add("恢复成功但状态保存失败：" + ex.Message); }
			progress?.Invoke(plan.Count, plan.Count, "complete");
			return new ModRecoveryResult(complete.Count - already, already, skipped, pending.Keys.ToArray(), history);
		}
		finally { if (!keepStage && Directory.Exists(staging)) Directory.Delete(staging, true); }

		Pending GetPending(string target, string backup, bool rotate)
		{
			if (pending.TryGetValue(target, out Pending? existing)) { existing.RotateBackup |= rotate; return existing; }
			DirectModArchive.EnsureNoLinks(target);
			string name = pending.Count.ToString("D5");
			string stage = Path.Combine(staging, name + ".bundle");
			string original = Path.Combine(staging, name + ".original");
			string before = Hash(target);
			File.Copy(target, original); File.Copy(original, stage);
			if (Hash(original) != before || Hash(target) != before) throw new IOException("资源正在更新，未开始提交：" + target);
			var write = new Pending { Target = target, Stage = stage, Original = original, Hash = before, Backup = backup, RotateBackup = rotate };
			pending.Add(target, write);
			return write;
		}
	}

	private TexRef Resolve(string gameRoot, RecoverableTextureMod entry)
	{
		DirectModArchive.ValidatePath(entry.RelativePath);
		string local = IndexService.FindLocalRoot(gameRoot)!;
		string root = entry.StorageKind switch { "LocalData" => local, "StreamingAssets" => IndexService.StreamingRoot(gameRoot), "GameRoot" => gameRoot, _ => throw new InvalidDataException("恢复资源来源无效。") };
		var paths = new List<(string Path, string Root)> { (Path.Combine(root, entry.RelativePath), root) };
		if (ushort.TryParse(entry.CardKey, out ushort card) && card > 0)
			foreach (string alternateRoot in new[] { local, IndexService.StreamingRoot(gameRoot) })
				paths.AddRange(IndexService.CardIllustrationBundleCandidates(alternateRoot, entry.CardKey).Select(x => (x, alternateRoot)));
		foreach (var candidate in paths.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
		{
			if (!File.Exists(candidate.Path)) continue;
			DirectModArchive.EnsureNoLinks(candidate.Path);
			string kind = candidate.Root == local ? (entry.CardKey.Length > 0 ? "本地卡图" : entry.SourceKind)
				: candidate.Root == IndexService.StreamingRoot(gameRoot) ? "游戏内图片" : entry.SourceKind;
			var textures = _engine.ScanBundle(candidate.Path, candidate.Root, kind, includeDependencies: false).Textures;
			var exact = textures.Where(x => x.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
			if (exact.Length == 1) return exact[0];
			var byCard = textures.Where(x => x.CardKey == entry.CardKey && CardTextureRules.HasCardIdentity(x)).ToArray();
			if (entry.CardKey.Length > 0 && byCard.Length == 1) return byCard[0];
		}
		throw new FileNotFoundException("当前游戏中无法唯一定位目标 Texture2D，请先完成资源下载或重新定位：" + entry.CardKey + " " + entry.Name);
	}

	private static TexRef CloneAt(TexRef value, string path) => new() { BundlePath = path, RelativeBundlePath = Path.GetFileName(path), SourceKind = value.SourceKind,
		Name = value.Name, CardKey = value.CardKey, AssetFileName = value.AssetFileName, PathId = value.PathId, Width = value.Width, Height = value.Height };
	internal static byte[] EncodeMappings(IEnumerable<OverFrameMapping> mappings)
	{
		var items = mappings.OrderBy(x => x.CardId).ToArray();
		byte[] data = new byte[items.Length * 4];
		for (int i = 0; i < items.Length; i++) { BitConverter.TryWriteBytes(data.AsSpan(i * 4, 2), items[i].CardId); BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2, 2), items[i].ArtId); }
		return data;
	}
	internal static string PixelHash(byte[] png)
	{
		var info = Image.Identify(png) ?? throw new InvalidDataException("恢复图片无效。");
		if ((long)info.Width * info.Height > 67108864) throw new InvalidDataException("恢复图片超出像素预算。");
		using var image = Image.Load<Rgba32>(png);
		byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
		image.CopyPixelDataTo(pixels);
		return image.Width + "x" + image.Height + ":" + Convert.ToHexString(SHA256.HashData(pixels));
	}
	private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
