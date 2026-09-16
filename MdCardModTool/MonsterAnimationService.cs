using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MdCardModTool;

public sealed class MonsterAnimationService
{
	private readonly ModEngine _engine = new ModEngine();

	public MonsterAnimationTemplate ReadTemplate(MonsterAnimationSet set)
	{
		if (set.Skeletons.Count == 0)
		{
			throw new InvalidOperationException("未找到 P卡号JS。");
		}
		return MergeTemplates(set.Skeletons.Select((MonsterAnimationAssetRef skeleton) => MonsterAnimationTemplate.Parse(_engine.ReadTextAsset(skeleton).Data)));
	}

	public MonsterAnimationTemplate ReadTemplate(string gameRoot, MonsterAnimationSet set)
	{
		if (set.Skeletons.Count == 0)
		{
			throw new InvalidOperationException("未找到 P卡号JS。");
		}
		List<MonsterAnimationTemplate> list = new List<MonsterAnimationTemplate>();
		foreach (MonsterAnimationAssetRef skeleton in set.Skeletons)
		{
			string text = Path.Combine(gameRoot, "_MD卡图备份", skeleton.ModSourceKind);
			string text2 = Path.Combine(text, skeleton.RelativeBundlePath);
			list.Add(MonsterAnimationTemplate.Parse((File.Exists(text2) ? _engine.FindTextAssetFast(text2, text, skeleton.Name) : null)?.Data ?? _engine.ReadTextAsset(skeleton).Data));
		}
		return MergeTemplates(list);
	}

	private static MonsterAnimationTemplate MergeTemplates(IEnumerable<MonsterAnimationTemplate> source)
	{
		MonsterAnimationTemplate[] array = source.ToArray();
		if (array.Length == 0)
		{
			throw new InvalidOperationException("未找到可读取的动画模板。");
		}
		string[] array2 = array.SelectMany((MonsterAnimationTemplate x) => x.EffectiveAnimationNames).Distinct<string>(StringComparer.Ordinal).ToArray();
		return array[0]with
		{
			AnimationName = array2[0],
			AnimationNames = array2
		};
	}

	public bool HasCreationTransaction(string gameRoot, string cardId)
	{
		string text = IndexService.FindLocalRoot(gameRoot);
		if (text != null)
		{
			return File.Exists(TransactionPath(text, cardId));
		}
		return false;
	}

