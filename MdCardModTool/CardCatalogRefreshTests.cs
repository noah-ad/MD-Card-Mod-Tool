using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace MdCardModTool;

internal static class CardCatalogRefreshTests
{
    internal static void Run(string gameRoot, string output)
    {
        Directory.CreateDirectory(output);
        using var trace = new System.Diagnostics.ConsoleTraceListener();
        System.Diagnostics.Trace.Listeners.Add(trace);
        string fixture = Path.Combine(output, "files", "aa");
        Directory.CreateDirectory(fixture);
        string path = Path.Combine(fixture, "aaaaaaaa");
        File.WriteAllText(path, "old");
        var stamp = GameCardCatalogUpdater.CaptureResourceFingerprint(fixture);
        File.WriteAllText(path, "new language data");
        var changed = GameCardCatalogUpdater.CaptureResourceFingerprint(fixture);
        if (changed == stamp) throw new Exception("Same-build resource replacement not detected");
        File.WriteAllText(Path.Combine(fixture, "bbbbbbbb"), "new card bundle");
        if (GameCardCatalogUpdater.CaptureResourceFingerprint(fixture) == changed)
            throw new Exception("Added Bundle not detected");

        string cache = Path.Combine(output, "cache");
        var entries = GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, cache,
            (done, total, found) => { if (done % 1000 == 0 || done == total) Console.WriteLine($"scanned={done}/{total}; dictionaries={found}/9"); });
        var catalog = new CardCatalogService(entries);
        var card = catalog.Find(22920);
        if (card?.SimplifiedChineseName != "小丑戏帮 哈特"
            || !catalog.Search("哈特", 100).Any(entry => entry.CardId == 22920))
            throw new Exception("22920 game name or name search missing");
        if (GameCardCatalogUpdater.NeedsUpdate(gameRoot, cache)) throw new Exception("Unchanged data rescans");
        GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, cache, (_, _, _) => throw new Exception("Cache hit rescanned Bundles"));
        string statePath = Path.Combine(cache, "card-catalog-game-state-v2.json");
        var state = JsonNode.Parse(File.ReadAllText(statePath))!;
        state["FormatVersion"] = 2;
        File.WriteAllText(statePath, state.ToJsonString());
        if (!GameCardCatalogUpdater.NeedsUpdate(gameRoot, cache)) throw new Exception("Legacy cache not invalidated");
        state["FormatVersion"] = 3;
        state["ResourceFingerprint"] = "old-resources-same-build";
        File.WriteAllText(statePath, state.ToJsonString());
        if (!GameCardCatalogUpdater.NeedsUpdate(gameRoot, cache)) throw new Exception("Same-build update ignored");

        // Current local game's verified dictionary triplet, copied into an
        // isolated install. This fixture is deliberately not a production path list.
        string mirror = Path.Combine(output, "mirror-game");
        string mirrorLocal = Path.Combine(mirror, "LocalData", "test-account", "0000");
        string liveLocal = IndexService.FindLocalRoot(gameRoot)!;
        foreach (string id in new[] { "fa2ed7f8", "2f2cc400", "0b636c14" })
        {
            string copy = Path.Combine(mirrorLocal, id[..2], id);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(Path.Combine(liveLocal, id[..2], id), copy, true);
        }
        string mirrorCache = Path.Combine(output, "mirror-cache");
        GameCardCatalogUpdater.UpdateIfNeeded(mirror, mirrorCache);
        // Simulate an old catalog followed by an in-place game data download,
        // without changing its Steam build ID or any real game file.
        CardCatalogService.Write(Path.Combine(mirrorCache, "card-catalog-extra-v2.json.br"),
            new[] { new CardCatalogEntry { CardId = 22920, EnglishName = "Retained test translation" } });
        string touched = Path.Combine(mirrorLocal, "0b", "0b636c14");
        File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddSeconds(2));
        if (!GameCardCatalogUpdater.NeedsUpdate(mirror, mirrorCache)) throw new Exception("In-place download ignored");
        var refreshed = new CardCatalogService(GameCardCatalogUpdater.UpdateIfNeeded(mirror, mirrorCache)).Find(22920);
        if (refreshed?.SimplifiedChineseName != card.SimplifiedChineseName || refreshed.EnglishName != "Retained test translation")
            throw new Exception("Same-build automatic name merge or translation retention failed");
        if (GameCardCatalogUpdater.NeedsUpdate(mirror, mirrorCache)) throw new Exception("Mirror refresh not persisted");
        Console.WriteLine($"card=22920; name={card.SimplifiedChineseName}; cards={entries.Count}; search=True; sameBuild=True; cacheHit=True; legacyInvalidated=True; gameWrites=False; ready=True");
    }
}
