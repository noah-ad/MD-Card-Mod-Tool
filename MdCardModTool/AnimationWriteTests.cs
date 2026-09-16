using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class AnimationWriteTests
{
	public static void Run(string gameRoot, string output, bool largeAtlas = false, string cardId = "22524")
	{
		Directory.CreateDirectory(output);
		MonsterAnimationSet monsterAnimationSet = MonsterAnimationIndexService.Find(gameRoot, cardId);
		Dictionary<string, string> source = monsterAnimationSet.Assets.ToDictionary((MonsterAnimationAssetRef a) => a.BundlePath, (MonsterAnimationAssetRef a) => Hash(a.BundlePath));
		MonsterAnimationSet monsterAnimationSet2 = new MonsterAnimationSet
		{
			CardId = monsterAnimationSet.CardId,
			Assets = monsterAnimationSet.Assets.Select(delegate(MonsterAnimationAssetRef a)
			{
				string text2 = Path.Combine(output, "mirror", a.RelativeBundlePath);
				Directory.CreateDirectory(Path.GetDirectoryName(text2));
				File.Copy(a.BundlePath, text2, overwrite: true);
				return MonsterAnimationTransferService.AtPath(a, text2);
			}).ToList()
		};
		string bundlePath = monsterAnimationSet2.Textures.First().BundlePath;
		MonsterAnimationService monsterAnimationService = new MonsterAnimationService();
		Stopwatch stopwatch = Stopwatch.StartNew();
		using (File.OpenRead(bundlePath))
		{
			try
			{
				monsterAnimationService.Apply(output, monsterAnimationSet2, null);
				throw new Exception("Lock not detected before compression");
			}
			catch (IOException ex) when (ex.Message.Contains("尚未开始"))
			{
			}
		}
		if (stopwatch.Elapsed.TotalSeconds > 4.0)
		{
			throw new Exception("Locked target did not fail promptly");
		}
		long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		string[] array = new string[2]
		{
			Path.Combine(output, "a.png"),
			Path.Combine(output, "b.png")
		};
		int width = (largeAtlas ? 4096 : 160);
		int height = (largeAtlas ? 2304 : 90);
		using (Image<Rgba32> source2 = new Image<Rgba32>(width, height, new Rgba32(200, 30, 40, byte.MaxValue)))
		{
			source2.SaveAsPng(array[0]);
		}
		using (Image<Rgba32> source3 = new Image<Rgba32>(width, height, new Rgba32(20, 130, 220, byte.MaxValue)))
		{
			source3.SaveAsPng(array[1]);
		}
		if (cardId == "6969")
		{
			IReadOnlyList<MonsterAnimationAssetTriplet> readOnlyList = MonsterAnimationAssetPairing.FindComplete(monsterAnimationSet2);
			if (readOnlyList.Count != 2 || readOnlyList.Any((MonsterAnimationAssetTriplet p) => p.Texture.Name != "P9696" || p.Textures.Count != 2))
			{
				throw new Exception("Official alias or second atlas page was not resolved");
			}
			Spine42CompatibilityResult spine42CompatibilityResult = Spine42PreviewRenderer.Probe(monsterAnimationSet2, 160);
			if (!spine42CompatibilityResult.Success || spine42CompatibilityResult.OpaquePixels <= 0)
			{
				throw new Exception("Original multi-page preview failed: " + spine42CompatibilityResult.Message);
			}
			Console.WriteLine("officialAlias=P9696; pagesPerTier=2; originalPreview=True");
			using CurrentMonsterAnimationPreview currentMonsterAnimationPreview = Spine42PreviewRenderer.TryLoad(monsterAnimationSet2, null, 6, 20, 512) ?? throw new Exception("Original preview did not load");
			currentMonsterAnimationPreview.Frames[currentMonsterAnimationPreview.Frames.Count / 2].Save(Path.Combine(output, "original-multipage.png"));
		}
		using MonsterAnimationBuildResult monsterAnimationBuildResult = MonsterAnimationBuilder.Build(array, cardId, 12, 100, monsterAnimationService.ReadTemplate(monsterAnimationSet2), largeAtlas ? 8192 : 4096);
		if (largeAtlas && (monsterAnimationBuildResult.Hd.AtlasImage.Width != 8192 || monsterAnimationBuildResult.Hd.AtlasImage.Height != 8192))
		{
			throw new Exception("Large atlas test must exercise a real 8192 x 8192 texture");
		}
		Console.WriteLine($"atlas={monsterAnimationBuildResult.Hd.AtlasImage.Width}x{monsterAnimationBuildResult.Hd.AtlasImage.Height}; generated=True");
		if (Math.Abs(monsterAnimationBuildResult.DisplayWidth - 6720.0) > 0.001 || Math.Abs(monsterAnimationBuildResult.DisplayHeight - 3780.0) > 0.001)
		{
			throw new Exception("New 100% must equal old 140% geometry");
		}
		Dictionary<string, string> dictionary = monsterAnimationSet2.Assets.ToDictionary((MonsterAnimationAssetRef a) => a.BundlePath, (MonsterAnimationAssetRef a) => Hash(a.BundlePath));
		try
		{
			monsterAnimationService.Apply(output, monsterAnimationSet2, monsterAnimationBuildResult, delegate(int count)
			{
				if (count == 2)
				{
					throw new IOException("injected");
				}
			});
			throw new Exception("Missing injected failure");
		}
		catch (IOException ex2) when (ex2.Message == "injected")
		{
		}
		if (dictionary.Any((KeyValuePair<string, string> x) => Hash(x.Key) != x.Value))
		{
			throw new Exception("Rollback mismatch");
		}
		stopwatch.Restart();
		monsterAnimationService.Apply(output, monsterAnimationSet2, monsterAnimationBuildResult);
		long elapsedMilliseconds2 = stopwatch.ElapsedMilliseconds;
		MonsterAnimationCompatibilityValidator.Validate(monsterAnimationSet2, requireExactlySixBundles: false);
		MonsterAnimationAssetTriplet monsterAnimationAssetTriplet = MonsterAnimationAssetPairing.FindComplete(monsterAnimationSet2).First((MonsterAnimationAssetTriplet p) => p.Tier == "HighEnd_HD");
		AnimationTextureMetadata animationTextureMetadata = new ModEngine().ReadAnimationTextureMetadata(monsterAnimationAssetTriplet.Texture);
		if (animationTextureMetadata.Width != monsterAnimationBuildResult.Hd.AtlasImage.Width || animationTextureMetadata.Height != monsterAnimationBuildResult.Hd.AtlasImage.Height)
		{
			throw new Exception("Written atlas dimensions changed");
		}
		using (CurrentMonsterAnimationPreview currentMonsterAnimationPreview2 = MonsterAnimationCurrentPreview.TryLoad(monsterAnimationSet2) ?? throw new Exception("Written calibrated animation cannot be read"))
		{
			if (currentMonsterAnimationPreview2.ScalePercent != 100)
			{
				throw new Exception("Calibrated scale does not round-trip");
			}
		}
		string[] array2 = new string[2] { "HighEnd_HD", "SD" };
		foreach (string tier in array2)
		{
			MonsterAnimationAssetTriplet monsterAnimationAssetTriplet2 = MonsterAnimationAssetPairing.FindComplete(monsterAnimationSet2).First((MonsterAnimationAssetTriplet p) => p.Tier == tier);
			using JsonDocument jsonDocument = JsonDocument.Parse(new ModEngine().ReadTextAsset(monsterAnimationAssetTriplet2.Skeleton).Data);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.GetProperty("bones").EnumerateArray().First((JsonElement b) => b.GetProperty("name").GetString() == "Body")
				.GetProperty("y")
				.GetDouble() != -120.0)
			{
				throw new Exception("Viewport center mismatch");
			}
			foreach (JsonProperty item in rootElement.GetProperty("animations").EnumerateObject())
			{
				JsonElement value = item.Value;
				JsonElement property = value.GetProperty("bones");
				if (property.TryGetProperty("Body", out value))
				{
					throw new Exception("Unexpected video drift");
				}
				foreach (JsonElement item2 in property.GetProperty("root").GetProperty("scale").EnumerateArray())
				{
					if (item2.GetProperty("x").GetDouble() != 1.0 || item2.GetProperty("y").GetDouble() != 1.0)
					{
						throw new Exception("Hidden popup scale still shrinks video");
					}
				}
			}
		}
		using (CurrentMonsterAnimationPreview currentMonsterAnimationPreview3 = Spine42PreviewRenderer.TryLoad(monsterAnimationSet2, null, 12, 24, 160) ?? throw new Exception("Written video could not be rendered"))
		{
			foreach (Bitmap frame in currentMonsterAnimationPreview3.Frames)
			{
				System.Drawing.Point[] array3 = new System.Drawing.Point[4]
				{
					new System.Drawing.Point(2, 2),
					new System.Drawing.Point(frame.Width - 3, 2),
					new System.Drawing.Point(2, frame.Height - 3),
					new System.Drawing.Point(frame.Width - 3, frame.Height - 3)
				};
				for (int num = 0; num < array3.Length; num++)
				{
					System.Drawing.Point point = array3[num];
					if (frame.GetPixel(point.X, point.Y).A < 240)
					{
						throw new Exception("Video does not cover the viewport corners");
					}
				}
			}
			currentMonsterAnimationPreview3.Frames[0].Save(Path.Combine(output, "written-first-frame.png"));
			List<Bitmap> frames = currentMonsterAnimationPreview3.Frames;
			frames[frames.Count - 1].Save(Path.Combine(output, "written-last-frame.png"));
		}
		foreach (MonsterAnimationAssetRef asset in monsterAnimationSet2.Assets)
		{
			string text = Path.Combine(output, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath);
			if (Hash(text) != dictionary[asset.BundlePath])
			{
				throw new Exception("Backup overwritten");
			}
			File.Copy(text, asset.BundlePath, overwrite: true);
		}
		if (source.Any((KeyValuePair<string, string> x) => Hash(x.Key) != x.Value))
		{
			throw new Exception("Real game changed");
		}
		Console.WriteLine($"card={cardId}; lockFailMs={elapsedMilliseconds}; applyMs={elapsedMilliseconds2}; sixBundleApply=True; flatVideoGeometryHD_SD=True; renderedCorners=True; rollback=True; restore=True; gameWrites=False; ready=True");
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}
}