	public void Apply(string gameRoot, MonsterAnimationSet set, MonsterAnimationBuildResult animation, Action<int>? afterCommit = null)
	{
		if (!set.IsComplete)
		{
			throw new InvalidOperationException("动画 HD/SD 六资源不完整，未开始替换。");
		}
		AnimationWriteLease.Preflight(set);
		string text = Path.Combine(Path.GetDirectoryName(set.Assets[0].BundlePath), ".md-apply-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		bool flag = false;
		MonsterAnimationAssetRef[] array = set.Assets.DistinctBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef a) => a.BundlePath, StringComparer.OrdinalIgnoreCase).ToArray();
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			MonsterAnimationAssetRef[] array2 = array;
			foreach (MonsterAnimationAssetRef monsterAnimationAssetRef in array2)
			{
				string text2 = Path.Combine(text, paths.Count + ".bundle");
				File.Copy(monsterAnimationAssetRef.BundlePath, text2 + ".original");
				File.Copy(text2 + ".original", text2);
				paths.Add(monsterAnimationAssetRef.BundlePath, text2);
				dictionary.Add(monsterAnimationAssetRef.BundlePath, text2 + ".original");
				using FileStream source = File.OpenRead(text2);
				dictionary2.Add(monsterAnimationAssetRef.BundlePath, Convert.ToHexString(SHA256.HashData(source)));
			}
			MonsterAnimationSet set2 = new MonsterAnimationSet
			{
				CardId = set.CardId,
				Assets = set.Assets.Select((MonsterAnimationAssetRef a) => MonsterAnimationTransferService.AtPath(a, paths[a.BundlePath])).ToList()
			};
			ApplyStaged(text, set2, animation, ResourceSource.IsMobile(gameRoot));
			array2 = array;
			foreach (MonsterAnimationAssetRef monsterAnimationAssetRef2 in array2)
			{
				string text3 = Path.Combine(gameRoot, "_MD卡图备份", monsterAnimationAssetRef2.ModSourceKind, monsterAnimationAssetRef2.RelativeBundlePath);
				Directory.CreateDirectory(Path.GetDirectoryName(text3));
				if (!File.Exists(text3))
				{
					File.Copy(dictionary[monsterAnimationAssetRef2.BundlePath], text3);
				}
			}
			using AnimationWriteLease animationWriteLease = AnimationWriteLease.Acquire(paths.Keys);
			foreach (KeyValuePair<string, FileStream> stream in animationWriteLease.Streams)
			{
				if (Convert.ToHexString(SHA256.HashData(stream.Value)) != dictionary2[stream.Key])
				{
					throw new IOException("动画在制作期间已改变，未提交，请重新载入。");
				}
			}
			List<string> list = new List<string>();
			try
			{
				foreach (KeyValuePair<string, string> item2 in paths)
				{
					File.Replace(item2.Value, item2.Key, null);
					list.Add(item2.Key);
					afterCommit?.Invoke(list.Count);
				}
			}
			catch (Exception element)
			{
				List<Exception> list2 = new List<Exception>();
				foreach (string item3 in list.AsEnumerable().Reverse())
				{
					try
					{
						File.Copy(dictionary[item3], item3, overwrite: true);
					}
					catch (Exception item)
					{
						list2.Add(item);
					}
				}
				if (list2.Count > 0)
				{
					flag = true;
					throw new AggregateException("回滚未完成；恢复副本保留在：" + text, list2.Prepend(element));
				}
				throw;
			}
		}
		finally
		{
			if (!flag)
			{
				try
				{
					Directory.Delete(text, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private void ApplyStaged(string gameRoot, MonsterAnimationSet set, MonsterAnimationBuildResult animation, bool mobile = false)
	{
		if (!set.IsComplete)
		{
			throw new InvalidOperationException("该卡没有定位到教程要求的两套 Texture2D + Atlas + JS，不能进行不完整替换。");
		}
		string text = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_rollback_" + Guid.NewGuid().ToString("N"));
		string[] array = set.Assets.Select((MonsterAnimationAssetRef x) => x.BundlePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		Directory.CreateDirectory(text);
		try
		{
			for (int num = 0; num < array.Length; num++)
			{
				File.Copy(array[num], Path.Combine(text, $"{num:D3}.bundle"), overwrite: true);
			}
			try
			{
				IReadOnlyList<MonsterAnimationAssetTriplet> readOnlyList = MonsterAnimationAssetPairing.FindComplete(set);
				if (readOnlyList.Count == 0 || (!mobile && (!readOnlyList.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "HighEnd_HD") || !readOnlyList.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "SD"))))
				{
					throw new InvalidDataException("动画资源没有形成可写入的 HD/SD 配对。");
				}
				AnimationAtlasTextureData animationAtlasTextureData = (readOnlyList.Any((MonsterAnimationAssetTriplet p) => p.Tier == "HighEnd_HD") ? _engine.EncodeAnimationAtlas(animation.Hd.AtlasImage, mobile) : null);
				AnimationAtlasTextureData animationAtlasTextureData2 = (readOnlyList.Any((MonsterAnimationAssetTriplet p) => p.Tier == "SD") ? _engine.EncodeAnimationAtlas(animation.Sd.AtlasImage, mobile) : null);
				HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (MonsterAnimationAssetTriplet item in readOnlyList)
				{
					MonsterAnimationTierBuildResult monsterAnimationTierBuildResult = animation.ForTier(item.Tier);
					if (hashSet.Add(item.Texture.BundlePath))
					{
						_engine.ReplaceAnimationAtlas(item.Texture, (item.Tier == "SD") ? animationAtlasTextureData2 : animationAtlasTextureData, Path.Combine(gameRoot, "_MD卡图备份", item.Texture.ModSourceKind));
					}
					if (hashSet.Add(item.Atlas.BundlePath))
					{
						_engine.ReplaceTextAsset(_engine.ReadTextAsset(item.Atlas), Encoding.UTF8.GetBytes(monsterAnimationTierBuildResult.AtlasText.Replace("P" + set.CardId + ".png", item.Texture.Name + ".png", StringComparison.Ordinal)), Path.Combine(gameRoot, "_MD卡图备份", item.Atlas.ModSourceKind));
					}
					if (hashSet.Add(item.Skeleton.BundlePath))
					{
						_engine.ReplaceTextAsset(_engine.ReadTextAsset(item.Skeleton), monsterAnimationTierBuildResult.SkeletonJson, Path.Combine(gameRoot, "_MD卡图备份", item.Skeleton.ModSourceKind));
					}
				}
				MonsterAnimationCompatibilityValidator.Validate(set, requireExactlySixBundles: false);
			}
			catch
			{
				for (int num2 = 0; num2 < array.Length; num2++)
				{
					File.Copy(Path.Combine(text, $"{num2:D3}.bundle"), array[num2], overwrite: true);
				}
				throw;
			}
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}

	public int Restore(string gameRoot, MonsterAnimationSet set)
	{
		string text = IndexService.FindLocalRoot(gameRoot);
		if (text != null && RestoreCreatedSet(gameRoot, text, set.CardId, out var restored))
		{
			return restored;
		}
		int num = 0;
		foreach (MonsterAnimationAssetRef item in from x in set.Assets.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.BundlePath, StringComparer.OrdinalIgnoreCase)
			select x.First())
		{
			string text2 = Path.Combine(gameRoot, "_MD卡图备份", item.ModSourceKind, item.RelativeBundlePath);
			if (File.Exists(text2))
			{
				File.Copy(text2, item.BundlePath, overwrite: true);
				num++;
			}
		}
		return num;
	}

	private static bool RestoreCreatedSet(string gameRoot, string localRoot, string cardId, out int restored)
	{
		restored = 0;
		string path = TransactionPath(localRoot, cardId);
		if (!File.Exists(path))
		{
			return false;
		}
		foreach (AnimationTransactionFile file in (JsonSerializer.Deserialize<AnimationTransactionRecord>(File.ReadAllText(path)) ?? throw new InvalidDataException("动画事务记录无法读取。")).Files)
		{
			string text = SafeInside(localRoot, file.RelativePath);
			if (file.State == AnimationBundleState.Created)
			{
				if (File.Exists(text))
				{
					File.Delete(text);
					restored++;
				}
			}
			else if (File.Exists(file.BackupPath))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(text));
				File.Copy(file.BackupPath, text, overwrite: true);
				restored++;
			}
		}
		File.Delete(path);
		return true;
	}

	internal static string TransactionPath(string localRoot, string cardId)
	{
		string path = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(localRoot)))).Substring(0, 12);
		return Path.Combine(AppSettingsStore.AppDataRoot, "animation-transactions", path, "card-" + cardId + ".json");
	}

	private static string SafeInside(string root, string relative)
	{
		if (Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("动画事务包含绝对相对路径。");
		}
		string text = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, relative));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("动画路径越出了目标目录。");
		}
		return fullPath;
	}
}
