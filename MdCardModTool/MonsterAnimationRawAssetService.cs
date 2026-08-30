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
		string relative = Normalize(asset.RelativeBundlePath);
		string[] array = new string[2] { "tcg", "ocg" };
		foreach (string region in array)
		{
			string basePath = "Duel/Timeline/Duel/MonsterCutIn/" + region + "/P" + asset.CardId;
			string[] array2 = new string[2] { "HighEnd_HD", "SD" };
			foreach (string tier in array2)
			{
				if (asset.Kind == MonsterAnimationAssetKind.Skeleton)
				{
					if (relative == Normalize(IndexService.ResourceBundleRelativePath($"{basePath}/{tier}/P{asset.CardId}JS")))
					{
						return new MonsterAnimationAssetProfile(tier, region, "");
					}
					continue;
				}
				string[] scales = Scales;
				foreach (string scale in scales)
				{
					string suffix = ((asset.Kind == MonsterAnimationAssetKind.Atlas) ? ("/P" + asset.CardId + ".atlas") : ("/P" + asset.CardId));
					if (relative == Normalize(IndexService.ResourceBundleRelativePath($"{basePath}/{tier}/{scale}{suffix}")))
					{
						return new MonsterAnimationAssetProfile(tier, region, scale);
					}
				}
			}
		}
		return new MonsterAnimationAssetProfile("未识别组", asset.StorageKind, Path.GetFileName(asset.RelativeBundlePath));
	}

	public string ExportFileName(MonsterAnimationAssetRef asset)
	{
		MonsterAnimationAssetProfile profile = ResolveProfile(asset);
		string extension = asset.Kind switch
		{
			MonsterAnimationAssetKind.Texture => ".png",
			MonsterAnimationAssetKind.Atlas => ".atlas",
			_ => ".json",
		};
		string resourceName = ((asset.Kind == MonsterAnimationAssetKind.Skeleton) ? (asset.Name + extension) : (Path.GetFileNameWithoutExtension(asset.Name) + extension));
		return Safe(profile.FilePrefix + "_" + resourceName);
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
		RawAnimationManifest manifest = new RawAnimationManifest
		{
			CardId = set.CardId
		};
		HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (MonsterAnimationAssetRef asset in Ordered(set.Assets))
		{
			string fileName = UniqueName(ExportFileName(asset), usedNames);
			File.WriteAllBytes(Path.Combine(directory, fileName), Read(asset));
			manifest.Files.Add(new RawAnimationManifestEntry
			{
				FileName = fileName,
				RelativeBundlePath = Normalize(asset.RelativeBundlePath),
				AssetFileName = asset.AssetFileName,
				PathId = asset.PathId,
				Kind = asset.Kind
			});
		}
		File.WriteAllText(Path.Combine(directory, "_动画资源映射.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions
		{
			WriteIndented = true
		}), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return manifest;
	}

	public void ReplaceOne(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
	{
		string rollback = TemporaryRollbackDirectory();
		Directory.CreateDirectory(rollback);
		string snapshot = Path.Combine(rollback, "000.bundle");
		File.Copy(asset.BundlePath, snapshot, overwrite: true);
		try
		{
			ValidateInput(asset.Kind, inputPath);
			Apply(gameRoot, asset, inputPath);
		}
		catch
		{
			File.Copy(snapshot, asset.BundlePath, overwrite: true);
			throw;
		}
		finally
		{
			TryDeleteDirectory(rollback);
		}
	}

	public int ImportAll(string gameRoot, MonsterAnimationSet set, string directory)
	{
		string manifestPath = Path.Combine(directory, "_动画资源映射.json");
		if (!File.Exists(manifestPath))
		{
			throw new FileNotFoundException("所选目录缺少 _动画资源映射.json，请先用本窗口“导出全部”建立可回导目录。", manifestPath);
		}
		RawAnimationManifest manifest = JsonSerializer.Deserialize<RawAnimationManifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("动画资源映射无法读取。");
		if (manifest.FormatVersion != 1 || manifest.CardId != set.CardId)
		{
			throw new InvalidDataException($"映射属于卡号 {manifest.CardId}，当前窗口是 {set.CardId}。");
		}
		List<(MonsterAnimationAssetRef, string)> imports = new List<(MonsterAnimationAssetRef, string)>();
		foreach (RawAnimationManifestEntry entry in manifest.Files)
		{
			if (entry.FileName != Path.GetFileName(entry.FileName))
			{
				throw new InvalidDataException("映射中包含越界文件名。");
			}
			MonsterAnimationAssetRef asset = set.Assets.FirstOrDefault((MonsterAnimationAssetRef x) => x.Kind == entry.Kind && x.PathId == entry.PathId && string.Equals(x.AssetFileName, entry.AssetFileName, StringComparison.Ordinal) && string.Equals(Normalize(x.RelativeBundlePath), Normalize(entry.RelativeBundlePath), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("当前游戏版本找不到映射资源：" + entry.FileName);
			string input = Path.Combine(directory, entry.FileName);
			if (!File.Exists(input))
			{
				throw new FileNotFoundException("缺少待导入文件：" + entry.FileName, input);
			}
			ValidateInput(asset.Kind, input);
			imports.Add((asset, input));
		}
		if (imports.Count == 0)
		{
			throw new InvalidDataException("映射中没有可导入的动画资源。");
		}
		string rollback = TemporaryRollbackDirectory();
		Directory.CreateDirectory(rollback);
		string[] bundles = imports.Select<(MonsterAnimationAssetRef, string), string>(((MonsterAnimationAssetRef Asset, string Path) x) => x.Asset.BundlePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		try
		{
			for (int i = 0; i < bundles.Length; i++)
			{
				File.Copy(bundles[i], Path.Combine(rollback, $"{i:D3}.bundle"), overwrite: true);
			}
			try
			{
				foreach (var item in imports)
				{
					Apply(gameRoot, item.Item1, item.Item2);
				}
			}
			catch
			{
				for (int i2 = 0; i2 < bundles.Length; i2++)
				{
					File.Copy(Path.Combine(rollback, $"{i2:D3}.bundle"), bundles[i2], overwrite: true);
				}
				throw;
			}
		}
		finally
		{
			TryDeleteDirectory(rollback);
		}
		return imports.Count;
	}

	public bool Restore(string gameRoot, MonsterAnimationAssetRef asset)
	{
		string backup = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
		if (!File.Exists(backup))
		{
			return false;
		}
		File.Copy(backup, asset.BundlePath, overwrite: true);
		return true;
	}

	private void Apply(string gameRoot, MonsterAnimationAssetRef asset, string inputPath)
	{
		string backupRoot = Path.Combine(gameRoot, "_MD卡图备份", asset.ModSourceKind);
		if (asset.Kind == MonsterAnimationAssetKind.Texture)
		{
			using (Image<Rgba32> image = Image.Load<Rgba32>(inputPath))
			{
				_engine.ReplaceAnimationAtlas(asset, image, backupRoot);
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
			ImageInfo info = Image.Identify(inputPath) ?? throw new InvalidDataException("无法读取动画图集图片。");
			if (info.Width < 1 || info.Height < 1 || info.Width > 16384 || info.Height > 16384)
			{
				throw new InvalidDataException("动画图集尺寸必须在 1–16384 像素范围内。");
			}
			return;
		}
		byte[] data = NormalizeTextData(File.ReadAllBytes(inputPath));
		if (data.Length == 0)
		{
			throw new InvalidDataException("文本资源不能为空。");
		}
		if (kind == MonsterAnimationAssetKind.Skeleton)
		{
			using (JsonDocument.Parse(data))
			{
				return;
			}
		}
		new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
	}

	private static byte[] NormalizeTextData(byte[] data)
	{
		int start = (data.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0);
		int end = data.Length;
		while (end > start && data[end - 1] == 0)
		{
			end--;
		}
		return data.AsSpan(start, end - start).ToArray();
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
		string stem = Path.GetFileNameWithoutExtension(candidate);
		string extension = Path.GetExtension(candidate);
		int i = 2;
		string value;
		while (true)
		{
			value = $"{stem}_{i}{extension}";
			if (used.Add(value))
			{
				break;
			}
			i++;
		}
		return value;
	}

	private static string Normalize(string path)
	{
		return path.Replace('\\', '/').Trim('/');
	}

	private static string Safe(string value)
	{
		return string.Concat(value.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
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
