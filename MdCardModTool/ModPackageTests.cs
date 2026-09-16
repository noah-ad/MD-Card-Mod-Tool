using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MdCardModTool;

internal static class ModPackageTests
{
	public static void Run(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		MonsterAnimationAssetRef[] array = MonsterAnimationIndexService.Find(gameRoot, "10001").Assets.DistinctBy((MonsterAnimationAssetRef x) => x.BundlePath).Take(2).ToArray();
		if (array.Length != 2)
		{
			throw new InvalidDataException("Need two local real bundles for read-only fixtures");
		}
		byte[] original = File.ReadAllBytes(array[0].BundlePath);
		byte[] modified = File.ReadAllBytes(array[1].BundlePath);
		string[] second = array.Select((MonsterAnimationAssetRef a) => Hash(a.BundlePath)).ToArray();
		string source = Path.Combine(output, "exporter");
		string target = Path.Combine(output, "importer");
		string sourceLocal = Path.Combine(source, "LocalData", "abc001", "0000");
		string targetLocal = Path.Combine(target, "LocalData", "def002", "0000");
		Directory.CreateDirectory(sourceLocal);
		Directory.CreateDirectory(targetLocal);
		Directory.CreateDirectory(Path.Combine(target, "LocalData", "other003", "0000"));
		IndexService.SetPreferredLocalRoot(source, sourceLocal);
		IndexService.SetPreferredLocalRoot(target, targetLocal);
		string[] kinds = new string[4] { "本地卡图", "视觉资源", "游戏内图片", "基础视觉资源" };
		string[] relative = new string[4] { "12/12345678", "34/34567890", "56/56789012", "masterduel_Data/SharedAssets/cardframe.bundle" };
		TexRef[] array2 = (from i in Enumerable.Range(0, 4)
			select new TexRef
			{
				SourceKind = kinds[i],
				Name = "fixture" + i,
				RelativeBundlePath = relative[i],
				BundlePath = Path.Combine((i < 2) ? sourceLocal : ((i == 2) ? IndexService.StreamingRoot(source) : source), relative[i])
			}).ToArray();
		string[] targets = (from i in Enumerable.Range(0, 4)
			select Path.Combine((i < 2) ? targetLocal : ((i == 2) ? IndexService.StreamingRoot(target) : target), relative[i])).ToArray();
		TexRef[] targetTextures = array2.Select((TexRef x, int i) => new TexRef
		{
			BundlePath = targets[i],
			SourceKind = x.SourceKind,
			RelativeBundlePath = x.RelativeBundlePath
		}).ToArray();
		for (int num = 0; num < 4; num++)
		{
			Write(array2[num].BundlePath, modified);
			Write(Path.Combine(source, "_MD卡图备份", kinds[num], relative[num]), original);
			Write(targets[num], original);
		}
		ModPackageService service = new ModPackageService();
		string text = Path.Combine(output, "direct.zip");
		string text2 = Path.Combine(output, "legacy.mdmod.zip");
		string text3 = Path.Combine(output, "without-manifest.zip");
		service.Export(source, array2, text, directReplacement: true);
		service.Export(source, array2, text2);
		using (ZipArchive zipArchive = ZipFile.OpenRead(text))
		{
			Assert(zipArchive.GetEntry("LocalData/abc001/0000/12/12345678") != null, "Direct path");
			Assert(zipArchive.GetEntry("masterduel_Data/StreamingAssets/AssetBundle/56/56789012") != null, "Streaming path");
			Assert(!zipArchive.Entries.Any((ZipArchiveEntry e) => e.FullName.StartsWith("files/")), "No numbered files in direct ZIP");
			using ZipArchive zipArchive2 = ZipFile.Open(text3, ZipArchiveMode.Create);
			foreach (ZipArchiveEntry item in zipArchive.Entries.Where((ZipArchiveEntry e) => e.FullName != "manifest.json"))
			{
				using Stream stream = item.Open();
				using Stream destination = zipArchive2.CreateEntry(item.FullName).Open();
				stream.CopyTo(destination);
			}
		}
		string[] array3 = new string[3] { text2, text, text3 };
		foreach (string text4 in array3)
		{
			Assert(service.Inspect(text4).BundleCount == 4, "Inspect count");
			Assert(service.Import(target, text4, targetTextures).BundleCount == 4, "Import count");
			Assert(targets.All((string t) => File.ReadAllBytes(t).SequenceEqual(modified)), "Installed bytes");
			Assert(!Directory.Exists(Path.Combine(target, "LocalData", "abc001")), "Exporter's account must not be installed");
			for (int num3 = 0; num3 < 4; num3++)
			{
				string path = ((text4 == text3 && num3 < 3) ? ((num3 < 2) ? "召唤动画" : "召唤动画-游戏内") : kinds[num3]);
				Assert(File.ReadAllBytes(Path.Combine(target, "_MD卡图备份", path, relative[num3])).SequenceEqual(original), "First backup unchanged");
				Write(targets[num3], original);
			}
		}
		array3 = new string[3] { "0000/12/12345678", "12/12345678", "Shared Mod/LocalData/a/0000/12/12345678" };
		foreach (string entryName in array3)
		{
			string text5 = Path.Combine(output, "layout-" + Guid.NewGuid().ToString("N") + ".zip");
			using (ZipArchive zipArchive3 = ZipFile.Open(text5, ZipArchiveMode.Create))
			{
				using Stream stream2 = zipArchive3.CreateEntry(entryName).Open();
				stream2.Write(modified);
			}
			Assert(service.Import(target, text5, targetTextures).BundleCount == 1, "Alternative directory layout");
			Assert(File.ReadAllBytes(targets[0]).SequenceEqual(modified), "Alternative account mapping");
			Write(targets[0], original);
		}
		try
		{
			service.Import(target, text, targetTextures, delegate(int count)
			{
				if (count == 2)
				{
					throw new IOException("test commit failure");
				}
			});
			throw new Exception("Missing injected failure");
		}
		catch (IOException ex) when (ex.Message == "test commit failure")
		{
		}
		Assert(targets.All((string t) => File.ReadAllBytes(t).SequenceEqual(original)), "Rollback exact");
		int rejected = 0;
		Reject("traversal", delegate(ZipArchive z)
		{
			Add(z, "../outside", modified);
		}, inspectOnly: true);
		Reject("absolute", delegate(ZipArchive z)
		{
			Add(z, "C:/outside", modified);
		}, inspectOnly: true);
		Reject("ads", delegate(ZipArchive z)
		{
			Add(z, "12/12345678:evil", modified);
		}, inspectOnly: true);
		Reject("duplicate", delegate(ZipArchive z)
		{
			Add(z, "12/12345678", modified);
			Add(z, "12/12345678", modified);
		}, inspectOnly: true);
		Reject("symlink", delegate(ZipArchive z)
		{
			ZipArchiveEntry zipArchiveEntry = z.CreateEntry("12/12345678");
			zipArchiveEntry.ExternalAttributes = -1577123840;
			using Stream stream3 = zipArchiveEntry.Open();
			stream3.Write(modified);
		}, inspectOnly: true);
		Reject("two-accounts", delegate(ZipArchive z)
		{
			Add(z, "LocalData/a/0000/12/12345678", modified);
			Add(z, "LocalData/b/0000/12/12345678", modified);
		}, inspectOnly: true);
		Reject("executable", delegate(ZipArchive z)
		{
			Add(z, "masterduel_Data/evil.dll", Encoding.UTF8.GetBytes("MZ-not-a-bundle"));
		}, inspectOnly: true);
		Reject("missing-target", delegate(ZipArchive z)
		{
			Add(z, "12/12345679", modified);
		});
		Reject("corrupt", delegate(ZipArchive z)
		{
			Add(z, "12/12345678", Encoding.ASCII.GetBytes("UnityFS\0malformed"));
		});
		string text6 = Path.Combine(output, "tampered.zip");
		File.Copy(text, text6);
		using (ZipArchive zipArchive4 = ZipFile.Open(text6, ZipArchiveMode.Update))
		{
			zipArchive4.GetEntry("LocalData/abc001/0000/12/12345678").Delete();
			Add(zipArchive4, "LocalData/abc001/0000/12/12345678", original);
		}
		try
		{
			service.Import(target, text6, targetTextures);
			throw new Exception("Tampered hash accepted");
		}
		catch (InvalidDataException)
		{
			rejected++;
		}
		Assert(targets.All((string t) => File.ReadAllBytes(t).SequenceEqual(original)), "Rejected import wrote files");
		Assert(array.Select((MonsterAnimationAssetRef a) => Hash(a.BundlePath)).SequenceEqual(second), "Real game modified");
		AppLanguage[] values = Enum.GetValues<AppLanguage>();
		for (int num2 = 0; num2 < values.Length; num2++)
		{
			Localizer.SetLanguage(values[num2]);
			Assert(Localizer.T("mods.export.filter").Split('|').Length == 4, "Format choices");
		}
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		Console.WriteLine($"legacy=True; directLayout=True; rawImport=True; selectedAccount=True; kinds=4; backup=True; rollback=True; rejected={rejected}; gameWrites=False; ready=True");
		static void Add(ZipArchive zip, string entryName2, byte[] data)
		{
			using Stream stream3 = zip.CreateEntry(entryName2).Open();
			stream3.Write(data);
		}
		void Reject(string name, Action<ZipArchive> make, bool inspectOnly = false)
		{
			string text7 = Path.Combine(output, name + ".zip");
			using (ZipArchive obj = ZipFile.Open(text7, ZipArchiveMode.Create))
			{
				make(obj);
			}
			try
			{
				if (inspectOnly)
				{
					service.Inspect(text7);
				}
				else
				{
					service.Import(target, text7, targetTextures);
				}
			}
			catch (Exception ex3) when (((ex3 is InvalidDataException || ex3 is FileNotFoundException || ex3 is ArgumentException) ? 1 : 0) != 0)
			{
				rejected++;
				return;
			}
			throw new Exception("Unsafe archive was accepted: " + name);
		}
	}

	private static void Write(string path, byte[] data)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllBytes(path, data);
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static void Assert(bool ok, string message)
	{
		if (!ok)
		{
			throw new InvalidDataException(message);
		}
	}
}
