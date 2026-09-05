using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

/// <summary>Replaces an existing cut-in as a complete paired rig, never mixes individual limbs.</summary>
public static class MonsterAnimationTransferService
{
	public static int Replace(string gameRoot, MonsterAnimationSet target, MonsterAnimationSet donor, Action<int>? afterCommit = null)
	{
		EnsureGameClosed();
		AnimationWriteLease.Preflight(target);
		if (target.CardId == donor.CardId) throw new InvalidOperationException("来源卡与目标卡不能相同。");
		var targets = MonsterAnimationAssetPairing.FindComplete(target);
		var sources = MonsterAnimationAssetPairing.FindComplete(donor);
		if (!targets.Any(x => x.Tier == "SD") || !targets.Any(x => x.Tier == "HighEnd_HD"))
			throw new InvalidOperationException("目标卡必须已有完整 HD/SD 原生动画；此操作不会为无动画卡创建召唤触发。");
		ModEngine engine = new();
		string stage = Path.Combine(Path.GetDirectoryName(targets[0].Texture.BundlePath)!, ".md-animation-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stage);
		var pending = new Dictionary<string, (MonsterAnimationAssetRef Original, MonsterAnimationAssetRef Staged, string Snapshot, string Hash)>(StringComparer.OrdinalIgnoreCase);
		var encoded = new Dictionary<string, AnimationAtlasTextureData>(StringComparer.OrdinalIgnoreCase);
		bool preserveRecovery = false;
		try
		{
			foreach (var pair in targets)
			{
				var source = sources.Where(x => x.Tier == pair.Tier)
					.OrderByDescending(x => x.Region == pair.Region).ThenByDescending(x => x.Scale == pair.Scale).FirstOrDefault()
					?? throw new InvalidDataException("来源卡缺少 " + pair.Tier + " 动画。");
				if (!encoded.TryGetValue(source.Texture.BundlePath, out var texture))
				{
					using Image<Rgba32> image = Image.Load<Rgba32>(engine.DecodePng(source.Texture.AsTexture()));
					texture = engine.EncodeAnimationAtlas(image);
					encoded.Add(source.Texture.BundlePath, texture);
				}
				string atlas = Encoding.UTF8.GetString(engine.ReadTextAsset(source.Atlas).Data).TrimEnd('\0').Replace("\r", "");
				string[] lines = atlas.Split('\n');
				int page = Array.FindIndex(lines, x => !string.IsNullOrWhiteSpace(x));
				if (page < 0) throw new InvalidDataException("来源 Atlas 为空。");
				lines[page] = "P" + target.CardId + ".png";
				var sourceJson = JsonNode.Parse(Encoding.UTF8.GetString(engine.ReadTextAsset(source.Skeleton).Data).TrimEnd('\0'))!.AsObject();
				var targetTemplate = MonsterAnimationTemplate.Parse(engine.ReadTextAsset(pair.Skeleton).Data);
				var sourceTemplate = MonsterAnimationTemplate.Parse(Encoding.UTF8.GetBytes(sourceJson.ToJsonString()));
				if (sourceTemplate.SpineVersion.Split('.')[0..2].SequenceEqual(targetTemplate.SpineVersion.Split('.')[0..2]) == false)
					throw new InvalidDataException("来源与目标的 Spine 主次版本不兼容。");
				var animations = sourceJson["animations"]!.AsObject();
				JsonNode first = animations.First().Value ?? throw new InvalidDataException("来源动画为空。");
				foreach (string name in targetTemplate.EffectiveAnimationNames)
					if (!animations.ContainsKey(name)) animations[name] = first.DeepClone();
				Prepare(pair.Texture, temp => engine.ReplaceAnimationAtlas(temp, texture, Path.Combine(stage, "work-backups")));
				Prepare(pair.Atlas, temp => engine.ReplaceTextAsset(engine.ReadTextAsset(temp), Encoding.UTF8.GetBytes(string.Join("\n", lines)), Path.Combine(stage, "work-backups")));
				Prepare(pair.Skeleton, temp => engine.ReplaceTextAsset(engine.ReadTextAsset(temp), Encoding.UTF8.GetBytes(sourceJson.ToJsonString()), Path.Combine(stage, "work-backups")));
			}
			var stagedSet = new MonsterAnimationSet { CardId = target.CardId, Assets = pending.Values.Select(x => x.Staged).ToList() };
			var stagedPairs = MonsterAnimationAssetPairing.FindComplete(stagedSet);
			if (stagedPairs.Count != targets.Count) throw new InvalidDataException("临时资源的配对校验失败。");
			foreach (var pair in stagedPairs)
			{
				using Image image = Image.Load(engine.DecodePng(pair.Texture.AsTexture()));
				string atlas = Encoding.UTF8.GetString(engine.ReadTextAsset(pair.Atlas).Data).TrimStart('\r', '\n');
				if (!atlas.StartsWith("P" + target.CardId + ".png", StringComparison.Ordinal)) throw new InvalidDataException("图集页名不匹配。");
				_ = MonsterAnimationTemplate.Parse(engine.ReadTextAsset(pair.Skeleton).Data);
			}
			EnsureGameClosed();
			foreach (var item in pending.Values)
			{
				if (Hash(item.Original.BundlePath) != item.Hash) throw new IOException("目标动画在制作期间被其他程序修改，请重新加载。");
				string backup = Path.Combine(gameRoot, "_MD卡图备份", item.Original.ModSourceKind, item.Original.RelativeBundlePath);
				Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
				if (!File.Exists(backup)) File.Copy(item.Snapshot, backup);
			}
			var committed = new List<(string Path, string Snapshot)>();
			using var lease = AnimationWriteLease.Acquire(pending.Keys);
			foreach (var item in pending.Values)
				if (Convert.ToHexString(SHA256.HashData(lease.Streams[item.Original.BundlePath])) != item.Hash)
					throw new IOException("目标动画在制作期间已改变，未提交，请重新载入。");
			try
			{
				foreach (var item in pending.Values)
				{
					File.Replace(item.Staged.BundlePath, item.Original.BundlePath, null);
					committed.Add((item.Original.BundlePath, item.Snapshot));
					afterCommit?.Invoke(committed.Count);
				}
			}
			catch (Exception commitError)
			{
				try { foreach (var item in committed) File.Copy(item.Snapshot, item.Path, overwrite: true); }
				catch (Exception restoreError)
				{
					preserveRecovery = true;
					throw new AggregateException("提交与回滚失败；恢复副本保留于 " + stage, commitError, restoreError);
				}
				throw;
			}
			return pending.Count;

			void Prepare(MonsterAnimationAssetRef asset, Action<MonsterAnimationAssetRef> write)
			{
				if (pending.ContainsKey(asset.BundlePath)) return;
				string file = Path.Combine(stage, pending.Count + ".bundle");
				string snapshot = file + ".original";
				File.Copy(asset.BundlePath, snapshot);
				File.Copy(snapshot, file);
				var temp = AtPath(asset, file);
				pending.Add(asset.BundlePath, (asset, temp, snapshot, Hash(snapshot)));
				write(temp);
			}
		}
		finally { if (!preserveRecovery) { try { Directory.Delete(stage, recursive: true); } catch { } } }
	}

	internal static MonsterAnimationAssetRef AtPath(MonsterAnimationAssetRef asset, string path) => new()
	{
		BundlePath = path, RelativeBundlePath = asset.RelativeBundlePath, AssetFileName = asset.AssetFileName,
		PathId = asset.PathId, Name = asset.Name, CardId = asset.CardId, Kind = asset.Kind, StorageKind = asset.StorageKind
	};
	private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
	private static void EnsureGameClosed()
	{
		var games = Process.GetProcessesByName("masterduel");
		try { if (games.Length != 0) throw new InvalidOperationException("请完全退出 Master Duel 后再替换动画。"); }
		finally { foreach (var game in games) game.Dispose(); }
	}
}
