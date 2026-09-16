using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed class MonsterAnimationRawAssetService
{
	public const string ManifestFileName = "_动画资源映射.json";

	private static readonly string[] Scales = BuildCandidateScales();

	private readonly ModEngine _engine = new ModEngine();

	public MonsterAnimationAssetProfile ResolveProfile(MonsterAnimationAssetRef asset)
	{
		string text = Normalize(asset.RelativeBundlePath);
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string text2 in array)
		{
			string value = "Duel/Timeline/Duel/MonsterCutIn/" + text2 + "/P" + asset.CardId;
			string[] array2 = new string[2] { "HighEnd_HD", "SD" };
			foreach (string text3 in array2)
			{
				if (asset.Kind == MonsterAnimationAssetKind.Skeleton)
				{
					if (text == Normalize(IndexService.ResourceBundleRelativePath($"{value}/{text3}/P{asset.CardId}JS")))
					{
						return new MonsterAnimationAssetProfile(text3, text2, "");
					}
					continue;
				}
				string[] scales = Scales;
				foreach (string text4 in scales)
				{
					string value2 = ((asset.Kind == MonsterAnimationAssetKind.Atlas) ? ("/P" + asset.CardId + ".atlas") : ("/P" + asset.CardId));
					if (text == Normalize(IndexService.ResourceBundleRelativePath($"{value}/{text3}/{text4}{value2}")))
					{
						return new MonsterAnimationAssetProfile(text3, text2, text4);
					}
				}
			}
		}
		return new MonsterAnimationAssetProfile("未识别组", asset.StorageKind, Path.GetFileName(asset.RelativeBundlePath));
	}

	public string ExportFileName(MonsterAnimationAssetRef asset)
	{
		MonsterAnimationAssetProfile monsterAnimationAssetProfile = ResolveProfile(asset);
		string text = asset.Kind switch
		{
			MonsterAnimationAssetKind.Texture => ".png",
			MonsterAnimationAssetKind.Atlas => ".atlas",
			_ => ".json",
		};
		string text2 = ((asset.Kind == MonsterAnimationAssetKind.Skeleton) ? (asset.Name + text) : (Path.GetFileNameWithoutExtension(asset.Name) + text));
		return Safe(monsterAnimationAssetProfile.FilePrefix + "_" + text2);
	}

	public byte[] Read(MonsterAnimationAssetRef asset)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			return NormalizeTextData(_engine.ReadTextAsset(asset).Data);
		}
		return _engine.DecodePng(asset.AsTexture());
	}

	public RawAnimationManifest ExportAll(MonsterAnimationSet set, string directory)
	{
		Directory.CreateDirectory(directory);
		RawAnimationManifest rawAnimationManifest = new RawAnimationManifest
		{
			CardId = set.CardId
		};
		HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (MonsterAnimationAssetRef item in Ordered(set.Assets))
		{
			string text = UniqueName(ExportFileName(item), used);
			File.WriteAllBytes(Path.Combine(directory, text), Read(item));
			rawAnimationManifest.Files.Add(new RawAnimationManifestEntry
			{
				FileName = text,
				RelativeBundlePath = Normalize(item.RelativeBundlePath),
				AssetFileName = item.AssetFileName,
				PathId = item.PathId,
				Kind = item.Kind
			});
		}
		File.WriteAllText(Path.Combine(directory, "_动画资源映射.json"), JsonSerializer.Serialize(rawAnimationManifest, new JsonSerializerOptions
		{
			WriteIndented = true
		}), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return rawAnimationManifest;
	}

	public void ReplaceOne(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
	{
		string text = TemporaryRollbackDirectory();
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, "000.bundle");
		File.Copy(asset.BundlePath, text2, overwrite: true);
		try
		{
			ValidateInput(asset.Kind, inputPath);
			Apply(gameRoot, asset, inputPath);
		}
		catch
		{
			File.Copy(text2, asset.BundlePath, overwrite: true);
			throw;
		}
		finally
		{
			TryDeleteDirectory(text);
		}
	}

	public int ImportAll(string gameRoot, MonsterAnimationSet set, string directory)
	{
		string text = Path.Combine(directory, "_动画资源映射.json");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("所选目录缺少 _动画资源映射.json，请先用本窗口“导出全部”建立可回导目录。", text);
		}
		RawAnimationManifest rawAnimationManifest = JsonSerializer.Deserialize<RawAnimationManifest>(File.ReadAllText(text)) ?? throw new InvalidDataException("动画资源映射无法读取。");
		if (rawAnimationManifest.FormatVersion != 1 || rawAnimationManifest.CardId != set.CardId)
		{
			throw new InvalidDataException($"映射属于卡号 {rawAnimationManifest.CardId}，当前窗口是 {set.CardId}。");
		}
		List<(MonsterAnimationAssetRef, string)> list = new List<(MonsterAnimationAssetRef, string)>();
		foreach (RawAnimationManifestEntry entry in rawAnimationManifest.Files)
		{
			if (entry.FileName != Path.GetFileName(entry.FileName))
			{
				throw new InvalidDataException("映射中包含越界文件名。");
			}
			MonsterAnimationAssetRef monsterAnimationAssetRef = set.Assets.FirstOrDefault((MonsterAnimationAssetRef x) => x.Kind == entry.Kind && x.PathId == entry.PathId && string.Equals(x.AssetFileName, entry.AssetFileName, StringComparison.Ordinal) && string.Equals(Normalize(x.RelativeBundlePath), Normalize(entry.RelativeBundlePath), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("当前游戏版本找不到映射资源：" + entry.FileName);
			string text2 = Path.Combine(directory, entry.FileName);
			if (!File.Exists(text2))
			{
				throw new FileNotFoundException("缺少待导入文件：" + entry.FileName, text2);
			}
			ValidateInput(monsterAnimationAssetRef.Kind, text2);
			list.Add((monsterAnimationAssetRef, text2));
		}
		if (list.Count == 0)
		{
			throw new InvalidDataException("映射中没有可导入的动画资源。");
		}
		string text3 = TemporaryRollbackDirectory();
		Directory.CreateDirectory(text3);
		string[] array = list.Select<(MonsterAnimationAssetRef, string), string>(((MonsterAnimationAssetRef Asset, string Path) x) => x.Asset.BundlePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		try
		{
			for (int num = 0; num < array.Length; num++)
			{
				File.Copy(array[num], Path.Combine(text3, $"{num:D3}.bundle"), overwrite: true);
			}
			try
			{
				foreach (var item in list)
				{
					Apply(gameRoot, item.Item1, item.Item2);
				}
			}
			catch
			{
				for (int num2 = 0; num2 < array.Length; num2++)
				{
					File.Copy(Path.Combine(text3, $"{num2:D3}.bundle"), array[num2], overwrite: true);
				}
				throw;
			}
		}
		finally
		{
			TryDeleteDirectory(text3);
		}
		return list.Count;
	}

	public bool Restore(string gameRoot, MonsterAnimationAssetRef asset)
	{
		string text = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
		if (!File.Exists(text))
		{
			return false;
		}
		File.Copy(text, asset.BundlePath, overwrite: true);
		return true;
	}

	private void Apply(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
	{
		string backupRoot = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind);
		if (asset.Kind == MonsterAnimationAssetKind.Texture)
		{
			using (Image<Rgba32> atlas = Image.Load<Rgba32>(inputPath))
			{
				_engine.ReplaceAnimationAtlas(asset, atlas, backupRoot);
				return;
			}
		}
		byte[] data = NormalizeTextData(File.ReadAllBytes(inputPath));
		_engine.ReplaceTextAsset(_engine.ReadTextAsset(asset), data, backupRoot);
	}

	private static void ValidateInput(MonsterAnimationAssetKind kind, string inputPath)
	{
		if (!File.Exists(inputPath))
		{
			throw new FileNotFoundException("找不到待替换文件。", inputPath);
		}
		if (kind == MonsterAnimationAssetKind.Texture)
		{
			ImageInfo imageInfo = Image.Identify(inputPath) ?? throw new InvalidDataException("无法读取动画图集图片。");
			if (imageInfo.Width < 1 || imageInfo.Height < 1 || imageInfo.Width > 16384 || imageInfo.Height > 16384)
			{
				throw new InvalidDataException("动画图集尺寸必须在 1–16384 像素范围内。");
			}
			return;
		}
		byte[] array = NormalizeTextData(File.ReadAllBytes(inputPath));
		if (array.Length == 0)
		{
			throw new InvalidDataException("文本资源不能为空。");
		}
		if (kind == MonsterAnimationAssetKind.Skeleton)
		{
			using (JsonDocument.Parse(array))
			{
				return;
			}
		}
		new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(array);
	}

	private static byte[] NormalizeTextData(byte[] data)
	{
		int num = (data.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0);
		int num2 = data.Length;
		while (num2 > num && data[num2 - 1] == 0)
		{
			num2--;
		}
		return data.AsSpan(num, num2 - num).ToArray();
	}

	private static IEnumerable<MonsterAnimationAssetRef> Ordered(IEnumerable<MonsterAnimationAssetRef> assets)
	{
		return assets.OrderBy((MonsterAnimationAssetRef x) => x.Kind).ThenBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef x) => x.RelativeBundlePath, StringComparer.OrdinalIgnoreCase);
	}

	private static string[] BuildCandidateScales()
	{
		return new string[10] { "1", "0.5", "0.89", "0.445", "0.56", "0.28", "0.75", "0.375", "0.8", "0.4" }.Concat(from x in Enumerable.Range(1, 2000)
			select ((double)x / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Distinct<string>(StringComparer.Ordinal).ToArray();
	}

	private static string UniqueName(string candidate, HashSet<string> used)
	{
		if (used.Add(candidate))
		{
			return candidate;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidate);
		string extension = Path.GetExtension(candidate);
		int num = 2;
		string text;
		while (true)
		{
			text = $"{fileNameWithoutExtension}_{num}{extension}";
			if (used.Add(text))
			{
				break;
			}
			num++;
		}
		return text;
	}

	private static string Normalize(string path)
	{
		return path.Replace('\\', '/').Trim('/');
	}

	private static string Safe(string value)
	{
		return string.Concat(value.Select((char c) => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
	}

	private static string TemporaryRollbackDirectory()
	{
		return Path.Combine(Path.GetTempPath(), "MDCardModTool", "raw_animation_rollback_" + Guid.NewGuid().ToString("N"));
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
