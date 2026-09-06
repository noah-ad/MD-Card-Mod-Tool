using System;
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
        string liveRoot = IndexService.FindLocalRoot(gameRoot)!;
        string original = IndexService.CardIllustrationBundleCandidates(liveRoot,"22524").First();
        string donor = IndexService.CardIllustrationBundleCandidates(liveRoot,"3801").First();
        string originalHash = Hash(original), donorHash = Hash(donor);
        string mirror = Path.Combine(output,"game");
        string local = Path.Combine(mirror,"LocalData","test-account","0000");
        string relative = Path.GetRelativePath(liveRoot,original);
        string target = Path.Combine(local,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(original,target,true);
        var engine = new ModEngine();
        var existing = engine.ScanBundle(target,local,"本地卡图",false).Textures.Single();
        string package = Path.Combine(output,"renamed-card.zip");
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
            zip.CreateEntryFromFile(donor,"LocalData/shared-account/0000/"+relative.Replace('\\','/'));
        var imported = new ModPackageService().Import(mirror,package,new[]{existing});
        var scanned = engine.ScanBundle(target,local,"本地卡图",false).Textures;
        CardBundleIdentity.Refresh(existing,scanned);
        if(imported.BundleCount!=1 || existing.CardKey!="22524" || scanned.Single().CardKey!="22524"
            || existing.PathId!=scanned.Single().PathId || existing.AssetFileName!=scanned.Single().AssetFileName)
            throw new Exception("Imported donor identity replaced target card or locator is stale");
        if(engine.DecodePng(existing).Length==0) throw new Exception("Imported card preview failed");
        if(CardCatalogService.LoadBestAvailable().Search("22524").All(c=>c.CardId!=22524))
            throw new Exception("Card catalog search lost target");
        string backup=Path.Combine(mirror,"_MD卡图备份","本地卡图",relative);
        if(Hash(backup)!=originalHash) throw new Exception("Original backup missing");
        File.Copy(backup,target,true);
        CardBundleIdentity.Refresh(existing,engine.ScanBundle(target,local,"本地卡图",false).Textures);
        if(Hash(target)!=originalHash || engine.DecodePng(existing).Length==0) throw new Exception("Restore or preview failed");
        if(Hash(original)!=originalHash || Hash(donor)!=donorHash) throw new Exception("Real game changed");
        Console.WriteLine("card=22524; foreignNameAndPathId=True; imported=True; rescanIdentity=True; preview=True; search=True; backupRestore=True; gameWrites=False; ready=True");
    }
    static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
}
