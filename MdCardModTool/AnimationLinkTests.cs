using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MdCardModTool;

internal static class AnimationLinkTests
{
	public static void Run(string gameRoot, string output)
	{
		MonsterAnimationSet monsterAnimationSet = MonsterAnimationIndexService.Find(gameRoot, "22524");
		IReadOnlyList<MonsterAnimationAssetTriplet> source = MonsterAnimationAssetPairing.FindComplete(monsterAnimationSet);
		if (!monsterAnimationSet.IsComplete || !source.Any((MonsterAnimationAssetTriplet p) => p.Tier == "SD" && p.Scale == "0.2925"))
		{
			throw new InvalidDataException("22524 companion SD resource was not resolved");
		}
		string text = Path.Combine(output, "game");
		string text2 = Path.Combine(text, "LocalData", "newcard-test", "0000");
		Directory.CreateDirectory(text2);
		IndexService.SetPreferredLocalRoot(text, text2);
		Dictionary<string, string> source2 = monsterAnimationSet.Assets.ToDictionary((MonsterAnimationAssetRef a) => a.BundlePath, (MonsterAnimationAssetRef a) => Hash(a.BundlePath));
		foreach (MonsterAnimationAssetRef asset in monsterAnimationSet.Assets)
		{
			string text3 = Path.Combine((asset.StorageKind == "StreamingAssets") ? IndexService.StreamingRoot(text) : text2, asset.RelativeBundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(text3));
			File.Copy(asset.BundlePath, text3, overwrite: true);
		}
		MonsterAnimationAssetTriplet sd = source.Single((MonsterAnimationAssetTriplet p) => p.Tier == "SD");
		PortableMonsterAnimationIndex index = new PortableMonsterAnimationIndex
		{
			GameBuildId = PortableIndexService.GetGameBuildId(text),
			Assets = monsterAnimationSet.Assets.Where((MonsterAnimationAssetRef a) => a.BundlePath != sd.Texture.BundlePath && a.BundlePath != sd.Atlas.BundlePath).ToList()
		};
		string path = MonsterAnimationIndexService.CachePath(text);
		try
		{
			MonsterAnimationIndexService.Write(path, index);
			PortableMonsterAnimationIndex portableMonsterAnimationIndex = MonsterAnimationIndexService.EnsureCurrentIndex(text);
			if (!MonsterAnimationIndexService.CompleteCardIds(portableMonsterAnimationIndex).Contains("22524"))
			{
				throw new InvalidDataException("Same-build new-card link was not repaired");
			}
			if (MonsterAnimationIndexService.EnsureCurrentIndex(text).Assets.Count != portableMonsterAnimationIndex.Assets.Count)
			{
				throw new InvalidDataException("Refresh duplicated links");
			}
			if (source2.Any((KeyValuePair<string, string> x) => Hash(x.Key) != x.Value))
			{
				throw new InvalidDataException("Real game files changed");
			}
			Console.WriteLine("card=22524; hd=0.585; sd=0.2925; sixBundles=True; sameBuildRefresh=True; idempotent=True; gameWrites=False; ready=True");
		}
		finally
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}

	private static string Hash(string file)
	{
		using FileStream source = File.OpenRead(file);
		return Convert.ToHexString(SHA256.HashData(source));
	}
}
