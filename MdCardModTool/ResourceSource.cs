using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public static class ResourceSource
{
	public static bool IsMobile(string? root)
	{
		if (!string.IsNullOrWhiteSpace(root) && Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Equals("0000", StringComparison.OrdinalIgnoreCase))
		{
			return Directory.Exists(root);
		}
		return false;
	}

	public static GameInstallation Mobile(string root)
	{
		return new GameInstallation
		{
			GameRoot = Path.GetFullPath(root),
			BuildId = "mobile",
			Profiles = new _003C_003Ez__ReadOnlySingleElementList<LocalDataProfile>(new LocalDataProfile
			{
				AccountId = "手机资源",
				RootPath = Path.GetFullPath(root),
				LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(root)
			})
		};
	}

	public static GameIndex BuildMobile(string root, Action<int, int, int>? progress = null)
	{
		string[] files = StandaloneResourceService.EnumerateBundles(root).ToArray();
		ConcurrentBag<TexRef> textures = new ConcurrentBag<TexRef>();
		int done = 0;
		Parallel.ForEach(files, new ParallelOptions
		{
			MaxDegreeOfParallelism = 2
		}, delegate(string path)
		{
			try
			{
				if (!IndexService.IsUnityBundle(path))
				{
					return;
				}
				foreach (TexRef texture in new ModEngine().ScanBundle(path, root, "本地卡图", includeDependencies: false).Textures)
				{
					if (IndexService.IsDirectCardIllustration(texture) || (texture.CardKey.Length > 0 && texture.Name.All(char.IsAsciiDigit) && texture.Width == 704 && texture.Height == 1024))
					{
						textures.Add(texture);
					}
					else if (!Regex.IsMatch(texture.Name, "^P\\d+(?:_\\d+)?$", RegexOptions.IgnoreCase))
					{
						textures.Add(new TexRef
						{
							BundlePath = texture.BundlePath,
							RelativeBundlePath = texture.RelativeBundlePath,
							PathId = texture.PathId,
							AssetFileName = texture.AssetFileName,
							Name = texture.Name,
							Width = texture.Width,
							Height = texture.Height,
							SourceKind = "视觉资源",
							Category = (VisualAssetClassifier.CategoryFor(texture.Name) ?? "其他贴图")
						});
					}
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
			{
			}
			finally
			{
				int num = Interlocked.Increment(ref done);
				if (num % 100 == 0 || num == files.Length)
				{
					progress?.Invoke(num, files.Length, textures.Count);
				}
			}
		});
		return new GameIndex
		{
			Textures = (from t in textures
				orderby t.SourceKind, t.Name
				select t).ToList()
		};
	}
}
