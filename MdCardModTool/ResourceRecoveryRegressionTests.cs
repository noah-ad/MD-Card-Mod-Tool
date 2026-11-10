using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace MdCardModTool;

internal static class ResourceRecoveryRegressionTests
{
	internal static void Run(string gameRoot, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Fixtures must be outside the game.");
		Directory.CreateDirectory(output);
		var engine = new ModEngine();
		string stream = IndexService.StreamingRoot(gameRoot);
		var source = IndexService.CardIllustrationBundleCandidates(stream, "4007").SelectMany(path => engine.ScanBundle(path, stream, "游戏内图片", false).Textures).First(CardTextureRules.IsCardArt);
		string original = Hash(source.BundlePath);
		var (mirror, local) = Mirror("stale-reference");
		string installed = Path.Combine(IndexService.StreamingRoot(mirror), source.RelativeBundlePath);
		Copy(source.BundlePath, installed);
		var stale = Reference(local, source.CardKey);
		byte[] decoded = engine.DecodePng(stale);
		Check(decoded.Length > 100 && stale.PathId == source.PathId && stale.AssetFileName == source.AssetFileName, "missing Bundle + stale PathID/asset filename repairs through canonical illustration");
		Check(stale.SourceKind == "游戏内图片" && IndexWorkspaceGuard.Belongs(mirror, stale), "cross-storage repair updates storage kind and remains saveable");
		var index = new GameIndex(); index.Textures.Add(stale); index.Textures.Add(Reference(local, "30006"));
		Check(IndexService.RemoveUnavailableCardEntries(mirror, index) == 1 && index.Textures.Single() == stale, "uninstalled portable-index card is removed, installed card remains");

		var (movedMirror, movedLocal) = Mirror("moved-bundle");
		string moved = Path.Combine(movedLocal, "ab", "abcdef12");
		Copy(source.BundlePath, moved);
		var movedRef = Reference(movedLocal, source.CardKey);
		var repaired = engine.ResolveTextureReference(movedRef, searchAllBundles: true);
		Check(repaired?.BundlePath == moved && repaired.CardKey == source.CardKey, "explicit deep lookup finds moved Bundle by parsed card identity");
		var auto = IndexService.ScanMissingLocalCard(movedMirror, new GameIndex(), "0", includeNonCanonical: true);
		Check(auto.Textures.Any(t => t.CardKey == source.CardKey), "opt-in full inventory scan includes noncanonical illustration paths");
		var known = new GameIndex { Textures = auto.Textures };
		NewCardDiscovery.Merge(known, known.Textures, auto);
		Check(IndexService.ScanMissingLocalCard(movedMirror, known, "0").ScannedBundles == 0, "unchanged account is incremental after repair");
		Check(IndexService.Build(movedMirror).Textures.Any(t => t.CardKey == source.CardKey), "full rebuild includes noncanonical illustrations without a size heuristic");

		var (_, wrongLocal) = Mirror("wrong-card");
		string wrong = Path.Combine(wrongLocal, IndexService.CardIllustrationRelativePath("7687"));
		Copy(source.BundlePath, wrong);
		var expected = Reference(wrongLocal, source.CardKey); expected.BundlePath = wrong; expected.RelativeBundlePath = Path.GetRelativePath(wrongLocal, wrong);
		Check(engine.ResolveTextureReference(expected) == null, "same-size wrong-card Bundle can never become a writable repair");
		using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
		try { engine.ResolveTextureReference(movedRef, true, cancellation.Token); throw new InvalidDataException("Ignored cancellation"); }
		catch (OperationCanceledException) { Console.WriteLine("PASS cancelled lookup stops before scanning"); }

		string stamp = IndexService.BundleContentStamp(moved);
		DateTime modified = File.GetLastWriteTimeUtc(moved);
		byte[] bytes = File.ReadAllBytes(moved); bytes[^1] ^= 1; File.WriteAllBytes(moved, bytes); File.SetLastWriteTimeUtc(moved, modified);
		Check(IndexService.BundleContentStamp(moved) != stamp, "known-dictionary content stamps detect same-size/same-time changes");
		Check(Hash(source.BundlePath) == original, "real game fixture unchanged");
		Console.WriteLine("PASS stale references / storage / missing installed cards / moved bundles / incremental scan / wrong-card rejection / cancellation; gameWrites=False");

		(string Root, string Local) Mirror(string name)
		{
			string root = Path.Combine(output, name + "-" + Guid.NewGuid().ToString("N"));
			string local = Path.Combine(root, "LocalData", "test", "0000");
			Directory.CreateDirectory(local); IndexService.SetPreferredLocalRoot(root, local); return (root, local);
		}
	}
	private static TexRef Reference(string local, string id) => new()
	{
		BundlePath = Path.Combine(local, "ff", "ffffffff"), RelativeBundlePath = Path.Combine("ff", "ffffffff"),
		Name = id, CardKey = id, SourceKind = "本地卡图", AssetFileName = "stale.assets", PathId = long.MinValue, Width = 512, Height = 512
	};
	private static void Copy(string from, string to) { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to, true); }
	private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
	private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
}
