using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace MdCardModTool;

public sealed class ModPackageService
{
	private sealed record ChangedBundle(string LivePath, string RelativePath, string TargetKind, string SourceKind, string DisplayName);

	private sealed record ImportPlan(string Target, string Staged, string Backup, string Rollback);

	private const string ManifestName = "manifest.json";

	private static readonly string[] SourceKinds = new string[8] { "本地卡图", "视觉资源", "游戏内图片", "卡框资源", "基础视觉资源", "超框开关", "召唤动画", "召唤动画-游戏内" };

	public int RefreshFlags(string gameRoot, IEnumerable<TexRef> textures)
	{
		List<TexRef> list = textures.ToList();
		foreach (TexRef item in list)
		{
			item.IsModded = false;
		}
		HashSet<string> changed = (from x in EnumerateChangedBundles(gameRoot, list)
			select x.LivePath).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TexRef item2 in list.Where((TexRef x) => changed.Contains(x.BundlePath)))
		{
			item2.IsModded = true;
		}
		return list.Count((TexRef x) => x.IsModded);
	}

	public ModChangeSummary GetChangeSummary(string gameRoot, IEnumerable<TexRef> textures)
	{
		ChangedBundle[] array = EnumerateChangedBundles(gameRoot, textures.ToList()).ToArray();
		return new ModChangeSummary(array.Length, array.Count((ChangedBundle x) => x.SourceKind.StartsWith("召唤动画", StringComparison.Ordinal)));
	}

	public ModPackageInfo Export(string gameRoot, IEnumerable<TexRef> textures, string outputPath, bool directReplacement = false)
	{
		List<ChangedBundle> list = EnumerateChangedBundles(gameRoot, textures.ToList()).DistinctBy<ChangedBundle, string>((ChangedBundle x) => x.LivePath, StringComparer.OrdinalIgnoreCase).ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException("当前没有可导出的 Mod。替换过但已还原的 Bundle 不会被导出。");
		}
		ModPackageManifest modPackageManifest = new ModPackageManifest
		{
			Platform = (ResourceSource.IsMobile(gameRoot) ? "Mobile" : "PC"),
			Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(outputPath)),
			Entries = list.Select((ChangedBundle x, int num2) => new ModPackageEntry
			{
				ArchivePath = (directReplacement ? ((ResourceSource.IsMobile(gameRoot) ? "0000/" : "") + NormalizeRelativePath(Path.GetRelativePath(gameRoot, x.LivePath))) : $"files/{num2 + 1:D4}.bundle"),
				TargetKind = x.TargetKind,
				RelativePath = NormalizeRelativePath(x.RelativePath),
				SourceKind = x.SourceKind,
				DisplayName = x.DisplayName,
				Sha256 = HashFile(x.LivePath),
				Size = new FileInfo(x.LivePath).Length
			}).ToList()
		};
		string fullPath = Path.GetFullPath(outputPath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
		string text = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		try
		{
			using (ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create))
			{
				for (int num = 0; num < list.Count; num++)
				{
					DirectModArchive.ValidatePath(modPackageManifest.Entries[num].ArchivePath);
					zipArchive.CreateEntryFromFile(list[num].LivePath, modPackageManifest.Entries[num].ArchivePath, CompressionLevel.Optimal);
				}
				if (directReplacement)
				{
					using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("安装说明.txt").Open());
					streamWriter.Write(ResourceSource.IsMobile(gameRoot) ? "请先退出手机游戏。将本包 0000 下的两位哈希目录合并到手机对应 0000，保留原文件备份。不要将 manifest.json、说明或工具备份目录复制进手机资源。仅适用于兼容版本的手机端 Bundle，不能安装到 PC。也可由工具在当前手机资源工作区导入。" : "先完全退出 Master Duel，并备份将被覆盖的文件。\r\n本包保留游戏目录结构：把 LocalData、masterduel_Data 等资源目录合并到游戏根目录。\r\nLocalData 下的账号名来自导出者；在其他账号使用时，先改成自己的账号文件夹名，不要创建导出者的账号目录。\r\n推荐使用工具的‘导入 Mod 包’：自动映射当前选中账号、校验文件并保存首次备份。\r\nmanifest.json 与本说明只用于工具和说明，不必复制到游戏目录。\r\n只适用于已下载对应资源的兼容游戏版本；整套替换可能覆盖同一 Bundle 内其他修改。\r\n");
				}
				using Stream utf8Json = zipArchive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
				JsonSerializer.Serialize(utf8Json, modPackageManifest, new JsonSerializerOptions
				{
					WriteIndented = true
				});
			}
			using (ZipArchive zipArchive2 = ZipFile.OpenRead(text))
			{
				foreach (ModPackageEntry entry2 in modPackageManifest.Entries)
				{
					ZipArchiveEntry entry = zipArchive2.GetEntry(entry2.ArchivePath);
					using Stream source = entry.Open();
					if (entry.Length != entry2.Size || Convert.ToHexString(SHA256.HashData(source)) != entry2.Sha256)
					{
						throw new IOException("导出期间游戏资源发生变化，请退出游戏并重新导出。");
					}
				}
			}
			File.Move(text, fullPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
		return new ModPackageInfo(modPackageManifest.Name, modPackageManifest.CreatedAt, modPackageManifest.Entries.Count, modPackageManifest.Entries.Sum((ModPackageEntry x) => x.Size));
	}

	public ModPackageInfo Inspect(string packagePath)
	{
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		ModPackageManifest modPackageManifest = ReadManifest(archive);
		return new ModPackageInfo(modPackageManifest.Name, modPackageManifest.CreatedAt, modPackageManifest.Entries.Count, modPackageManifest.Entries.Sum((ModPackageEntry x) => x.Size));
	}

	public ModImportResult Import(string gameRoot, string packagePath, IEnumerable<TexRef>? textures = null, Action<int>? afterCommit = null)
	{
		DirectModArchive.EnsureGameClosed();
		TexRef[] source = textures?.ToArray() ?? Array.Empty<TexRef>();
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string text = Path.Combine(Path.GetTempPath(), "MDCardModTool", "import_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		bool flag = false;
		try
		{
			using ZipArchive zipArchive = ZipFile.OpenRead(packagePath);
			ModPackageManifest modPackageManifest = ReadManifest(zipArchive);
			string text2 = (ResourceSource.IsMobile(gameRoot) ? "Mobile" : "PC");
			if (modPackageManifest.Platform.Length > 0 && modPackageManifest.Platform != text2)
			{
				throw new InvalidDataException("此 Mod 包的平台与当前资源目录不一致，不能把 PC Bundle 直接装到手机端或反向安装。");
			}
			bool flag2 = zipArchive.GetEntry("manifest.json") == null;
			List<ImportPlan> list = new List<ImportPlan>();
			for (int i = 0; i < modPackageManifest.Entries.Count; i++)
			{
				ModPackageEntry entry = modPackageManifest.Entries[i];
				ValidateSourceKind(entry.SourceKind);
				if (!entry.TargetKind.Equals(TargetKindFor(entry.SourceKind), StringComparison.Ordinal))
				{
					throw new InvalidDataException("资源类型与目标目录不匹配：" + entry.DisplayName);
				}
				string target = ResolveInside(TargetRoot(gameRoot, localRoot, entry.TargetKind), entry.RelativePath);
				DirectModArchive.EnsureNoLinks(target);
				if (!File.Exists(target))
				{
					throw new FileNotFoundException("本机没有 Mod 所需的目标 Bundle：" + entry.RelativePath, target);
				}
				ZipArchiveEntry? obj = zipArchive.GetEntry(entry.ArchivePath) ?? throw new InvalidDataException("Mod 包缺少文件：" + entry.ArchivePath);
				if (obj.Length != entry.Size)
				{
					throw new InvalidDataException("Mod 包文件大小不匹配：" + entry.DisplayName);
				}
				string text3 = Path.Combine(text, $"{i:D4}.bundle");
				obj.ExtractToFile(text3, overwrite: true);
				if (!HashFile(text3).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Mod 包校验失败：" + entry.DisplayName);
				}
				DirectModArchive.ValidateBundleFile(text3);
				DirectModArchive.ValidateBundleFile(target);
				string text4 = entry.SourceKind;
				if (flag2)
				{
					text4 = source.FirstOrDefault((TexRef x) => x.BundlePath.Equals(target, StringComparison.OrdinalIgnoreCase) && SourceKinds.Contains(x.SourceKind) && TargetKindFor(x.SourceKind) == entry.TargetKind)?.SourceKind ?? text4;
					if (entry.TargetKind != "GameRoot" && new ModEngine().ScanAnimationAssetsFast(text3, text).Count > 0)
					{
						text4 = ((entry.TargetKind == "LocalData") ? "召唤动画" : "召唤动画-游戏内");
					}
				}
				string text5 = ResolveInside(Path.Combine(gameRoot, "_MD卡图备份", text4), entry.RelativePath);
				DirectModArchive.EnsureNoLinks(text5);
				list.Add(new ImportPlan(target, text3, text5, Path.Combine(text, $"rollback_{i:D4}.bundle")));
			}
			if (list.Select((ImportPlan x) => x.Target).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
			{
				throw new InvalidDataException("Mod 包包含重复的游戏目标路径。");
			}
			List<ImportPlan> list2 = new List<ImportPlan>();
			DirectModArchive.EnsureGameClosed();
			try
			{
				foreach (ImportPlan item2 in list)
				{
					DirectModArchive.EnsureNoLinks(item2.Target);
					DirectModArchive.EnsureNoLinks(item2.Backup);
					File.Copy(item2.Target, item2.Rollback, overwrite: true);
					Directory.CreateDirectory(Path.GetDirectoryName(item2.Backup));
					if (!File.Exists(item2.Backup))
					{
						File.Copy(item2.Target, item2.Backup);
					}
					string text6 = item2.Target + ".mdcardmod.import.tmp";
					try
					{
						File.Copy(item2.Staged, text6, overwrite: true);
						File.Move(text6, item2.Target, overwrite: true);
					}
					finally
					{
						if (File.Exists(text6))
						{
							File.Delete(text6);
						}
					}
					list2.Add(item2);
					afterCommit?.Invoke(list2.Count);
				}
			}
			catch (Exception element)
			{
				List<Exception> list3 = new List<Exception>();
				foreach (ImportPlan item3 in list2.AsEnumerable().Reverse())
				{
					try
					{
						File.Copy(item3.Rollback, item3.Target, overwrite: true);
					}
					catch (Exception item)
					{
						list3.Add(item);
					}
				}
				if (list3.Count > 0)
				{
					flag = true;
					throw new AggregateException("导入与回滚失败，恢复副本保留在：" + text, list3.Prepend(element));
				}
				throw;
			}
			return new ModImportResult(list.Count, list.Select((ImportPlan x) => x.Target).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray());
		}
		finally
		{
			try
			{
				if (!flag && Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private IEnumerable<ChangedBundle> EnumerateChangedBundles(string gameRoot, IReadOnlyCollection<TexRef> textures)
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot);
		if (localRoot == null)
		{
			yield break;
		}
		string backupRoot = Path.Combine(gameRoot, "_MD卡图备份");
		string[] sourceKinds = SourceKinds;
		string[] array = sourceKinds;
		foreach (string sourceKind in array)
		{
			string sourceBackup = Path.Combine(backupRoot, sourceKind);
			if (!Directory.Exists(sourceBackup))
			{
				continue;
			}
			string targetKind = TargetKindFor(sourceKind);
			string targetRoot = TargetRoot(gameRoot, localRoot, targetKind);
			foreach (string item in Directory.EnumerateFiles(sourceBackup, "*", SearchOption.AllDirectories))
			{
				string relativePath = Path.GetRelativePath(sourceBackup, item);
				string live = ResolveInside(targetRoot, relativePath);
				if (File.Exists(live) && !FilesEqual(item, live))
				{
					string[] array2 = (from x in textures
						where x.BundlePath.Equals(live, StringComparison.OrdinalIgnoreCase)
						select (x.CardKey.Length > 0) ? x.CardKey : x.Name).Distinct().Take(4).ToArray();
					string displayName = ((array2.Length != 0) ? string.Join("、", array2) : Path.GetFileName(relativePath));
					yield return new ChangedBundle(live, relativePath, targetKind, sourceKind, displayName);
				}
			}
		}
	}

	private static ModPackageManifest ReadManifest(ZipArchive archive)
	{
		DirectModArchive.ValidateArchive(archive);
		ZipArchiveEntry? entry = archive.GetEntry("manifest.json");
		if (entry != null && entry.Length > 4194304)
		{
			throw new InvalidDataException("Mod 清单过大。");
		}
		using Stream stream = entry?.Open();
		ModPackageManifest modPackageManifest = ((stream == null) ? DirectModArchive.Read(archive) : (JsonSerializer.Deserialize<ModPackageManifest>(stream) ?? throw new InvalidDataException("Mod 包清单无法读取。")));
		if (modPackageManifest.FormatVersion != 1)
		{
			throw new InvalidDataException($"暂不支持 Mod 包格式版本 {modPackageManifest.FormatVersion}。");
		}
		if (modPackageManifest.Entries == null)
		{
			throw new InvalidDataException("Mod 包清单缺少 Entries。");
		}
		int count = modPackageManifest.Entries.Count;
		if (count < 1 || count > 10000)
		{
			throw new InvalidDataException("Mod 包内没有 Bundle，或文件数量异常。");
		}
		if (modPackageManifest.Entries.Any(delegate(ModPackageEntry x)
		{
			long size = x.Size;
			return (size < 1 || size > 1073741824) ? true : false;
		}) || modPackageManifest.Entries.Sum((ModPackageEntry x) => x.Size) > 4294967296L)
		{
			throw new InvalidDataException("Mod 包解压后的文件大小异常。");
		}
		if (modPackageManifest.Entries.Select((ModPackageEntry x) => x.ArchivePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != modPackageManifest.Entries.Count)
		{
			throw new InvalidDataException("Mod 包内存在重复文件路径。");
		}
		foreach (ModPackageEntry entry2 in modPackageManifest.Entries)
		{
			DirectModArchive.ValidatePath(entry2.ArchivePath);
			DirectModArchive.ValidatePath(entry2.RelativePath);
		}
		return modPackageManifest;
	}

	private static string TargetKindFor(string sourceKind)
	{
		switch (sourceKind)
		{
		case "本地卡图":
		case "视觉资源":
		case "超框开关":
		case "召唤动画":
			return "LocalData";
		case "召唤动画-游戏内":
			return "StreamingAssets";
		case "游戏内图片":
			return "StreamingAssets";
		case "卡框资源":
		case "基础视觉资源":
			return "GameRoot";
		default:
			throw new InvalidDataException("不支持的资源类型：" + sourceKind);
		}
	}

	private static string TargetRoot(string gameRoot, string localRoot, string targetKind)
	{
		return targetKind switch
		{
			"LocalData" => localRoot,
			"StreamingAssets" => IndexService.StreamingRoot(gameRoot),
			"GameRoot" => gameRoot,
			_ => throw new InvalidDataException("不支持的目标类型：" + targetKind),
		};
	}

	private static void ValidateSourceKind(string sourceKind)
	{
		if (!SourceKinds.Contains<string>(sourceKind, StringComparer.Ordinal))
		{
			throw new InvalidDataException("不支持的资源类型：" + sourceKind);
		}
	}

	private static string ResolveInside(string root, string relative)
	{
		DirectModArchive.ValidatePath(relative.Replace('\\', '/'));
		if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
		{
			throw new InvalidDataException("Mod 包包含无效的绝对路径。");
		}
		string text = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Mod 包路径越出了游戏目录，已停止导入。");
		}
		return fullPath;
	}

	private static string NormalizeRelativePath(string path)
	{
		return path.Replace('\\', '/');
	}

	private static string HashFile(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static bool FilesEqual(string left, string right)
	{
		FileInfo fileInfo = new FileInfo(left);
		FileInfo fileInfo2 = new FileInfo(right);
		if (fileInfo.Length == fileInfo2.Length)
		{
			return HashFile(left).Equals(HashFile(right), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}
}
