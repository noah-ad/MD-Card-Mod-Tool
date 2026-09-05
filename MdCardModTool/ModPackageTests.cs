using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class ModPackageTests
{
    public static void Run(string gameRoot, string output)
    {
        Directory.CreateDirectory(output);
        var assets = MonsterAnimationIndexService.Find(gameRoot, "10001").Assets.DistinctBy(x => x.BundlePath).Take(2).ToArray();
        if (assets.Length != 2) throw new InvalidDataException("Need two local real bundles for read-only fixtures");
        byte[] original = File.ReadAllBytes(assets[0].BundlePath), modified = File.ReadAllBytes(assets[1].BundlePath);
        string[] gameHashes = assets.Select(a => Hash(a.BundlePath)).ToArray();
        string source = Path.Combine(output, "exporter"), target = Path.Combine(output, "importer");
        string sourceLocal = Path.Combine(source, "LocalData", "abc001", "0000");
        string targetLocal = Path.Combine(target, "LocalData", "def002", "0000");
        Directory.CreateDirectory(sourceLocal); Directory.CreateDirectory(targetLocal);
        Directory.CreateDirectory(Path.Combine(target, "LocalData", "other003", "0000"));
        IndexService.SetPreferredLocalRoot(source, sourceLocal); IndexService.SetPreferredLocalRoot(target, targetLocal);
        string[] kinds = ["本地卡图", "视觉资源", "游戏内图片", "基础视觉资源"];
        string[] relative = ["12/12345678", "34/34567890", "56/56789012", "masterduel_Data/SharedAssets/cardframe.bundle"];
        TexRef[] textures = Enumerable.Range(0,4).Select(i => new TexRef { SourceKind=kinds[i], Name="fixture"+i,
            RelativeBundlePath=relative[i], BundlePath=Path.Combine(i<2 ? sourceLocal : i==2 ? IndexService.StreamingRoot(source) : source, relative[i]) }).ToArray();
        string[] targets = Enumerable.Range(0,4).Select(i => Path.Combine(i<2 ? targetLocal : i==2 ? IndexService.StreamingRoot(target) : target,relative[i])).ToArray();
        TexRef[] targetTextures = textures.Select((x,i) => new TexRef { BundlePath=targets[i], SourceKind=x.SourceKind, RelativeBundlePath=x.RelativeBundlePath }).ToArray();
        for(int i=0;i<4;i++)
        {
            Write(textures[i].BundlePath, modified);
            Write(Path.Combine(source,"_MD卡图备份",kinds[i],relative[i]),original);
            Write(targets[i],original);
        }
        ModPackageService service = new();
        string direct=Path.Combine(output,"direct.zip"), legacy=Path.Combine(output,"legacy.mdmod.zip"), raw=Path.Combine(output,"without-manifest.zip");
        service.Export(source,textures,direct,true); service.Export(source,textures,legacy);
        using(var archive=ZipFile.OpenRead(direct))
        {
            Assert(archive.GetEntry("LocalData/abc001/0000/12/12345678") != null,"Direct path");
            Assert(archive.GetEntry("masterduel_Data/StreamingAssets/AssetBundle/56/56789012") != null,"Streaming path");
            Assert(!archive.Entries.Any(e=>e.FullName.StartsWith("files/")),"No numbered files in direct ZIP");
            using var copy=ZipFile.Open(raw,ZipArchiveMode.Create);
            foreach(var entry in archive.Entries.Where(e=>e.FullName!="manifest.json"))
            { using var input=entry.Open(); using var dest=copy.CreateEntry(entry.FullName).Open(); input.CopyTo(dest); }
        }
        foreach(string zip in new[]{legacy,direct,raw})
        {
            Assert(service.Inspect(zip).BundleCount==4,"Inspect count");
            Assert(service.Import(target,zip,targetTextures).BundleCount==4,"Import count");
            Assert(targets.All(t=>File.ReadAllBytes(t).SequenceEqual(modified)),"Installed bytes");
            Assert(!Directory.Exists(Path.Combine(target,"LocalData","abc001")),"Exporter's account must not be installed");
            for(int i=0;i<4;i++)
            {
                // Loose real animation fixtures are classified as animation by asset names.
                string kind = zip==raw && i<3 ? (i<2 ? "召唤动画" : "召唤动画-游戏内") : kinds[i];
                string backup=Path.Combine(target,"_MD卡图备份",kind,relative[i]);
                Assert(File.ReadAllBytes(backup).SequenceEqual(original),"First backup unchanged");
                Write(targets[i],original);
            }
        }
        foreach(string layout in new[]{"0000/12/12345678","12/12345678","Shared Mod/LocalData/a/0000/12/12345678"})
        {
            string zip=Path.Combine(output,"layout-"+Guid.NewGuid().ToString("N")+".zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)) {using var stream=archive.CreateEntry(layout).Open();stream.Write(modified);}
            Assert(service.Import(target,zip,targetTextures).BundleCount==1,"Alternative directory layout");
            Assert(File.ReadAllBytes(targets[0]).SequenceEqual(modified),"Alternative account mapping");
            Write(targets[0],original);
        }
        try { service.Import(target,direct,targetTextures, count=>{if(count==2) throw new IOException("test commit failure");}); throw new Exception("Missing injected failure"); }
        catch(IOException ex) when(ex.Message=="test commit failure") { }
        Assert(targets.All(t=>File.ReadAllBytes(t).SequenceEqual(original)),"Rollback exact");

        int rejected=0;
        void Reject(string name, Action<ZipArchive> make, bool inspectOnly=false)
        {
            string file=Path.Combine(output,name+".zip"); using(var zip=ZipFile.Open(file,ZipArchiveMode.Create)) make(zip);
            try { if(inspectOnly) service.Inspect(file); else service.Import(target,file,targetTextures); }
            catch(Exception ex) when(ex is InvalidDataException or FileNotFoundException or ArgumentException) { rejected++; return; }
            throw new Exception("Unsafe archive was accepted: "+name);
        }
        void Add(ZipArchive zip,string path,byte[] data) { using var s=zip.CreateEntry(path).Open();s.Write(data); }
        Reject("traversal",z=>Add(z,"../outside",modified),true);
        Reject("absolute",z=>Add(z,"C:/outside",modified),true);
        Reject("ads",z=>Add(z,"12/12345678:evil",modified),true);
        Reject("duplicate",z=>{Add(z,"12/12345678",modified);Add(z,"12/12345678",modified);},true);
        Reject("symlink",z=>{var e=z.CreateEntry("12/12345678");e.ExternalAttributes=unchecked((int)0xa1ff0000);using var s=e.Open();s.Write(modified);},true);
        Reject("two-accounts",z=>{Add(z,"LocalData/a/0000/12/12345678",modified);Add(z,"LocalData/b/0000/12/12345678",modified);},true);
        Reject("executable",z=>Add(z,"masterduel_Data/evil.dll",Encoding.UTF8.GetBytes("MZ-not-a-bundle")),true);
        Reject("missing-target",z=>Add(z,"12/12345679",modified));
        Reject("corrupt",z=>Add(z,"12/12345678",Encoding.ASCII.GetBytes("UnityFS\0malformed")));
        string tampered=Path.Combine(output,"tampered.zip"); File.Copy(direct,tampered);
        using(var zip=ZipFile.Open(tampered,ZipArchiveMode.Update))
        { var entry=zip.GetEntry("LocalData/abc001/0000/12/12345678")!;entry.Delete();Add(zip,"LocalData/abc001/0000/12/12345678",original); }
        try {service.Import(target,tampered,targetTextures);throw new Exception("Tampered hash accepted");}
        catch(InvalidDataException){rejected++;}
        Assert(targets.All(t=>File.ReadAllBytes(t).SequenceEqual(original)),"Rejected import wrote files");
        Assert(assets.Select(a=>Hash(a.BundlePath)).SequenceEqual(gameHashes),"Real game modified");
        foreach(AppLanguage language in Enum.GetValues<AppLanguage>())
        { Localizer.SetLanguage(language); Assert(Localizer.T("mods.export.filter").Split('|').Length==4,"Format choices"); }
        Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
        Console.WriteLine($"legacy=True; directLayout=True; rawImport=True; selectedAccount=True; kinds=4; backup=True; rollback=True; rejected={rejected}; gameWrites=False; ready=True");
    }
    private static void Write(string path, byte[] data) { Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,data); }
    private static string Hash(string path) {using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s));}
    private static void Assert(bool ok,string message) {if(!ok) throw new InvalidDataException(message);}
}
