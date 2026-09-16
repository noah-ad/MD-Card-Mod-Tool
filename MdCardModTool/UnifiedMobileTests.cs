using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class UnifiedMobileTests
{
	public static void Run(string source, string output)
	{
		source = StandaloneResourceService.ResolveRoot(source) ?? throw new Exception("Invalid mobile root");
		string root = Path.Combine(output, Guid.NewGuid().ToString("N"), "0000");
		Directory.CreateDirectory(root);
		Dictionary<string, string> originals = new Dictionary<string, string>();
		foreach (string item in StandaloneResourceService.EnumerateBundles(source).Take(60))
		{
			Copy(item);
		}
		GameInstallation gameInstallation = SteamGameDiscovery.FromPath(root);
		IndexService.SetPreferredLocalRoot(root, gameInstallation.Profiles.Single().RootPath);
		if (IndexService.FindLocalRoot(root) != root || PortableIndexService.TryLoadBundled(root, out GameIndex _, out string _))
		{
			throw new Exception("Source isolation failed");
		}
		GameIndex gameIndex = IndexService.Build(root);
		Console.WriteLine($"cards={gameIndex.Textures.Count((TexRef t) => t.SourceKind == "本地卡图")}; visual={gameIndex.Textures.Count((TexRef t) => t.SourceKind == "视觉资源")}");
		TexRef card = gameIndex.Textures.First((TexRef t) => t.SourceKind == "本地卡图");
		TexRef texRef = gameIndex.Textures.First((TexRef t) => t.SourceKind == "视觉资源");
		ModEngine engine = new ModEngine();
		ModPackageService modPackageService = new ModPackageService();
		TexRef[] array = new TexRef[2] { card, texRef };
		foreach (TexRef texRef2 in array)
		{
			using Image<Rgba32> source2 = new Image<Rgba32>(texRef2.Width, texRef2.Height, new Rgba32(80, 180, 20, 128));
			using MemoryStream memoryStream = new MemoryStream();
			source2.SaveAsPng(memoryStream);
			engine.Replace(texRef2, memoryStream.ToArray(), Path.Combine(root, "_MD卡图备份", texRef2.SourceKind));
			if (engine.DecodePng(texRef2).Length == 0)
			{
				throw new Exception("Shared preview failed");
			}
		}
		if (modPackageService.RefreshFlags(root, gameIndex.Textures) < 2)
		{
			throw new Exception("Shared Mod flags failed");
		}
		string text = Path.Combine(output, "mobile-direct.zip");
		modPackageService.Export(root, gameIndex.Textures, text, directReplacement: true);
		using (ZipArchive zipArchive = ZipFile.OpenRead(text))
		{
			if (zipArchive.Entries.Count((ZipArchiveEntry e) => e.FullName.StartsWith("0000/")) < 2)
			{
				throw new Exception("Mobile ZIP layout wrong");
			}
		}
		array = new TexRef[2] { card, texRef };
		foreach (TexRef texRef3 in array)
		{
			File.Copy(Path.Combine(root, "_MD卡图备份", texRef3.SourceKind, texRef3.RelativeBundlePath), texRef3.BundlePath, overwrite: true);
		}
		modPackageService.Import(root, text, gameIndex.Textures);
		if (modPackageService.RefreshFlags(root, gameIndex.Textures) < 2)
		{
			throw new Exception("Shared Mod import failed");
		}
		Console.WriteLine("sharedReplace=True; sharedModZip=True; sourceKinds=PC; root=" + root);
		TextAssetRef textAssetRef = new OverFrameService().FindGate(source);
		Copy(textAssetRef.BundlePath);
		new OverFrameService().EnableOrUpdate(root, ushort.Parse(card.CardKey), ushort.Parse(card.CardKey));
		if (!new OverFrameService().Read(root).Any((OverFrameMapping m) => m.CardId == ushort.Parse(card.CardKey)))
		{
			throw new Exception("Mobile overframe gate failed");
		}
		new OverFrameService().RestoreBackup(root);
		Console.WriteLine("sharedOverframeGate=True");
		MonsterAnimationSet monsterAnimationSet = MonsterAnimationIndexService.Find(source, "22524");
		Console.WriteLine($"mobileAnimation22524={monsterAnimationSet.IsComplete}; assets={monsterAnimationSet.Assets.Count}");
		if (!monsterAnimationSet.IsComplete)
		{
			throw new Exception("Mobile animation not paired");
		}
		foreach (MonsterAnimationAssetRef asset in monsterAnimationSet.Assets)
		{
			Copy(asset.BundlePath);
		}
		MonsterAnimationSet monsterAnimationSet2 = new MonsterAnimationSet
		{
			CardId = monsterAnimationSet.CardId,
			Assets = monsterAnimationSet.Assets.Select((MonsterAnimationAssetRef a) => MonsterAnimationTransferService.AtPath(a, Path.Combine(root, a.RelativeBundlePath))).ToList()
		};
		Console.WriteLine($"mirrorMobile={monsterAnimationSet2.IsMobile}; complete={monsterAnimationSet2.IsComplete}; pairs={MonsterAnimationAssetPairing.FindComplete(monsterAnimationSet2).Count}; assets={monsterAnimationSet2.Assets.Count}");
		foreach (MonsterAnimationAssetRef asset2 in monsterAnimationSet2.Assets)
		{
			Console.WriteLine($"{asset2.Kind} {asset2.Name} {asset2.StorageKind} {asset2.RelativeBundlePath}");
		}
		MonsterAnimationService monsterAnimationService = new MonsterAnimationService();
		string text2 = Path.Combine(output, "frame.png");
		using (Image<Rgba32> source3 = new Image<Rgba32>(160, 90, new Rgba32(80, 180, 20)))
		{
			source3.SaveAsPng(text2);
		}
		using (MonsterAnimationBuildResult animation = MonsterAnimationBuilder.Build(new _003C_003Ez__ReadOnlySingleElementList<string>(text2), "22524", 12, 100, monsterAnimationService.ReadTemplate(monsterAnimationSet2)))
		{
			Dictionary<string, string> source4 = monsterAnimationSet2.Assets.DistinctBy((MonsterAnimationAssetRef a) => a.BundlePath).ToDictionary((MonsterAnimationAssetRef a) => a.BundlePath, (MonsterAnimationAssetRef a) => StandaloneModService.Hash(a.BundlePath));
			try
			{
				monsterAnimationService.Apply(root, monsterAnimationSet2, animation, delegate(int n)
				{
					if (n == 2)
					{
						throw new IOException("injected");
					}
				});
				throw new Exception("Failure injection missing");
			}
			catch (IOException ex) when (ex.Message == "injected")
			{
			}
			if (source4.Any((KeyValuePair<string, string> p) => StandaloneModService.Hash(p.Key) != p.Value))
			{
				throw new Exception("Mobile animation rollback failed");
			}
			monsterAnimationService.Apply(root, monsterAnimationSet2, animation);
			if (monsterAnimationSet2.Textures.Any((MonsterAnimationAssetRef a) => engine.ReadAnimationTextureMetadata(a).TextureFormat != 4))
			{
				throw new Exception("PC compression used on mobile");
			}
			using (MonsterAnimationCurrentPreview.TryLoad(monsterAnimationSet2) ?? throw new Exception("Mobile animation preview failed"))
			{
				monsterAnimationService.Restore(root, monsterAnimationSet2);
				if (source4.Any((KeyValuePair<string, string> p) => StandaloneModService.Hash(p.Key) != p.Value))
				{
					throw new Exception("Animation restore failed");
				}
				Console.WriteLine("sharedAnimation=True; mobileRGBA32=True; rollback=True; restore=True");
				MonsterAnimationSet monsterAnimationSet3 = MonsterAnimationIndexService.Find(source, "3413");
				if (!monsterAnimationSet3.IsComplete)
				{
					throw new Exception("Mobile donor 3413 absent");
				}
				foreach (MonsterAnimationAssetRef asset3 in monsterAnimationSet3.Assets)
				{
					originals.TryAdd(asset3.BundlePath, StandaloneModService.Hash(asset3.BundlePath));
				}
				int value = MonsterAnimationTransferService.Replace(root, monsterAnimationSet2, monsterAnimationSet3);
				monsterAnimationService.Restore(root, monsterAnimationSet2);
				if (source4.Any((KeyValuePair<string, string> p) => StandaloneModService.Hash(p.Key) != p.Value))
				{
					throw new Exception("Transfer restore failed");
				}
				Console.WriteLine($"mobileDonorTransfer={value}");
				if (originals.Any<KeyValuePair<string, string>>((KeyValuePair<string, string> p) => StandaloneModService.Hash(p.Key) != p.Value))
				{
					throw new Exception("Real mobile files changed");
				}
				gameIndex.AlternateArtIndexVersion = 4;
				IndexService.Save(root, gameIndex);
				AppSettings settings = AppSettingsStore.Load();
				try
				{
					using MainForm mainForm = new MainForm(null, root, backgroundRefresh: false)
					{
						ShowInTaskbar = false
					};
					mainForm.Show();
					FieldInfo field = typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic);
					Stopwatch stopwatch = Stopwatch.StartNew();
					while (((List<TexRef>)field.GetValue(mainForm)).Count == 0 && stopwatch.Elapsed.TotalSeconds < 30.0)
					{
						Application.DoEvents();
						Thread.Sleep(15);
					}
					List<TexRef> source5 = (List<TexRef>)field.GetValue(mainForm);
					if (!source5.Any((TexRef t) => t.SourceKind == "本地卡图") || !source5.Any((TexRef t) => t.SourceKind == "视觉资源"))
					{
						throw new Exception("Main workspace did not load mobile index");
					}
					typeof(MainForm).GetMethod("SelectTexture", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mainForm, new object[1] { source5.First((TexRef t) => t.SourceKind == "本地卡图") });
					PictureBox pictureBox = (PictureBox)typeof(MainForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mainForm);
					stopwatch.Restart();
					while (pictureBox.Image == null && stopwatch.Elapsed.TotalSeconds < 20.0)
					{
						Application.DoEvents();
						Thread.Sleep(15);
					}
					if (pictureBox.Image == null)
					{
						throw new Exception("Main mobile preview missing");
					}
					using Bitmap bitmap = new Bitmap(mainForm.Width, mainForm.Height);
					mainForm.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, mainForm.Size));
					bitmap.Save(Path.Combine(output, "main-mobile.png"));
					mainForm.Close();
				}
				finally
				{
					AppSettingsStore.Save(settings);
				}
				Console.WriteLine("mainWorkspace=True; sharedCardPreview=True");
				Console.WriteLine("ready=True; sourceWrites=False");
			}
		}
		string Copy(string file)
		{
			originals.TryAdd(file, StandaloneModService.Hash(file));
			string text3 = Path.Combine(root, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(text3));
			File.Copy(file, text3, overwrite: true);
			return text3;
		}
	}
}
