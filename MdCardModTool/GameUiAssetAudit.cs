using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MdCardModTool;

internal static class GameUiAssetAudit
{
	internal static void Run(string gameRoot, string output)
	{
		if (ModRecoveryStore.IsUnder(output, gameRoot)) throw new ArgumentException("Output must be outside the game.");
		Directory.CreateDirectory(output);
		var watch = Stopwatch.StartNew();
		var engine = new ModEngine();
		var all = new List<TexRef>();
		string streaming = IndexService.StreamingRoot(gameRoot);
		var paths = Directory.EnumerateFiles(streaming, "*", SearchOption.AllDirectories).Where(IndexService.IsUnityBundle)
			.Append(Path.Combine(gameRoot, "masterduel_Data", "data.unity3d")).Where(File.Exists).ToArray();
		foreach (string path in paths)
		{
			try { all.AddRange(engine.ListTextureHeaders(path, gameRoot, "游戏基础视觉资源")); }
			catch (Exception ex) { Console.WriteLine(Path.GetFileName(path) + ": " + ex); }
		}
		File.WriteAllText(Path.Combine(output, "texture-headers.json"), JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
		foreach (string name in new[] { "GUI_TitleBG", "Background", "ShopBGBase02", "BackGround0001", "GUI_Download_CertificationWriting_MDLogo", "GUI_ProfileCard_Back", "TitleLogo01", "sactx-0-1024x1024-BC7-CommonImagesAtlasAll-5f16ec27" })
		{
			TexRef? texture = all.FirstOrDefault(t => t.Name == name);
			if (texture == null) continue;
			try { File.WriteAllBytes(Path.Combine(output, name + ".png"), engine.DecodePng(texture, 2048)); Console.WriteLine($"decoded {name} {texture.Width}x{texture.Height}"); }
			catch (Exception ex) { Console.WriteLine("decode " + name + ": " + ex.Message); }
		}
		foreach (var item in all.Where(x => Regex.IsMatch(x.Name, "(?:bg|back|frame|button|btn|hex|home|icon|common|logo)", RegexOptions.IgnoreCase)).Take(160))
			Console.WriteLine($"{item.Name}\t{item.RelativeBundlePath}\t{item.PathId}");
		Console.WriteLine($"bundles={paths.Length}; textures={all.Count}; seconds={watch.Elapsed.TotalSeconds:0.00}; gameWrites=False");
	}
}
