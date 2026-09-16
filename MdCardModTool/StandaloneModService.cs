using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class StandaloneModService
{
	internal static string BackupRoot(string root)
	{
		return Path.Combine(Path.GetDirectoryName(root), "_MDMobileMods", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Substring(0, 16), "original");
	}

	private static string Target(string root, TexRef texture)
	{
		root = Path.GetFullPath(root);
		string fullPath = Path.GetFullPath(texture.ActiveBundlePath);
		if (!StandaloneResourceService.IsInside(fullPath, root))
		{
			throw new IOException("资源不属于当前手机 0000。");
		}
		string[] array = Path.GetRelativePath(root, fullPath).Split(Path.DirectorySeparatorChar);
		if (array.Length != 2 || array[0].Length != 2 || array[1].Length != 8 || !array.All((string p) => p.All(char.IsAsciiHexDigit)))
		{
			throw new IOException("目标不是有效的两位目录／八位文件名资源。");
		}
		DirectModArchive.EnsureNoLinks(fullPath);
		return fullPath;
	}

	internal static bool HasBackup(string root, TexRef texture)
	{
		return File.Exists(Path.Combine(BackupRoot(root), Path.GetRelativePath(root, Target(root, texture))));
	}

	internal static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	internal static void Replace(string root, TexRef texture, byte[] png, Action? beforeCommit = null)
	{
		Image<Rgba32> image = Image.Load<Rgba32>(png);
		try
		{
			if (image.Width != texture.Width || image.Height != texture.Height)
			{
				throw new InvalidDataException("替换图片必须先裁剪为当前纹理尺寸。");
			}
			Transact(root, texture, delegate(string stageRoot, string stageFile)
			{
				TexRef staged = StandaloneResourceService.ReadBundle(stageFile, stageRoot).Single((TexRef t) => t.PathId == texture.PathId && t.AssetFileName == texture.AssetFileName);
				new ModEngine().Replace(staged, png, Path.Combine(stageRoot, "engine-backup"));
				using Image<Rgba32> image2 = Image.Load<Rgba32>(StandaloneResourceService.Decode(StandaloneResourceService.ReadBundle(stageFile, stageRoot).Single((TexRef t) => t.PathId == staged.PathId && t.AssetFileName == staged.AssetFileName)));
				if (image2.Width != image.Width || image2.Height != image.Height)
				{
					throw new InvalidDataException("手机 Bundle 尺寸回读失败。");
				}
				byte[] array = new byte[image.Width * image.Height * 4];
				image.CopyPixelDataTo(array);
				byte[] array2 = new byte[array.Length];
				image2.CopyPixelDataTo(array2);
				if (!array.AsSpan().SequenceEqual(array2))
				{
					throw new InvalidDataException("手机 Bundle 像素回读不一致，未提交。");
				}
			}, beforeCommit);
		}
		finally
		{
			if (image != null)
			{
				((IDisposable)image).Dispose();
			}
		}
	}

	internal static void Restore(string root, TexRef texture)
	{
		string path = Target(root, texture);
		string backup = Path.Combine(BackupRoot(root), Path.GetRelativePath(root, path));
		DirectModArchive.EnsureNoLinks(backup);
		if (!File.Exists(backup))
		{
			throw new FileNotFoundException("没有首次备份。");
		}
		Transact(root, texture, delegate(string stageRoot, string stageFile)
		{
			File.Copy(backup, stageFile, overwrite: true);
			if (StandaloneResourceService.ReadBundle(stageFile, stageRoot).Count == 0)
			{
				throw new InvalidDataException("备份无法读取，未还原。");
			}
		});
	}

	private static void Transact(string root, TexRef texture, Action<string, string> prepare, Action? beforeCommit = null)
	{
		string text = Target(root, texture);
		string relativePath = Path.GetRelativePath(root, text);
		string text2 = Path.Combine(BackupRoot(root), relativePath);
		DirectModArchive.EnsureNoLinks(text2);
		string text3 = Path.Combine(Path.GetDirectoryName(root), ".mdmobile-" + Guid.NewGuid().ToString("N"));
		string text4 = Path.Combine(text3, "0000");
		string text5 = Path.Combine(text4, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(text5));
		try
		{
			string text6 = Hash(text);
			File.Copy(text, text5);
			prepare(text4, text5);
			beforeCommit?.Invoke();
			DirectModArchive.EnsureNoLinks(text);
			if (Hash(text) != text6)
			{
				throw new IOException("源文件已被其他程序修改，请重新扫描后再试。");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(text2));
			if (!File.Exists(text2))
			{
				File.Copy(text, text2);
			}
			File.Replace(text5, text, null);
		}
		finally
		{
			if (Directory.Exists(text3))
			{
				Directory.Delete(text3, recursive: true);
			}
		}
	}
}
