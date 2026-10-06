using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class AutomaticCardUpdateTests
{
	internal static void Run(string gameRoot, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Test output must be outside game.");
		Directory.CreateDirectory(output);
		var engine = new ModEngine();
		string realLocal = IndexService.FindLocalRoot(gameRoot)!;
		string realStream = IndexService.StreamingRoot(gameRoot);
		var source = IndexService.CardIllustrationBundleCandidates(realStream, "4007").SelectMany(x => engine.ScanBundle(x, realStream, "游戏内图片", false).Textures).First(CardTextureRules.IsCardArt);
		var originals = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [source.BundlePath] = Hash(source.BundlePath) };
		string mirror = Path.Combine(output, "game");
		string local = Path.Combine(mirror, "LocalData", "test001", "0000");
		Directory.CreateDirectory(local); IndexService.SetPreferredLocalRoot(mirror, local);
		string unknown = Path.Combine(local, IndexService.CardIllustrationRelativePath("60000"));
		Copy(source.BundlePath, unknown);
		Check(CardCatalogService.LoadBestAvailable().Find(60000) == null, "fixture absent from bundled/extra catalogs");
		var index = new GameIndex();
		var missing = IndexService.ScanMissingLocalCard(mirror, index, "0");
		Check(missing.Textures.Any(x => x.CardKey == "60000"), "uncatalogued downloaded card discovered by format-domain CRC");
		var visible = new System.Collections.Generic.List<TexRef>();
		Check(NewCardDiscovery.Merge(index, visible, missing) == 1, "new card merged once");
		Check(IndexService.ScanMissingLocalCard(mirror, index, "0").ScannedBundles == 0, "unchanged illustrations are not rescanned");
		string idsPath = Path.Combine(output, "observed.json.br");
		NewCardDiscovery.SaveObservedIds(index.Textures, idsPath);
		var nameless = CardCatalogService.Read(idsPath).Single(x => x.CardId == 60000);
		Check(nameless.Name(AppLanguage.SimplifiedChinese) == "60000" && !nameless.IsMonster, "ID fallback does not fabricate names or eligibility");
		var texture = engine.ScanBundle(unknown, local, "本地卡图", false).Textures.First();
		using (var image = new Image<Rgba32>(1024, 1024, new Rgba32(50, 70, 100, 255)))
		{
			using var bytes = new MemoryStream(); image.SaveAsPng(bytes);
			engine.Replace(texture, bytes.ToArray(), Path.Combine(output, "fixture-backups"));
		}
		var changed = IndexService.ScanMissingLocalCard(mirror, index, "0");
		Check(changed.ScannedBundles == 1 && changed.Textures.Single().Width == 1024, "same-build resource replacement detected");
		Check(NewCardDiscovery.Merge(index, visible, changed) == 0 && index.Textures.Count == 1 && visible.Count == 1, "stale texture metadata replaced, not duplicated");
		IndexService.Save(mirror, index);
		Check(CardCatalogService.Read(idsPath).Count == 1, "observed IDs persist");
		try { GameCardCatalogUpdater.UpdateIfNeeded(mirror, Path.Combine(output, "missing-dictionary-cache")); throw new Exception("Missing dictionaries unexpectedly succeeded"); }
		catch (InvalidDataException) { }
		string other = Path.Combine(local, IndexService.CardIllustrationRelativePath("60001"));
		Copy(source.BundlePath, other);
		var despiteNames = IndexService.ScanMissingLocalCard(mirror, index, "0");
		Check(despiteNames.Textures.Any(x => x.CardKey == "60001"), "dictionary failure does not prevent card discovery");

		// Real dictionary fixtures are copied. Update the final record in decoded
		// CARD_Name/Indx/Prop, keeping all old records and their logical containers.
		string dictionaryMirror = Path.Combine(output, "dictionary-game");
		string dictionaryLocal = Path.Combine(dictionaryMirror, "LocalData", "test002", "0000");
		Directory.CreateDirectory(dictionaryLocal); IndexService.SetPreferredLocalRoot(dictionaryMirror, dictionaryLocal);
		var locations = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string file in new[] { "fa2ed7f8", "2f2cc400", "0b636c14" })
		{
			string real = Path.Combine(realLocal, file[..2], file);
			originals[real] = Hash(real);
			string copied = Path.Combine(dictionaryLocal, file[..2], file);
			Copy(real, copied);
			foreach (string container in engine.ReadAssetBundleContainerPaths(copied))
			{
				string kind = Path.GetFileName(container).ToLowerInvariant();
				if (kind is "card_name.bytes" or "card_indx.bytes" or "card_prop.bytes") locations[kind] = copied;
			}
		}
		Check(locations.Count == 3, "coherent real dictionary fixtures");
		string cache = Path.Combine(output, "dictionary-cache");
		var first = GameCardCatalogUpdater.UpdateIfNeeded(dictionaryMirror, cache);
		Check(first.Any(x => x.CardId == 22920 && x.SimplifiedChineseName == "小丑戏帮 哈特"), "existing new Pendulum card names retained");
		byte[] props = GameCardCatalogUpdater.ReadDictionary(locations["card_prop.bytes"], "card_prop.bytes");
		byte[] indx = GameCardCatalogUpdater.ReadDictionary(locations["card_indx.bytes"], "card_indx.bytes");
		byte[] names = GameCardCatalogUpdater.ReadDictionary(locations["card_name.bytes"], "card_name.bytes");
		int rows = Math.Min(props.Length, indx.Length) / 8;
		byte[] newProps = new byte[(rows + 1) * 8], newIndex = new byte[(rows + 1) * 8];
		Array.Copy(props, newProps, rows * 8); Array.Copy(indx, newIndex, rows * 8);
		BitConverter.TryWriteBytes(newProps.AsSpan(rows * 8, 2), (ushort)60000);
		newProps[rows * 8 + 2] = 1;
		BitConverter.TryWriteBytes(newIndex.AsSpan(rows * 8, 4), names.Length);
		byte[] newNames = names.Concat(Encoding.UTF8.GetBytes("新卡自动更新测试\0")).ToArray();
		WriteDictionary("card_prop.bytes", newProps); WriteDictionary("card_indx.bytes", newIndex); WriteDictionary("card_name.bytes", newNames);
		Check(GameCardCatalogUpdater.NeedsUpdate(dictionaryMirror, cache), "same-build dictionary download invalidates cache");
		var updated = new CardCatalogService(GameCardCatalogUpdater.UpdateIfNeeded(dictionaryMirror, cache));
		Check(updated.Find(60000)?.SimplifiedChineseName == "新卡自动更新测试" && updated.Search("自动更新", 20).Any(x => x.CardId == 60000), "new card names automatically merge and search");
		Check(!GameCardCatalogUpdater.NeedsUpdate(dictionaryMirror, cache), "refreshed catalog cached");
		Check(originals.All(x => Hash(x.Key) == x.Value), "real game / dictionaries unchanged");
		Console.WriteLine("PASS automatic card ID / names / incremental scan / missing dictionary / no duplication; gameWrites=False");

		void WriteDictionary(string name, byte[] data)
		{
			string path = locations[name];
			var text = engine.ReadTextAssets(path).First();
			engine.ReplaceTextAsset(text, data, Path.Combine(output, "dictionary-fixture-backups"));
		}
	}
	private static void Copy(string source, string dest) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(source, dest, true); }
	private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
	private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
}
