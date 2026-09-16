using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace MdCardModTool;

internal static class CardCatalogRefreshTests
{
	internal static void Run(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		using ConsoleTraceListener listener = new ConsoleTraceListener();
		Trace.Listeners.Add(listener);
		string text = Path.Combine(output, "files", "aa");
		Directory.CreateDirectory(text);
		string path = Path.Combine(text, "aaaaaaaa");
		File.WriteAllText(path, "old");
		string text2 = GameCardCatalogUpdater.CaptureResourceFingerprint(text);
		File.WriteAllText(path, "new language data");
		string text3 = GameCardCatalogUpdater.CaptureResourceFingerprint(text);
		if (text3 == text2)
		{
			throw new Exception("Same-build resource replacement not detected");
		}
		File.WriteAllText(Path.Combine(text, "bbbbbbbb"), "new card bundle");
		if (GameCardCatalogUpdater.CaptureResourceFingerprint(text) == text3)
		{
			throw new Exception("Added Bundle not detected");
		}
		string text4 = Path.Combine(output, "cache");
		IReadOnlyList<CardCatalogEntry> readOnlyList = GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, text4, delegate(int done, int total, int found)
		{
			if (done % 1000 == 0 || done == total)
			{
				Console.WriteLine($"scanned={done}/{total}; dictionaries={found}/9");
			}
		});
		CardCatalogService cardCatalogService = new CardCatalogService(readOnlyList);
		CardCatalogEntry cardCatalogEntry = cardCatalogService.Find(22920);
		if (cardCatalogEntry?.SimplifiedChineseName != "小丑戏帮 哈特" || !cardCatalogService.Search("哈特", 100).Any((CardCatalogEntry entry) => entry.CardId == 22920))
		{
			throw new Exception("22920 game name or name search missing");
		}
		if (GameCardCatalogUpdater.NeedsUpdate(gameRoot, text4))
		{
			throw new Exception("Unchanged data rescans");
		}
		GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, text4, delegate
		{
			throw new Exception("Cache hit rescanned Bundles");
		});
		string path2 = Path.Combine(text4, "card-catalog-game-state-v2.json");
		JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(path2));
		jsonNode["FormatVersion"] = 2;
		File.WriteAllText(path2, jsonNode.ToJsonString());
		if (!GameCardCatalogUpdater.NeedsUpdate(gameRoot, text4))
		{
			throw new Exception("Legacy cache not invalidated");
		}
		jsonNode["FormatVersion"] = 3;
		jsonNode["ResourceFingerprint"] = "old-resources-same-build";
		File.WriteAllText(path2, jsonNode.ToJsonString());
		if (!GameCardCatalogUpdater.NeedsUpdate(gameRoot, text4))
		{
			throw new Exception("Same-build update ignored");
		}
		string text5 = Path.Combine(output, "mirror-game");
		string path3 = Path.Combine(text5, "LocalData", "test-account", "0000");
		string path4 = IndexService.FindLocalRoot(gameRoot);
		string[] array = new string[3] { "fa2ed7f8", "2f2cc400", "0b636c14" };
		foreach (string text6 in array)
		{
			string text7 = Path.Combine(path3, text6.Substring(0, 2), text6);
			Directory.CreateDirectory(Path.GetDirectoryName(text7));
			File.Copy(Path.Combine(path4, text6.Substring(0, 2), text6), text7, overwrite: true);
		}
		string text8 = Path.Combine(output, "mirror-cache");
		GameCardCatalogUpdater.UpdateIfNeeded(text5, text8);
		CardCatalogService.Write(Path.Combine(text8, "card-catalog-extra-v2.json.br"), new CardCatalogEntry[1]
		{
			new CardCatalogEntry
			{
				CardId = 22920,
				EnglishName = "Retained test translation"
			}
		});
		string path5 = Path.Combine(path3, "0b", "0b636c14");
		File.SetLastWriteTimeUtc(path5, File.GetLastWriteTimeUtc(path5).AddSeconds(2.0));
		if (!GameCardCatalogUpdater.NeedsUpdate(text5, text8))
		{
			throw new Exception("In-place download ignored");
		}
		CardCatalogEntry cardCatalogEntry2 = new CardCatalogService(GameCardCatalogUpdater.UpdateIfNeeded(text5, text8)).Find(22920);
		if (cardCatalogEntry2?.SimplifiedChineseName != cardCatalogEntry.SimplifiedChineseName || cardCatalogEntry2.EnglishName != "Retained test translation")
		{
			throw new Exception("Same-build automatic name merge or translation retention failed");
		}
		if (GameCardCatalogUpdater.NeedsUpdate(text5, text8))
		{
			throw new Exception("Mirror refresh not persisted");
		}
		Console.WriteLine($"card=22920; name={cardCatalogEntry.SimplifiedChineseName}; cards={readOnlyList.Count}; search=True; sameBuild=True; cacheHit=True; legacyInvalidated=True; gameWrites=False; ready=True");
	}
}
