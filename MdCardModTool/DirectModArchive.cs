using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AssetsTools.NET.Extra;

namespace MdCardModTool;

internal static class DirectModArchive
{
	public const string InstallInstructions = "先完全退出 Master Duel，并备份将被覆盖的文件。\r\n本包保留游戏目录结构：把 LocalData、masterduel_Data 等资源目录合并到游戏根目录。\r\nLocalData 下的账号名来自导出者；在其他账号使用时，先改成自己的账号文件夹名，不要创建导出者的账号目录。\r\n推荐使用工具的‘导入 Mod 包’：自动映射当前选中账号、校验文件并保存首次备份。\r\nmanifest.json 与本说明只用于工具和说明，不必复制到游戏目录。\r\n只适用于已下载对应资源的兼容游戏版本；整套替换可能覆盖同一 Bundle 内其他修改。\r\n";

	public static void ValidatePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/') || path.Any((char c) => c < ' ' || ":*?\"<>|".Contains(c)))
		{
			throw new InvalidDataException("ZIP 包含非法路径：" + path);
		}
		string[] array = path.Split('/');
		foreach (string text in array)
		{
			bool flag = text.Length == 0;
			if (!flag)
			{
				bool flag2 = ((text == "." || text == "..") ? true : false);
				flag = flag2;
			}
			if (flag || text.EndsWith('.') || text.EndsWith(' ') || Regex.IsMatch(text, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", RegexOptions.IgnoreCase))
			{
				throw new InvalidDataException("ZIP 路径不安全：" + path);
			}
		}
	}

	public static void ValidateArchive(ZipArchive archive)
	{
		if (archive.Entries.Count > 12000)
		{
			throw new InvalidDataException("ZIP 文件数量超出限制。");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		long num = 0L;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			ValidatePath(entry.FullName.TrimEnd('/'));
			if (!hashSet.Add(entry.FullName.TrimEnd('/')))
			{
				throw new InvalidDataException("ZIP 包含重复路径。");
			}
			if (((entry.ExternalAttributes >> 16) & 0xF000) == 40960)
			{
				throw new InvalidDataException("ZIP 不允许符号链接。");
			}
			num = checked(num + entry.Length);
			if (entry.Length > 1073741824 || num > 4294967296L)
			{
				throw new InvalidDataException("ZIP 解压大小超出限制。");
			}
		}
	}

	public static ModPackageManifest Read(ZipArchive archive)
	{
		List<ModPackageEntry> list = new List<ModPackageEntry>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (entry.FullName.EndsWith('/') || entry.FullName.Equals("安装说明.txt", StringComparison.OrdinalIgnoreCase) || entry.FullName.Equals("README.txt", StringComparison.OrdinalIgnoreCase) || entry.FullName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string fullName = entry.FullName;
			string[] array = fullName.Split('/');
			int num = Array.FindIndex(array, (string s) => s.Equals("LocalData", StringComparison.OrdinalIgnoreCase));
			int num2 = Array.FindIndex(array, (string s) => s.Equals("masterduel_Data", StringComparison.OrdinalIgnoreCase));
			string text;
			string text2;
			string sourceKind;
			if (num >= 0 && array.Length > num + 3 && array[num + 2] == "0000")
			{
				text = "LocalData";
				text2 = string.Join('/', array.Skip(num + 3));
				sourceKind = "本地卡图";
			}
			else if (num2 >= 0 && array.Length > num2 + 3 && array[num2 + 1].Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) && array[num2 + 2].Equals("AssetBundle", StringComparison.OrdinalIgnoreCase))
			{
				text = "StreamingAssets";
				text2 = string.Join('/', array.Skip(num2 + 3));
				sourceKind = "游戏内图片";
			}
			else if (array[0] == "0000" && array.Length >= 3)
			{
				text = "LocalData";
				text2 = string.Join('/', array.Skip(1));
				sourceKind = "本地卡图";
			}
			else if (Regex.IsMatch(fullName, "^[0-9a-fA-F]{2}/[0-9a-fA-F]{8}$"))
			{
				text = "LocalData";
				text2 = fullName;
				sourceKind = "本地卡图";
			}
			else
			{
				if (num2 != 0)
				{
					throw new InvalidDataException("无法确定此文件的游戏目标目录：" + fullName + "。请保留 LocalData 或 masterduel_Data 目录结构。");
				}
				text = "GameRoot";
				text2 = fullName;
				sourceKind = "基础视觉资源";
			}
			if (!hashSet.Add(text + "/" + text2))
			{
				throw new InvalidDataException("ZIP 的多个文件映射到同一目标（可能包含多个账号）。");
			}
			using Stream stream = entry.Open();
			ValidateSignature(stream, fullName);
			using Stream source = entry.Open();
			list.Add(new ModPackageEntry
			{
				ArchivePath = fullName,
				TargetKind = text,
				RelativePath = text2,
				SourceKind = sourceKind,
				DisplayName = Path.GetFileName(fullName),
				Size = entry.Length,
				Sha256 = Convert.ToHexString(SHA256.HashData(source))
			});
		}
		return new ModPackageManifest
		{
			Name = "直接替换 ZIP",
			Entries = list
		};
	}

	private static void ValidateSignature(Stream stream, string name)
	{
		Span<byte> buffer = stackalloc byte[8];
		int length = stream.ReadAtLeast(buffer, 8, throwOnEndOfStream: false);
		string text = Encoding.ASCII.GetString(buffer.Slice(0, length));
		if (text != "UnityFS\0" && text != "UnityRaw" && text != "UnityWeb")
		{
			throw new InvalidDataException("文件不是 Unity Bundle，已拒绝安装：" + name);
		}
	}

	public static void ValidateBundleFile(string file)
	{
		using (FileStream stream = File.OpenRead(file))
		{
			ValidateSignature(stream, file);
		}
		AssetsManager assetsManager = new AssetsManager();
		try
		{
			if (!assetsManager.LoadBundleFile(file).file.GetAllFileNames().Any())
			{
				throw new InvalidDataException("空 Bundle：" + file);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is ArgumentException || ex is IndexOutOfRangeException) ? 1 : 0) != 0)
		{
			throw new InvalidDataException("Bundle 结构损坏，已拒绝导入：" + Path.GetFileName(file), ex);
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	public static void EnsureNoLinks(string path)
	{
		for (string text = Path.GetFullPath(path); text != null; text = Path.GetDirectoryName(text))
		{
			if ((File.Exists(text) || Directory.Exists(text)) && (File.GetAttributes(text) & FileAttributes.ReparsePoint) != FileAttributes.None)
			{
				throw new InvalidDataException("Mod 目标或备份路径包含目录链接，已停止：" + text);
			}
		}
	}

	public static void EnsureGameClosed()
	{
		Process[] processesByName = Process.GetProcessesByName("masterduel");
		try
		{
			if (processesByName.Length != 0)
			{
				throw new InvalidOperationException("请完全退出 Master Duel 后再导入 Mod。");
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
