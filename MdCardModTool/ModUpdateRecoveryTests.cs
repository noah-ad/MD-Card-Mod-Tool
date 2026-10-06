using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class ModUpdateRecoveryTests
{
	internal static void Run(string gameRoot, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Tests must be outside game files.");
		Directory.CreateDirectory(output);
		var engine = new ModEngine();
		string realStreaming = IndexService.StreamingRoot(gameRoot);
		TexRef fixture = IndexService.CardIllustrationBundleCandidates(realStreaming, "4007")
			.SelectMany(x => engine.ScanBundle(x, realStreaming, "游戏内图片", false).Textures).First(CardTextureRules.IsCardArt);
		TextAssetRef realGate = new OverFrameService().FindCachedGate(gameRoot) ?? throw new InvalidDataException("Need cached read-only gate fixture.");
		var realHashes = new[] { fixture.BundlePath, realGate.BundlePath }.ToDictionary(x => x, Hash, StringComparer.OrdinalIgnoreCase);
		string mirror = Path.Combine(output, "mirror", "steamapps", "common", "MD");
		string local = Path.Combine(mirror, "LocalData", "test001", "0000");
		Directory.CreateDirectory(local);
		IndexService.SetPreferredLocalRoot(mirror, local);
		SetBuild(mirror, "100");
		TexRef target = CopyCard(4007);
		string gatePath = Path.Combine(local, realGate.RelativeBundlePath);
		Copy(realGate.BundlePath, gatePath);
		var frames = new OverFrameService();
		void GateRows(IEnumerable<OverFrameMapping> rows) => engine.ReplaceTextAsset(engine.FindTextAssetFast(gatePath, local, OverFrameService.GateName)!,
			ModRecoveryService.EncodeMappings(rows), Path.Combine(output, "fixture-gate-backups"));
		GateRows(new[] { new OverFrameMapping(65000, 65000) });
		byte[] want = AstellarOverFrameComposer.Compose(Art(), AstellarOverFrameTemplateCatalog.Load("card_frame00")).GamePng;
		string cardBackup = Path.Combine(mirror, "_MD卡图备份", target.SourceKind, target.RelativeBundlePath);
		string gateBackup = Path.Combine(mirror, "_MD卡图备份", "超框开关", realGate.RelativeBundlePath);
		Copy(target.BundlePath, cardBackup); Copy(gatePath, gateBackup);
		engine.Replace(target, want, Path.Combine(output, "fixture-mod-backups"));
		frames.EnableOrUpdate(mirror, 4007, 4007);
		var store = new ModRecoveryStore(Path.Combine(output, "library"));
		store.RecordTexture(mirror, target, want, 4007);
		var recovery = new ModRecoveryService(store);
		Check(recovery.Plan(mirror, new[] { target }).Saved.Count == 1, "saved final image + overframe intent");
		string originalBackupHash = Hash(cardBackup);
		string oldGateBackupHash = Hash(gateBackup);

		// A new build replaces both the card and table and contains a new official row.
		SetBuild(mirror, "101");
		Copy(fixture.BundlePath, target.BundlePath);
		target = engine.ScanBundle(target.BundlePath, local, "本地卡图", false).Textures.First();
		byte[] updatedOfficial = Png(1024, 1024, new Rgba32(30, 60, 90, 255));
		engine.Replace(target, updatedOfficial, Path.Combine(output, "fixture-new-build"));
		GateRows(new[] { new OverFrameMapping(65000, 65000), new OverFrameMapping(65001, 65001) });
		string currentCard = Hash(target.BundlePath), currentGate = Hash(gatePath);
		ModRecoveryPlan plan = recovery.Plan(mirror, new[] { target });
		try { recovery.Reapply(mirror, plan, afterCommit: _ => throw new IOException("injected commit failure")); throw new Exception("Failure injection did not trigger"); }
		catch (IOException ex) when (ex.InnerException != null) { }
		Check(Hash(target.BundlePath) == currentCard && Hash(gatePath) == currentGate, "whole batch rollback");
		Check(Hash(cardBackup) == originalBackupHash && Hash(gateBackup) == oldGateBackupHash, "backup rollback");
		var result = recovery.Reapply(mirror, plan);
		Check(result.Reapplied == 1 && result.Skipped.Count == 0 && result.ChangedPaths.Count == 2, "reapply card and gate");
		Check(ModRecoveryService.PixelHash(engine.DecodePng(target)) == ModRecoveryService.PixelHash(want), "all RGBA pixels including transparent RGB");
		var mergedRows = frames.Read(mirror);
		Check(mergedRows.Any(x => x.CardId == 4007 && x.ArtId == 4007) && mergedRows.Any(x => x.CardId == 65001), "new official table rows preserved");
		Check(Hash(cardBackup) == currentCard && Hash(gateBackup) == currentGate, "current-build restoration baseline");
		Check(Directory.EnumerateFiles(Path.Combine(result.HistoryFolder, "previous-baseline"), "*", SearchOption.AllDirectories).Select(Hash).Contains(originalBackupHash), "old originals retained");
		string appliedCard = Hash(target.BundlePath), appliedGate = Hash(gatePath);
		var duplicate = recovery.Reapply(mirror, recovery.Plan(mirror, new[] { target }));
		Check(duplicate.Reapplied == 0 && duplicate.AlreadyApplied == 1 && duplicate.ChangedPaths.Count == 0, "idempotence; no needless rewrites");
		var cancelled = new CancellationTokenSource(); cancelled.Cancel();
		try { recovery.Reapply(mirror, plan, cancellationToken: cancelled.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { }
		Check(Hash(target.BundlePath) == appliedCard && Hash(gatePath) == appliedGate, "cancel before commit");
		Copy(cardBackup, target.BundlePath); frames.Disable(mirror, 4007);
		store.DeactivateBundle(mirror, target);
		Check(recovery.Plan(mirror, new[] { target }).Count == 0, "restored Mods do not reactivate");

		// Account isolation, including after selecting a different profile.
		string second = Path.Combine(mirror, "LocalData", "test002", "0000"); Directory.CreateDirectory(second);
		IndexService.SetPreferredLocalRoot(mirror, second);
		Check(store.Load(mirror).Textures.Count == 0, "account isolation");
		IndexService.SetPreferredLocalRoot(mirror, local);
		Check(store.Load(mirror).Textures.Count == 1, "original account retained");

		TexRef legacy = CopyCard(65010);
		Copy(legacy.BundlePath, Path.Combine(mirror, "_MD卡图备份", legacy.SourceKind, legacy.RelativeBundlePath));
		OverFrameArtStore.SaveArt(mirror, 65010, Art());
		OverFrameArtStore.SaveBackground(mirror, 65010, Png(300, 200, new Rgba32(20, 100, 150, 255)));
		OverFrameArtStore.SaveSettings(mirror, 65010, new OverFrameFrameSettings("transparent_card_frame01", false, true, "AstellarTransparent",
			BackgroundImageScale: 3, BackgroundOffsetX: 37, BackgroundOffsetY: -23, RemoveFoilInnerFrame: true));
		var legacyPlan = recovery.Plan(mirror, new[] { legacy, target });
		Check(legacyPlan.LegacyCards.SequenceEqual(new ushort[] { 65010 }), "legacy materials require target backups");
		byte[] legacyPng = OverFrameRecoveryComposer.Compose(mirror, 65010);
		var legacyResult = recovery.Reapply(mirror, legacyPlan);
		Check(legacyResult.Reapplied == 1 && legacyResult.Skipped.Count == 0, "legacy draft recomposed");
		Check(ModRecoveryService.PixelHash(engine.DecodePng(legacy)) == ModRecoveryService.PixelHash(legacyPng), "legacy backdrop transform / transparent foil mask retained");
		Check(store.Load(mirror).Textures.Any(x => x.CardKey == "65010" && x.Active && x.OverFrameArtId == 65010), "legacy migration persisted");
		foreach (var mode in new[] { ("transparent_card_frame01", "AstellarTransparent"), ("transparent_gradient_card_frame01", "TransparentGradient"),
			("gradient_card_frame01", "FloowanGradient"), ("card_frame01", "StandardComplete") })
		{
			OverFrameArtStore.SaveSettings(mirror, 65010, new OverFrameFrameSettings(mode.Item1, false, true, mode.Item2));
			Check(Image.Identify(OverFrameRecoveryComposer.Compose(mirror, 65010)) is { Width: 704, Height: 1024 }, "legacy mode " + mode.Item2);
		}

		var active = store.Load(mirror).Textures.First(x => x.CardKey == "65010");
		string blob = Path.Combine(store.Folder(mirror), "images", active.PngSha256 + ".png");
		byte[] savedBlob = File.ReadAllBytes(blob);
		File.WriteAllBytes(blob, new byte[] { 0, 1, 2 });
		string beforeCorrupt = Hash(legacy.BundlePath);
		var corrupt = recovery.Reapply(mirror, recovery.Plan(mirror, new[] { legacy }));
		Check(corrupt.Skipped.Count == 1 && corrupt.Reapplied == 0 && Hash(legacy.BundlePath) == beforeCorrupt, "corrupt desired output rejected before write");
		File.WriteAllBytes(blob, savedBlob);
		var missing = new RecoverableTextureMod { Id = "missing", RelativePath = "ff/ffffffff", Name = "missing", SourceKind = "视觉资源", PngSha256 = active.PngSha256 };
		var unsupported = recovery.Reapply(mirror, new ModRecoveryPlan(new[] { missing }, Array.Empty<ushort>()));
		Check(unsupported.Skipped.Count == 1 && unsupported.ChangedPaths.Count == 0, "missing resources fail closed");
		var variants = new ModRecoveryStore(Path.Combine(output, "variant-library"));
		variants.RecordTexture(mirror, target, want, 4007);
		string ocgPath = Path.Combine(local, IndexService.CardIllustrationRelativePath("4007", "ocg"));
		Copy(fixture.BundlePath, ocgPath);
		TexRef ocg = engine.ScanBundle(ocgPath, local, "本地卡图", false).Textures.First();
		variants.RecordTexture(mirror, ocg, updatedOfficial, 4007);
		variants.RecordTexture(mirror, target, legacyPng, 4007);
		var variantEntries = variants.Load(mirror).Textures;
		Check(variantEntries.Count == 2 && variantEntries.Single(x => x.RelativePath == target.RelativeBundlePath.Replace('\\', '/')).PngSha256 == Convert.ToHexString(SHA256.HashData(legacyPng)),
			"TCG/OCG recovery intents independent; second edit replaces same variant");
		TestVersionedGate();
		Check(realHashes.All(x => Hash(x.Key) == x.Value), "real game files unchanged");
		Console.WriteLine("PASS update recovery / transparent RGB / gate merge / legacy materials / account isolation / rollback; gameWrites=False");

		TexRef CopyCard(ushort id)
		{
			string path = Path.Combine(local, IndexService.CardIllustrationRelativePath(id.ToString()));
			Copy(fixture.BundlePath, path);
			return engine.ScanBundle(path, local, "本地卡图", false).Textures.First();
		}
		void TestVersionedGate()
		{
			string gateGame = Path.Combine(output, "versioned-gate-game");
			string gateLocal = Path.Combine(gateGame, "LocalData", "gate001", "0000");
			Directory.CreateDirectory(gateLocal); IndexService.SetPreferredLocalRoot(gateGame, gateLocal);
			string oldGate = Path.Combine(gateLocal, IndexService.ResourceBundleRelativePath("Card/Data/old-test/md/of_card_asset"));
			Copy(realGate.BundlePath, oldGate);
			RewriteFixtureFamily(oldGate, "old-test");
			var versionFrames = new OverFrameService(Path.Combine(output, "versioned-gate-cache"));
			Check(versionFrames.FindGate(gateGame).BundlePath == oldGate, "legacy gate fixture cached");
			string currentGate = Path.Combine(gateLocal, realGate.RelativeBundlePath);
			Copy(realGate.BundlePath, currentGate);
			string realLocal = IndexService.FindLocalRoot(gameRoot)!;
			foreach (string dictionary in new[] { "fa2ed7f8", "2f2cc400", "0b636c14" })
			{
				string source = Path.Combine(realLocal, dictionary[..2], dictionary);
				realHashes[source] = Hash(source);
				Copy(source, Path.Combine(gateLocal, dictionary[..2], dictionary));
			}
			File.SetLastWriteTimeUtc(oldGate, DateTime.UtcNow.AddDays(1)); // Old Mod mtime is not a version.
			string oldHash = Hash(oldGate);
			Check(versionFrames.FindGate(gateGame).BundlePath == currentGate, "cached old-version gate rejected; current dictionary family selected");
			versionFrames.EnableOrUpdate(gateGame, 65020, 65020);
			Check(Hash(oldGate) == oldHash && versionFrames.Read(gateGame).Any(x => x.CardId == 65020), "gate change writes only current version");
			string currentCopy = Path.Combine(output, "current-gate-held.bundle"); Copy(currentGate, currentCopy);
			File.Delete(currentGate); // Only a test-created copy inside this output directory.
			try { versionFrames.FindGate(gateGame); throw new Exception("Missing current version accepted an old gate"); }
			catch (FileNotFoundException) { }
			Check(Hash(oldGate) == oldHash, "missing current-version gate fails closed, not old-version fallback");
			Copy(currentCopy, currentGate);
		}
	}

	private static void RewriteFixtureFamily(string path, string version)
	{
		var manager = new AssetsManager();
		manager.LoadClassPackage(AppPaths.ResolveFile("classdata.tpk"));
		string temp = path + ".test-family";
		try
		{
			var bundle = manager.LoadBundleFile(path);
			var asset = manager.LoadAssetsFileFromBundle(bundle, bundle.file.GetAllFileNames().First());
			manager.LoadClassDatabaseFromPackage(asset.file.Metadata.UnityVersion);
			var replacements = new List<AssetsReplacer>();
			foreach (var info in asset.file.GetAssetsOfType(AssetClassID.AssetBundle))
			{
				var field = manager.GetBaseField(asset, info);
				foreach (var pair in field["m_Container"]["Array"].Children)
					pair["first"].AsString = Regex.Replace(pair["first"].AsString, "/card/data/[^/]+/", "/card/data/" + version + "/");
				replacements.Add(new AssetsReplacerFromMemory(asset.file, info, field));
			}
			using var bytes = new MemoryStream();
			using (var writer = new AssetsFileWriter(bytes)) asset.file.Write(writer, 0, replacements);
			using (var writer = new AssetsFileWriter(temp)) bundle.file.Write(writer, new List<BundleReplacer>
				{ new BundleReplacerFromMemory(asset.name, asset.name, true, bytes.ToArray(), -1) });
		}
		finally { manager.UnloadAll(); }
		File.Move(temp, path, true);
	}

	private static byte[] Art()
	{
		using var image = new Image<Rgba32>(704, 1024, new Rgba32(91, 39, 212, 0));
		for (int y = 180; y < 560; y++) for (int x = 210; x < 480; x++) image[x, y] = new Rgba32((byte)(x % 251), (byte)(y % 251), 120, 255);
		using var memory = new MemoryStream();
		image.Save(memory, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, TransparentColorMode = PngTransparentColorMode.Preserve });
		return memory.ToArray();
	}
	private static byte[] Png(int width, int height, Rgba32 color) { using var image = new Image<Rgba32>(width, height, color); using var memory = new MemoryStream(); image.SaveAsPng(memory); return memory.ToArray(); }
	private static void SetBuild(string gameRoot, string build) { string path = Path.GetFullPath(Path.Combine(gameRoot, "..", "..", "appmanifest_1449850.acf")); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "\"buildid\" \"" + build + "\""); }
	private static void Copy(string source, string dest) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest, true); }
	private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
	private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
