using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

// Exercise the production startup/timer worker and actual search results. The
// real installation supplies read-only fixtures; every write targets a mirror.
internal static class NewCardRefreshTests
{
	internal static void Run(string gameRoot, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Test output must be outside game.");
		Directory.CreateDirectory(output);
		string mirror = Path.Combine(output, "game_" + Guid.NewGuid().ToString("N"));
		string local = Path.Combine(mirror, "LocalData", "test001", "0000");
		string cache = Path.Combine(output, "cache_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(local);
		IndexService.SetPreferredLocalRoot(mirror, local);
		string realLocal = IndexService.FindLocalRoot(gameRoot)!;
		var engine = new ModEngine();
		var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var dictionaries = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string hash in new[] { "fa2ed7f8", "2f2cc400", "0b636c14" })
		{
			string source = Path.Combine(realLocal, hash[..2], hash);
			originals[source] = Hash(source);
			string copied = Path.Combine(local, hash[..2], hash);
			Copy(source, copied);
			dictionaries[Path.GetFileName(engine.ReadAssetBundleContainerPaths(copied).Single())] = copied;
		}
		string realCard = IndexService.CardIllustrationBundleCandidates(realLocal, "22730").First();
		originals[realCard] = Hash(realCard);
		var card = engine.ScanBundle(realCard, realLocal, "本地卡图", false).Textures.Single(CardTextureRules.IsCardArt);
		using (var image = SixLabors.ImageSharp.Image.Load(engine.DecodePng(card)))
			Check(image.Width > 0 && image.Height > 0, "installed 22730 illustration decodes");
		string downloaded = AddCard("22730");
		var names = new CardCatalogService(GameCardCatalogUpdater.UpdateIfNeeded(mirror, cache));
		Check(names.Find(22730)?.SimplifiedChineseName == "四天之龙 暗反叛超量龙", "22730 name comes from coherent game dictionary, not a hard-coded card entry");
		Check(!GameCardCatalogUpdater.NeedsUpdate(mirror, cache), "catalog is already current before repairing a missing illustration index");
		string checkpointCache = Path.Combine(output, "cancelled-discovery-" + Guid.NewGuid().ToString("N"));
		using (var cancellation = new CancellationTokenSource())
		{
			try
			{
				GameCardCatalogUpdater.UpdateIfNeeded(mirror, checkpointCache, (done, total, found) => cancellation.Cancel(), cancellation.Token);
				throw new InvalidDataException("Discovery fixture did not cancel");
			}
			catch (OperationCanceledException) { }
			Check(Directory.EnumerateFiles(checkpointCache, "dictionary-discovery-*.json").Any()
				&& !File.Exists(Path.Combine(checkpointCache, "card-catalog-game-state-v2.json")), "cancelled dictionary discovery retains completed stamps without acknowledging catalog completion");
			Check(GameCardCatalogUpdater.UpdateIfNeeded(mirror, checkpointCache).Any(x => x.CardId == 22730), "dictionary discovery resumes after cancellation");
		}
		var index = new GameIndex { BuiltInCardIndexVersion = 2 };
		// Reproduce the legacy state: a matching stamp exists but no card row.
		index.CardBundleScanStamps[Path.GetFullPath(downloaded)] = IndexService.BundleScanStamp(downloaded);
		using MainForm form = new(null, backgroundDataDirectory: cache)
		{
			ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000)
		};
		Set(form, "_assetRoot", null); // Suppress Shown's automatic real-installation scan.
		form.Show();
		Application.DoEvents();
		Get<System.Windows.Forms.Timer>(form, "_gameResourcePoll").Stop();
		Set(form, "_gameRoot", mirror);
		Set(form, "_assetRoot", local);
		Set(form, "_streamingRoot", IndexService.StreamingRoot(mirror));
		Set(form, "_index", index);
		Get<List<TexRef>>(form, "_textures").Clear();
		TextBox search = Get<TextBox>(form, "_search");
		Search("22730");
		Poll();
		Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "22730"), "production worker repairs stamp-only cached 22730 and updates numeric search");
		Search("四天");
		Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "22730"), "actual card resource list searches 四天 after refresh");
		Search("暗反叛");
		Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "22730"), "actual card resource list searches the game-localized dragon name");
		Task completed = Get<Task>(form, "_backgroundRefreshTask");
		Poll();
		Check(ReferenceEquals(completed, Get<Task>(form, "_backgroundRefreshTask")), "successful unchanged resource fingerprint does not restart scans");

		AddCard("60001");
		Search("60001");
		Task check = (Task)Invoke(form, "CheckGameResourceChangesAsync")!;
		Pump(() => check.IsCompleted, "start cancelled timer refresh");
		check.GetAwaiter().GetResult();
		Get<CancellationTokenSource>(form, "_backgroundRefreshCancellation").Cancel();
		Task cancelled = Get<Task>(form, "_backgroundRefreshTask");
		Pump(() => cancelled.IsCompleted, "cancelled worker completes");
		cancelled.GetAwaiter().GetResult();
		Check(Get<string>(form, "_lastCheckedResourceSignature") == "", "cancellation cannot acknowledge a resource fingerprint");
		Poll();
		Check(!ReferenceEquals(cancelled, Get<Task>(form, "_backgroundRefreshTask"))
			&& Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "60001"), "same unchanged fingerprint retries after cancellation and discovers downloaded card");

		string bad = AddCard("60002");
		byte[] good = File.ReadAllBytes(bad);
		DateTime stamp = File.GetLastWriteTimeUtc(bad);
		byte[] broken = (byte[])good.Clone();
		Encoding.ASCII.GetBytes("Broken!").CopyTo(broken, 0);
		File.WriteAllBytes(bad, broken);
		File.SetLastWriteTimeUtc(bad, stamp);
		GameCardCatalogUpdater.UpdateIfNeeded(mirror, cache);
		Check(!GameCardCatalogUpdater.NeedsUpdate(mirror, cache), "failed-illustration fixture has a current name cache");
		string failedFingerprint = GameCardCatalogUpdater.CaptureResourceFingerprint(local);
		Search("60002");
		Poll();
		Check(Get<string>(form, "_lastCheckedResourceSignature") == "" && !index.CardBundleScanStamps.ContainsKey(Path.GetFullPath(bad)), "unreadable illustration is not acknowledged or negatively cached");
		File.WriteAllBytes(bad, good);
		File.SetLastWriteTimeUtc(bad, stamp);
		Check(GameCardCatalogUpdater.CaptureResourceFingerprint(local) == failedFingerprint, "repair fixture deliberately keeps length and timestamp unchanged");
		Poll();
		Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "60002") && Get<string>(form, "_lastCheckedResourceSignature").Length > 0, "same-metadata repair retries through production timer and becomes searchable");

		AddCard("60000");
		byte[] props = GameCardCatalogUpdater.ReadDictionary(dictionaries["card_prop.bytes"], "card_prop.bytes");
		byte[] offsets = GameCardCatalogUpdater.ReadDictionary(dictionaries["card_indx.bytes"], "card_indx.bytes");
		byte[] text = GameCardCatalogUpdater.ReadDictionary(dictionaries["card_name.bytes"], "card_name.bytes");
		int rows = Math.Min(props.Length, offsets.Length) / 8;
		byte[] newProps = new byte[(rows + 1) * 8], newOffsets = new byte[(rows + 1) * 8];
		Array.Copy(props, newProps, rows * 8); Array.Copy(offsets, newOffsets, rows * 8);
		BitConverter.TryWriteBytes(newProps.AsSpan(rows * 8, 2), (ushort)60000);
		newProps[rows * 8 + 2] = 1;
		BitConverter.TryWriteBytes(newOffsets.AsSpan(rows * 8, 4), text.Length);
		WriteDictionary("card_prop.bytes", newProps);
		WriteDictionary("card_indx.bytes", newOffsets);
		WriteDictionary("card_name.bytes", text.Concat(Encoding.UTF8.GetBytes("计时器新卡检索测试\0")).ToArray());
		Search("计时器新卡");
		Poll();
		Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "60000")
			&& Get<CardCatalogService>(form, "_cardCatalog").Find(60000)?.SimplifiedChineseName == "计时器新卡检索测试", "same-build dictionary download automatically updates names and active text search");
		Check(index.Textures.Count(CardTextureRules.IsCardArt) == 4, "retries do not duplicate cards");
		AddCard("60003");
		string dictionaryPath = dictionaries["card_prop.bytes"];
		string heldDictionary = Path.Combine(output, "dictionary-held-" + Guid.NewGuid().ToString("N"));
		File.Move(dictionaryPath, heldDictionary);
		try
		{
			Search("60003");
			Poll();
			Check(Get<List<TexRef>>(form, "_visibleTextures").Any(x => x.CardKey == "60003") && Get<string>(form, "_lastCheckedResourceSignature") == "",
				"production dictionary failure still publishes downloaded card with numeric fallback and remains retryable");
		}
		finally { File.Move(heldDictionary, dictionaryPath); }
		Poll();
		Check(Get<string>(form, "_lastCheckedResourceSignature").Length > 0 && index.Textures.Count(CardTextureRules.IsCardArt) == 5,
			"restored dictionary completes refresh without duplicated cards");
		Check(originals.All(x => Hash(x.Key) == x.Value), "all real game fixture hashes unchanged");
		form.Close();
		Console.WriteLine("PASS production new-card refresh / 22730 / current catalog / cancelled retry / failed retry / same-build names / actual search; gameWrites=False");

		string AddCard(string id)
		{
			string path = Path.Combine(local, IndexService.CardIllustrationRelativePath(id));
			Copy(realCard, path);
			return path;
		}
		void WriteDictionary(string name, byte[] data) => engine.ReplaceTextAsset(engine.ReadTextAssets(dictionaries[name]).First(), data, Path.Combine(output, "fixture-backups"));
		void Search(string query)
		{
			search.Text = query;
			Get<System.Windows.Forms.Timer>(form, "_searchDebounce").Stop();
			Invoke(form, "RefreshSearchResults");
		}
		void Poll()
		{
			Task task = (Task)Invoke(form, "CheckGameResourceChangesAsync")!;
			Pump(() => task.IsCompleted, "resource metadata poll");
			task.GetAwaiter().GetResult();
			Task? refresh = Get<Task?>(form, "_backgroundRefreshTask");
			if (refresh != null) { Pump(() => refresh.IsCompleted, "production background refresh"); refresh.GetAwaiter().GetResult(); }
		}
	}

	internal static void Audit(string gameRoot, string cardId, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Audit output must be outside game.");
		Directory.CreateDirectory(output);
		var engine = new ModEngine();
		var watch = Stopwatch.StartNew();
		string local = IndexService.FindLocalRoot(gameRoot)!;
		var paths = IndexService.CardIllustrationBundleCandidates(local, cardId).ToArray();
		var hashes = paths.ToDictionary(x => x, Hash);
		var names = GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, Path.Combine(output, "cache"));
		var catalog = new CardCatalogService(CardCatalogService.MergeGameCatalogs(CardCatalogService.Read(CardCatalogService.BundledPath), names));
		CardCatalogService.Write(Path.Combine(output, CardCatalogService.BundledFileName), catalog.Entries);
		var scan = IndexService.ScanMissingLocalCard(gameRoot, new GameIndex(), cardId);
		foreach (var texture in scan.Textures)
		{
			using var image = SixLabors.ImageSharp.Image.Load(engine.DecodePng(texture));
			Console.WriteLine($"card={texture.CardKey}; name={catalog.Find(cardId)?.Name(AppLanguage.SimplifiedChinese)}; texture={texture.Width}x{texture.Height}; decoded={image.Width}x{image.Height}; bundle={texture.BundlePath}");
		}
		Check(catalog.Find(cardId) != null && scan.Textures.Any(x => x.CardKey == cardId), "requested installed new card has both name and decoded illustration");
		Check(hashes.All(x => Hash(x.Key) == x.Value), "audited game bundles unchanged");
		Console.WriteLine($"catalog={catalog.Count}; pending={scan.PendingBundles.Count}; seconds={watch.Elapsed.TotalSeconds:0.00}; gameWrites=False");
	}

	private static T Get<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
	private static void Set(MainForm form, string name, object? value) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);
	private static object? Invoke(MainForm form, string name) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
	private static void Pump(Func<bool> done, string name)
	{
		var watch = Stopwatch.StartNew();
		while (!done() && watch.ElapsedMilliseconds < 60000) { Application.DoEvents(); Thread.Sleep(5); }
		Check(done(), name);
	}
	private static void Copy(string source, string target) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true); }
	private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
	private static void Check(bool success, string name) { if (!success) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
}
