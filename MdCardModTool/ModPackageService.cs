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
		ChangedBundle[] changed = EnumerateChangedBundles(gameRoot, textures.ToList()).ToArray();
		return new ModChangeSummary(changed.Length, changed.Count((ChangedBundle x) => x.SourceKind.StartsWith("召唤动画", StringComparison.Ordinal)));
	}

	public ModPackageInfo Export(string gameRoot, IEnumerable<TexRef> textures, string outputPath, bool directReplacement = false)
	{
		List<ChangedBundle> changed = EnumerateChangedBundles(gameRoot, textures.ToList())
			.DistinctBy(x => x.LivePath, StringComparer.OrdinalIgnoreCase).ToList();
		if (changed.Count == 0)
		{
			throw new InvalidOperationException("当前没有可导出的 Mod。替换过但已还原的 Bundle 不会被导出。");
		}
		ModPackageManifest manifest = new ModPackageManifest
		{
			Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(outputPath)),
			Entries = changed.Select((ChangedBundle x, int num) => new ModPackageEntry
			{
				ArchivePath = directReplacement ? NormalizeRelativePath(Path.GetRelativePath(gameRoot, x.LivePath)) : $"files/{num + 1:D4}.bundle",
				TargetKind = x.TargetKind,
				RelativePath = NormalizeRelativePath(x.RelativePath),
				SourceKind = x.SourceKind,
				DisplayName = x.DisplayName,
				Sha256 = HashFile(x.LivePath),
				Size = new FileInfo(x.LivePath).Length
			}).ToList()
		};
		string fullOutput = Path.GetFullPath(outputPath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
		string temporary = fullOutput + "." + Guid.NewGuid().ToString("N") + ".tmp";
		if (File.Exists(temporary))
		{
			File.Delete(temporary);
		}
		try
		{
			using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
			{
				for (int i = 0; i < changed.Count; i++)
				{
					DirectModArchive.ValidatePath(manifest.Entries[i].ArchivePath);
					archive.CreateEntryFromFile(changed[i].LivePath, manifest.Entries[i].ArchivePath, CompressionLevel.Optimal);
				}
				if (directReplacement)
				{
					using StreamWriter instructions = new(archive.CreateEntry("安装说明.txt").Open());
					instructions.Write(DirectModArchive.InstallInstructions);
				}
				using Stream stream = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
				JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions
				{
					WriteIndented = true
				});
			}
			// The game or another editor may change a live bundle while ZIP compression
			// is running. Never publish a package whose metadata no longer matches.
			using (ZipArchive verify = ZipFile.OpenRead(temporary))
			{
				foreach (ModPackageEntry entry in manifest.Entries)
				{
					ZipArchiveEntry file = verify.GetEntry(entry.ArchivePath)!;
					using Stream input = file.Open();
					if (file.Length != entry.Size || Convert.ToHexString(SHA256.HashData(input)) != entry.Sha256)
						throw new IOException("导出期间游戏资源发生变化，请退出游戏并重新导出。");
				}
			}
			File.Move(temporary, fullOutput, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
		return new ModPackageInfo(manifest.Name, manifest.CreatedAt, manifest.Entries.Count, manifest.Entries.Sum((ModPackageEntry x) => x.Size));
	}

	public ModPackageInfo Inspect(string packagePath)
	{
		using ZipArchive archive = ZipFile.OpenRead(packagePath);
		ModPackageManifest manifest = ReadManifest(archive);
		return new ModPackageInfo(manifest.Name, manifest.CreatedAt, manifest.Entries.Count, manifest.Entries.Sum((ModPackageEntry x) => x.Size));
	}

	public ModImportResult Import(string gameRoot, string packagePath, IEnumerable<TexRef>? textures = null, Action<int>? afterCommit = null)
	{
		DirectModArchive.EnsureGameClosed();
		TexRef[] knownTextures = textures?.ToArray() ?? Array.Empty<TexRef>();
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string staging = Path.Combine(Path.GetTempPath(), "MDCardModTool", "import_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(staging);
		bool preserveRecovery = false;
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(packagePath);
			ModPackageManifest manifest = ReadManifest(archive);
			bool looseArchive = archive.GetEntry(ManifestName) == null;
			List<ImportPlan> plans = new List<ImportPlan>();
			for (int i = 0; i < manifest.Entries.Count; i++)
			{
				ModPackageEntry entry = manifest.Entries[i];
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
				ZipArchiveEntry? obj = archive.GetEntry(entry.ArchivePath) ?? throw new InvalidDataException("Mod 包缺少文件：" + entry.ArchivePath);
				if (obj.Length != entry.Size)
				{
					throw new InvalidDataException("Mod 包文件大小不匹配：" + entry.DisplayName);
				}
				string staged = Path.Combine(staging, $"{i:D4}.bundle");
				obj.ExtractToFile(staged, overwrite: true);
				if (!HashFile(staged).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Mod 包校验失败：" + entry.DisplayName);
				}
				DirectModArchive.ValidateBundleFile(staged);
				DirectModArchive.ValidateBundleFile(target);
				string sourceKind = entry.SourceKind;
				if (looseArchive)
				{
					sourceKind = knownTextures.FirstOrDefault(x => x.BundlePath.Equals(target, StringComparison.OrdinalIgnoreCase)
						&& SourceKinds.Contains(x.SourceKind) && TargetKindFor(x.SourceKind) == entry.TargetKind)?.SourceKind ?? sourceKind;
					if (entry.TargetKind != "GameRoot" && new ModEngine().ScanAnimationAssetsFast(staged, staging).Count > 0)
						sourceKind = entry.TargetKind == "LocalData" ? "召唤动画" : "召唤动画-游戏内";
				}
				string backup = ResolveInside(Path.Combine(gameRoot, "_MD卡图备份", sourceKind), entry.RelativePath);
				DirectModArchive.EnsureNoLinks(backup);
				plans.Add(new ImportPlan(target, staged, backup, Path.Combine(staging, $"rollback_{i:D4}.bundle")));
			}
			if (plans.Select((ImportPlan x) => x.Target).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
			{
				throw new InvalidDataException("Mod 包包含重复的游戏目标路径。");
			}
			List<ImportPlan> applied = new List<ImportPlan>();
			DirectModArchive.EnsureGameClosed();
			try
			{
				foreach (ImportPlan plan in plans)
				{
					DirectModArchive.EnsureNoLinks(plan.Target);
					DirectModArchive.EnsureNoLinks(plan.Backup);
					File.Copy(plan.Target, plan.Rollback, overwrite: true);
					Directory.CreateDirectory(Path.GetDirectoryName(plan.Backup));
					if (!File.Exists(plan.Backup))
					{
						File.Copy(plan.Target, plan.Backup);
					}
					string writeTemp = plan.Target + ".mdcardmod.import.tmp";
					try
					{
						File.Copy(plan.Staged, writeTemp, overwrite: true);
						File.Move(writeTemp, plan.Target, overwrite: true);
					}
					finally
					{
						if (File.Exists(writeTemp))
						{
							File.Delete(writeTemp);
						}
					}
					applied.Add(plan);
					afterCommit?.Invoke(applied.Count);
				}
			}
			catch (Exception commitError)
			{
				List<Exception> recoveryErrors = new();
				foreach (ImportPlan plan2 in applied.AsEnumerable().Reverse())
				{
					try { File.Copy(plan2.Rollback, plan2.Target, overwrite: true); }
					catch (Exception error) { recoveryErrors.Add(error); }
				}
				if (recoveryErrors.Count > 0)
				{
					preserveRecovery = true;
					throw new AggregateException("导入与回滚失败，恢复副本保留在：" + staging, recoveryErrors.Prepend(commitError));
				}
				throw;
			}
			return new ModImportResult(plans.Count, plans.Select((ImportPlan x) => x.Target).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray());
		}
		finally
		{
			try
			{
				if (!preserveRecovery && Directory.Exists(staging))
				{
					Directory.Delete(staging, recursive: true);
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
		foreach (string sourceKind in sourceKinds)
		{
			string sourceBackup = Path.Combine(backupRoot, sourceKind);
			if (!Directory.Exists(sourceBackup))
			{
				continue;
			}
			string targetKind = TargetKindFor(sourceKind);
			string targetRoot = TargetRoot(gameRoot, localRoot, targetKind);
			foreach (string backup in Directory.EnumerateFiles(sourceBackup, "*", SearchOption.AllDirectories))
			{
				string relative = Path.GetRelativePath(sourceBackup, backup);
				string live = ResolveInside(targetRoot, relative);
				if (File.Exists(live) && !FilesEqual(backup, live))
				{
					string[] names = (from x in textures
						where x.BundlePath.Equals(live, StringComparison.OrdinalIgnoreCase)
						select (x.CardKey.Length <= 0) ? x.Name : x.CardKey).Distinct().Take(4).ToArray();
					string display = ((names.Length != 0) ? string.Join("、", names) : Path.GetFileName(relative));
					yield return new ChangedBundle(live, relative, targetKind, sourceKind, display);
				}
			}
		}
	}

	private static ModPackageManifest ReadManifest(ZipArchive archive)
	{
		DirectModArchive.ValidateArchive(archive);
		ZipArchiveEntry? metadata = archive.GetEntry(ManifestName);
		if (metadata?.Length > 4 * 1024 * 1024) throw new InvalidDataException("Mod 清单过大。");
		using Stream? stream = metadata?.Open();
		ModPackageManifest manifest = stream == null ? DirectModArchive.Read(archive)
			: JsonSerializer.Deserialize<ModPackageManifest>(stream) ?? throw new InvalidDataException("Mod 包清单无法读取。");
		if (manifest.FormatVersion != 1)
		{
			throw new InvalidDataException($"暂不支持 Mod 包格式版本 {manifest.FormatVersion}。");
		}
		if (manifest.Entries == null) throw new InvalidDataException("Mod 包清单缺少 Entries。");
		int count = manifest.Entries.Count;
		if ((count < 1 || count > 10000) ? true : false)
		{
			throw new InvalidDataException("Mod 包内没有 Bundle，或文件数量异常。");
		}
		if (manifest.Entries.Any(delegate(ModPackageEntry x)
		{
			long size = x.Size;
			return (size < 1 || size > 1073741824) ? true : false;
		}) || manifest.Entries.Sum((ModPackageEntry x) => x.Size) > 4294967296L)
		{
			throw new InvalidDataException("Mod 包解压后的文件大小异常。");
		}
		if (manifest.Entries.Select((ModPackageEntry x) => x.ArchivePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != manifest.Entries.Count)
		{
			throw new InvalidDataException("Mod 包内存在重复文件路径。");
		}
		foreach (ModPackageEntry entry in manifest.Entries)
		{
			DirectModArchive.ValidatePath(entry.ArchivePath);
			DirectModArchive.ValidatePath(entry.RelativePath);
		}
		return manifest;
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
		string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
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
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private static bool FilesEqual(string left, string right)
	{
		FileInfo fileInfo = new FileInfo(left);
		FileInfo b = new FileInfo(right);
		if (fileInfo.Length == b.Length)
		{
			return HashFile(left).Equals(HashFile(right), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}
}
