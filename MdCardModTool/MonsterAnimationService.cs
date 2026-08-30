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
		List<MonsterAnimationTemplate> templates = new List<MonsterAnimationTemplate>();
		foreach (MonsterAnimationAssetRef skeleton in set.Skeletons)
		{
			string backupRoot = Path.Combine(gameRoot, "_MD卡图备份", skeleton.ModSourceKind);
			string backupPath = Path.Combine(backupRoot, skeleton.RelativeBundlePath);
			templates.Add(MonsterAnimationTemplate.Parse((File.Exists(backupPath) ? _engine.FindTextAssetFast(backupPath, backupRoot, skeleton.Name) : null)?.Data ?? _engine.ReadTextAsset(skeleton).Data));
		}
		return MergeTemplates(templates);
	}

	private static MonsterAnimationTemplate MergeTemplates(IEnumerable<MonsterAnimationTemplate> source)
	{
		MonsterAnimationTemplate[] array = source.ToArray();
		if (array.Length == 0)
		{
			throw new InvalidOperationException("未找到可读取的动画模板。");
		}
		string[] names = array.SelectMany((MonsterAnimationTemplate x) => x.EffectiveAnimationNames).Distinct<string>(StringComparer.Ordinal).ToArray();
		return array[0]with
		{
			AnimationName = names[0],
			AnimationNames = names
		};
	}

	public bool HasCreationTransaction(string gameRoot, string cardId)
	{
		string? localRoot = IndexService.FindLocalRoot(gameRoot);
		return localRoot != null && File.Exists(TransactionPath(localRoot, cardId));
	}

	public void Apply(string gameRoot, MonsterAnimationSet set, MonsterAnimationBuildResult animation)
	{
		if (!set.IsComplete)
		{
			throw new InvalidOperationException("该卡没有定位到教程要求的两套 Texture2D + Atlas + JS，不能进行不完整替换。");
		}
		string rollbackRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_rollback_" + Guid.NewGuid().ToString("N"));
		string[] bundles = set.Assets.Select((MonsterAnimationAssetRef x) => x.BundlePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		Directory.CreateDirectory(rollbackRoot);
		try
		{
			for (int i = 0; i < bundles.Length; i++)
			{
				File.Copy(bundles[i], Path.Combine(rollbackRoot, $"{i:D3}.bundle"), overwrite: true);
			}
			try
			{
				IReadOnlyList<MonsterAnimationAssetTriplet> pairs = MonsterAnimationAssetPairing.FindComplete(set);
				if (!pairs.Any(pair => pair.Tier == "HighEnd_HD") || !pairs.Any(pair => pair.Tier == "SD"))
				{
					throw new InvalidDataException("动画资源没有形成可写入的 HD/SD 配对。" );
				}
				AnimationAtlasTextureData encodedHd = _engine.EncodeAnimationAtlas(animation.Hd.AtlasImage);
				AnimationAtlasTextureData encodedSd = _engine.EncodeAnimationAtlas(animation.Sd.AtlasImage);
				HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
				foreach (MonsterAnimationAssetTriplet pair in pairs)
				{
					MonsterAnimationTierBuildResult tier = animation.ForTier(pair.Tier);
					if (written.Add(pair.Texture.BundlePath))
					{
						_engine.ReplaceAnimationAtlas(pair.Texture, pair.Tier == "SD" ? encodedSd : encodedHd,
							Path.Combine(gameRoot, "_MD卡图备份", pair.Texture.ModSourceKind));
					}
					if (written.Add(pair.Atlas.BundlePath))
					{
						_engine.ReplaceTextAsset(_engine.ReadTextAsset(pair.Atlas), Encoding.UTF8.GetBytes(tier.AtlasText),
							Path.Combine(gameRoot, "_MD卡图备份", pair.Atlas.ModSourceKind));
					}
					if (written.Add(pair.Skeleton.BundlePath))
					{
						_engine.ReplaceTextAsset(_engine.ReadTextAsset(pair.Skeleton), tier.SkeletonJson,
							Path.Combine(gameRoot, "_MD卡图备份", pair.Skeleton.ModSourceKind));
					}
				}
				MonsterAnimationCompatibilityValidator.Validate(set, requireExactlySixBundles: false);
			}
			catch
			{
				for (int i2 = 0; i2 < bundles.Length; i2++)
				{
					File.Copy(Path.Combine(rollbackRoot, $"{i2:D3}.bundle"), bundles[i2], overwrite: true);
				}
				throw;
			}
		}
		finally
		{
			try
			{
				Directory.Delete(rollbackRoot, recursive: true);
			}
			catch
			{
			}
		}
	}

	public int Restore(string gameRoot, MonsterAnimationSet set)
	{
		string? localRoot = IndexService.FindLocalRoot(gameRoot);
		if (localRoot != null && RestoreCreatedSet(gameRoot, localRoot, set.CardId, out int transactionRestored))
		{
			return transactionRestored;
		}
		int restored = 0;
		foreach (MonsterAnimationAssetRef asset in from x in set.Assets.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.BundlePath, StringComparer.OrdinalIgnoreCase)
			select x.First())
		{
			string backup = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
			if (File.Exists(backup))
			{
				File.Copy(backup, asset.BundlePath, overwrite: true);
				restored++;
			}
		}
		return restored;
	}

	private static bool RestoreCreatedSet(string gameRoot, string localRoot, string cardId, out int restored)
	{
		restored = 0;
		string path = TransactionPath(localRoot, cardId);
		if (!File.Exists(path)) return false;
		AnimationTransactionRecord record = JsonSerializer.Deserialize<AnimationTransactionRecord>(File.ReadAllText(path))
			?? throw new InvalidDataException("动画事务记录无法读取。" );
		foreach (AnimationTransactionFile file in record.Files)
		{
			string target = SafeInside(localRoot, file.RelativePath);
			if (file.State == AnimationBundleState.Created)
			{
				if (File.Exists(target))
				{
					File.Delete(target);
					restored++;
				}
			}
			else if (File.Exists(file.BackupPath))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(file.BackupPath, target, overwrite: true);
				restored++;
			}
		}
		File.Delete(path);
		return true;
	}

	internal static string TransactionPath(string localRoot, string cardId)
	{
		string profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(localRoot)))).Substring(0, 12);
		return Path.Combine(AppSettingsStore.AppDataRoot, "animation-transactions", profile, "card-" + cardId + ".json");
	}

	private static string SafeInside(string root, string relative)
	{
		if (Path.IsPathRooted(relative)) throw new InvalidDataException("动画事务包含绝对相对路径。" );
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
		if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("动画路径越出了目标目录。" );
		return full;
	}
}
