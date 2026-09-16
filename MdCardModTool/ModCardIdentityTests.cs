using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace MdCardModTool;

internal static class ModCardIdentityTests
{
	internal static void Run(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		string? text = IndexService.FindLocalRoot(gameRoot);
		string text2 = IndexService.CardIllustrationBundleCandidates(text, "22524").First();
		string text3 = IndexService.CardIllustrationBundleCandidates(text, "3801").First();
		string text4 = Hash(text2);
		string text5 = Hash(text3);
		string text6 = Path.Combine(output, "game");
		string text7 = Path.Combine(text6, "LocalData", "test-account", "0000");
		string relativePath = Path.GetRelativePath(text, text2);
		string text8 = Path.Combine(text7, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(text8));
		File.Copy(text2, text8, overwrite: true);
		ModEngine modEngine = new ModEngine();
		TexRef texRef = modEngine.ScanBundle(text8, text7, "本地卡图", includeDependencies: false).Textures.Single();
		string text9 = Path.Combine(output, "renamed-card.zip");
		using (ZipArchive destination = ZipFile.Open(text9, ZipArchiveMode.Create))
		{
			destination.CreateEntryFromFile(text3, "LocalData/shared-account/0000/" + relativePath.Replace('\\', '/'));
		}
		ModImportResult modImportResult = new ModPackageService().Import(text6, text9, new TexRef[1] { texRef });
		List<TexRef> textures = modEngine.ScanBundle(text8, text7, "本地卡图", includeDependencies: false).Textures;
		CardBundleIdentity.Refresh(texRef, textures);
		if (modImportResult.BundleCount != 1 || texRef.CardKey != "22524" || textures.Single().CardKey != "22524" || texRef.PathId != textures.Single().PathId || texRef.AssetFileName != textures.Single().AssetFileName)
		{
			throw new Exception("Imported donor identity replaced target card or locator is stale");
		}
		if (modEngine.DecodePng(texRef).Length == 0)
		{
			throw new Exception("Imported card preview failed");
		}
		if (CardCatalogService.LoadBestAvailable().Search("22524").All((CardCatalogEntry c) => c.CardId != 22524))
		{
			throw new Exception("Card catalog search lost target");
		}
		string text10 = Path.Combine(text6, "_MD卡图备份", "本地卡图", relativePath);
		if (Hash(text10) != text4)
		{
			throw new Exception("Original backup missing");
		}
		File.Copy(text10, text8, overwrite: true);
		CardBundleIdentity.Refresh(texRef, modEngine.ScanBundle(text8, text7, "本地卡图", includeDependencies: false).Textures);
		if (Hash(text8) != text4 || modEngine.DecodePng(texRef).Length == 0)
		{
			throw new Exception("Restore or preview failed");
		}
		if (Hash(text2) != text4 || Hash(text3) != text5)
		{
			throw new Exception("Real game changed");
		}
		Console.WriteLine("card=22524; foreignNameAndPathId=True; imported=True; rescanIdentity=True; preview=True; search=True; backupRestore=True; gameWrites=False; ready=True");
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}
}
