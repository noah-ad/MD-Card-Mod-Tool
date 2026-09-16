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

public static class MonsterAnimationTransferService
{
	public static int Replace(string gameRoot, MonsterAnimationSet target, MonsterAnimationSet donor, Action<int>? afterCommit = null)
	{
		EnsureGameClosed();
		AnimationWriteLease.Preflight(target);
		if (target.CardId == donor.CardId)
		{
			throw new InvalidOperationException("来源卡与目标卡不能相同。");
		}
		IReadOnlyList<MonsterAnimationAssetTriplet> readOnlyList = MonsterAnimationAssetPairing.FindComplete(target);
		IReadOnlyList<MonsterAnimationAssetTriplet> source = MonsterAnimationAssetPairing.FindComplete(donor);
		if (readOnlyList.Count == 0 || (!target.IsMobile && (!readOnlyList.Any((MonsterAnimationAssetTriplet x) => x.Tier == "SD") || !readOnlyList.Any((MonsterAnimationAssetTriplet x) => x.Tier == "HighEnd_HD"))))
		{
			throw new InvalidOperationException("目标卡必须已有完整 HD/SD 原生动画；此操作不会为无动画卡创建召唤触发。");
		}
		ModEngine engine = new ModEngine();
		string stage = Path.Combine(Path.GetDirectoryName(readOnlyList[0].Texture.BundlePath), ".md-animation-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stage);
		Dictionary<string, (MonsterAnimationAssetRef Original, MonsterAnimationAssetRef Staged, string Snapshot, string Hash)> pending = new Dictionary<string, (MonsterAnimationAssetRef, MonsterAnimationAssetRef, string, string)>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, AnimationAtlasTextureData> dictionary = new Dictionary<string, AnimationAtlasTextureData>(StringComparer.OrdinalIgnoreCase);
		bool flag = false;
		try
		{
			foreach (MonsterAnimationAssetTriplet pair in readOnlyList)
			{
				MonsterAnimationAssetTriplet monsterAnimationAssetTriplet = (from x in source
					where x.Tier == pair.Tier
					orderby x.Region == pair.Region descending, x.Scale == pair.Scale descending
					select x).FirstOrDefault() ?? throw new InvalidDataException("来源卡缺少 " + pair.Tier + " 动画。");
				if (!dictionary.TryGetValue(monsterAnimationAssetTriplet.Texture.BundlePath, out var texture))
				{
					using Image<Rgba32> atlas = Image.Load<Rgba32>(engine.DecodePng(monsterAnimationAssetTriplet.Texture.AsTexture()));
					texture = engine.EncodeAnimationAtlas(atlas, ResourceSource.IsMobile(gameRoot));
					dictionary.Add(monsterAnimationAssetTriplet.Texture.BundlePath, texture);
				}
				string text = Encoding.UTF8.GetString(engine.ReadTextAsset(monsterAnimationAssetTriplet.Atlas).Data).TrimEnd('\0').Replace("\r", "");
				string[] lines = text.Split('\n');
				if (lines.Count((string line) => line.Trim().EndsWith(".png", StringComparison.OrdinalIgnoreCase)) != 1)
				{
					throw new InvalidDataException("来源动画使用多页图集，当前不能整套移植；可预览，或向该卡导入视频替换。");
				}
				int num = Array.FindIndex(lines, (string x) => !string.IsNullOrWhiteSpace(x));
				if (num < 0)
				{
					throw new InvalidDataException("来源 Atlas 为空。");
				}
				lines[num] = pair.Texture.Name + ".png";
				JsonObject sourceJson = JsonNode.Parse(Encoding.UTF8.GetString(engine.ReadTextAsset(monsterAnimationAssetTriplet.Skeleton).Data).TrimEnd('\0')).AsObject();
				MonsterAnimationTemplate monsterAnimationTemplate = MonsterAnimationTemplate.Parse(engine.ReadTextAsset(pair.Skeleton).Data);
				if (!MonsterAnimationTemplate.Parse(Encoding.UTF8.GetBytes(sourceJson.ToJsonString())).SpineVersion.Split('.')[0..2].SequenceEqual(monsterAnimationTemplate.SpineVersion.Split('.')[0..2]))
				{
					throw new InvalidDataException("来源与目标的 Spine 主次版本不兼容。");
				}
				JsonObject jsonObject = sourceJson["animations"].AsObject();
				JsonNode jsonNode = jsonObject.First().Value ?? throw new InvalidDataException("来源动画为空。");
				foreach (string effectiveAnimationName in monsterAnimationTemplate.EffectiveAnimationNames)
				{
					if (!jsonObject.ContainsKey(effectiveAnimationName))
					{
						jsonObject[effectiveAnimationName] = jsonNode.DeepClone();
					}
				}
				Prepare(pair.Texture, delegate(MonsterAnimationAssetRef temp)
				{
					engine.ReplaceAnimationAtlas(temp, texture, Path.Combine(stage, "work-backups"));
				});
				Prepare(pair.Atlas, delegate(MonsterAnimationAssetRef temp)
				{
					engine.ReplaceTextAsset(engine.ReadTextAsset(temp), Encoding.UTF8.GetBytes(string.Join("\n", lines)), Path.Combine(stage, "work-backups"));
				});
				Prepare(pair.Skeleton, delegate(MonsterAnimationAssetRef temp)
				{
					engine.ReplaceTextAsset(engine.ReadTextAsset(temp), Encoding.UTF8.GetBytes(sourceJson.ToJsonString()), Path.Combine(stage, "work-backups"));
				});
			}
			IReadOnlyList<MonsterAnimationAssetTriplet> readOnlyList2 = MonsterAnimationAssetPairing.FindComplete(new MonsterAnimationSet
			{
				CardId = target.CardId,
				Assets = pending.Values.Select<(MonsterAnimationAssetRef, MonsterAnimationAssetRef, string, string), MonsterAnimationAssetRef>(((MonsterAnimationAssetRef Original, MonsterAnimationAssetRef Staged, string Snapshot, string Hash) x) => x.Staged).ToList()
			});
			if (readOnlyList2.Count != readOnlyList.Count)
			{
				throw new InvalidDataException("临时资源的配对校验失败。");
			}
			foreach (MonsterAnimationAssetTriplet item in readOnlyList2)
			{
				using (Image.Load(engine.DecodePng(item.Texture.AsTexture())))
				{
					if (!Encoding.UTF8.GetString(engine.ReadTextAsset(item.Atlas).Data).TrimStart('\r', '\n').StartsWith(item.Texture.Name + ".png", StringComparison.Ordinal))
					{
						throw new InvalidDataException("图集页名不匹配。");
					}
					MonsterAnimationTemplate.Parse(engine.ReadTextAsset(item.Skeleton).Data);
				}
			}
			EnsureGameClosed();
			foreach (var value in pending.Values)
			{
				if (Hash(value.Original.BundlePath) != value.Hash)
				{
					throw new IOException("目标动画在制作期间被其他程序修改，请重新加载。");
				}
				string text2 = Path.Combine(gameRoot, "_MD卡图备份", value.Original.ModSourceKind, value.Original.RelativeBundlePath);
				Directory.CreateDirectory(Path.GetDirectoryName(text2));
				if (!File.Exists(text2))
				{
					File.Copy(value.Snapshot, text2);
				}
			}
			List<(string, string)> list = new List<(string, string)>();
			using AnimationWriteLease animationWriteLease = AnimationWriteLease.Acquire(pending.Keys);
			foreach (var value2 in pending.Values)
			{
				if (Convert.ToHexString(SHA256.HashData(animationWriteLease.Streams[value2.Original.BundlePath])) != value2.Hash)
				{
					throw new IOException("目标动画在制作期间已改变，未提交，请重新载入。");
				}
			}
			try
			{
				foreach (var value3 in pending.Values)
				{
					File.Replace(value3.Staged.BundlePath, value3.Original.BundlePath, null);
					list.Add((value3.Original.BundlePath, value3.Snapshot));
					afterCommit?.Invoke(list.Count);
				}
			}
			catch (Exception ex)
			{
				try
				{
					foreach (var item2 in list)
					{
						File.Copy(item2.Item2, item2.Item1, overwrite: true);
					}
				}
				catch (Exception ex2)
				{
					flag = true;
					throw new AggregateException("提交与回滚失败；恢复副本保留于 " + stage, ex, ex2);
				}
				throw;
			}
			return pending.Count;
		}
		finally
		{
			if (!flag)
			{
				try
				{
					Directory.Delete(stage, recursive: true);
				}
				catch
				{
				}
			}
		}
		void Prepare(MonsterAnimationAssetRef asset, Action<MonsterAnimationAssetRef> write)
		{
			if (!pending.ContainsKey(asset.BundlePath))
			{
				string text3 = Path.Combine(stage, pending.Count + ".bundle");
				string text4 = text3 + ".original";
				File.Copy(asset.BundlePath, text4);
				File.Copy(text4, text3);
				MonsterAnimationAssetRef monsterAnimationAssetRef = AtPath(asset, text3);
				pending.Add(asset.BundlePath, (asset, monsterAnimationAssetRef, text4, Hash(text4)));
				write(monsterAnimationAssetRef);
			}
		}
	}

	internal static MonsterAnimationAssetRef AtPath(MonsterAnimationAssetRef asset, string path)
	{
		return new MonsterAnimationAssetRef
		{
			BundlePath = path,
			RelativeBundlePath = asset.RelativeBundlePath,
			AssetFileName = asset.AssetFileName,
			PathId = asset.PathId,
			Name = asset.Name,
			CardId = asset.CardId,
			Kind = asset.Kind,
			StorageKind = asset.StorageKind
		};
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static void EnsureGameClosed()
	{
		Process[] processesByName = Process.GetProcessesByName("masterduel");
		try
		{
			if (processesByName.Length != 0)
			{
				throw new InvalidOperationException("请完全退出 Master Duel 后再替换动画。");
			}
		}
		finally
		{
			Process[] array = processesByName;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Dispose();
			}
		}
	}
}
