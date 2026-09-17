using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

internal static class Program
{
	private sealed record TextureMappingLiveResult(bool Ready, int StoredWidth, int StoredHeight, int DisplayWidth, int DisplayHeight, bool GeometryReady, bool OriginalUnchanged)
	{
		public override string ToString()
		{
			return $"{DisplayWidth}x{DisplayHeight}->{StoredWidth}x{StoredHeight}:geometry={GeometryReady}:original={OriginalUnchanged}";
		}
	}

	[STAThread]
	private static void Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		if (args.Length == 1 && args[0] == "--test-foil-inner-frame")
		{
			FoilInnerFrameTests.Run();
			return;
		}
		if (args.Length == 2 && args[0] == "--open-0000")
		{
			using (MainForm mainForm = new MainForm(null, args[1]))
			{
				Application.Run(mainForm);
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--audit-animation-resources")
		{
			AnimationResourceAudit.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-write-card")
		{
			AnimationWriteTests.Run(args[1], args[3], largeAtlas: false, args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-preview-card")
		{
			Spine42CompatibilityResult spine42CompatibilityResult = Spine42PreviewRenderer.Probe(MonsterAnimationIndexService.Find(args[1], args[2]), 160);
			Console.WriteLine(JsonSerializer.Serialize(spine42CompatibilityResult));
			if (!spine42CompatibilityResult.Success || spine42CompatibilityResult.OpaquePixels == 0)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-mod-card-identity")
		{
			ModCardIdentityTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 2 && args[0] == "--test-resource-preview-scroll")
		{
			ResourcePreviewScrollTests.Run(args[1]);
			return;
		}
		if (args.Length != 0 && args[0].StartsWith("--test-", StringComparison.Ordinal))
		{
			WindowsFormsSynchronizationContext.AutoInstall = false;
			SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
			Control.CheckForIllegalCrossThreadCalls = true;
		}
		if (args.Length == 3 && args[0] == "--test-animation-write-lock")
		{
			AnimationWriteTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-atlas-8192")
		{
			AnimationWriteTests.Run(args[1], args[2], largeAtlas: true);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-alias-6969")
		{
			AnimationWriteTests.Run(args[1], args[2], largeAtlas: false, "6969");
			return;
		}
		if (args.Length == 3 && args[0] == "--test-standalone-resources")
		{
			StandaloneResourceTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-unified-mobile")
		{
			UnifiedMobileTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-transfer-6969")
		{
			StudioOptimizationTests.Transfer(args[1], args[2], "6969");
			return;
		}
		if (args.Length == 2 && args[0] == "--bundle-dependencies")
		{
			foreach (string item2 in new ModEngine().ReadAssetBundleContainerPaths(args[1], dependencies: true))
			{
				Console.WriteLine(item2);
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-card-catalog-refresh")
		{
			CardCatalogRefreshTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 2 && args[0] == "--test-sidebar-dpi")
		{
			SidebarDpiTests.Run(args[1]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-new-animation-links")
		{
			AnimationLinkTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 4 && args[0] == "--test-index-recovery")
		{
			IndexRecoveryTests.Run(args[1], args[2], args[3]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-mod-packages")
		{
			ModPackageTests.Run(args[1], args[2]);
			return;
		}
		if (args.Length == 2 && args[0] == "--test-studio-optimizations")
		{
			StudioOptimizationTests.Run(args[1]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-transfer")
		{
			StudioOptimizationTests.Transfer(args[1], args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-donor-picker")
		{
			StudioOptimizationTests.Picker(args[1], args[2]);
			return;
		}
		if (args.Length == 3 && args[0] == "--build-card-catalog")
		{
			int value = CardCatalogService.GenerateFromAstellarCsv(args[1], args[2]);
			Console.WriteLine($"cards={value:N0}; output={Path.GetFullPath(args[2])}; bytes={new FileInfo(args[2]).Length:N0}");
			return;
		}
		if (args.Length == 2 && args[0] == "--test-card-catalog")
		{
			CardCatalogService cardCatalogService = new CardCatalogService(CardCatalogService.Read(args[1]));
			CardCatalogEntry cardCatalogEntry = cardCatalogService.Search("青眼白龙", 5).FirstOrDefault();
			CardCatalogEntry cardCatalogEntry2 = cardCatalogService.Search("青眼白龍", 5).FirstOrDefault();
			CardCatalogEntry cardCatalogEntry3 = cardCatalogService.Search("Blue-Eyes White Dragon", 5).FirstOrDefault();
			CardCatalogEntry cardCatalogEntry4 = cardCatalogService.Search("青眼の白龍", 5).FirstOrDefault();
			IReadOnlyList<CardCatalogEntry> readOnlyList = cardCatalogService.Search("独角");
			bool flag = readOnlyList.Any((CardCatalogEntry entry) => entry.CardId == 14338);
			Console.WriteLine($"cards={cardCatalogService.Count}; zh-cn={cardCatalogEntry?.CardId}; zh-tw={cardCatalogEntry2?.CardId}; en={cardCatalogEntry3?.CardId}; ja={cardCatalogEntry4?.CardId}; 独角-results={readOnlyList.Count}; has-14338={flag}");
			if (cardCatalogService.Count < 10000 || cardCatalogEntry == null || cardCatalogEntry2 == null || cardCatalogEntry3 == null || cardCatalogEntry4 == null || cardCatalogEntry.CardId != cardCatalogEntry2.CardId || cardCatalogEntry.CardId != cardCatalogEntry3.CardId || cardCatalogEntry.CardId != cardCatalogEntry4.CardId || !flag)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-card-preview-fallback")
		{
			using (Bitmap bitmap = new Bitmap(512, 512))
			{
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.Clear(System.Drawing.Color.MediumPurple);
				}
				using Bitmap bitmap2 = new Bitmap(128, 128);
				using (Graphics graphics2 = Graphics.FromImage(bitmap2))
				{
					graphics2.Clear(System.Drawing.Color.Transparent);
				}
				using MemoryStream memoryStream = new MemoryStream();
				using MemoryStream memoryStream2 = new MemoryStream();
				bitmap.Save(memoryStream, ImageFormat.Png);
				bitmap2.Save(memoryStream2, ImageFormat.Png);
				string enhancementWarning;
				using Bitmap bitmap3 = CardPreviewRenderer.Render(memoryStream.ToArray(), memoryStream2.ToArray(), fullArt: false, out enhancementWarning);
				bool flag2 = bitmap3.Width == 512 && bitmap3.Height == 512 && !string.IsNullOrWhiteSpace(enhancementWarning);
				Console.WriteLine($"preview={bitmap3.Width}x{bitmap3.Height}; fallbackWarning={enhancementWarning}; ready={flag2}");
				if (!flag2)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 1 && args[0] == "--test-card-preview-raw")
		{
			byte[] texturePng;
			using (Image<Rgba32> image = new Image<Rgba32>(512, 1024, new Rgba32(System.Drawing.Color.MediumPurple.R, System.Drawing.Color.MediumPurple.G, System.Drawing.Color.MediumPurple.B, byte.MaxValue)))
			{
				using MemoryStream memoryStream3 = new MemoryStream();
				image[10, 10] = new Rgba32(19, 143, 227, 0);
				image.Save(memoryStream3, new PngEncoder
				{
					ColorType = PngColorType.RgbWithAlpha,
					TransparentColorMode = PngTransparentColorMode.Preserve
				});
				texturePng = memoryStream3.ToArray();
			}
			using Bitmap bitmap4 = CardPreviewRenderer.RenderRaw(texturePng);
			using Bitmap bitmap5 = CardPreviewRenderer.RenderRaw(texturePng, showTransparentRgb: true);
			System.Drawing.Color pixel = bitmap4.GetPixel(bitmap4.Width / 2, bitmap4.Height / 2);
			System.Drawing.Color pixel2 = bitmap4.GetPixel(10, 10);
			System.Drawing.Color pixel3 = bitmap5.GetPixel(10, 10);
			bool flag3 = bitmap4.Width == 512 && bitmap4.Height == 1024 && pixel.R == System.Drawing.Color.MediumPurple.R && pixel.G == System.Drawing.Color.MediumPurple.G && pixel.B == System.Drawing.Color.MediumPurple.B && pixel2.A == 0 && pixel2.R == 19 && pixel2.G == 143 && pixel2.B == 227 && pixel3.A == byte.MaxValue && pixel3.R == pixel2.R && pixel3.G == pixel2.G && pixel3.B == pixel2.B;
			Console.WriteLine($"preview={bitmap4.Width}x{bitmap4.Height}; center={pixel.R},{pixel.G},{pixel.B}; hidden={pixel2.R},{pixel2.G},{pixel2.B},{pixel2.A}; projected={pixel3.R},{pixel3.G},{pixel3.B},{pixel3.A}; frameComposed=False; ready={flag3}");
			if (!flag3)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-texture-display-mapping")
		{
			TexRef texture = new TexRef
			{
				BundlePath = "sleeve",
				RelativeBundlePath = "sleeve",
				Name = "ProtectorIcon1070005",
				Width = 512,
				Height = 1024,
				Category = "卡套",
				SourceKind = "视觉资源"
			};
			TexRef texture2 = new TexRef
			{
				BundlePath = "pendulum",
				RelativeBundlePath = "pendulum",
				Name = "20486",
				Width = 512,
				Height = 1024,
				Category = "灵摆卡图",
				SourceKind = "本地卡图",
				CardKey = "20486"
			};
			TexRef texture3 = new TexRef
			{
				BundlePath = "wallpaper",
				RelativeBundlePath = "wallpaper",
				Name = "UnrelatedTallTexture",
				Width = 512,
				Height = 1024,
				Category = "壁纸／大厅背景",
				SourceKind = "视觉资源"
			};
			GameTextureDisplayMapping gameTextureDisplayMapping = GameTextureDisplayMapping.Resolve(texture);
			GameTextureDisplayMapping gameTextureDisplayMapping2 = GameTextureDisplayMapping.Resolve(texture2, "card_frame14");
			GameTextureDisplayMapping gameTextureDisplayMapping3 = GameTextureDisplayMapping.ResolveCanvas(new TexRef
			{
				BundlePath = "pendulum-overframe",
				RelativeBundlePath = "pendulum-overframe",
				Name = "20486",
				Width = 704,
				Height = 1024,
				Category = "灵摆卡图",
				SourceKind = "本地卡图",
				CardKey = "20486"
			}, 512, 1024, "card_frame14");
			GameTextureDisplayMapping gameTextureDisplayMapping4 = GameTextureDisplayMapping.Resolve(texture3);
			byte[] array = CreateTextureMappingPattern(gameTextureDisplayMapping.DisplayWidth, gameTextureDisplayMapping.DisplayHeight);
			byte[] array2 = CreateTextureMappingPattern(gameTextureDisplayMapping2.DisplayWidth, gameTextureDisplayMapping2.DisplayHeight);
			byte[] array3 = gameTextureDisplayMapping.EncodeForStorage(array);
			byte[] array4 = gameTextureDisplayMapping2.EncodeForStorage(array2);
			byte[] png = gameTextureDisplayMapping.DecodeForDisplay(array3);
			byte[] png2 = gameTextureDisplayMapping2.DecodeForDisplay(array4);
			bool flag4 = PngHasSize(array3, 512, 1024);
			bool flag5 = PngHasSize(array4, 512, 1024);
			bool flag6 = TextureMappingPatternReady(png, gameTextureDisplayMapping.DisplayWidth, gameTextureDisplayMapping.DisplayHeight);
			bool flag7 = TextureMappingPatternReady(png2, gameTextureDisplayMapping2.DisplayWidth, gameTextureDisplayMapping2.DisplayHeight);
			byte[] storedArtPng = CreateSplitTallTexture();
			byte[] framePng;
			using (Bitmap bitmap6 = new Bitmap(704, 1024, PixelFormat.Format32bppArgb))
			{
				using Graphics graphics3 = Graphics.FromImage(bitmap6);
				using MemoryStream memoryStream4 = new MemoryStream();
				graphics3.Clear(System.Drawing.Color.Transparent);
				bitmap6.Save(memoryStream4, ImageFormat.Png);
				framePng = memoryStream4.ToArray();
			}
			using Bitmap bitmap7 = FrameComposer.BitmapFrom(CardFrameRenderer.ComposeStoredArtPreview(storedArtPng, framePng));
			System.Drawing.Color pixel4 = bitmap7.GetPixel(bitmap7.Width / 2, bitmap7.Height - 80);
			bool flag8 = pixel4.B > 180 && pixel4.R < 80;
			using CropCanvas cropCanvas = new CropCanvas(new Bitmap(512, 683), 512, 683)
			{
				Size = new System.Drawing.Size(900, 720)
			};
			cropCanvas.CreateControl();
			cropCanvas.SetFrame(new Bitmap(704, 1024, PixelFormat.Format32bppArgb));
			System.Drawing.RectangleF rectangleF = (System.Drawing.RectangleF)(typeof(CropCanvas).GetProperty("CardRectangle", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(cropCanvas) ?? ((object)System.Drawing.RectangleF.Empty));
			bool flag9 = Math.Abs(rectangleF.Width / rectangleF.Height - 0.6875f) < 0.001f;
			bool flag10 = true;
			string cropScreenshotRoot = Environment.GetEnvironmentVariable("MDCT_TEXTURE_MAPPING_SCREENSHOT_ROOT");
			if (!string.IsNullOrWhiteSpace(cropScreenshotRoot))
			{
				cropScreenshotRoot = Path.GetFullPath(cropScreenshotRoot);
				Directory.CreateDirectory(cropScreenshotRoot);
				string text = Path.Combine(Path.GetTempPath(), "MDCardModTool", "TextureMappingUi", Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(text);
				try
				{
					string text2 = Path.Combine(text, "sleeve-display.png");
					string text3 = Path.Combine(text, "pendulum-display.png");
					File.WriteAllBytes(text2, array);
					File.WriteAllBytes(text3, array2);
					using (ImageCropForm form = new ImageCropForm(text2, gameTextureDisplayMapping.DisplayWidth, gameTextureDisplayMapping.DisplayHeight, "卡套正常比例回归", null, null, fullCardOverlay: false, null, gameTextureDisplayMapping))
					{
						flag10 &= CaptureCrop(form, "sleeve-crop-normal-ratio.png", needsFrame: false, "正常预览 704×1024");
					}
					using ImageCropForm form2 = new ImageCropForm(text3, gameTextureDisplayMapping2.DisplayWidth, gameTextureDisplayMapping2.DisplayHeight, "灵摆卡图正常比例回归", BuiltInCardFrameCatalog.Load(), "card_frame14", fullCardOverlay: false, null, gameTextureDisplayMapping2);
					flag10 &= CaptureCrop(form2, "pendulum-crop-normal-ratio.png", needsFrame: true, "正常预览 512×683");
				}
				finally
				{
					if (Directory.Exists(text))
					{
						Directory.Delete(text, recursive: true);
					}
				}
			}
			bool flag11 = gameTextureDisplayMapping.Kind == TextureDisplayMappingKind.CardSleeve && gameTextureDisplayMapping.DisplayWidth == 704 && gameTextureDisplayMapping.DisplayHeight == 1024 && gameTextureDisplayMapping2.Kind == TextureDisplayMappingKind.PendulumCardArt && gameTextureDisplayMapping2.DisplayWidth == 512 && gameTextureDisplayMapping2.DisplayHeight == 683 && gameTextureDisplayMapping3.Kind == TextureDisplayMappingKind.PendulumCardArt && gameTextureDisplayMapping3.DisplayWidth == 512 && gameTextureDisplayMapping3.DisplayHeight == 683 && gameTextureDisplayMapping4.Kind == TextureDisplayMappingKind.Native && !gameTextureDisplayMapping4.RequiresMapping && flag4 && flag5 && flag6 && flag7 && flag8 && flag9 && flag10;
			Console.WriteLine($"sleeve={gameTextureDisplayMapping.DisplayWidth}x{gameTextureDisplayMapping.DisplayHeight}->{gameTextureDisplayMapping.StorageWidth}x{gameTextureDisplayMapping.StorageHeight}:{flag6}; pendulum={gameTextureDisplayMapping2.DisplayWidth}x{gameTextureDisplayMapping2.DisplayHeight}->{gameTextureDisplayMapping2.StorageWidth}x{gameTextureDisplayMapping2.StorageHeight}:{flag7}; legacyOverFrameDraft={gameTextureDisplayMapping3.Kind}:{gameTextureDisplayMapping3.DisplayWidth}x{gameTextureDisplayMapping3.DisplayHeight}; unrelated={gameTextureDisplayMapping4.Kind}; fullCanvas={flag8}:{pixel4.R},{pixel4.G},{pixel4.B}; frameAspect={rectangleF.Width:0.0}x{rectangleF.Height:0.0}:{flag9}; cropUi={flag10}; ready={flag11}");
			if (!flag11)
			{
				Environment.ExitCode = 2;
			}
			return;
			bool CaptureCrop(ImageCropForm imageCropForm, string outputName, bool needsFrame, string expectedMappingText)
			{
				imageCropForm.StartPosition = FormStartPosition.Manual;
				imageCropForm.Location = new System.Drawing.Point(-32000, -32000);
				imageCropForm.ShowInTaskbar = false;
				imageCropForm.Show();
				CropCanvas cropCanvas4 = (CropCanvas)(typeof(ImageCropForm).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(imageCropForm) ?? throw new MissingFieldException("ImageCropForm", "_canvas"));
				Label label12 = (Label)(typeof(ImageCropForm).GetField("_mapping", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(imageCropForm) ?? throw new MissingFieldException("ImageCropForm", "_mapping"));
				Stopwatch stopwatch22 = Stopwatch.StartNew();
				while (needsFrame && !cropCanvas4.HasFrame && stopwatch22.ElapsedMilliseconds < 15000)
				{
					Application.DoEvents();
					Thread.Sleep(15);
				}
				imageCropForm.PerformLayout();
				Application.DoEvents();
				Button[] array40 = Descendants(imageCropForm).OfType<Button>().Where(delegate(Button button11)
				{
					string text29 = button11.Text;
					return (text29 == "取消" || text29 == "按预览效果替换") ? true : false;
				}).ToArray();
				bool flag94 = array40.Length == 2 && array40.All((Button button11) => button11.Visible && imageCropForm.ClientRectangle.Contains(imageCropForm.RectangleToClient(button11.RectangleToScreen(button11.ClientRectangle))));
				using Bitmap bitmap20 = new Bitmap(imageCropForm.ClientSize.Width, imageCropForm.ClientSize.Height);
				imageCropForm.DrawToBitmap(bitmap20, imageCropForm.ClientRectangle);
				bitmap20.Save(Path.Combine(cropScreenshotRoot, outputName), ImageFormat.Png);
				bool result = (!needsFrame || cropCanvas4.HasFrame) && label12.Visible && label12.Text.Contains(expectedMappingText, StringComparison.Ordinal) && flag94 && imageCropForm.AutoScaleMode == AutoScaleMode.Dpi;
				Console.WriteLine($"crop={outputName}; client={imageCropForm.ClientSize}; mapping={label12.Bounds}; buttons={flag94}:" + string.Join(",", array40.Select((Button button11) => $"{button11.Text}@{imageCropForm.RectangleToClient(button11.RectangleToScreen(button11.ClientRectangle))}")));
				imageCropForm.Close();
				return result;
			}
		}
		if (args.Length == 2 && args[0] == "--test-texture-display-mapping-live")
		{
			string fullPath = Path.GetFullPath(args[1]);
			if (!PortableIndexService.TryLoadBundled(fullPath, out GameIndex index, out string buildId))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef texRef = index.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.Width == 512 && texRef20.Height == 1024 && (texRef20.CardKey == "20486" || texRef20.Category.Contains("灵摆", StringComparison.OrdinalIgnoreCase))) ?? throw new InvalidDataException("预绑定索引中没有可用于只读回归的灵摆卡图。");
			TexRef texRef2 = VisualAssetIndexService.Scan(fullPath).Textures.FirstOrDefault((TexRef texRef20) => texRef20.Width == 512 && texRef20.Height == 1024 && texRef20.Category.Contains("卡套", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("当前游戏资源中没有可用于只读回归的卡套。");
			string text4 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "TextureDisplayMappingLiveTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text4);
			try
			{
				TextureMappingLiveResult textureMappingLiveResult = TestTextureMappingLiveCopy(texRef2, GameTextureDisplayMapping.Resolve(texRef2), Path.Combine(text4, "sleeve"));
				TextureMappingLiveResult textureMappingLiveResult2 = TestTextureMappingLiveCopy(texRef, GameTextureDisplayMapping.Resolve(texRef, "card_frame14"), Path.Combine(text4, "pendulum"));
				bool flag12 = textureMappingLiveResult.Ready && textureMappingLiveResult2.Ready;
				Console.WriteLine($"build={buildId}; sleeve={texRef2.Name}:{textureMappingLiveResult}; pendulum={texRef.CardKey}:{textureMappingLiveResult2}; temporaryCopies=True; gameWrites=False; ready={flag12}");
				if (!flag12)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				string fullPath2 = Path.GetFullPath(text4);
				string value2 = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool", "TextureDisplayMappingLiveTests")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath2.StartsWith(value2, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath2))
				{
					Directory.Delete(fullPath2, recursive: true);
				}
			}
		}
		ModEngine engine;
		if (args.Length == 1 && args[0] == "--test-packaged-card-frames")
		{
			IReadOnlyList<TexRef> readOnlyList2 = BuiltInCardFrameCatalog.Load();
			engine = new ModEngine();
			int num = 0;
			foreach (TexRef item3 in readOnlyList2)
			{
				using Bitmap bitmap8 = FrameComposer.BitmapFrom(engine.DecodePng(item3));
				System.Drawing.RectangleF rectangleF2 = CardFrameRenderer.FindArtWindow(bitmap8);
				if (bitmap8.Width == 704 && bitmap8.Height == 1024 && rectangleF2.Width > 100f && rectangleF2.Height > 100f)
				{
					num++;
				}
			}
			string text5 = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 22747,
				Type = "怪獸",
				SubType = "連結"
			}, 704, 1024);
			string text6 = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 20486,
				Type = "怪獸",
				SubType = "效果=靈擺"
			}, 512, 1024);
			string text7 = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 1,
				Type = "怪獸",
				SubType = "通常"
			}, 512, 512);
			CardCatalogService cardCatalogService2 = CardCatalogService.LoadBestAvailable();
			string text8 = CardFrameCatalog.RecommendedKey(cardCatalogService2.Find(22747), 704, 1024);
			string text9 = CardFrameCatalog.RecommendedKey(cardCatalogService2.Find(20486), 512, 1024);
			int num2 = readOnlyList2.Count(BuiltInCardFrameCatalog.IsNormalFrame);
			int num3 = readOnlyList2.Count(BuiltInCardFrameCatalog.IsTransparentFrame);
			int num4 = readOnlyList2.Count(BuiltInCardFrameCatalog.IsTransparentGradientFrame);
			int num5 = readOnlyList2.Count(BuiltInCardFrameCatalog.IsGradientFrame);
			TexRef frame = readOnlyList2.Single((TexRef texRef20) => texRef20.Name == "card_frame01");
			TexRef frame2 = readOnlyList2.Single((TexRef texRef20) => texRef20.Name == "transparent_card_frame01");
			TexRef frame3 = readOnlyList2.Single((TexRef texRef20) => texRef20.Name == "transparent_gradient_card_frame01");
			TexRef frame4 = readOnlyList2.Single((TexRef texRef20) => texRef20.Name == "gradient_card_frame01");
			Rgba32[] array5 = ReadPixels(frame);
			Rgba32[] array6 = ReadPixels(frame2);
			Rgba32[] array7 = ReadPixels(frame3);
			Rgba32[] array8 = ReadPixels(frame4);
			int num6 = array5.Count((Rgba32 rgba10) => rgba10.A > 0);
			int num7 = array6.Count((Rgba32 rgba10) => rgba10.A > 0);
			int num8 = array6.Count((Rgba32 rgba10) => rgba10.A == 0 && (rgba10.R != 0 || rgba10.G != 0 || rgba10.B != 0));
			int num9 = array7.Count((Rgba32 rgba10) => rgba10.A > 0);
			int num10 = array7.Count((Rgba32 rgba10) => rgba10.A == 0 && (rgba10.R != 0 || rgba10.G != 0 || rgba10.B != 0));
			int num11 = array6.Zip(array7).Count(((Rgba32 First, Rgba32 Second) pair) => pair.First.R != pair.Second.R || pair.First.G != pair.Second.G || pair.First.B != pair.Second.B);
			int num12 = array8.Zip(array7).Count(((Rgba32 First, Rgba32 Second) pair) => pair.First.A != pair.Second.A);
			int num13 = array5.Zip(array8).Count(((Rgba32 First, Rgba32 Second) pair) => pair.First.R != pair.Second.R || pair.First.G != pair.Second.G || pair.First.B != pair.Second.B || pair.First.A != pair.Second.A);
			bool flag13 = readOnlyList2.Count == 64 && num == readOnlyList2.Count && num2 == 16 && num3 == 16 && num4 == 16 && num5 == 16 && num7 + 100000 < num6 && num8 > 10000 && num9 + 100000 < num6 && num10 > 10000 && num11 > 10000 && num12 > 10000 && num13 > 10000 && readOnlyList2.Any((TexRef texRef20) => texRef20.Name == "card_frame18") && readOnlyList2.Any((TexRef texRef20) => texRef20.Name == "transparent_card_frame18") && readOnlyList2.Any((TexRef texRef20) => texRef20.Name == "transparent_gradient_card_frame18") && readOnlyList2.Any((TexRef texRef20) => texRef20.Name == "gradient_card_frame18") && readOnlyList2.All(BuiltInCardFrameCatalog.IsPackagedFrame) && text5 == "card_frame18" && text6 == "card_frame14" && text7 == "card_frame00" && text8 == "card_frame18" && text9 == "card_frame14";
			Console.WriteLine($"frames={readOnlyList2.Count}; normal={num2}:{num6}; transparent={num3}:{num7}:rgb0={num8}; transparentGradient={num4}:{num9}:rgb0={num10}:colorDiff={num11}:alphaDiff={num12}; gradient={num5}:diff={num13}; artWindows={num}; link={text5}/{text8}; pendulumEffect={text6}/{text9}; normalMonster={text7}; ready={flag13}");
			if (!flag13)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-overframe-workflow")
		{
			string text10 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameWorkflowTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text10);
			try
			{
				byte[] initialArt;
				using (Image<Rgba32> image2 = new Image<Rgba32>(512, 512, new Rgba32(0, 0, 0, 0)))
				{
					for (int num14 = 22; num14 < 500; num14++)
					{
						for (int num15 = 176; num15 < 346; num15++)
						{
							image2[num15, num14] = new Rgba32(24, 112, 208, byte.MaxValue);
						}
					}
					initialArt = PngBytes(image2);
				}
				byte[] array9;
				using (Image<Rgba32> image3 = new Image<Rgba32>(704, 1024, new Rgba32(217, 45, 81, byte.MaxValue)))
				{
					array9 = PngBytes(image3);
				}
				TexRef[] frames = BuiltInCardFrameCatalog.Load().ToArray();
				TexRef texRef3 = new TexRef
				{
					BundlePath = Path.Combine(text10, "not-written.bundle"),
					RelativeBundlePath = "not-written.bundle",
					Name = "3899",
					Width = 512,
					Height = 512,
					SourceKind = "本地卡图",
					CardKey = "3899"
				};
				int[] array10 = new int[4];
				bool flag14 = false;
				OverFrameFrameEditorForm editor = new OverFrameFrameEditorForm(text10, texRef3, frames, initialArt, "transparent_card_frame01", array9, replaceStoredBackground: true)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				};
				ImageRenderSpec movedSpec;
				bool flag19;
				try
				{
					editor.Show();
					ModernComboBox modernComboBox = (ModernComboBox)(typeof(OverFrameFrameEditorForm).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_mode"));
					ModernComboBox modernComboBox2 = (ModernComboBox)(typeof(OverFrameFrameEditorForm).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_frames"));
					CropCanvas canvas = (CropCanvas)(typeof(OverFrameFrameEditorForm).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_canvas"));
					Label status = (Label)(typeof(OverFrameFrameEditorForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_status"));
					Label label = (Label)(typeof(OverFrameFrameEditorForm).GetField("_layerStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_layerStatus"));
					FieldInfo outputField = typeof(OverFrameFrameEditorForm).GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_outputBytes");
					FieldInfo fieldInfo = typeof(OverFrameFrameEditorForm).GetField("_previewBytes", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_previewBytes");
					bool flag15 = WaitFor(() => outputField.GetValue(editor) is byte[]);
					var foilToggle = (CheckBox)typeof(OverFrameFrameEditorForm).GetField("_removeFoilInnerFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
					var renderingField = typeof(OverFrameFrameEditorForm).GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic)!;
					if (!WaitFor(() => !(bool)renderingField.GetValue(editor)!)) throw new Exception("Initial render timeout");
					byte[] defaultFoil = (byte[])outputField.GetValue(editor)!;
					foilToggle.Checked = true;
					if (!(bool)renderingField.GetValue(editor)!) throw new Exception("Race fixture did not start rendering");
					// Simulate another edit while composing, then immediately prepare Apply.
					typeof(OverFrameFrameEditorForm).GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
					var readyTask = (Task)typeof(OverFrameFrameEditorForm).GetMethod("EnsurePreviewReadyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
					if (!WaitFor(() => readyTask.IsCompleted)) throw new Exception("Apply readiness timed out");
					readyTask.GetAwaiter().GetResult();
					if (outputField.GetValue(editor) is not byte[]) throw new Exception("Apply returned before latest output was ready");
					Console.WriteLine("applyDuringRender=True; pendingEditWaited=True; latestOutputReady=True; gameWrites=False");
					if (!WaitFor(() => outputField.GetValue(editor) is byte[] && !(bool)renderingField.GetValue(editor)!)) throw new Exception("Foil render timeout");
					if (defaultFoil.AsSpan().SequenceEqual((byte[])outputField.GetValue(editor)!) || !OverFrameArtStore.ReadSettings(text10, 3899).RemoveFoilInnerFrame) throw new Exception("Foil option not applied or saved");
					if (!((byte[])outputField.GetValue(editor)!).AsSpan().SequenceEqual((byte[])fieldInfo.GetValue(editor)!)) throw new Exception("Foil preview/export mismatch");
					foilToggle.Checked = false;
					if (!WaitFor(() => outputField.GetValue(editor) is byte[] && !(bool)renderingField.GetValue(editor)!) || !defaultFoil.AsSpan().SequenceEqual((byte[])outputField.GetValue(editor)!)) throw new Exception("Disabling foil option did not restore default");
					Console.WriteLine("foilToggle=True; draftSaved=True; previewMatches=True; disabledRestoresDefault=True");
					ImageRenderSpec renderSpec = canvas.RenderSpec;
					movedSpec = new ImageRenderSpec(704f, 1024f, renderSpec.ImageScale * 1.28f, renderSpec.OffsetX + 37f, renderSpec.OffsetY - 24f);
					canvas.SetRenderSpec(movedSpec);
					bool flag16 = WaitFor(() => outputField.GetValue(editor) is byte[] && Math.Abs(canvas.RenderSpec.OffsetX - movedSpec.OffsetX) < 1f);
					string[] expectedModes = new string[4] { "透明卡框", "透明炫彩卡框", "炫彩卡框", "普通卡框" };
					bool flag17 = flag15 && flag16;
					int index2;
					for (index2 = 0; index2 < expectedModes.Length; index2++)
					{
						modernComboBox.SelectedIndex = index2;
						bool flag18 = WaitFor(() => outputField.GetValue(editor) is byte[] && status.Text.StartsWith(expectedModes[index2], StringComparison.Ordinal));
						array10[index2] = modernComboBox2.Items.Count;
						Console.WriteLine($"mode={index2}; generated={flag18}; status={status.Text}");
						byte[] array11 = outputField.GetValue(editor) as byte[];
						ImageInfo imageInfo = ((array11 == null) ? null : SixLabors.ImageSharp.Image.Identify(array11));
						ImageRenderSpec renderSpec2 = canvas.RenderSpec;
						flag17 &= flag18 && array10[index2] == 16 && array11 != null && imageInfo != null && imageInfo.Width == 704 && imageInfo.Height == 1024 && Math.Abs(renderSpec2.ImageScale - movedSpec.ImageScale) < 0.02f && Math.Abs(renderSpec2.OffsetX - movedSpec.OffsetX) < 1f && Math.Abs(renderSpec2.OffsetY - movedSpec.OffsetY) < 1f;
					}
					modernComboBox.SelectedIndex = 1;
					modernComboBox.SelectedIndex = 2;
					modernComboBox.SelectedIndex = 3;
					modernComboBox.SelectedIndex = 0;
					flag17 &= WaitFor(() => outputField.GetValue(editor) is byte[] && status.Text.StartsWith(expectedModes[0], StringComparison.Ordinal));
					if (outputField.GetValue(editor) is byte[] array12 && fieldInfo.GetValue(editor) is byte[] array13)
					{
						using Image<Rgba32> image4 = SixLabors.ImageSharp.Image.Load<Rgba32>(array12);
						using Image<Rgba32> image5 = SixLabors.ImageSharp.Image.Load<Rgba32>(array13);
						int num16 = 0;
						int num17 = 0;
						for (int num18 = 0; num18 < image4.Height; num18++)
						{
							for (int num19 = 0; num19 < image4.Width; num19++)
							{
								Rgba32 rgba = image4[num19, num18];
								Rgba32 rgba2 = image5[num19, num18];
								if (rgba.A == 0)
								{
									num16++;
								}
								if (rgba != rgba2)
								{
									num17++;
								}
							}
						}
						flag14 = num16 > 10000 && num17 == 0;
						Console.WriteLine($"alphaPixels={num16}; mismatches={num17}; status={status.Text}");
					}
					string[] source = (from button11 in Descendants(editor).OfType<Button>()
						select button11.Text).ToArray();
					string[] first = (from object obj10 in modernComboBox.Items
						select obj10.ToString() ?? "").ToArray();
					flag19 = flag17 && canvas.IsOverFrameEditing && canvas.ShowingRenderedPreview && first.SequenceEqual(expectedModes) && label.Text.Contains("已添加背景", StringComparison.Ordinal) && label.Text.Contains("主体可越过卡框", StringComparison.Ordinal) && source.Contains("更换卡图") && source.Contains("添加叠底背景") && source.Contains("清除背景") && flag14 && source.Contains("真实 Alpha 预览") && source.Contains("构图编辑") && source.Contains("导出最终 PNG") && source.Contains("铺满插图区") && source.Contains("显示整张图");
					string environmentVariable = Environment.GetEnvironmentVariable("MDCT_OVERFRAME_SCREENSHOT");
					if (!string.IsNullOrWhiteSpace(environmentVariable))
					{
						if (int.TryParse(Environment.GetEnvironmentVariable("MDCT_OVERFRAME_SCREENSHOT_MODE"), out var screenshotMode) && screenshotMode >= 0 && screenshotMode < expectedModes.Length)
						{
							modernComboBox.SelectedIndex = screenshotMode;
							WaitFor(() => outputField.GetValue(editor) is byte[] && status.Text.StartsWith(expectedModes[screenshotMode], StringComparison.Ordinal));
						}
						Application.DoEvents();
						using Bitmap bitmap9 = new Bitmap(editor.Width, editor.Height);
						editor.DrawToBitmap(bitmap9, new System.Drawing.Rectangle(0, 0, bitmap9.Width, bitmap9.Height));
						bitmap9.Save(environmentVariable, ImageFormat.Png);
					}
					editor.Close();
				}
				finally
				{
					if (editor != null)
					{
						((IDisposable)editor).Dispose();
					}
				}
				OverFrameFrameEditorForm reopened = new OverFrameFrameEditorForm(text10, texRef3, frames)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				};
				bool flag20;
				try
				{
					reopened.Show();
					CropCanvas cropCanvas2 = (CropCanvas)(typeof(OverFrameFrameEditorForm).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reopened) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_canvas"));
					FieldInfo reopenedOutput = typeof(OverFrameFrameEditorForm).GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_outputBytes");
					flag20 = WaitFor(() => reopenedOutput.GetValue(reopened) is byte[]) && cropCanvas2.IsOverFrameEditing && cropCanvas2.ShowingRenderedPreview && Math.Abs(cropCanvas2.RenderSpec.ImageScale - movedSpec.ImageScale) < 0.02f && Math.Abs(cropCanvas2.RenderSpec.OffsetX - movedSpec.OffsetX) < 1f && Math.Abs(cropCanvas2.RenderSpec.OffsetY - movedSpec.OffsetY) < 1f;
					reopened.Close();
				}
				finally
				{
					if (reopened != null)
					{
						((IDisposable)reopened).Dispose();
					}
				}
				OverFrameArtStore.SaveSource(text10, 20486, CreateTextureMappingPattern(512, 1024));
				OverFrameArtStore.SaveSettings(text10, 20486, new OverFrameFrameSettings("card_frame14", UsesCustomFrame: false, UserSelected: true));
				TexRef texRef4 = new TexRef
				{
					BundlePath = Path.Combine(text10, "not-written-pendulum.bundle"),
					RelativeBundlePath = "not-written-pendulum.bundle",
					Name = ((ushort)20486).ToString(),
					Width = 704,
					Height = 1024,
					Category = "灵摆卡图",
					SourceKind = "本地卡图",
					CardKey = ((ushort)20486).ToString()
				};
				OverFrameFrameEditorForm legacyEditor = new OverFrameFrameEditorForm(text10, texRef4, frames)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				};
				bool flag21;
				try
				{
					legacyEditor.Show();
					FieldInfo legacyOutput = typeof(OverFrameFrameEditorForm).GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_outputBytes");
					bool num20 = WaitFor(() => legacyOutput.GetValue(legacyEditor) is byte[]);
					ImageInfo imageInfo2 = SixLabors.ImageSharp.Image.Identify(OverFrameArtStore.SourcePath(text10, 20486));
					flag21 = num20 && imageInfo2 != null && imageInfo2.Width == 512 && imageInfo2.Height == 683 && !File.Exists(texRef4.BundlePath);
					legacyEditor.Close();
				}
				finally
				{
					if (legacyEditor != null)
					{
						((IDisposable)legacyEditor).Dispose();
					}
				}
				byte[] artPng;
				using (Image<Rgba32> image6 = new Image<Rgba32>(704, 1024, new Rgba32(0, 0, 0, 0)))
				{
					image6[100, 100] = new Rgba32(239, 31, 47, byte.MaxValue);
					artPng = PngBytes(image6);
				}
				byte[] framePng2;
				using (Image<Rgba32> image7 = new Image<Rgba32>(704, 1024, new Rgba32(0, 0, 0, 0)))
				{
					image7[100, 100] = new Rgba32(20, 230, 61, byte.MaxValue);
					framePng2 = PngBytes(image7);
				}
				using Image<Rgba32> image8 = SixLabors.ImageSharp.Image.Load<Rgba32>(AstellarOverFrameComposer.ComposeFlatFrame(artPng, framePng2, array9));
				Rgba32 rgba3 = image8[100, 100];
				Rgba32 rgba4 = image8[352, 512];
				string path = OverFrameArtStore.BackgroundPath(text10, 3899);
				ImageInfo imageInfo3 = (File.Exists(path) ? SixLabors.ImageSharp.Image.Identify(path) : null);
				ImageInfo imageInfo4 = SixLabors.ImageSharp.Image.Identify(OverFrameArtStore.SourcePath(text10, 3899));
				OverFrameFrameSettings overFrameFrameSettings = OverFrameArtStore.ReadSettings(text10, 3899);
				bool flag22 = rgba3.R == 239 && rgba3.G == 31 && rgba3.B == 47 && rgba3.A == byte.MaxValue && rgba4.R == 217 && rgba4.G == 45 && rgba4.B == 81 && rgba4.A == byte.MaxValue;
				bool flag23 = Math.Abs(overFrameFrameSettings.ArtImageScale - movedSpec.ImageScale) < 0.02f && Math.Abs(overFrameFrameSettings.ArtOffsetX - movedSpec.OffsetX) < 1f && Math.Abs(overFrameFrameSettings.ArtOffsetY - movedSpec.OffsetY) < 1f;
				bool flag24 = flag19 && flag20 && flag21 && flag22 && flag23 && imageInfo3 != null && imageInfo3.Width == 704 && imageInfo3.Height == 1024 && imageInfo4 != null && imageInfo4.Width == 512 && imageInfo4.Height == 512 && !File.Exists(texRef3.BundlePath);
				Console.WriteLine($"singleEditor=True; reopenRestored={flag20}; legacyPendulumDraftMigrated={flag21}; alphaPreview={flag14}; source={imageInfo4?.Width}x{imageInfo4?.Height}; editorModes={string.Join(',', array10)}; dragScale={overFrameFrameSettings.ArtImageScale:0.000}; dragOffset={overFrameFrameSettings.ArtOffsetX:0.0},{overFrameFrameSettings.ArtOffsetY:0.0}; subjectOverFrame={rgba3.R},{rgba3.G},{rgba3.B},{rgba3.A}; backgroundThrough={rgba4.R},{rgba4.G},{rgba4.B},{rgba4.A}; gameWrites=False; ready={flag24}");
				if (!flag24)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				string fullPath3 = Path.GetFullPath(text10);
				string value3 = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameWorkflowTests")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath3.StartsWith(value3, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath3))
				{
					Directory.Delete(fullPath3, recursive: true);
				}
			}
		}
		if (args.Length == 2 && args[0] == "--test-astellar-overframe")
		{
			string text11 = args[1];
			AstellarOverFrameTemplate astellarOverFrameTemplate = AstellarOverFrameTemplateCatalog.Load(text11);
			bool[] array14 = new bool[720896];
			using Image<Rgba32> image9 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameTemplate.Layers["EffBox"]);
			string[] array15 = new string[3] { "PeriFrame", "ArtFrame", "EffFrame" };
			foreach (string text12 in array15)
			{
				using Image<Rgba32> image10 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameTemplate.Layers[text12]);
				for (int num22 = 0; num22 < image10.Height; num22++)
				{
					for (int num23 = 0; num23 < image10.Width; num23++)
					{
						if (image10[num23, num22].A != 0 && (!(text12 != "PeriFrame") || image9[num23, num22].A <= 0))
						{
							array14[num22 * 704 + num23] = true;
						}
					}
				}
			}
			int num24 = Array.FindIndex(array14, (bool result) => result);
			if (num24 < 0)
			{
				throw new InvalidDataException("透明边缘模板 " + text11 + " 没有 Dirty Alpha 几何。");
			}
			System.Drawing.Point point = new System.Drawing.Point(num24 % 704, num24 / 704);
			byte[] transparentArtPng;
			using (Image<Rgba32> image11 = new Image<Rgba32>(704, 1024, new Rgba32(0, 0, 0, 0)))
			{
				using MemoryStream memoryStream5 = new MemoryStream();
				for (int num25 = Math.Max(0, point.Y - 5); num25 <= Math.Min(image11.Height - 1, point.Y + 5); num25++)
				{
					for (int num26 = Math.Max(0, point.X - 5); num26 <= Math.Min(image11.Width - 1, point.X + 5); num26++)
					{
						image11[num26, num25] = new Rgba32(239, 31, 47, byte.MaxValue);
					}
				}
				image11.SaveAsPng(memoryStream5);
				transparentArtPng = memoryStream5.ToArray();
			}
			AstellarOverFrameComposition astellarOverFrameComposition = AstellarOverFrameComposer.Compose(transparentArtPng, astellarOverFrameTemplate);
			using Image<Rgba32> image12 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameComposition.GamePng);
			using Image<Rgba32> image13 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameComposition.PreviewPng);
			using Image<Rgba32> image14 = SixLabors.ImageSharp.Image.Load<Rgba32>(AstellarOverFrameComposer.CreateVisibleRgbPreview(astellarOverFrameComposition.GamePng));
			int num27 = 0;
			int num28 = 0;
			int num29 = 0;
			int num30 = 0;
			int num31 = 0;
			int num32 = 0;
			int num33 = 0;
			for (int num34 = 0; num34 < image12.Height; num34++)
			{
				for (int num35 = 0; num35 < image12.Width; num35++)
				{
					int num36 = num34 * image12.Width + num35;
					Rgba32 rgba5 = image12[num35, num34];
					Rgba32 rgba6 = image13[num35, num34];
					bool num37 = rgba5.A == 0 && (rgba5.R != 0 || rgba5.G != 0 || rgba5.B != 0);
					if (rgba6 != rgba5)
					{
						num32++;
					}
					if (num37)
					{
						num27++;
						if (rgba6.A == 0)
						{
							num28++;
						}
						if (image14[num35, num34].A == byte.MaxValue)
						{
							num29++;
						}
					}
					if (array14[num36])
					{
						num30++;
						if (rgba5.A != 0)
						{
							num31++;
						}
					}
					else if (rgba5.A > 0)
					{
						num33++;
					}
				}
			}
			Rgba32 rgba7 = image12[point.X, point.Y];
			bool flag25 = astellarOverFrameTemplate.Layers.Count == 6 && image12.Width == 704 && image12.Height == 1024 && num27 > 10000 && num28 == num27 && num29 == num27 && num32 == 0 && num30 > 10000 && num31 == 0 && num33 > 10000 && astellarOverFrameComposition.TransparentEdgePixels == num27 && rgba7.R == 239 && rgba7.G == 31 && rgba7.B == 47 && rgba7.A == 0 && image13[point.X, point.Y].A == 0;
			Console.WriteLine($"template={text11}; layers={astellarOverFrameTemplate.Layers.Count}; transparentRgb={num27}/{astellarOverFrameComposition.TransparentEdgePixels}; previewTransparent={num28}; diagnosticVisible={num29}; previewMismatches={num32}; dirty={num30}; dirtyAlphaFailures={num31}; visibleOutsideDirty={num33}; subjectOverFrame={rgba7.R},{rgba7.G},{rgba7.B},{rgba7.A}@{point.X},{point.Y}; gameBytes={astellarOverFrameComposition.GamePng.Length}; ready={flag25}");
			if (!flag25)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		string frameKey;
		if (args.Length == 2 && args[0] == "--test-astellar-overframe-draft")
		{
			string fullPath4 = Path.GetFullPath(args[1]);
			string path2 = Path.Combine(fullPath4, "卡框设置.json");
			string path3 = Path.Combine(fullPath4, "透明原画.png");
			string path4 = Path.Combine(fullPath4, "叠底背景.png");
			OverFrameFrameSettings overFrameFrameSettings2 = JsonSerializer.Deserialize<OverFrameFrameSettings>(File.ReadAllText(path2)) ?? throw new InvalidDataException("无法读取超框草稿设置。");
			string text13;
			if (!overFrameFrameSettings2.FrameKey.StartsWith("transparent_", StringComparison.Ordinal))
			{
				text13 = overFrameFrameSettings2.FrameKey;
			}
			else
			{
				frameKey = overFrameFrameSettings2.FrameKey;
				int num21 = "transparent_".Length;
				text13 = frameKey.Substring(num21, frameKey.Length - num21);
			}
			string text14 = text13;
			AstellarOverFrameComposition astellarOverFrameComposition2 = AstellarOverFrameComposer.Compose(File.ReadAllBytes(path3), AstellarOverFrameTemplateCatalog.Load(text14), File.Exists(path4) ? File.ReadAllBytes(path4) : null);
			using Image<Rgba32> image15 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameComposition2.GamePng);
			using Image<Rgba32> image16 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameComposition2.PreviewPng);
			using Image<Rgba32> image17 = SixLabors.ImageSharp.Image.Load<Rgba32>(AstellarOverFrameComposer.CreateVisibleRgbPreview(astellarOverFrameComposition2.GamePng));
			int num38 = 0;
			int num39 = 0;
			int num40 = 0;
			int num41 = 0;
			int num42 = 0;
			int num43 = 0;
			for (int num44 = 0; num44 < image15.Height; num44++)
			{
				for (int num45 = 0; num45 < image15.Width; num45++)
				{
					Rgba32 rgba8 = image15[num45, num44];
					Rgba32 rgba9 = image16[num45, num44];
					bool flag26 = rgba8.A == 0 && (rgba8.R != 0 || rgba8.G != 0 || rgba8.B != 0);
					if (rgba8.A == 0)
					{
						num38++;
						if (flag26)
						{
							num39++;
							if (rgba9.A == 0)
							{
								num41++;
							}
							if (image17[num45, num44].A == byte.MaxValue)
							{
								num42++;
							}
						}
					}
					if (rgba8.A == byte.MaxValue)
					{
						num40++;
					}
					if (rgba9 != rgba8)
					{
						num43++;
					}
				}
			}
			bool flag27 = image15.Width == 704 && image15.Height == 1024 && num38 > 100000 && num39 > 100000 && num40 > 100000 && num41 == num39 && num42 == num39 && num43 == 0 && astellarOverFrameComposition2.TransparentEdgePixels == num39;
			Console.WriteLine($"draft={fullPath4}; frame={text14}; zeroAlpha={num38}; hiddenRgb={num39}/{astellarOverFrameComposition2.TransparentEdgePixels}; opaque={num40}; previewTransparentRgb={num41}; diagnosticVisibleRgb={num42}; previewMismatches={num43}; gameBytes={astellarOverFrameComposition2.GamePng.Length}; ready={flag27}");
			if (!flag27)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-texture-rgba-roundtrip")
		{
			string fullPath5 = Path.GetFullPath(args[1]);
			string cardId = args[2];
			string fullPath6 = Path.GetFullPath(args[3]);
			if (!PortableIndexService.TryLoadBundled(fullPath5, out GameIndex index3, out frameKey))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef texture4 = index3.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == cardId) ?? throw new InvalidDataException("预绑定索引不含卡号 " + cardId + "。");
			ModEngine modEngine = new ModEngine();
			TexRef texRef5 = modEngine.ResolveTextureReference(texture4) ?? throw new InvalidDataException("无法重新定位卡号 " + cardId + " 的 Texture2D。");
			string fullPath7 = Path.GetFullPath(texRef5.ActiveBundlePath);
			byte[] first2 = HashFile(fullPath7);
			OverFrameFrameSettings overFrameFrameSettings3 = JsonSerializer.Deserialize<OverFrameFrameSettings>(File.ReadAllText(Path.Combine(fullPath6, "卡框设置.json"))) ?? throw new InvalidDataException("无法读取超框草稿设置。");
			string text15;
			if (!overFrameFrameSettings3.FrameKey.StartsWith("transparent_", StringComparison.Ordinal))
			{
				text15 = overFrameFrameSettings3.FrameKey;
			}
			else
			{
				frameKey = overFrameFrameSettings3.FrameKey;
				int num21 = "transparent_".Length;
				text15 = frameKey.Substring(num21, frameKey.Length - num21);
			}
			string key = text15;
			string path5 = Path.Combine(fullPath6, "叠底背景.png");
			AstellarOverFrameComposition astellarOverFrameComposition3 = AstellarOverFrameComposer.Compose(File.ReadAllBytes(Path.Combine(fullPath6, "透明原画.png")), AstellarOverFrameTemplateCatalog.Load(key), File.Exists(path5) ? File.ReadAllBytes(path5) : null);
			string text16 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "TextureRoundTripTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text16);
			try
			{
				string text17 = Path.Combine(text16, Path.GetFileName(fullPath7));
				File.Copy(fullPath7, text17);
				TexRef texture5 = new TexRef
				{
					BundlePath = text17,
					RelativeBundlePath = Path.GetFileName(text17),
					PathId = texRef5.PathId,
					AssetFileName = texRef5.AssetFileName,
					Name = texRef5.Name,
					Width = texRef5.Width,
					Height = texRef5.Height,
					Category = texRef5.Category,
					SourceKind = texRef5.SourceKind,
					CardKey = texRef5.CardKey
				};
				modEngine.Replace(texture5, astellarOverFrameComposition3.GamePng, Path.Combine(text16, "backup"));
				byte[] array16 = modEngine.DecodePng(texture5);
				using Image<Rgba32> image18 = SixLabors.ImageSharp.Image.Load<Rgba32>(astellarOverFrameComposition3.GamePng);
				using Image<Rgba32> image19 = SixLabors.ImageSharp.Image.Load<Rgba32>(array16);
				Rgba32[] array17 = new Rgba32[image18.Width * image18.Height];
				Rgba32[] array18 = new Rgba32[image19.Width * image19.Height];
				image18.CopyPixelDataTo(array17);
				image19.CopyPixelDataTo(array18);
				int num46 = ((array17.Length == array18.Length) ? array17.Zip(array18).Count(((Rgba32 First, Rgba32 Second) pair) => pair.First != pair.Second) : Math.Max(array17.Length, array18.Length));
				int num47 = array18.Count((Rgba32 rgba10) => rgba10.A == 0 && (rgba10.R != 0 || rgba10.G != 0 || rgba10.B != 0));
				bool flag28 = first2.SequenceEqual(HashFile(fullPath7));
				bool flag29 = image19.Width == image18.Width && image19.Height == image18.Height && num46 == 0 && num47 == astellarOverFrameComposition3.TransparentEdgePixels && flag28;
				Console.WriteLine($"card={cardId}; temporaryBundle=True; expected={image18.Width}x{image18.Height}; decoded={image19.Width}x{image19.Height}; rgbaMismatches={num46}; hiddenRgb={num47}/{astellarOverFrameComposition3.TransparentEdgePixels}; realBundleUnchanged={flag28}; ready={flag29}");
				if (!flag29)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				string fullPath8 = Path.GetFullPath(text16);
				string value4 = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool", "TextureRoundTripTests")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath8.StartsWith(value4, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath8))
				{
					Directory.Delete(fullPath8, recursive: true);
				}
			}
		}
		if (args.Length == 3 && args[0] == "--test-overframe-editor-current")
		{
			string gameRoot = args[1];
			string cardId2 = args[2];
			if (!PortableIndexService.TryLoadBundled(gameRoot, out GameIndex index4, out frameKey))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef texRef6 = index4.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == cardId2) ?? throw new InvalidDataException("预绑定索引不含卡号 " + cardId2 + "。");
			byte[] array19 = new ModEngine().DecodePng(texRef6);
			string text18 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameEditorTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text18);
			try
			{
				TexRef[] array20 = BuiltInCardFrameCatalog.Load().ToArray();
				string initialFrameKey = CardFrameCatalog.RecommendedKey(CardCatalogService.LoadBestAvailable().Find(cardId2), texRef6.Width, texRef6.Height);
				using OverFrameFrameEditorForm overFrameFrameEditorForm = new OverFrameFrameEditorForm(text18, texRef6, array20, array19, initialFrameKey)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				};
				overFrameFrameEditorForm.Show();
				ModernComboBox modernComboBox3 = (ModernComboBox)(typeof(OverFrameFrameEditorForm).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameFrameEditorForm) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_mode"));
				ModernComboBox modernComboBox4 = (ModernComboBox)(typeof(OverFrameFrameEditorForm).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameFrameEditorForm) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_frames"));
				CropCanvas cropCanvas3 = (CropCanvas)(typeof(OverFrameFrameEditorForm).GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameFrameEditorForm) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_canvas"));
				Label label2 = (Label)(typeof(OverFrameFrameEditorForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameFrameEditorForm) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_status"));
				FieldInfo fieldInfo2 = typeof(OverFrameFrameEditorForm).GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("OverFrameFrameEditorForm", "_outputBytes");
				DateTime dateTime = DateTime.UtcNow.AddSeconds(20.0);
				while (DateTime.UtcNow < dateTime && (!(fieldInfo2.GetValue(overFrameFrameEditorForm) is byte[]) || modernComboBox3.SelectedIndex != 0 || !label2.Text.StartsWith("透明卡框", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(20);
				}
				string[] array21 = new string[4] { "透明卡框", "透明炫彩卡框", "炫彩卡框", "普通卡框" };
				int[] array22 = new int[array21.Length];
				bool flag30 = fieldInfo2.GetValue(overFrameFrameEditorForm) is byte[] && modernComboBox3.SelectedIndex == 0 && label2.Text.StartsWith(array21[0], StringComparison.Ordinal);
				bool flag31 = flag30;
				for (int num48 = 0; num48 < array21.Length; num48++)
				{
					modernComboBox3.SelectedIndex = num48;
					DateTime dateTime2 = DateTime.UtcNow.AddSeconds(20.0);
					while (DateTime.UtcNow < dateTime2 && (!(fieldInfo2.GetValue(overFrameFrameEditorForm) is byte[]) || !label2.Text.StartsWith(array21[num48], StringComparison.Ordinal)))
					{
						Application.DoEvents();
						Thread.Sleep(20);
					}
					array22[num48] = modernComboBox4.Items.Count;
					byte[] array23 = fieldInfo2.GetValue(overFrameFrameEditorForm) as byte[];
					bool flag32 = false;
					if (array23 != null)
					{
						using Image<Rgba32> image20 = SixLabors.ImageSharp.Image.Load<Rgba32>(array23);
						flag32 = image20.Width == 704 && image20.Height == 1024;
					}
					flag31 &= array23 != null && flag32 && modernComboBox4.Items.Count == 16 && label2.Text.StartsWith(array21[num48], StringComparison.Ordinal);
				}
				modernComboBox3.SelectedIndex = 0;
				DateTime dateTime3 = DateTime.UtcNow.AddSeconds(20.0);
				while (DateTime.UtcNow < dateTime3 && (!(fieldInfo2.GetValue(overFrameFrameEditorForm) is byte[]) || !label2.Text.StartsWith(array21[0], StringComparison.Ordinal) || !cropCanvas3.ShowingRenderedPreview))
				{
					Application.DoEvents();
					Thread.Sleep(20);
				}
				flag31 &= fieldInfo2.GetValue(overFrameFrameEditorForm) is byte[] && label2.Text.StartsWith(array21[0], StringComparison.Ordinal) && cropCanvas3.ShowingRenderedPreview;
				string[] source2 = (from button11 in Descendants(overFrameFrameEditorForm).OfType<Button>()
					select button11.Text).ToArray();
				bool flag33 = array19.Length != 0 && cropCanvas3.IsOverFrameEditing && cropCanvas3.HasFrame && cropCanvas3.ShowingRenderedPreview && flag31 && source2.Contains("真实 Alpha 预览") && source2.Contains("构图编辑") && source2.Contains("导出最终 PNG") && array20.Count(BuiltInCardFrameCatalog.IsNormalFrame) == 16 && array20.Count(BuiltInCardFrameCatalog.IsTransparentFrame) == 16 && array20.Count(BuiltInCardFrameCatalog.IsTransparentGradientFrame) == 16 && array20.Count(BuiltInCardFrameCatalog.IsGradientFrame) == 16;
				ImageRenderSpec renderSpec3 = cropCanvas3.RenderSpec;
				Console.WriteLine($"card={cardId2}; source={texRef6.Width}x{texRef6.Height}; decoded={array19.Length}; defaultOverframe={flag30}; modes={string.Join(',', array22)}; status={label2.Text}; directCanvas={cropCanvas3.IsOverFrameEditing}; transform={renderSpec3.ImageScale:0.000}@{renderSpec3.OffsetX:0.0},{renderSpec3.OffsetY:0.0}; gameWrites=False; ready={flag33}");
				string environmentVariable2 = Environment.GetEnvironmentVariable("MDCT_OVERFRAME_CURRENT_SCREENSHOT");
				if (!string.IsNullOrWhiteSpace(environmentVariable2))
				{
					using Bitmap bitmap10 = new Bitmap(overFrameFrameEditorForm.Width, overFrameFrameEditorForm.Height);
					overFrameFrameEditorForm.DrawToBitmap(bitmap10, new System.Drawing.Rectangle(0, 0, bitmap10.Width, bitmap10.Height));
					bitmap10.Save(environmentVariable2, ImageFormat.Png);
				}
				overFrameFrameEditorForm.Close();
				if (!flag33)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				string fullPath9 = Path.GetFullPath(text18);
				string value5 = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameEditorTests")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullPath9.StartsWith(value5, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath9))
				{
					Directory.Delete(fullPath9, recursive: true);
				}
			}
		}
		if (args.Length == 1 && args[0] == "--test-overframe-theme")
		{
			using (OverFrameForm overFrameForm = new OverFrameForm(AppContext.BaseDirectory, null))
			{
				overFrameForm.Size = new System.Drawing.Size(1180, 700);
				overFrameForm.CreateControl();
				overFrameForm.PerformLayout();
				OverFrameMappingTable overFrameMappingTable = (OverFrameMappingTable)(typeof(OverFrameForm).GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm) ?? throw new MissingFieldException("OverFrameForm", "_mappings"));
				overFrameMappingTable.SetMappings(from num86 in Enumerable.Range(1, 80)
					select new OverFrameMapping((ushort)(3000 + num86), (ushort)((num86 % 3 != 0) ? ((uint)(3000 + num86)) : 0u)));
				TextBox textBox = (TextBox)(typeof(OverFrameForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm) ?? throw new MissingFieldException("OverFrameForm", "_cardId"));
				TextBox textBox2 = (TextBox)(typeof(OverFrameForm).GetField("_artId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm) ?? throw new MissingFieldException("OverFrameForm", "_artId"));
				Button[] array24 = Descendants(overFrameForm).OfType<Button>().ToArray();
				bool flag34 = overFrameMappingTable.BackColor == UiTheme.Surface && overFrameMappingTable.ForeColor == UiTheme.Text && overFrameMappingTable.MappingCount == 80 && overFrameMappingTable.HasVerticalScrollIndicator && !overFrameMappingTable.HasHorizontalScrollBar && textBox.BorderStyle == BorderStyle.None && textBox2.BorderStyle == BorderStyle.None && textBox.Parent is RoundedField && textBox2.Parent is RoundedField && array24.Length == 4 && array24.All((Button button11) => button11 is RoundedButton);
				Console.WriteLine($"customTable={overFrameMappingTable.GetType().Name}; list={overFrameMappingTable.BackColor}; rows={overFrameMappingTable.MappingCount}; vertical={overFrameMappingTable.HasVerticalScrollIndicator}; horizontal={overFrameMappingTable.HasHorizontalScrollBar}; roundedFields={textBox.Parent is RoundedField && textBox2.Parent is RoundedField}; roundedButtons={array24.Count((Button button11) => button11 is RoundedButton)}/{array24.Length}; ready={flag34}");
				if (!flag34)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 1 && args[0] == "--test-card-catalog-merge")
		{
			CardCatalogEntry item = new CardCatalogEntry
			{
				CardId = 100,
				SimplifiedChineseName = "旧简中名",
				TraditionalChineseName = "繁中保留",
				JapaneseName = "日本語保持",
				EnglishName = "English Kept",
				Type = "Monster",
				SubType = "Fusion"
			};
			CardCatalogEntry cardCatalogEntry5 = new CardCatalogEntry
			{
				CardId = 100,
				SimplifiedChineseName = "游戏内新名称",
				Type = "Monster",
				SubType = "0x01"
			};
			CardCatalogEntry cardCatalogEntry6 = new CardCatalogEntry
			{
				CardId = 101,
				SimplifiedChineseName = "更新后新卡",
				Type = "Monster"
			};
			IReadOnlyList<CardCatalogEntry> readOnlyList3 = CardCatalogService.MergeGameCatalogs(new _003C_003Ez__ReadOnlySingleElementList<CardCatalogEntry>(item), new _003C_003Ez__ReadOnlyArray<CardCatalogEntry>(new CardCatalogEntry[2] { cardCatalogEntry5, cardCatalogEntry6 }));
			CardCatalogEntry cardCatalogEntry7 = readOnlyList3.Single((CardCatalogEntry entry) => entry.CardId == 100);
			Console.WriteLine($"cards={readOnlyList3.Count}; zh-cn={cardCatalogEntry7.SimplifiedChineseName}; zh-tw={cardCatalogEntry7.TraditionalChineseName}; type={cardCatalogEntry7.SubType}; new={readOnlyList3.Any((CardCatalogEntry entry) => entry.CardId == 101)}");
			if (readOnlyList3.Count != 2 || cardCatalogEntry7.SimplifiedChineseName != "游戏内新名称" || cardCatalogEntry7.TraditionalChineseName != "繁中保留" || cardCatalogEntry7.JapaneseName != "日本語保持" || cardCatalogEntry7.EnglishName != "English Kept" || cardCatalogEntry7.SubType != "Fusion")
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--extract-game-card-catalog")
		{
			IReadOnlyList<CardCatalogEntry> readOnlyList4 = GameCardCatalogUpdater.Extract(args[1], delegate(int done, int total, int found)
			{
				if (done % 250 == 0 || done == total)
				{
					Console.WriteLine($"{done:N0}/{total:N0}; located={found}/9");
				}
			});
			CardCatalogService.Write(args[2], readOnlyList4);
			new CardCatalogService(readOnlyList4);
			int num49 = readOnlyList4.Count((CardCatalogEntry entry) => entry.SimplifiedChineseName.Length > 0);
			int num50 = readOnlyList4.Count((CardCatalogEntry entry) => entry.TraditionalChineseName.Length > 0);
			int num51 = readOnlyList4.Count((CardCatalogEntry entry) => entry.JapaneseName.Length > 0);
			int num52 = readOnlyList4.Count((CardCatalogEntry entry) => entry.EnglishName.Length > 0);
			Console.WriteLine($"cards={readOnlyList4.Count:N0}; zh-cn={num49:N0}; zh-tw={num50:N0}; ja-jp={num51:N0}; en-us={num52:N0}; output={Path.GetFullPath(args[2])}; bytes={new FileInfo(args[2]).Length:N0}");
			if (readOnlyList4.Count < 10000 || Math.Max(Math.Max(num49, num50), Math.Max(num51, num52)) < 10000)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-game-discovery")
		{
			GameInstallation gameInstallation = SteamGameDiscovery.FromPath(args[1]);
			Console.WriteLine($"root={gameInstallation.GameRoot}; build={gameInstallation.BuildId}; profiles={gameInstallation.Profiles.Count}");
			foreach (LocalDataProfile profile in gameInstallation.Profiles)
			{
				Console.WriteLine($"{profile.AccountId}; {profile.RootPath}; {profile.LastWriteTimeUtc:O}");
			}
			if (gameInstallation.Profiles.Count == 0)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-ui-interaction-render")
		{
			string fullPath10 = Path.GetFullPath(args[1]);
			string fullPath11 = Path.GetFullPath(args[2]);
			Directory.CreateDirectory(fullPath11);
			List<string> list = new List<string>();
			bool flag35 = false;
			bool flag36 = false;
			int num53 = int.MaxValue;
			int num54 = int.MaxValue;
			System.Drawing.Point position = Cursor.Position;
			try
			{
				Cursor.Position = new System.Drawing.Point(SystemInformation.VirtualScreen.Left + 2, SystemInformation.VirtualScreen.Top + 2);
				MainForm form3 = new MainForm
				{
					StartPosition = FormStartPosition.Manual,
					Location = new System.Drawing.Point(8, 8),
					Size = new System.Drawing.Size(1504, 912),
					ShowInTaskbar = false,
					TopMost = true
				};
				try
				{
					typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form3, fullPath10);
					typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form3, null);
					typeof(MainForm).GetField("_streamingRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form3, null);
					GetPrivateField<TextBox>(form3, "_gameFolder").Text = fullPath10;
					form3.Show();
					PumpMessagesFor(300);
					IReadOnlyCollection<NavigationButton> privateField = GetPrivateField<IReadOnlyCollection<NavigationButton>>(form3, "_navigationButtons");
					NavigationButton navigationButton = privateField.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Animation);
					NavigationButton navigationButton2 = privateField.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Cards);
					navigationButton.PerformClick();
					bool num55 = PumpMessagesUntil(() => typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3) is MonsterAnimationForm, 15000);
					MonsterAnimationForm monsterAnimationForm = typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3) as MonsterAnimationForm;
					if (num55 && monsterAnimationForm != null)
					{
						Task task = monsterAnimationForm.PreviewCardAsync("3899");
						bool num56 = PumpTask(task, 60000);
						AnimationPreviewCanvas privateField2 = GetPrivateField<AnimationPreviewCanvas>(monsterAnimationForm, "_preview");
						Button privateField3 = GetPrivateField<Button>(monsterAnimationForm, "_play");
						flag35 = num56 && task.IsCompletedSuccessfully && monsterAnimationForm.LocatedCardId == "3899" && monsterAnimationForm.PreviewSourceCardId == "13668" && privateField2.Frame != null;
						if (flag35)
						{
							privateField3.PerformClick();
							PumpMessagesFor(1450);
							privateField3.PerformClick();
							PumpMessagesFor(120);
							if ((bool)(typeof(MonsterAnimationForm).GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm) ?? ((object)false)))
							{
								privateField3.PerformClick();
								PumpMessagesFor(120);
							}
							ExerciseRoundedButtonTransitions(form3);
							form3.Size = new System.Drawing.Size(1440, 860);
							PumpMessagesFor(180);
							form3.Size = new System.Drawing.Size(1504, 800);
							PumpMessagesFor(260);
							navigationButton2.PerformClick();
							PumpMessagesFor(180);
							navigationButton.PerformClick();
							PumpMessagesFor(650);
							if ((bool)(typeof(MonsterAnimationForm).GetField("_playing", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm) ?? ((object)false)))
							{
								privateField3.PerformClick();
								PumpMessagesFor(120);
							}
							privateField3.Focus();
							PumpMessagesFor(350);
							using Bitmap bitmap11 = CaptureClientFromScreen(form3);
							bitmap11.Save(Path.Combine(fullPath11, "animation-3899-after-interactions.png"), ImageFormat.Png);
							form3.PerformLayout();
							form3.Invalidate(invalidateChildren: true);
							form3.Update();
							PumpMessagesFor(80);
							using Bitmap bitmap12 = CaptureClientFromScreen(form3);
							bitmap12.Save(Path.Combine(fullPath11, "animation-3899-after-forced-repaint.png"), ImageFormat.Png);
							num53 = CountDifferentPixels(bitmap11, bitmap12);
						}
					}
					list.Add($"animationReady={flag35}; source={monsterAnimationForm?.PreviewSourceCardId}; repaintDifference={num53}");
					form3.Close();
				}
				finally
				{
					if (form3 != null)
					{
						((IDisposable)form3).Dispose();
					}
				}
				string text19 = Path.Combine(Path.GetTempPath(), "MDCardModTool-ui-interaction-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(text19);
				try
				{
					if (!PortableIndexService.TryLoadBundled(fullPath10, out GameIndex index5, out frameKey))
					{
						throw new InvalidDataException("交互回归无法读取随包卡图索引。");
					}
					TexRef texRef7 = index5.Textures.First((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == "3899");
					byte[] initialArt2 = new ModEngine().DecodePng(texRef7);
					byte[] initialBackground = CreateInteractionBackground();
					TexRef[] array25 = BuiltInCardFrameCatalog.Load().ToArray();
					TexRef texRef8 = array25.First(BuiltInCardFrameCatalog.IsTransparentFrame);
					OverFrameFrameEditorForm editor2 = new OverFrameFrameEditorForm(text19, texRef7, array25, initialArt2, texRef8.Name, initialBackground, replaceStoredBackground: true)
					{
						StartPosition = FormStartPosition.Manual,
						Location = new System.Drawing.Point(8, 8),
						Size = new System.Drawing.Size(1120, 900),
						ShowInTaskbar = false,
						TopMost = true
					};
					try
					{
						editor2.Show();
						bool num57 = PumpMessagesUntil(() => EditorRenderSettled(editor2), 60000);
						ModernComboBox privateField4 = GetPrivateField<ModernComboBox>(editor2, "_mode");
						ModernComboBox privateField5 = GetPrivateField<ModernComboBox>(editor2, "_frames");
						TrackBar privateField6 = GetPrivateField<TrackBar>(editor2, "_zoom");
						RoundedButton privateField7 = GetPrivateField<RoundedButton>(editor2, "_finalPreviewButton");
						RoundedButton privateField8 = GetPrivateField<RoundedButton>(editor2, "_editCanvasButton");
						int value6 = privateField6.Value;
						int selectedIndex = privateField5.SelectedIndex;
						if (num57 && privateField4.Items.Count >= 4)
						{
							int[] array26 = new int[4] { 1, 2, 3, 0 };
							foreach (int selectedIndex2 in array26)
							{
								privateField4.SelectedIndex = selectedIndex2;
								if (!PumpMessagesUntil(() => EditorRenderSettled(editor2), 60000))
								{
									break;
								}
							}
							privateField8.PerformClick();
							privateField6.Value = Math.Clamp(value6 + 25, privateField6.Minimum, privateField6.Maximum);
							PumpMessagesFor(220);
							if (privateField5.Items.Count > 1)
							{
								privateField5.SelectedIndex = (selectedIndex + 1) % privateField5.Items.Count;
								PumpMessagesUntil(() => EditorRenderSettled(editor2), 60000);
								privateField5.SelectedIndex = selectedIndex;
								PumpMessagesUntil(() => EditorRenderSettled(editor2), 60000);
							}
							privateField6.Value = value6;
							privateField7.PerformClick();
							PumpMessagesUntil(() => EditorRenderSettled(editor2), 60000);
							ExerciseRoundedButtonTransitions(editor2);
							editor2.Size = new System.Drawing.Size(1040, 820);
							PumpMessagesFor(180);
							editor2.Size = new System.Drawing.Size(1120, 800);
							PumpMessagesFor(500);
							privateField7.Focus();
							PumpMessagesFor(250);
							flag36 = EditorRenderSettled(editor2);
							using Bitmap bitmap13 = CaptureClientFromScreen(editor2);
							bitmap13.Save(Path.Combine(fullPath11, "overframe-3899-after-interactions.png"), ImageFormat.Png);
							editor2.PerformLayout();
							editor2.Invalidate(invalidateChildren: true);
							editor2.Update();
							PumpMessagesFor(80);
							using Bitmap bitmap14 = CaptureClientFromScreen(editor2);
							bitmap14.Save(Path.Combine(fullPath11, "overframe-3899-after-forced-repaint.png"), ImageFormat.Png);
							num54 = CountDifferentPixels(bitmap13, bitmap14);
						}
						list.Add($"overFrameReady={flag36}; modes={privateField4.Items.Count}; frames={privateField5.Items.Count}; background=True; repaintDifference={num54}");
						editor2.Close();
					}
					finally
					{
						if (editor2 != null)
						{
							((IDisposable)editor2).Dispose();
						}
					}
				}
				finally
				{
					if (Directory.Exists(text19))
					{
						Directory.Delete(text19, recursive: true);
					}
				}
			}
			catch (Exception ex)
			{
				list.Add("error=" + ex);
			}
			finally
			{
				Cursor.Position = position;
			}
			bool flag37 = flag35 && flag36 && num53 <= 64 && num54 <= 64;
			list.Add("ready=" + flag37);
			File.WriteAllLines(Path.Combine(fullPath11, "interaction-report.txt"), list);
			foreach (string item4 in list)
			{
				Console.WriteLine(item4);
			}
			if (!flag37)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-ui-render-layout")
		{
			bool flag38 = string.Equals(Environment.GetEnvironmentVariable("MDCARDMODTOOL_SCREEN_CAPTURE"), "1", StringComparison.Ordinal);
			using MainForm mainForm2 = new MainForm
			{
				StartPosition = FormStartPosition.Manual,
				Location = (flag38 ? new System.Drawing.Point(8, 8) : new System.Drawing.Point(-32000, -32000)),
				Size = new System.Drawing.Size(1504, 912),
				ShowInTaskbar = false,
				TopMost = flag38
			};
			string tempPath = Path.GetTempPath();
			typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm2, tempPath);
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm2, null);
			typeof(MainForm).GetField("_streamingRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm2, null);
			((TextBox)(typeof(MainForm).GetField("_gameFolder", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm2) ?? throw new MissingFieldException("MainForm", "_gameFolder"))).Text = tempPath;
			mainForm2.Show();
			Application.DoEvents();
			IReadOnlyCollection<NavigationButton> source3 = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm2) ?? throw new MissingFieldException("MainForm", "_navigationButtons"));
			source3.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Animation).PerformClick();
			Stopwatch stopwatch = Stopwatch.StartNew();
			MonsterAnimationForm monsterAnimationForm2 = null;
			while (stopwatch.ElapsedMilliseconds < 5000 && monsterAnimationForm2 == null)
			{
				Application.DoEvents();
				monsterAnimationForm2 = typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm2) as MonsterAnimationForm;
				Thread.Sleep(10);
			}
			if (monsterAnimationForm2 == null)
			{
				throw new InvalidOperationException("Embedded animation workspace did not load.");
			}
			mainForm2.Size = new System.Drawing.Size(1460, 880);
			Application.DoEvents();
			mainForm2.Size = new System.Drawing.Size(1504, 912);
			UiTheme.QueueStableRepaint(mainForm2);
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			while (stopwatch2.ElapsedMilliseconds < 350)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Control control = Descendants(monsterAnimationForm2).Single((Control control8) => control8.Name == "MonsterAnimationBannerTitle");
			Control control2 = Descendants(monsterAnimationForm2).Single((Control control8) => control8.Name == "MonsterAnimationBannerSubtitle");
			Button button = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm2) ?? throw new MissingFieldException("MonsterAnimationForm", "_chooseMedia"));
			Button button2 = (Button)(typeof(MonsterAnimationForm).GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm2) ?? throw new MissingFieldException("MonsterAnimationForm", "_play"));
			Button button3 = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm2) ?? throw new MissingFieldException("MonsterAnimationForm", "_apply"));
			Button button4 = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm2) ?? throw new MissingFieldException("MonsterAnimationForm", "_restore"));
			Label label3 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm2) ?? throw new MissingFieldException("MonsterAnimationForm", "_sourceStatus"));
			TableLayoutPanel buttonGrid = (button.Parent as TableLayoutPanel) ?? throw new InvalidOperationException("Animation button grid missing.");
			Button[] source4 = new Button[4] { button, button2, button3, button4 };
			Control parent = control2.Parent;
			bool flag39 = parent != null && control.Parent == parent && control.Bottom <= control2.Top && control2.Bottom <= parent.ClientSize.Height;
			bool flag40 = source4.All((Button button11) => buttonGrid.ClientRectangle.Contains(button11.Bounds)) && label3.Parent == buttonGrid.Parent && label3.Bottom <= buttonGrid.Top;
			bool flag41 = source3.All((NavigationButton navigationButton8) => navigationButton8.Parent != null && navigationButton8.Parent.ClientRectangle.Contains(navigationButton8.Bounds) && navigationButton8.Height >= 40);
			bool flag42 = Descendants(mainForm2).OfType<RoundedButton>().All((RoundedButton roundedButton) => !roundedButton.UsesSharedOptimizedBuffer);
			string fullPath12 = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath12) ?? ".");
			using Bitmap bitmap15 = new Bitmap(mainForm2.ClientSize.Width, mainForm2.ClientSize.Height);
			if (flag38)
			{
				using Graphics graphics4 = Graphics.FromImage(bitmap15);
				graphics4.CopyFromScreen(mainForm2.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, mainForm2.ClientSize, CopyPixelOperation.SourceCopy);
			}
			else
			{
				mainForm2.DrawToBitmap(bitmap15, mainForm2.ClientRectangle);
			}
			bitmap15.Save(fullPath12, ImageFormat.Png);
			bool flag43 = flag39 && flag40 && flag41 && flag42;
			Console.WriteLine($"capture={fullPath12}; banner={flag39}:{control.Bounds}/{control2.Bounds}/{parent?.ClientSize}; actions={flag40}; navigation={flag41}; button-buffers={flag42}; ready={flag43}");
			mainForm2.Close();
			if (!flag43)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-layout")
		{
			MainForm form4 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			try
			{
				form4.Size = new System.Drawing.Size(1180, 760);
				typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form4, null);
				form4.Show();
				Application.DoEvents();
				form4.PerformLayout();
				Button button5 = (Button)(typeof(MainForm).GetField("_visualAssetsButton", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_visualAssetsButton"));
				ComboBox comboBox = (ComboBox)(typeof(MainForm).GetField("_profileSelector", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_profileSelector"));
				ComboBox comboBox2 = (ComboBox)(typeof(MainForm).GetField("_languageSelector", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_languageSelector"));
				Control control3 = (Control)(typeof(MainForm).GetField("_cardSearchField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_cardSearchField"));
				PictureBox pictureBox = (PictureBox)(typeof(MainForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_preview"));
				TableLayoutPanel tableLayoutPanel = (TableLayoutPanel)(typeof(MainForm).GetField("_visualShortcutBar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_visualShortcutBar"));
				IReadOnlyCollection<NavigationButton> readOnlyCollection = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_navigationButtons"));
				Panel panel = (Panel)(typeof(MainForm).GetField("_resourcePage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_resourcePage"));
				Panel panel2 = (Panel)(typeof(MainForm).GetField("_animationPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_animationPage"));
				Panel panel3 = (Panel)(typeof(MainForm).GetField("_animationHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_animationHost"));
				Panel root = (Panel)(typeof(MainForm).GetField("_settingsPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_settingsPage"));
				TableLayoutPanel tableLayoutPanel2 = (TableLayoutPanel)(typeof(MainForm).GetField("_resourceActionGrid", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) ?? throw new MissingFieldException("_resourceActionGrid"));
				string[] source5 = (from button11 in Descendants(panel).OfType<Button>()
					select button11.Text).ToArray();
				string[] source6 = (from button11 in Descendants(panel2).OfType<Button>()
					select button11.Text).ToArray();
				string[] source7 = (from button11 in Descendants(root).OfType<Button>()
					select button11.Text).ToArray();
				bool flag44 = form4.Controls.Cast<Control>().Any((Control control8) => control8.Right > form4.ClientSize.Width || control8.Bottom > form4.ClientSize.Height);
				NavigationButton navigationButton3 = readOnlyCollection.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Animation);
				NavigationButton navigationButton4 = readOnlyCollection.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Cards);
				Stopwatch stopwatch3 = Stopwatch.StartNew();
				navigationButton3.PerformClick();
				stopwatch3.Stop();
				long elapsedMilliseconds = stopwatch3.ElapsedMilliseconds;
				bool flag45 = panel2.Visible && panel3.Controls.OfType<Label>().Any((Label label12) => label12.Text == Localizer.T("page.animation.loading"));
				bool flag46 = !control3.Visible;
				Application.DoEvents();
				bool flag47 = panel2.Visible && !panel.Visible && navigationButton3.Selected;
				MonsterAnimationForm monsterAnimationForm3 = typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4) as MonsterAnimationForm;
				Control[] source8 = ((monsterAnimationForm3 == null) ? Array.Empty<Control>() : Descendants(monsterAnimationForm3).ToArray());
				bool flag48 = monsterAnimationForm3 == null || (source8.OfType<NumericUpDown>().All((NumericUpDown numericUpDown3) => !numericUpDown3.Visible || (numericUpDown3.Width >= 80 && numericUpDown3.Height >= 20)) && source8.OfType<Button>().All((Button button11) => !button11.Visible || button11.Height >= 24));
				navigationButton4.PerformClick();
				Application.DoEvents();
				bool flag49 = panel.Visible && !panel2.Visible && navigationButton4.Selected && control3.Visible;
				stopwatch3.Restart();
				navigationButton3.PerformClick();
				stopwatch3.Stop();
				long elapsedMilliseconds2 = stopwatch3.ElapsedMilliseconds;
				Application.DoEvents();
				bool flag50 = panel2.Visible && navigationButton3.Selected;
				using NavigationButton navigationButton5 = new NavigationButton
				{
					Page = WorkspacePage.Cards,
					Size = new System.Drawing.Size(180, 44)
				};
				navigationButton5.CreateControl();
				navigationButton5.Selected = true;
				(typeof(RoundedButton).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RoundedButton", "BeginTransition")).Invoke(navigationButton5, new object[1] { navigationButton5.HoverColor });
				navigationButton5.Selected = false;
				Stopwatch stopwatch4 = Stopwatch.StartNew();
				while (stopwatch4.ElapsedMilliseconds < 220)
				{
					Application.DoEvents();
					Thread.Sleep(5);
				}
				bool flag51 = navigationButton5.BackColor.ToArgb() == navigationButton5.NormalColor.ToArgb();
				bool flag52 = Application.HighDpiMode == HighDpiMode.PerMonitorV2 && button5.Parent != null && form4.Text.Contains("2.0", StringComparison.Ordinal) && comboBox.Parent != null && comboBox2.Parent != null && comboBox2.Items.Count == 4 && pictureBox is AlphaPreviewBox && readOnlyCollection.Count == 4 && !readOnlyCollection.Any(delegate(NavigationButton navigationButton8)
				{
					WorkspacePage page = navigationButton8.Page;
					return (uint)(page - 3) <= 1u;
				}) && tableLayoutPanel.ColumnCount == 8 && !tableLayoutPanel.AutoScroll && !flag44 && tableLayoutPanel2.ColumnCount == 2 && tableLayoutPanel2.RowCount == 4 && tableLayoutPanel2.Controls.OfType<Button>().Count() == 8 && !tableLayoutPanel2.AutoScroll && source5.Contains("制作超框") && source5.Contains("管理超框登记") && source5.Contains("一键导出全部 Mod") && source5.Contains("预览怪兽动画") && flag47 && flag45 && flag48 && flag49 && flag46 && flag50 && flag51 && elapsedMilliseconds < 750 && elapsedMilliseconds2 < 750 && !source6.Any((string text29) => text29.Contains("运行时扩展", StringComparison.Ordinal)) && !source7.Any((string text29) => text29.Contains("运行时扩展", StringComparison.Ordinal));
				Console.WriteLine($"title={form4.Text}; dpi={Application.HighDpiMode}; alphaPreview={pictureBox is AlphaPreviewBox}; visualButton={button5.Text}; profiles={comboBox.Items.Count}; languages={comboBox2.Items.Count}; navigation={readOnlyCollection.Count}; cardSearchHiddenOnAnimation={flag46}; navigationPaletteStable={flag51}; shortcutColumns={tableLayoutPanel.ColumnCount}; shortcutScroll={tableLayoutPanel.AutoScroll}; resourceGrid={tableLayoutPanel2.ColumnCount}x{tableLayoutPanel2.RowCount}:{tableLayoutPanel2.Controls.Count}; resourceScroll={tableLayoutPanel2.AutoScroll}; animationSwitch={flag47}; animationLoading={flag45}; animationLayout={flag48}; firstSwitchMs={elapsedMilliseconds}; returnSwitchMs={elapsedMilliseconds2}; cardsSwitch={flag49}; cardFrameActions={source5.Count((string text29) => text29.Contains("框", StringComparison.Ordinal))}; runtimeOnAnimation={source6.Any((string text29) => text29.Contains("运行时扩展", StringComparison.Ordinal))}; runtimeInSettings={source7.Any((string text29) => text29.Contains("运行时扩展", StringComparison.Ordinal))}; clipped={flag44}; ready={flag52}");
				form4.Close();
				if (!flag52)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				if (form4 != null)
				{
					((IDisposable)form4).Dispose();
				}
			}
		}
		if (args.Length == 1 && args[0] == "--test-main-form-search")
		{
			using (MainForm mainForm3 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				mainForm3.Show();
				TextBox textBox3 = (TextBox)(typeof(MainForm).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm3) ?? throw new MissingFieldException("_search"));
				ListBox listBox = (ListBox)(typeof(MainForm).GetField("_searchSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm3) ?? throw new MissingFieldException("_searchSuggestions"));
				textBox3.Focus();
				textBox3.Text = "独角";
				textBox3.SelectionStart = textBox3.TextLength;
				Stopwatch stopwatch5 = Stopwatch.StartNew();
				while (stopwatch5.ElapsedMilliseconds < 800 && listBox.Items.Count == 0)
				{
					Application.DoEvents();
					Thread.Sleep(10);
				}
				object obj = listBox.Items.Cast<object>().FirstOrDefault();
				string value7 = obj?.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj)?.ToString() ?? "";
				CardCatalogEntry cardCatalogEntry8 = (from object obj10 in listBox.Items
					select obj10.GetType().GetProperty("Entry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj10) as CardCatalogEntry).FirstOrDefault((CardCatalogEntry entry) => (object)entry != null && entry.CardId == 14338);
				bool flag53 = listBox.Items.Count > 0 && cardCatalogEntry8 != null && textBox3.Focused && textBox3.SelectionStart == textBox3.TextLength;
				Console.WriteLine($"query={textBox3.Text}; suggestions={listBox.Items.Count}; first={value7}; has14338={cardCatalogEntry8 != null}; focused={textBox3.Focused}; caret={textBox3.SelectionStart}; ready={flag53}");
				mainForm3.Close();
				if (!flag53)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 1 && args[0] == "--test-main-form-frames")
		{
			using (MainForm mainForm4 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				mainForm4.Size = new System.Drawing.Size(1180, 760);
				mainForm4.Show();
				Application.DoEvents();
				mainForm4.PerformLayout();
				IReadOnlyCollection<NavigationButton> readOnlyCollection2 = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_navigationButtons"));
				Panel root2 = (Panel)(typeof(MainForm).GetField("_resourcePage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_resourcePage"));
				Control control4 = (Control)(typeof(MainForm).GetField("_cardSearchField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_cardSearchField"));
				Panel panel4 = (Panel)(typeof(MainForm).GetField("_resourceContextBar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_resourceContextBar"));
				FlowLayoutPanel flowLayoutPanel = (FlowLayoutPanel)(typeof(MainForm).GetField("_modContextActions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_modContextActions"));
				FlowLayoutPanel flowLayoutPanel2 = (FlowLayoutPanel)(typeof(MainForm).GetField("_overFrameContextActions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_overFrameContextActions"));
				TreeView treeView = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_groups"));
				List<TexRef> list2 = (List<TexRef>)(typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm4) ?? throw new MissingFieldException("MainForm", "_textures"));
				list2.Clear();
				list2.Add(new TexRef
				{
					BundlePath = "overframe",
					RelativeBundlePath = "overframe",
					Name = "22747",
					CardKey = "22747",
					Width = 704,
					Height = 1024,
					SourceKind = "本地卡图",
					Category = "超框卡图",
					IsModded = true
				});
				string[] array27 = new string[4] { "透明卡框", "透明炫彩卡框", "炫彩卡框", "普通卡框" };
				for (int num58 = 0; num58 < array27.Length; num58++)
				{
					list2.Add(new TexRef
					{
						BundlePath = $"frame-{num58}.png",
						RelativeBundlePath = $"frame-{num58}.png",
						Name = $"diagnostic_frame_{num58}",
						Width = 704,
						Height = 1024,
						SourceKind = "卡框资源",
						Category = array27[num58]
					});
				}
				(typeof(MainForm).GetMethod("RefreshCategories", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("MainForm", "RefreshCategories")).Invoke(mainForm4, null);
				string[] array28 = (from TreeNode node in treeView.Nodes.Cast<TreeNode>().FirstOrDefault((TreeNode node) => node.Nodes.Cast<TreeNode>().Any((TreeNode child) => (child.Tag as string)?.StartsWith("卡框资源|", StringComparison.Ordinal) ?? false))?.Nodes
					select ((node.Tag as string) ?? "").Split('|').Last()).ToArray() ?? Array.Empty<string>();
				TreeNode selectedNode = treeView.Nodes.Cast<TreeNode>().SelectMany((TreeNode node) => node.Nodes.Cast<TreeNode>()).FirstOrDefault((TreeNode node) => string.Equals(node.Tag as string, "本地卡图|超框卡图", StringComparison.Ordinal));
				treeView.SelectedNode = selectedNode;
				Application.DoEvents();
				bool flag54 = panel4.Visible && flowLayoutPanel2.Visible && !flowLayoutPanel.Visible;
				treeView.SelectedNode = treeView.Nodes.Cast<TreeNode>().FirstOrDefault((TreeNode node) => string.Equals(node.Tag as string, "__mods__", StringComparison.Ordinal));
				Application.DoEvents();
				bool flag55 = panel4.Visible && flowLayoutPanel.Visible && !flowLayoutPanel2.Visible;
				bool flag56 = Descendants(root2).OfType<Button>().Any((Button button11) => button11.Text == "管理超框登记") && Descendants(root2).OfType<Button>().Any((Button button11) => button11.Text == "一键导出全部 Mod");
				using OverFrameForm overFrameForm2 = new OverFrameForm(AppContext.BaseDirectory, null);
				overFrameForm2.Size = new System.Drawing.Size(1180, 700);
				overFrameForm2.CreateControl();
				overFrameForm2.PerformLayout();
				OverFrameMappingTable overFrameMappingTable2 = (OverFrameMappingTable)(typeof(OverFrameForm).GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm2) ?? throw new MissingFieldException("OverFrameForm", "_mappings"));
				overFrameMappingTable2.SetMappings(from num86 in Enumerable.Range(1, 90)
					select new OverFrameMapping((ushort)(3000 + num86), (ushort)((num86 % 4 != 0) ? ((uint)(3000 + num86)) : 0u)));
				TextBox textBox4 = (TextBox)(typeof(OverFrameForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm2) ?? throw new MissingFieldException("OverFrameForm", "_cardId"));
				Button[] array29 = Descendants(overFrameForm2).OfType<Button>().ToArray();
				bool flag57 = readOnlyCollection2.Count == 4 && !readOnlyCollection2.Any(delegate(NavigationButton navigationButton8)
				{
					WorkspacePage page = navigationButton8.Page;
					return (uint)(page - 3) <= 1u;
				}) && control4.Visible && flag56 && flag54 && flag55 && overFrameMappingTable2.MappingCount == 90 && array28.SequenceEqual(array27) && overFrameMappingTable2.HasVerticalScrollIndicator && !overFrameMappingTable2.HasHorizontalScrollBar && textBox4.Parent is RoundedField && array29.Length == 4 && array29.All((Button button11) => button11 is RoundedButton);
				Console.WriteLine($"navigation={readOnlyCollection2.Count}; standaloneFrames={readOnlyCollection2.Any((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Frames)}; standaloneMods={readOnlyCollection2.Any((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Mods)}; cardSearch={control4.Visible}; frameCategories={string.Join('>', array28)}; overFrameContext={flag54}; modContext={flag55}; contextActions={flag56}; table={overFrameMappingTable2.GetType().Name}:{overFrameMappingTable2.MappingCount}; vertical={overFrameMappingTable2.HasVerticalScrollIndicator}; horizontal={overFrameMappingTable2.HasHorizontalScrollBar}; ready={flag57}");
				if (!flag57)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-main-form-frames-live-scan")
		{
			using MainForm mainForm5 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm5, null);
			mainForm5.Show();
			Application.DoEvents();
			typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm5, args[1]);
			typeof(MainForm).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(mainForm5, new GameIndex());
			FieldInfo fieldInfo3 = typeof(MainForm).GetField("_backgroundRefreshCancellation", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_backgroundRefreshCancellation");
			FieldInfo? obj2 = typeof(MainForm).GetField("_backgroundRefreshTask", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_backgroundRefreshTask");
			CancellationTokenSource cancellation = new CancellationTokenSource();
			int scanDone = 0;
			int scanTotal = 0;
			Exception scanError = null;
			Task task2 = Task.Run(delegate
			{
				try
				{
					GameCardCatalogUpdater.Extract(args[1], delegate(int done, int total, int _)
					{
						Volatile.Write(ref scanDone, done);
						Volatile.Write(ref scanTotal, total);
					}, cancellation.Token);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex3)
				{
					scanError = ex3;
				}
			});
			fieldInfo3.SetValue(mainForm5, cancellation);
			obj2.SetValue(mainForm5, task2);
			Stopwatch stopwatch6 = Stopwatch.StartNew();
			while (Volatile.Read(in scanDone) < 100 && !task2.IsCompleted && stopwatch6.ElapsedMilliseconds < 20000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			bool flag58 = Volatile.Read(in scanDone) >= 100 && !task2.IsCompleted;
			MethodInfo? obj3 = typeof(MainForm).GetMethod("OpenOverFrameTable", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("MainForm", "OpenOverFrameTable");
			Stopwatch stopwatch7 = Stopwatch.StartNew();
			obj3.Invoke(mainForm5, null);
			stopwatch7.Stop();
			long num59 = 0L;
			Stopwatch stopwatch8 = Stopwatch.StartNew();
			OverFrameForm overFrameForm3 = null;
			while ((!task2.IsCompleted || overFrameForm3 == null) && stopwatch8.ElapsedMilliseconds < 10000)
			{
				Stopwatch stopwatch9 = Stopwatch.StartNew();
				Application.DoEvents();
				stopwatch9.Stop();
				num59 = Math.Max(num59, stopwatch9.ElapsedMilliseconds);
				overFrameForm3 = typeof(MainForm).GetField("_overFrameManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm5) as OverFrameForm;
				Thread.Sleep(10);
			}
			OverFrameMappingTable overFrameMappingTable3 = ((overFrameForm3 == null) ? null : (typeof(OverFrameForm).GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(overFrameForm3) as OverFrameMappingTable));
			bool flag59 = flag58 && scanError == null && cancellation.IsCancellationRequested && task2.IsCompleted && stopwatch7.ElapsedMilliseconds < 250 && num59 < 750 && overFrameForm3 != null && overFrameForm3.Visible && overFrameMappingTable3 != null && !overFrameMappingTable3.HasHorizontalScrollBar;
			Console.WriteLine($"scan={scanDone}/{scanTotal}; active={flag58}; cancelled={cancellation.IsCancellationRequested}; complete={task2.IsCompleted}; openMs={stopwatch7.ElapsedMilliseconds}; maxPumpMs={num59}; manager={overFrameForm3?.Visible}; customTable={overFrameMappingTable3?.GetType().Name}; error={scanError?.Message}; ready={flag59}");
			if (!flag59)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-main-form-frames-live-scan")
		{
			using MainForm mainForm6 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			mainForm6.Size = new System.Drawing.Size(1180, 760);
			mainForm6.Show();
			Application.DoEvents();
			FieldInfo fieldInfo4 = typeof(MainForm).GetField("_backgroundRefreshCancellation", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_backgroundRefreshCancellation");
			FieldInfo fieldInfo5 = typeof(MainForm).GetField("_backgroundRefreshTask", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_backgroundRefreshTask");
			FieldInfo fieldInfo6 = typeof(MainForm).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_index");
			Stopwatch stopwatch10 = Stopwatch.StartNew();
			while ((fieldInfo6.GetValue(mainForm6) == null || fieldInfo5.GetValue(mainForm6) == null) && stopwatch10.ElapsedMilliseconds < 15000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			(fieldInfo4.GetValue(mainForm6) as CancellationTokenSource)?.Cancel();
			Task task3 = fieldInfo5.GetValue(mainForm6) as Task;
			bool flag60 = fieldInfo6.GetValue(mainForm6) != null && task3 != null;
			Stopwatch stopwatch11 = Stopwatch.StartNew();
			while (task3 != null && !task3.IsCompleted && stopwatch11.ElapsedMilliseconds < 10000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			int scanDone2 = 0;
			int scanTotal2 = 0;
			int scanFound = 0;
			Exception scanError2 = null;
			CancellationTokenSource scanCancellation = new CancellationTokenSource();
			Task task4 = Task.Run(delegate
			{
				try
				{
					GameCardCatalogUpdater.Extract(args[1], delegate(int done, int total, int found)
					{
						Volatile.Write(ref scanDone2, done);
						Volatile.Write(ref scanTotal2, total);
						Volatile.Write(ref scanFound, found);
					}, scanCancellation.Token);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex3)
				{
					scanError2 = ex3;
				}
			});
			fieldInfo4.SetValue(mainForm6, scanCancellation);
			fieldInfo5.SetValue(mainForm6, task4);
			long num60 = 0L;
			Stopwatch stopwatch12 = Stopwatch.StartNew();
			while (Volatile.Read(in scanDone2) < 100 && !task4.IsCompleted && stopwatch12.ElapsedMilliseconds < 20000)
			{
				Stopwatch stopwatch13 = Stopwatch.StartNew();
				Application.DoEvents();
				stopwatch13.Stop();
				num60 = Math.Max(num60, stopwatch13.ElapsedMilliseconds);
				Thread.Sleep(10);
			}
			bool flag61 = Volatile.Read(in scanDone2) >= 100 && !task4.IsCompleted;
			IReadOnlyCollection<NavigationButton> source9 = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm6) ?? throw new MissingFieldException("MainForm", "_navigationButtons"));
			Panel panel5 = (Panel)(typeof(MainForm).GetField("_framesPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm6) ?? throw new MissingFieldException("MainForm", "_framesPage"));
			NavigationButton navigationButton6 = source9.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Frames);
			Stopwatch stopwatch14 = Stopwatch.StartNew();
			navigationButton6.PerformClick();
			stopwatch14.Stop();
			bool flag62 = panel5.Visible && panel5.Controls.Cast<Control>().SelectMany(Descendants).OfType<Label>()
				.Any((Label label12) => label12.Text == Localizer.T("page.frames.loading"));
			Stopwatch stopwatch15 = Stopwatch.StartNew();
			OverFrameForm overFrameForm4 = null;
			while (stopwatch15.ElapsedMilliseconds < 5000 && (overFrameForm4 == null || !task4.IsCompleted))
			{
				Stopwatch stopwatch16 = Stopwatch.StartNew();
				Application.DoEvents();
				stopwatch16.Stop();
				num60 = Math.Max(num60, stopwatch16.ElapsedMilliseconds);
				overFrameForm4 = typeof(MainForm).GetField("_embeddedFrames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm6) as OverFrameForm;
				Thread.Sleep(10);
			}
			bool flag63 = flag60 && (task3 == null || task3.IsCompleted) && flag61 && scanError2 == null && scanCancellation.IsCancellationRequested && task4.IsCompleted && stopwatch14.ElapsedMilliseconds < 250 && num60 < 750 && flag62 && panel5.Visible && navigationButton6.Selected && (overFrameForm4?.Visible ?? false);
			Console.WriteLine($"initialReady={flag60}; scan={scanDone2}/{scanTotal2}:{scanFound}; active={flag61}; cancelled={scanCancellation.IsCancellationRequested}; scanComplete={task4.IsCompleted}; switchMs={stopwatch14.ElapsedMilliseconds}; maxPumpMs={num60}; loading={flag62}; embedded={overFrameForm4?.Visible}; error={scanError2?.Message}; ready={flag63}");
			mainForm6.Close();
			if (!flag63)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-frames")
		{
			using MainForm mainForm7 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			mainForm7.Size = new System.Drawing.Size(1180, 760);
			mainForm7.Show();
			Application.DoEvents();
			IReadOnlyCollection<NavigationButton> source10 = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm7) ?? throw new MissingFieldException("MainForm", "_navigationButtons"));
			Panel panel6 = (Panel)(typeof(MainForm).GetField("_framesPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm7) ?? throw new MissingFieldException("MainForm", "_framesPage"));
			FieldInfo fieldInfo7 = typeof(MainForm).GetField("_backgroundRefreshCancellation", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MainForm", "_backgroundRefreshCancellation");
			CancellationTokenSource cancellationTokenSource = fieldInfo7.GetValue(mainForm7) as CancellationTokenSource;
			if (cancellationTokenSource == null)
			{
				cancellationTokenSource = new CancellationTokenSource();
				fieldInfo7.SetValue(mainForm7, cancellationTokenSource);
			}
			NavigationButton navigationButton7 = source10.Single((NavigationButton navigationButton8) => navigationButton8.Page == WorkspacePage.Frames);
			Stopwatch stopwatch17 = Stopwatch.StartNew();
			navigationButton7.PerformClick();
			stopwatch17.Stop();
			bool flag64 = panel6.Visible && panel6.Controls.Cast<Control>().SelectMany(Descendants).OfType<Label>()
				.Any((Label label12) => label12.Text == Localizer.T("page.frames.loading"));
			bool isCancellationRequested = cancellationTokenSource.IsCancellationRequested;
			Application.DoEvents();
			ToolStripStatusLabel obj4 = (ToolStripStatusLabel)(typeof(MainForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm7) ?? throw new MissingFieldException("MainForm", "_status"));
			int statusUpdates = 0;
			obj4.TextChanged += delegate
			{
				statusUpdates++;
			};
			Stopwatch stopwatch18 = Stopwatch.StartNew();
			long num61 = 0L;
			long num62 = 0L;
			long num63 = stopwatch18.ElapsedMilliseconds;
			while (stopwatch18.ElapsedMilliseconds < 2500)
			{
				Stopwatch stopwatch19 = Stopwatch.StartNew();
				Application.DoEvents();
				stopwatch19.Stop();
				num61 = Math.Max(num61, stopwatch19.ElapsedMilliseconds);
				long elapsedMilliseconds3 = stopwatch18.ElapsedMilliseconds;
				num62 = Math.Max(num62, elapsedMilliseconds3 - num63);
				num63 = elapsedMilliseconds3;
				Thread.Sleep(10);
			}
			OverFrameForm embedded = (OverFrameForm)(typeof(MainForm).GetField("_embeddedFrames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm7) ?? throw new MissingFieldException("MainForm", "_embeddedFrames"));
			ListView listView = (ListView)(typeof(OverFrameForm).GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded) ?? throw new MissingFieldException("OverFrameForm", "_mappings"));
			TextBox textBox5 = (TextBox)(typeof(OverFrameForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded) ?? throw new MissingFieldException("OverFrameForm", "_cardId"));
			Button[] array30 = Descendants(embedded).OfType<Button>().ToArray();
			bool flag65 = array30.All((Button button11) => button11.Bottom <= embedded.ClientSize.Height) && textBox5.Parent is RoundedField { Height: var height } && height >= 32 && height <= 60;
			bool flag66 = num61 < 750 && num62 < 1000 && statusUpdates < 80;
			bool flag67 = panel6.Visible && navigationButton7.Selected && embedded.Visible && stopwatch17.ElapsedMilliseconds < 750 && flag64 && isCancellationRequested && flag66 && flag65 && listView.OwnerDraw && listView.BackColor == UiTheme.Surface && array30.Length == 4 && array30.All((Button button11) => button11 is RoundedButton);
			Console.WriteLine($"switchMs={stopwatch17.ElapsedMilliseconds}; maxPumpMs={num61}; maxGapMs={num62}; statusUpdates={statusUpdates}; loadingFrame={flag64}; backgroundPaused={isCancellationRequested}; page={panel6.Visible}; embedded={embedded.Visible}; inputHeight={textBox5.Parent?.Height}; actions={array30.Length}; controlsInside={flag65}; ownerDraw={listView.OwnerDraw}; responsive={flag66}; ready={flag67}");
			mainForm7.Close();
			if (!flag67)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-list")
		{
			using (MainForm mainForm8 = new MainForm())
			{
				mainForm8.CreateControl();
				List<TexRef> list3 = (List<TexRef>)(typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_textures"));
				ListView listView2 = (ListView)(typeof(MainForm).GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_list"));
				TreeView treeView2 = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_groups"));
				ComboBox comboBox3 = (ComboBox)(typeof(MainForm).GetField("_category", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_category"));
				Label label4 = (Label)(typeof(MainForm).GetField("_resultCount", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_resultCount"));
				MethodInfo methodInfo = typeof(MainForm).GetMethod("RefreshCategories", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RefreshCategories");
				MethodInfo methodInfo2 = typeof(MainForm).GetMethod("RenderList", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RenderList");
				MethodInfo methodInfo3 = typeof(MainForm).GetMethod("SelectTexture", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("SelectTexture");
				MethodInfo methodInfo4 = typeof(MainForm).GetMethod("Selected", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("Selected");
				List<TexRef> list4 = (List<TexRef>)(typeof(MainForm).GetField("_visibleTextures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm8) ?? throw new MissingFieldException("_visibleTextures"));
				for (int num64 = 0; num64 < 18000; num64++)
				{
					list3.Add(new TexRef
					{
						BundlePath = $"diagnostic/{num64:D5}",
						RelativeBundlePath = $"diagnostic/{num64:D5}",
						Name = $"Card_{num64:D5}",
						CardKey = (10000 + num64).ToString(),
						Width = 512,
						Height = ((num64 % 2 == 0) ? 512 : 1024),
						SourceKind = "本地卡图",
						Category = ((num64 % 2 == 0) ? "卡图缩略图" : "灵摆卡图")
					});
				}
				Stopwatch stopwatch20 = Stopwatch.StartNew();
				methodInfo.Invoke(mainForm8, null);
				methodInfo2.Invoke(mainForm8, null);
				int count = listView2.Items.Count;
				comboBox3.SelectedIndex = 1;
				int count2 = listView2.Items.Count;
				_ = listView2.Handle;
				TexRef texRef9 = list4[Math.Min(123, list4.Count - 1)];
				methodInfo3.Invoke(mainForm8, new object[1] { texRef9 });
				Application.DoEvents();
				TexRef texRef10 = methodInfo4.Invoke(mainForm8, null) as TexRef;
				bool flag68 = listView2.Items[Math.Min(123, listView2.Items.Count - 1)].Tag == texRef9;
				stopwatch20.Stop();
				bool flag69 = count == 18000 && count2 == 9000 && treeView2.Nodes.Count >= 2 && comboBox3.Items.Count >= 3 && texRef10 == texRef9 && flag68 && !label4.Text.Contains(Localizer.T("list.updating"), StringComparison.Ordinal);
				Console.WriteLine($"all={count}; filtered={count2}; groups={treeView2.Nodes.Count}; categories={comboBox3.Items.Count}; virtualSelection={texRef10 == texRef9}; result={label4.Text}; elapsedMs={stopwatch20.ElapsedMilliseconds}; ready={flag69}");
				if (!flag69)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--scan-visual-assets")
		{
			VisualAssetScanResult visualAssetScanResult = VisualAssetIndexService.Scan(args[1], delegate(int done, int total, int found)
			{
				if (done % 250 == 0 || done == total)
				{
					Console.WriteLine($"{done}/{total}; visual textures={found}");
				}
			});
			foreach (IGrouping<string, TexRef> item5 in from x in visualAssetScanResult.Textures
				group x by x.Category into x
				orderby x.Key
				select x)
			{
				Console.WriteLine($"{item5.Key}={item5.Count()}");
			}
			Console.WriteLine($"catalog={visualAssetScanResult.CatalogEntries}; candidates={visualAssetScanResult.CandidateBundles}; installed={visualAssetScanResult.InstalledBundles}; textures={visualAssetScanResult.Textures.Count}");
			return;
		}
		if (args.Length == 3 && args[0] == "--test-visual-write-suite")
		{
			VisualAssetScanResult visualAssetScanResult2 = VisualAssetIndexService.Scan(args[1]);
			string[] array15 = new string[3] { "ShopBGBase02", "Mat_002_05_BaseColor_near", "WallPaper0001_1" };
			foreach (string name in array15)
			{
				TestVisualTextureWrite(visualAssetScanResult2.Textures.FirstOrDefault((TexRef x) => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("视觉资源目录中找不到 " + name + "。"), Path.Combine(args[2], name));
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-visual-texture-write")
		{
			TestVisualTextureWrite(VisualAssetIndexService.Scan(args[1]).Textures.FirstOrDefault((TexRef x) => string.Equals(x.Name, args[2], StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("视觉资源目录中找不到 " + args[2] + "。"), args[3]);
			return;
		}
		if (args.Length == 3 && args[0] == "--build-animation-index")
		{
			PortableMonsterAnimationIndex portableMonsterAnimationIndex = MonsterAnimationIndexService.Rebuild(args[1], delegate(int done, int total, int found)
			{
				Console.WriteLine($"{done}/{total}; animation assets={found}");
			});
			MonsterAnimationIndexService.Export(args[1], portableMonsterAnimationIndex, args[2]);
			Console.WriteLine($"build={portableMonsterAnimationIndex.GameBuildId}; assets={portableMonsterAnimationIndex.Assets.Count}; cards={portableMonsterAnimationIndex.Assets.Select((MonsterAnimationAssetRef monsterAnimationAssetRef4) => monsterAnimationAssetRef4.CardId).Distinct().Count()}; {args[2]}");
			return;
		}
		if ((args.Length == 2 || args.Length == 3) && args[0] == "--scan-spine42-compat")
		{
			string fullPath13 = Path.GetFullPath(args[1]);
			PortableMonsterAnimationIndex portableMonsterAnimationIndex2 = MonsterAnimationIndexService.EnsureCurrentIndex(fullPath13, delegate(int done, int total, int found)
			{
				if (done % 500 == 0 || done == total)
				{
					Console.WriteLine($"index {done:N0}/{total:N0}; assets={found:N0}");
				}
			});
			string buildId2;
			List<MonsterAnimationAssetRef> source11 = (from asset in MonsterAnimationIndexService.LoadBestAvailable(fullPath13, out buildId2)
				where File.Exists(asset.BundlePath)
				select asset).ToList();
			int result;
			List<MonsterAnimationSet> sets = (from @group in source11.GroupBy<MonsterAnimationAssetRef, string>((MonsterAnimationAssetRef asset) => asset.CardId, StringComparer.Ordinal)
				select new MonsterAnimationSet
				{
					CardId = @group.Key,
					Assets = @group.ToList()
				} into monsterAnimationSet6
				where monsterAnimationSet6.IsComplete
				orderby (!int.TryParse(monsterAnimationSet6.CardId, out result)) ? int.MaxValue : result
				select monsterAnimationSet6).ToList();
			Spine42CompatibilityResult[] probeResults = new Spine42CompatibilityResult[sets.Count];
			int completedProbes = 0;
			Parallel.For(0, sets.Count, new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6)
			}, delegate(int i)
			{
				Spine42CompatibilityResult spine42CompatibilityResult3 = Spine42PreviewRenderer.Probe(sets[i]);
				probeResults[i] = spine42CompatibilityResult3;
				int num86 = Interlocked.Increment(ref completedProbes);
				if (!spine42CompatibilityResult3.Success || spine42CompatibilityResult3.UnsupportedFeatures.Count > 0 || num86 % 25 == 0 || num86 == sets.Count)
				{
					Console.WriteLine($"probe {num86:N0}/{sets.Count:N0}; card={spine42CompatibilityResult3.CardId}; success={spine42CompatibilityResult3.Success}; opaque={spine42CompatibilityResult3.OpaquePixels:N0}; diagnostics={string.Join('|', spine42CompatibilityResult3.UnsupportedFeatures)}; {spine42CompatibilityResult3.Message}");
				}
			});
			List<Spine42CompatibilityResult> list5 = probeResults.ToList();
			if (args.Length == 3)
			{
				string fullPath14 = Path.GetFullPath(args[2]);
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath14));
				File.WriteAllText(fullPath14, JsonSerializer.Serialize(new
				{
					FormatVersion = 1,
					GameBuildId = ((buildId2.Length > 0) ? buildId2 : portableMonsterAnimationIndex2.GameBuildId),
					GeneratedUtc = DateTimeOffset.UtcNow,
					Results = list5
				}, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
			int num65 = list5.Count((Spine42CompatibilityResult result) => !result.Success);
			int value8 = list5.Count((Spine42CompatibilityResult result) => result.Success && result.UnsupportedFeatures.Count > 0);
			int value9 = list5.Count((Spine42CompatibilityResult result) => result.Success && result.OpaquePixels == 0);
			Console.WriteLine($"build={buildId2}; cards={list5.Count:N0}; passed={list5.Count - num65:N0}; failures={num65:N0}; diagnostics={value8:N0}; blank={value9:N0}");
			if (list5.Count == 0 || num65 > 0)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--list-animation-cards")
		{
			foreach (string item6 in MonsterAnimationIndexService.FindInstalledCardIds(args[1]))
			{
				Console.WriteLine(item6);
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-raw-animation-roundtrip")
		{
			MonsterAnimationSet monsterAnimationSet = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationRawAssetService service = new MonsterAnimationRawAssetService();
			RawAnimationManifest rawAnimationManifest = service.ExportAll(monsterAnimationSet, args[3]);
			Dictionary<string, string> source12 = monsterAnimationSet.Assets.Select((MonsterAnimationAssetRef asset) => asset.BundlePath).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToDictionary<string, string, string>((string result) => result, (string path12) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path12))), StringComparer.OrdinalIgnoreCase);
			string text20 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "raw_animation_roundtrip_" + Guid.NewGuid().ToString("N"));
			int num66;
			try
			{
				string text21 = Path.Combine(text20, "Yu-Gi-Oh!  Master Duel");
				string path6 = Path.Combine(text21, "LocalData", "test-profile", "0000");
				List<MonsterAnimationAssetRef> list6 = new List<MonsterAnimationAssetRef>();
				foreach (MonsterAnimationAssetRef asset in monsterAnimationSet.Assets)
				{
					string text22 = Path.Combine(path6, asset.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar));
					Directory.CreateDirectory(Path.GetDirectoryName(text22));
					if (!File.Exists(text22))
					{
						File.Copy(asset.BundlePath, text22);
					}
					list6.Add(new MonsterAnimationAssetRef
					{
						BundlePath = text22,
						RelativeBundlePath = asset.RelativeBundlePath,
						AssetFileName = asset.AssetFileName,
						PathId = asset.PathId,
						Name = asset.Name,
						CardId = asset.CardId,
						Kind = asset.Kind,
						StorageKind = asset.StorageKind
					});
				}
				MonsterAnimationSet set = new MonsterAnimationSet
				{
					CardId = monsterAnimationSet.CardId,
					Assets = list6
				};
				num66 = service.ImportAll(text21, set, args[3]);
			}
			finally
			{
				try
				{
					if (Directory.Exists(text20))
					{
						Directory.Delete(text20, recursive: true);
					}
				}
				catch
				{
				}
			}
			bool flag70 = source12.All((KeyValuePair<string, string> pair) => File.Exists(pair.Key) && string.Equals(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))), StringComparison.Ordinal));
			string[] value10 = monsterAnimationSet.Assets.Select((MonsterAnimationAssetRef asset5) => service.ResolveProfile(asset5).DisplayName).Distinct().ToArray();
			Dictionary<string, int> dictionary = (from rawAnimationManifestEntry in rawAnimationManifest.Files
				group rawAnimationManifestEntry by Path.GetExtension(rawAnimationManifestEntry.FileName).ToLowerInvariant()).ToDictionary((IGrouping<string, RawAnimationManifestEntry> grouping) => grouping.Key, (IGrouping<string, RawAnimationManifestEntry> source21) => source21.Count());
			Console.WriteLine($"complete={monsterAnimationSet.IsComplete}; exported={rawAnimationManifest.Files.Count}; imported={num66}; liveUnchanged={flag70}; profiles={string.Join(" / ", value10)}; files={string.Join(',', dictionary.Select<KeyValuePair<string, int>, string>((KeyValuePair<string, int> keyValuePair) => $"{keyValuePair.Key}:{keyValuePair.Value}"))}");
			if (!monsterAnimationSet.IsComplete || rawAnimationManifest.Files.Count < 6 || num66 != rawAnimationManifest.Files.Count || !flag70 || dictionary.GetValueOrDefault(".png") < 2 || dictionary.GetValueOrDefault(".atlas") < 2 || dictionary.GetValueOrDefault(".json") < 2)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-languages")
		{
			AppLanguage language = Localizer.Language;
			using MonsterAnimationForm monsterAnimationForm4 = new MonsterAnimationForm(args[1], args[2]);
			using MonsterAnimationRawAssetsForm monsterAnimationRawAssetsForm = new MonsterAnimationRawAssetsForm(args[1], args[2]);
			monsterAnimationForm4.CreateControl();
			monsterAnimationRawAssetsForm.CreateControl();
			Dictionary<Control, string> source13 = (Dictionary<Control, string>)(typeof(MonsterAnimationForm).GetField("_localizedControls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm4) ?? throw new MissingFieldException("MonsterAnimationForm", "_localizedControls"));
			Dictionary<Control, string> source14 = (Dictionary<Control, string>)(typeof(MonsterAnimationRawAssetsForm).GetField("_localizedControls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_localizedControls"));
			ComboBox comboBox4 = (ComboBox)(typeof(MonsterAnimationForm).GetField("_frameEdge", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm4) ?? throw new MissingFieldException("MonsterAnimationForm", "_frameEdge"));
			ListView listView3 = (ListView)(typeof(MonsterAnimationRawAssetsForm).GetField("_assets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_assets"));
			string[] source15 = new string[5] { "raw.column.profile", "raw.column.kind", "raw.column.name", "raw.column.pathid", "raw.column.bundle" };
			bool flag71 = true;
			AppLanguage[] values = Enum.GetValues<AppLanguage>();
			foreach (AppLanguage appLanguage in values)
			{
				Localizer.SetLanguage(appLanguage);
				Application.DoEvents();
				bool flag72 = monsterAnimationForm4.Text == Localizer.T("animation.title") && source13.All((KeyValuePair<Control, string> pair) => pair.Key.Text == Localizer.T(pair.Value)) && object.Equals(comboBox4.Items[0], Localizer.T("animation.quality.auto"));
				bool flag73 = monsterAnimationRawAssetsForm.Text == Localizer.F("raw.title", args[2]) && source14.All((KeyValuePair<Control, string> pair) => pair.Key.Text == Localizer.T(pair.Value)) && (from ColumnHeader column in listView3.Columns
					select column.Text).SequenceEqual(source15.Select(Localizer.T));
				Console.WriteLine($"language={appLanguage}; animation={flag72}; raw={flag73}; animationTitle={monsterAnimationForm4.Text}; rawTitle={monsterAnimationRawAssetsForm.Text}");
				flag71 = flag71 && flag72 && flag73;
			}
			Localizer.SetLanguage(language);
			if (!flag71)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-raw-animation-form")
		{
			using (MonsterAnimationRawAssetsForm monsterAnimationRawAssetsForm2 = new MonsterAnimationRawAssetsForm(args[1], args[2])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				monsterAnimationRawAssetsForm2.Show();
				ListView listView4 = (ListView)(typeof(MonsterAnimationRawAssetsForm).GetField("_assets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2));
				PictureBox pictureBox2 = (PictureBox)(typeof(MonsterAnimationRawAssetsForm).GetField("_imagePreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2));
				TextBox textBox6 = (TextBox)(typeof(MonsterAnimationRawAssetsForm).GetField("_textPreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2));
				List<Button> source16 = (List<Button>)(typeof(MonsterAnimationRawAssetsForm).GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_buttons"));
				HashSet<Button> mutationButtons = (HashSet<Button>)(typeof(MonsterAnimationRawAssetsForm).GetField("_mutationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_mutationButtons"));
				Label label5 = (Label)(typeof(MonsterAnimationRawAssetsForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationRawAssetsForm2) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_status"));
				FieldInfo fieldInfo8 = typeof(MonsterAnimationRawAssetsForm).GetField("_set", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_set");
				FieldInfo fieldInfo9 = typeof(MonsterAnimationRawAssetsForm).GetField("_readOnlyEquivalent", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("MonsterAnimationRawAssetsForm", "_readOnlyEquivalent");
				DateTime dateTime4 = DateTime.UtcNow.AddSeconds(60.0);
				while (DateTime.UtcNow < dateTime4 && ((listView4?.Items.Count ?? 0) < 6 || (pictureBox2?.Image == null && string.IsNullOrWhiteSpace(textBox6?.Text))))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				string[] array31 = (from ListViewItem listViewItem in listView4?.Items
					select listViewItem.SubItems[0].Text).Distinct().ToArray() ?? Array.Empty<string>();
				bool flag74 = pictureBox2?.Image != null || !string.IsNullOrWhiteSpace(textBox6?.Text);
				MonsterAnimationSet monsterAnimationSet2 = fieldInfo8.GetValue(monsterAnimationRawAssetsForm2) as MonsterAnimationSet;
				bool flag75 = (bool)(fieldInfo9.GetValue(monsterAnimationRawAssetsForm2) ?? ((object)false));
				bool flag76 = mutationButtons.All((Button button11) => !button11.Enabled);
				bool flag77 = source16.Where((Button item2) => !mutationButtons.Contains(item2)).All((Button button11) => button11.Enabled);
				bool flag78 = args[2] != "3899" || (flag75 && monsterAnimationSet2?.CardId == "13668" && label5.Text.Contains("13668", StringComparison.Ordinal) && flag76 && flag77);
				Console.WriteLine($"items={listView4?.Items.Count}; preview={flag74}; profiles={string.Join(" / ", array31)}; source={monsterAnimationSet2?.CardId}; readOnly={flag75}; mutationDisabled={flag76}; exportEnabled={flag77}; status={label5.Text}");
				bool num67 = (listView4?.Items.Count ?? 0) >= 6 && flag74 && array31.Any((string text29) => text29.Contains("SD", StringComparison.OrdinalIgnoreCase)) && array31.Any((string text29) => text29.Contains("HighEnd_HD", StringComparison.OrdinalIgnoreCase)) && flag78;
				monsterAnimationRawAssetsForm2.Close();
				if (!num67)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-animation-form-sequence")
		{
			using (MonsterAnimationForm monsterAnimationForm5 = new MonsterAnimationForm(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				monsterAnimationForm5.Show();
				string[] obj6 = new string[3] { "3899", "13668", "3899" };
				List<string> list7 = new List<string>();
				bool flag79 = true;
				string[] array15 = obj6;
				foreach (string text23 in array15)
				{
					Task task5 = monsterAnimationForm5.PreviewCardAsync(text23);
					DateTime dateTime5 = DateTime.UtcNow.AddSeconds(45.0);
					while (!task5.IsCompleted && DateTime.UtcNow < dateTime5)
					{
						Application.DoEvents();
						Thread.Sleep(20);
					}
					if (!task5.IsCompleted)
					{
						flag79 = false;
						list7.Add(text23 + ":timeout");
						break;
					}
					task5.GetAwaiter().GetResult();
					Application.DoEvents();
					AnimationPreviewCanvas animationPreviewCanvas = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm5) ?? throw new MissingFieldException("MonsterAnimationForm", "_preview"));
					Label label6 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm5) ?? throw new MissingFieldException("MonsterAnimationForm", "_sourceStatus"));
					bool flag80 = label6.Text.IndexOf('\0') < 0 && label6.Parent is TableLayoutPanel && Descendants(monsterAnimationForm5).OfType<RoundedButton>().All((RoundedButton roundedButton) => roundedButton.Region == null);
					string text24 = ((text23 == "3899") ? "13668" : text23);
					bool flag81 = monsterAnimationForm5.CardQuery == text23 && monsterAnimationForm5.LocatedCardId == text23 && monsterAnimationForm5.PreviewSourceCardId == text24 && animationPreviewCanvas.Frame != null && flag80;
					flag79 = flag79 && flag81;
					list7.Add($"{text23}->{monsterAnimationForm5.PreviewSourceCardId}:frame={animationPreviewCanvas.Frame != null}:clean={flag80}");
				}
				Stopwatch stopwatch21 = Stopwatch.StartNew();
				while (stopwatch21.ElapsedMilliseconds < 750)
				{
					Application.DoEvents();
					Thread.Sleep(15);
				}
				flag79 &= monsterAnimationForm5.CardQuery == "3899" && monsterAnimationForm5.LocatedCardId == "3899" && monsterAnimationForm5.PreviewSourceCardId == "13668";
				Console.WriteLine($"sequence={string.Join(" | ", list7)}; final={monsterAnimationForm5.CardQuery}/{monsterAnimationForm5.LocatedCardId}->{monsterAnimationForm5.PreviewSourceCardId}; ready={flag79}");
				monsterAnimationForm5.Close();
				if (!flag79)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-animation-form")
		{
			using (MonsterAnimationForm monsterAnimationForm6 = new MonsterAnimationForm(args[1]))
			{
				monsterAnimationForm6.Opacity = 0.0;
				monsterAnimationForm6.ShowInTaskbar = false;
				monsterAnimationForm6.Show();
				Application.DoEvents();
				Button[] source17 = Descendants(monsterAnimationForm6).OfType<Button>().ToArray();
				Button button6 = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm6) ?? throw new MissingFieldException("MonsterAnimationForm._chooseMedia"));
				Button control5 = (Button)(typeof(MonsterAnimationForm).GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm6) ?? throw new MissingFieldException("MonsterAnimationForm._play"));
				Button control6 = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm6) ?? throw new MissingFieldException("MonsterAnimationForm._apply"));
				Button control7 = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm6) ?? throw new MissingFieldException("MonsterAnimationForm._restore"));
				Label label7 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm6) ?? throw new MissingFieldException("MonsterAnimationForm._sourceStatus"));
				TableLayoutPanel tableLayoutPanel3 = (button6.Parent as TableLayoutPanel) ?? throw new InvalidOperationException("Animation button grid missing.");
				bool flag82 = tableLayoutPanel3.RowCount == 3 && tableLayoutPanel3.GetColumnSpan(button6) == 2 && tableLayoutPanel3.GetColumnSpan(control5) == 2 && tableLayoutPanel3.GetRow(control6) == 2 && tableLayoutPanel3.GetRow(control7) == 2 && label7.AutoEllipsis && source17.OfType<RoundedButton>().All((RoundedButton roundedButton) => roundedButton.Region == null);
				Console.WriteLine($"shown={monsterAnimationForm6.ClientSize.Width}x{monsterAnimationForm6.ClientSize.Height}; grid={tableLayoutPanel3.ColumnCount}x{tableLayoutPanel3.RowCount}; sourceEllipsis={label7.AutoEllipsis}; rectangularWindows={source17.OfType<RoundedButton>().All((RoundedButton roundedButton) => roundedButton.Region == null)}; ready={flag82}");
				monsterAnimationForm6.Close();
				if (!flag82)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-current")
		{
			using (MonsterAnimationForm monsterAnimationForm7 = new MonsterAnimationForm(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				monsterAnimationForm7.Show();
				Label label8 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				Label label9 = (Label)(typeof(MonsterAnimationForm).GetField("_resourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				Button button7 = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				AnimationPreviewCanvas animationPreviewCanvas2 = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				TextBox obj7 = (TextBox)(typeof(MonsterAnimationForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				obj7.Text = args[2];
				obj7.SelectionStart = obj7.TextLength;
				DateTime dateTime6 = DateTime.UtcNow.AddSeconds(30.0);
				while (DateTime.UtcNow < dateTime6 && animationPreviewCanvas2?.Frame == null && (label8 == null || !label8.Text.Contains("原版多骨骼", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				int num68 = animationPreviewCanvas2?.ScalePercent ?? 0;
				bool num69 = num68 >= 10 && num68 <= 500 && Math.Abs((animationPreviewCanvas2?.AnimationScale ?? 0f) - (float)num68 / 100f) < 0.001f;
				NumericUpDown numericUpDown = (NumericUpDown)(typeof(MonsterAnimationForm).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm7));
				if (numericUpDown != null)
				{
					numericUpDown.Value = 35m;
				}
				Application.DoEvents();
				bool flag83 = animationPreviewCanvas2 != null && animationPreviewCanvas2.ScalePercent == 35 && Math.Abs(animationPreviewCanvas2.AnimationScale - 0.35f) < 0.001f;
				bool flag84 = string.Equals(monsterAnimationForm7.LocatedCardId, args[2], StringComparison.Ordinal);
				bool flag85 = args[2] != "3899" || (string.Equals(monsterAnimationForm7.PreviewSourceCardId, "3899", StringComparison.Ordinal) && (button7?.Enabled ?? false));
				bool num70 = num69 && flag83 && flag84 && flag85 && (animationPreviewCanvas2?.Frame != null || (label8?.Text.Contains("原版多骨骼", StringComparison.Ordinal) ?? false));
				Console.WriteLine($"status={label9?.Text.Replace(Environment.NewLine, " | ")}; source={label8?.Text.Replace(Environment.NewLine, " | ")}; frame={animationPreviewCanvas2?.Frame != null}; located={monsterAnimationForm7.LocatedCardId}; previewSource={monsterAnimationForm7.PreviewSourceCardId}; replaceEnabled={button7?.Enabled}; initialScale={num68}; realtimeScale={animationPreviewCanvas2?.ScalePercent}");
				monsterAnimationForm7.Close();
				if (!num70)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-unsupported")
		{
			using (MonsterAnimationForm monsterAnimationForm8 = new MonsterAnimationForm(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				monsterAnimationForm8.Show();
				TextBox obj8 = (TextBox)(typeof(MonsterAnimationForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm8) ?? throw new MissingFieldException("MonsterAnimationForm._cardId"));
				Label label10 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm8) ?? throw new MissingFieldException("MonsterAnimationForm._sourceStatus"));
				Button button8 = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm8) ?? throw new MissingFieldException("MonsterAnimationForm._chooseMedia"));
				Button button9 = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm8) ?? throw new MissingFieldException("MonsterAnimationForm._apply"));
				Button button10 = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm8) ?? throw new MissingFieldException("MonsterAnimationForm._restore"));
				obj8.Text = args[2];
				obj8.SelectionStart = obj8.TextLength;
				DateTime dateTime7 = DateTime.UtcNow.AddSeconds(30.0);
				string b = Localizer.T("animation.source.unsupported");
				while (DateTime.UtcNow < dateTime7 && (!string.Equals(monsterAnimationForm8.LocatedCardId, args[2], StringComparison.Ordinal) || !string.Equals(label10.Text, b, StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				bool flag86 = string.Equals(monsterAnimationForm8.LocatedCardId, args[2], StringComparison.Ordinal) && string.Equals(label10.Text, b, StringComparison.Ordinal) && !button8.Enabled && !button9.Enabled && !button10.Enabled;
				Console.WriteLine($"card={args[2]}; status={label10.Text}; chooseMedia={button8.Enabled}; apply={button9.Enabled}; restore={button10.Enabled}; ready={flag86}");
				monsterAnimationForm8.Close();
				if (!flag86)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		bool flag87 = args.Length == 4;
		string buildId3;
		if (flag87)
		{
			buildId3 = args[0];
			flag87 = ((buildId3 == "--test-animation-form-media" || buildId3 == "--test-animation-form-chroma") ? true : false);
		}
		if (flag87)
		{
			using (MonsterAnimationForm monsterAnimationForm9 = new MonsterAnimationForm(args[1], args[2])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				monsterAnimationForm9.Show();
				AnimationPreviewCanvas animationPreviewCanvas3 = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm9));
				Label label11 = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm9));
				CheckBox checkBox = (CheckBox)(typeof(MonsterAnimationForm).GetField("_removeGreenScreen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm9));
				if (args[0] == "--test-animation-form-chroma" && checkBox != null)
				{
					checkBox.Checked = true;
				}
				DateTime dateTime8 = DateTime.UtcNow.AddSeconds(30.0);
				while (DateTime.UtcNow < dateTime8 && animationPreviewCanvas3?.Frame == null && (label11 == null || !label11.Text.Contains("原版多骨骼", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				Task task6 = ((Task)(typeof(MonsterAnimationForm).GetMethod("LoadMediaAsync", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("LoadMediaAsync")).Invoke(monsterAnimationForm9, new object[1] { args[3] })) ?? throw new InvalidOperationException("媒体加载任务没有启动。");
				dateTime8 = DateTime.UtcNow.AddSeconds(60.0);
				while (!task6.IsCompleted && DateTime.UtcNow < dateTime8)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				task6.GetAwaiter().GetResult();
				NumericUpDown numericUpDown2 = (NumericUpDown)(typeof(MonsterAnimationForm).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monsterAnimationForm9));
				int num73;
				if (task6.IsCompletedSuccessfully && animationPreviewCanvas3?.Frame != null)
				{
					decimal? num71 = numericUpDown2?.Value;
					decimal num72 = 100m;
					if (((num71.GetValueOrDefault() == num72) & num71.HasValue) && animationPreviewCanvas3.ScalePercent == 100)
					{
						num73 = ((args[0] != "--test-animation-form-chroma" || (label11 != null && label11.Text.Contains("绿幕已透明", StringComparison.Ordinal))) ? 1 : 0);
						goto IL_d481;
					}
				}
				num73 = 0;
				goto IL_d481;
				IL_d481:
				Console.WriteLine($"media={label11?.Text.Replace(Environment.NewLine, " | ")}; frame={animationPreviewCanvas3?.Frame != null}; fullCanvasScale={numericUpDown2?.Value}");
				monsterAnimationForm9.Close();
				if (num73 == 0)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-animation-catalog")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index6, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			HashSet<string> ids = MonsterAnimationIndexService.LoadBundledCardIds();
			TexRef[] array32 = index6.Textures.Where((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && ids.Contains(texRef20.CardKey)).ToArray();
			Console.WriteLine($"ids={ids.Count}; taggedTextures={array32.Length}; distinctCards={array32.Select((TexRef texRef20) => texRef20.CardKey).Distinct().Count()}");
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form")
		{
			using (MainForm mainForm9 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				mainForm9.Show();
				TreeView treeView3 = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainForm9));
				DateTime dateTime9 = DateTime.UtcNow.AddSeconds(30.0);
				TreeNode treeNode = null;
				while (DateTime.UtcNow < dateTime9 && treeNode == null)
				{
					Application.DoEvents();
					treeNode = treeView3?.Nodes.Cast<TreeNode>().SelectMany((TreeNode treeNode2) => treeNode2.Nodes.Cast<TreeNode>()).FirstOrDefault((TreeNode treeNode2) => string.Equals(treeNode2.Tag as string, "local-card|animation", StringComparison.Ordinal));
					if (treeNode == null)
					{
						Thread.Sleep(25);
					}
				}
				Console.WriteLine((treeNode == null) ? "animationCategory=missing" : ("animationCategory=" + treeNode.Text));
				mainForm9.Close();
				if (treeNode == null)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--inspect-animation-card")
		{
			MonsterAnimationSet monsterAnimationSet3 = MonsterAnimationIndexService.Find(args[1], args[2]);
			Console.WriteLine($"card={monsterAnimationSet3.CardId}; complete={monsterAnimationSet3.IsComplete}; {monsterAnimationSet3.CountSummary}");
			{
				foreach (MonsterAnimationAssetRef asset2 in monsterAnimationSet3.Assets)
				{
					Console.WriteLine($"{asset2.Kind}; {asset2.Name}; PathID={asset2.PathId}; {asset2.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 4 && args[0] == "--dump-animation-card")
		{
			MonsterAnimationSet monsterAnimationSet4 = MonsterAnimationIndexService.Find(args[1], args[2]);
			Directory.CreateDirectory(args[3]);
			ModEngine modEngine2 = new ModEngine();
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + "JS.json"), modEngine2.ReadTextAsset(monsterAnimationSet4.Skeletons[0]).Data);
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + ".atlas"), modEngine2.ReadTextAsset(monsterAnimationSet4.Atlases[0]).Data);
			string root3 = ((monsterAnimationSet4.Textures[0].StorageKind == "StreamingAssets") ? IndexService.StreamingRoot(args[1]) : IndexService.FindLocalRoot(args[1]));
			foreach (TexRef texture6 in modEngine2.ScanBundle(monsterAnimationSet4.Textures[0].BundlePath, root3, monsterAnimationSet4.Textures[0].ModSourceKind, includeDependencies: false).Textures)
			{
				File.WriteAllBytes(Path.Combine(args[3], texture6.Name + ".png"), modEngine2.DecodePng(texture6));
			}
			Console.WriteLine(args[3]);
			return;
		}
		if (args.Length == 4 && args[0] == "--test-current-animation-preview")
		{
			using (CurrentMonsterAnimationPreview currentMonsterAnimationPreview = MonsterAnimationCurrentPreview.TryLoad(MonsterAnimationIndexService.Find(args[1], args[2])) ?? throw new InvalidDataException("当前动画不是可逐帧还原的单槽序列动画。"))
			{
				Directory.CreateDirectory(args[3]);
				currentMonsterAnimationPreview.Frames[0].Save(Path.Combine(args[3], "frame-0001.png"));
				Console.WriteLine($"frames={currentMonsterAnimationPreview.Frames.Count}; fps={currentMonsterAnimationPreview.FramesPerSecond}; animation={currentMonsterAnimationPreview.AnimationName}");
				return;
			}
		}
		if (args.Length == 4 && args[0] == "--test-spine42-preview")
		{
			using (CurrentMonsterAnimationPreview currentMonsterAnimationPreview2 = Spine42PreviewRenderer.TryLoad(MonsterAnimationIndexService.Find(args[1], args[2])) ?? throw new InvalidDataException("Spine 4.2 preview could not be rendered."))
			{
				Directory.CreateDirectory(args[3]);
				int[] array33 = new int[3]
				{
					0,
					currentMonsterAnimationPreview2.Frames.Count / 2,
					currentMonsterAnimationPreview2.Frames.Count - 1
				};
				for (int num74 = 0; num74 < array33.Length; num74++)
				{
					currentMonsterAnimationPreview2.Frames[array33[num74]].Save(Path.Combine(args[3], $"spine42-{num74 + 1}.png"));
				}
				Console.WriteLine($"frames={currentMonsterAnimationPreview2.Frames.Count}; fps={currentMonsterAnimationPreview2.FramesPerSecond}; animation={currentMonsterAnimationPreview2.AnimationName}; output={Path.GetFullPath(args[3])}");
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--diagnose-spine42")
		{
			Console.WriteLine(Spine42PreviewRenderer.Diagnose(MonsterAnimationIndexService.Find(args[1], args[2])));
			return;
		}
		if (args.Length == 3 && args[0] == "--probe-spine42")
		{
			Spine42CompatibilityResult spine42CompatibilityResult2 = Spine42PreviewRenderer.Probe(MonsterAnimationIndexService.Find(args[1], args[2]));
			Console.WriteLine(JsonSerializer.Serialize(spine42CompatibilityResult2));
			if (!spine42CompatibilityResult2.Success)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-pairing")
		{
			IReadOnlyList<MonsterAnimationAssetTriplet> readOnlyList5 = MonsterAnimationAssetPairing.FindComplete(MonsterAnimationIndexService.Find(args[1], args[2]));
			foreach (MonsterAnimationAssetTriplet item7 in readOnlyList5)
			{
				Console.WriteLine($"{item7.Key}; texture={item7.Texture.RelativeBundlePath}; atlas={item7.Atlas.RelativeBundlePath}; skeleton={item7.Skeleton.RelativeBundlePath}");
			}
			if (!readOnlyList5.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "HighEnd_HD") || !readOnlyList5.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "SD"))
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-animation-bundle")
		{
			foreach (MonsterAnimationAssetRef item8 in new ModEngine().ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])))
			{
				Console.WriteLine($"{item8.Kind}; {item8.Name}; PathID={item8.PathId}; {item8.RelativeBundlePath}");
				if (item8.Kind == MonsterAnimationAssetKind.Texture)
				{
					AnimationTextureMetadata animationTextureMetadata = new ModEngine().ReadAnimationTextureMetadata(item8);
					Console.WriteLine($"texture={animationTextureMetadata.Width}x{animationTextureMetadata.Height}; format={animationTextureMetadata.TextureFormat}; colorSpace={animationTextureMetadata.ColorSpace}; mip={animationTextureMetadata.MipCount}; complete={animationTextureMetadata.CompleteImageSize}; inline={animationTextureMetadata.InlineDataSize}; stream={animationTextureMetadata.StreamSize}:{animationTextureMetadata.StreamPath}");
				}
				else
				{
					byte[] data = new ModEngine().ReadTextAsset(item8).Data;
					string text25 = Encoding.UTF8.GetString(data).TrimEnd('\0');
					Console.WriteLine(text25.Substring(0, Math.Min(text25.Length, 3000)));
				}
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--dump-animation-text")
		{
			ModEngine modEngine3 = new ModEngine();
			MonsterAnimationAssetRef monsterAnimationAssetRef = modEngine3.ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).First((MonsterAnimationAssetRef monsterAnimationAssetRef4) => monsterAnimationAssetRef4.Kind != MonsterAnimationAssetKind.Texture);
			File.WriteAllBytes(Path.GetFullPath(args[3]), modEngine3.ReadTextAsset(monsterAnimationAssetRef).Data);
			Console.WriteLine($"{monsterAnimationAssetRef.Kind}; {monsterAnimationAssetRef.Name}; {new FileInfo(args[3]).Length} bytes; {Path.GetFullPath(args[3])}");
			return;
		}
		if (args.Length == 2 && args[0] == "--bundle-containers")
		{
			foreach (string item9 in new ModEngine().ReadAssetBundleContainerPaths(Path.GetFullPath(args[1])))
			{
				Console.WriteLine(item9);
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-media")
		{
			Directory.CreateDirectory(args[3]);
			Console.WriteLine("extracting");
			using ExtractedAnimation extractedAnimation = MonsterAnimationMedia.ExtractAsync(args[1], 12, 48, 256).GetAwaiter().GetResult();
			Console.WriteLine($"extracted {extractedAnimation.FramePaths.Count}");
			using MonsterAnimationBuildResult monsterAnimationBuildResult = MonsterAnimationBuilder.Build(extractedAnimation.FramePaths, args[2], 12, 100);
			Console.WriteLine($"built {monsterAnimationBuildResult.AtlasWidth}x{monsterAnimationBuildResult.AtlasHeight}");
			monsterAnimationBuildResult.AtlasImage.SaveAsPng(Path.Combine(args[3], "P" + args[2] + ".png"));
			File.WriteAllText(Path.Combine(args[3], "P" + args[2] + ".atlas.txt"), monsterAnimationBuildResult.AtlasText);
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + "JS.json"), monsterAnimationBuildResult.SkeletonJson);
			Console.WriteLine("encoding bc7");
			AnimationAtlasTextureData animationAtlasTextureData = new ModEngine().EncodeAnimationAtlas(monsterAnimationBuildResult.AtlasImage);
			Console.WriteLine($"frames={monsterAnimationBuildResult.FrameCount}; fps={monsterAnimationBuildResult.FramesPerSecond}; atlas={monsterAnimationBuildResult.AtlasWidth}x{monsterAnimationBuildResult.AtlasHeight}; bc7={animationAtlasTextureData.Data.Length}");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-hd-media")
		{
			Directory.CreateDirectory(args[3]);
			using ExtractedAnimation extractedAnimation2 = MonsterAnimationMedia.ExtractAsync(args[1], 15, 28, 1920).GetAwaiter().GetResult();
			using Bitmap bitmap16 = extractedAnimation2.LoadFrame(0);
			using MonsterAnimationBuildResult monsterAnimationBuildResult2 = MonsterAnimationBuilder.Build(extractedAnimation2.FramePaths, args[2], 15, 100);
			AnimationAtlasTextureData animationAtlasTextureData2 = new ModEngine().EncodeAnimationAtlas(monsterAnimationBuildResult2.AtlasImage);
			Console.WriteLine($"frames={monsterAnimationBuildResult2.FrameCount}; frame={bitmap16.Width}x{bitmap16.Height}; atlas={monsterAnimationBuildResult2.AtlasWidth}x{monsterAnimationBuildResult2.AtlasHeight}; bc7={animationAtlasTextureData2.Data.Length}");
			return;
		}
		if (args.Length == 1 && args[0] == "--test-animation-quality-plan")
		{
			int num75 = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(22, 16, 9, 8192);
			int num76 = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(60, 16, 9, 8192);
			int num77 = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(180, 16, 9, 8192);
			Console.WriteLine($"22frames={num75}; 60frames={num76}; 180frames={num77}");
			if (num75 != 1920 || num76 != 1280 || num77 != 768)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-chroma-key")
		{
			using (ExtractedAnimation extractedAnimation3 = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512, 0.0, removeGreenScreen: true).GetAwaiter().GetResult())
			{
				using ExtractedAnimation extractedAnimation4 = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512).GetAwaiter().GetResult();
				using Bitmap bitmap17 = extractedAnimation3.LoadFrame(0);
				using Bitmap bitmap18 = extractedAnimation4.LoadFrame(0);
				System.Drawing.Color pixel5 = bitmap17.GetPixel(8, 8);
				System.Drawing.Color pixel6 = bitmap17.GetPixel(bitmap17.Width / 2, bitmap17.Height / 2);
				System.Drawing.Color pixel7 = bitmap18.GetPixel(8, 8);
				bitmap17.Save(args[2]);
				Console.WriteLine($"keyedBackground={pixel5}; keyedSubject={pixel6}; plainBackground={pixel7}; saved={args[2]}");
				if (!extractedAnimation3.GreenScreenRemoved || pixel5.A > 16 || pixel6.A < 240 || pixel7.A < 240)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 5 && args[0] == "--test-animation-texture")
		{
			ModEngine modEngine4 = new ModEngine();
			MonsterAnimationAssetRef monsterAnimationAssetRef2 = modEngine4.ScanAnimationAssetsFast(args[1], args[2]).First((MonsterAnimationAssetRef monsterAnimationAssetRef4) => monsterAnimationAssetRef4.Kind == MonsterAnimationAssetKind.Texture);
			using Image<Rgba32> image21 = SixLabors.ImageSharp.Image.Load<Rgba32>(args[3]);
			modEngine4.ReplaceAnimationAtlas(monsterAnimationAssetRef2, image21, Path.Combine(args[2], "backup"));
			File.WriteAllBytes(args[4], modEngine4.DecodePng(monsterAnimationAssetRef2.AsTexture()));
			Console.WriteLine($"roundtrip={image21.Width}x{image21.Height}; {new FileInfo(args[1]).Length} bytes");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-apply")
		{
			MonsterAnimationSet monsterAnimationSet5 = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationService monsterAnimationService = new MonsterAnimationService();
			MonsterAnimationTemplate template = monsterAnimationService.ReadTemplate(args[1], monsterAnimationSet5);
			using ExtractedAnimation extractedAnimation5 = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 128).GetAwaiter().GetResult();
			using MonsterAnimationBuildResult animation = MonsterAnimationBuilder.Build(extractedAnimation5.FramePaths, args[2], 12, 100, template);
			monsterAnimationService.Apply(args[1], monsterAnimationSet5, animation);
			ModEngine engine4 = new ModEngine();
			IEnumerable<string> values2 = monsterAnimationSet5.Textures.Select(delegate(MonsterAnimationAssetRef monsterAnimationAssetRef4)
			{
				using SixLabors.ImageSharp.Image image22 = SixLabors.ImageSharp.Image.Load(engine4.DecodePng(monsterAnimationAssetRef4.AsTexture()));
				return $"{image22.Width}x{image22.Height}";
			});
			IEnumerable<int> values3 = from asset5 in monsterAnimationSet5.Atlases.Concat(monsterAnimationSet5.Skeletons)
				select engine4.ReadTextAsset(asset5).Data.Length;
			Console.WriteLine($"complete={monsterAnimationSet5.IsComplete}; textures={string.Join(',', values2)}; textBytes={string.Join(',', values3)}");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-build-profile")
		{
			MonsterAnimationSet set2 = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationTemplate monsterAnimationTemplate = new MonsterAnimationService().ReadTemplate(args[1], set2);
			ExtractedAnimation media4 = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 256).GetAwaiter().GetResult();
			try
			{
				using MonsterAnimationBuildResult monsterAnimationBuildResult3 = MonsterAnimationBuilder.Build(media4.FramePaths, args[2], 12, 100, monsterAnimationTemplate);
				using MonsterAnimationBuildResult monsterAnimationBuildResult4 = MonsterAnimationBuilder.Build(media4.FramePaths, args[2], 12, 35, monsterAnimationTemplate);
				using JsonDocument jsonDocument = JsonDocument.Parse(monsterAnimationBuildResult3.SkeletonJson);
				string[] array34 = jsonDocument.RootElement.GetProperty("animations").EnumerateObject().Select(delegate(JsonProperty jsonProperty)
				{
					JsonProperty jsonProperty2 = jsonProperty;
					return jsonProperty2.Name;
				})
					.ToArray();
				int[] array35 = jsonDocument.RootElement.GetProperty("animations").EnumerateObject().Select(delegate(JsonProperty jsonProperty2)
				{
					JsonProperty jsonProperty = jsonProperty2;
					return jsonProperty.Value.GetProperty("slots").EnumerateObject().First()
						.Value.GetProperty("attachment").GetArrayLength();
				})
					.ToArray();
				bool flag88 = array35.All((int num86) => num86 == media4.FramePaths.Count);
				Console.WriteLine($"display100={monsterAnimationBuildResult3.DisplayWidth:0.##}x{monsterAnimationBuildResult3.DisplayHeight:0.##}; display35={monsterAnimationBuildResult4.DisplayWidth:0.##}x{monsterAnimationBuildResult4.DisplayHeight:0.##}; template={string.Join(',', monsterAnimationTemplate.EffectiveAnimationNames)}; generated={string.Join(',', array34)}; timelines={string.Join(',', array35)}");
				if (Math.Abs(monsterAnimationBuildResult3.DisplayWidth - 6720.0) > 0.1 || Math.Abs(monsterAnimationBuildResult3.DisplayHeight - 3780.0) > 0.1 || Math.Abs(monsterAnimationBuildResult4.DisplayWidth - 2352.0) > 0.1 || Math.Abs(monsterAnimationBuildResult4.DisplayHeight - 1323.0) > 0.1 || !monsterAnimationTemplate.EffectiveAnimationNames.SequenceEqual(array34) || !flag88)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
			finally
			{
				if (media4 != null)
				{
					((IDisposable)media4).Dispose();
				}
			}
		}
		if (args.Length == 3 && args[0] == "--test-animation-restore")
		{
			MonsterAnimationSet set3 = MonsterAnimationIndexService.Find(args[1], args[2]);
			int value11 = new MonsterAnimationService().Restore(args[1], set3);
			Console.WriteLine($"restored={value11}");
			return;
		}
		if (args.Length == 2 && args[0] == "--build-index")
		{
			IndexService.BuildAndSave(args[1], delegate(int done, int total, int found)
			{
				Console.WriteLine($"{done}/{total}; textures={found}");
			});
			return;
		}
		if (args.Length == 3 && args[0] == "--scan-card")
		{
			string path7 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex gameIndex = (File.Exists(path7) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path7)) ?? new GameIndex()) : new GameIndex());
			MissingCardScanResult missingCardScanResult = IndexService.ScanMissingLocalCard(args[1], gameIndex, args[2], delegate(int done, int total, int added)
			{
				Console.WriteLine($"{done}/{total}; added={added}");
			});
			HashSet<string> known = gameIndex.Textures.Select((TexRef texRef20) => $"{texRef20.BundlePath}\0{texRef20.AssetFileName}\0{texRef20.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			gameIndex.Textures.AddRange(missingCardScanResult.Textures.Where((TexRef texRef20) => known.Add($"{texRef20.BundlePath}\0{texRef20.AssetFileName}\0{texRef20.PathId}")));
			IndexService.Save(args[1], gameIndex);
			{
				foreach (TexRef item10 in gameIndex.Textures.Where((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == args[2]))
				{
					Console.WriteLine($"FOUND {item10.Name}; {item10.Width}x{item10.Height}; {item10.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--enrich-local-card-index")
		{
			string path8 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex gameIndex2 = (File.Exists(path8) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path8)) ?? new GameIndex()) : new GameIndex());
			MissingCardScanResult missingCardScanResult2 = IndexService.ScanMissingLocalCard(args[1], gameIndex2, "0", delegate(int done, int total, int added)
			{
				Console.WriteLine($"{done}/{total}; added={added}");
			});
			HashSet<string> known2 = gameIndex2.Textures.Select((TexRef texRef20) => $"{texRef20.BundlePath}\0{texRef20.AssetFileName}\0{texRef20.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<TexRef> list8 = missingCardScanResult2.Textures.Where((TexRef texRef20) => known2.Add($"{texRef20.BundlePath}\0{texRef20.AssetFileName}\0{texRef20.PathId}")).ToList();
			YgoCdbCardCatalog.ClassifyTexturesAsync(list8).GetAwaiter().GetResult();
			gameIndex2.Textures.AddRange(list8);
			IndexService.Save(args[1], gameIndex2);
			Console.WriteLine($"added={list8.Count}; total={gameIndex2.Textures.Count}");
			return;
		}
		if (args.Length == 2 && args[0] == "--sanitize-card-index")
		{
			string path9 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex gameIndex3 = (File.Exists(path9) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path9)) ?? new GameIndex()) : new GameIndex());
			int num78 = IndexService.RemoveSpineAtlasParts(gameIndex3);
			num78 += IndexService.RemoveNonCardLocalTextures(gameIndex3);
			gameIndex3.AlternateArtIndexVersion = 0;
			YgoCdbCardCatalog.ClassifyAlternateArtsAsync(gameIndex3).GetAwaiter().GetResult();
			IndexService.Save(args[1], gameIndex3);
			Console.WriteLine($"removed={num78}; total={gameIndex3.Textures.Count}");
			return;
		}
		if (args.Length == 2 && args[0] == "--find-card-frame")
		{
			string text26 = args[1];
			string[] array15 = new string[2]
			{
				Path.Combine(text26, "masterduel_Data", "data.unity3d"),
				IndexService.StreamingRoot(text26)
			};
			foreach (string text27 in array15)
			{
				IEnumerable<string> enumerable = (File.Exists(text27) ? new string[1] { text27 } : (Directory.Exists(text27) ? Directory.EnumerateFiles(text27, "*", SearchOption.AllDirectories) : Array.Empty<string>()));
				foreach (string item11 in enumerable)
				{
					try
					{
						foreach (TexRef item12 in from texRef20 in new ModEngine().ListTextures(item11, text26, "游戏内图片")
							where texRef20.Name.Contains("card", StringComparison.OrdinalIgnoreCase) && texRef20.Name.Contains("frame", StringComparison.OrdinalIgnoreCase)
							select texRef20)
						{
							Console.WriteLine($"{item12.Name}\t{item12.Width}x{item12.Height}\t{item12.RelativeBundlePath}\tPathID={item12.PathId}");
						}
					}
					catch
					{
					}
				}
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--add-card-frames")
		{
			IndexService.AddCardFramesAndSave(args[1]);
			return;
		}
		if (args.Length == 3 && (args[0] == "--export-mods" || args[0] == "--export-mods-direct"))
		{
			string path10 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex gameIndex4 = (File.Exists(path10) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(path10)) ?? new GameIndex()) : new GameIndex());
			ModPackageInfo modPackageInfo = new ModPackageService().Export(args[1], gameIndex4.Textures, args[2], args[0] == "--export-mods-direct");
			Console.WriteLine($"{modPackageInfo.BundleCount} bundles; {modPackageInfo.TotalSize} bytes; {args[2]}");
			return;
		}
		if (args.Length == 2 && args[0] == "--inspect-mod")
		{
			ModPackageInfo modPackageInfo2 = new ModPackageService().Inspect(args[1]);
			Console.WriteLine($"{modPackageInfo2.Name}; {modPackageInfo2.BundleCount} bundles; {modPackageInfo2.TotalSize} bytes");
			return;
		}
		if (args.Length == 3 && args[0] == "--import-mods")
		{
			ModImportResult modImportResult = new ModPackageService().Import(args[1], args[2]);
			Console.WriteLine($"{modImportResult.BundleCount} bundles imported");
			return;
		}
		if (args.Length == 4 && args[0] == "--export-card")
		{
			TexRef texRef11 = (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1])))) ?? new GameIndex()).Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == args[2]) ?? throw new FileNotFoundException("索引中没有卡号 " + args[2] + "。");
			byte[] array36 = new ModEngine().DecodePng(texRef11);
			string preferredFrameKey = CardFrameCatalog.RecommendedKey(CardCatalogService.LoadBestAvailable().Find(texRef11.CardKey), texRef11.Width, texRef11.Height);
			GameTextureDisplayMapping gameTextureDisplayMapping5 = GameTextureDisplayMapping.Resolve(texRef11, preferredFrameKey);
			byte[] bytes = (gameTextureDisplayMapping5.RequiresMapping ? gameTextureDisplayMapping5.DecodeForDisplay(array36) : array36);
			File.WriteAllBytes(args[3], bytes);
			Console.WriteLine(args[3] + "; " + gameTextureDisplayMapping5.EditorSummary);
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-bundle")
		{
			string fullPath15 = Path.GetFullPath(args[1]);
			string fullPath16 = Path.GetFullPath(args[2]);
			{
				foreach (TexRef texture7 in new ModEngine().ScanBundle(fullPath15, fullPath16, "诊断", includeDependencies: false).Textures)
				{
					Console.WriteLine($"{texture7.Name}; {texture7.Width}x{texture7.Height}; PathID={texture7.PathId}; file={texture7.AssetFileName}; {texture7.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--export-portable-index")
		{
			GameIndex index7 = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1])))) ?? throw new InvalidDataException("本地索引无法读取。");
			PortableIndexService.Export(args[1], index7, args[2]);
			PortableGameIndex portableGameIndex = PortableIndexService.Read(args[2]);
			Console.WriteLine($"build={portableGameIndex.GameBuildId}; textures={portableGameIndex.Textures.Count}; bytes={new FileInfo(args[2]).Length}");
			return;
		}
		if (args.Length == 2 && args[0] == "--inspect-portable-index")
		{
			PortableGameIndex portableGameIndex2 = PortableIndexService.Read(args[1]);
			Console.WriteLine($"format={portableGameIndex2.FormatVersion}; build={portableGameIndex2.GameBuildId}; textures={portableGameIndex2.Textures.Count}; alternateVersion={portableGameIndex2.AlternateArtIndexVersion}");
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-portable-card")
		{
			foreach (PortableTextureEntry item13 in PortableIndexService.Read(args[1]).Textures.Where((PortableTextureEntry portableTextureEntry) => portableTextureEntry.CardKey == args[2]))
			{
				Console.WriteLine($"{item13.CardKey}; {item13.Width}x{item13.Height}; {item13.Category}; {item13.RelativeBundlePath}");
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-prebuilt-index")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index8, out string buildId4))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef texRef12 = index8.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图");
			Console.WriteLine($"build={buildId4}; textures={index8.Textures.Count}; first={texRef12?.BundlePath}");
			return;
		}
		if (args.Length == 2 && args[0] == "--test-classification-overrides")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index9, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			index9.AlternateArtIndexVersion = 3;
			YgoCdbCardCatalog.ClassifyAlternateArtsAsync(index9).GetAwaiter().GetResult();
			string[] source18 = new string[2] { "30000", "30064" };
			string[] source19 = new string[4] { "3401", "3899", "19736", "20040" };
			bool flag89 = source18.All((string id) => index9.Textures.Any((TexRef texRef20) => texRef20.CardKey == id && texRef20.Category == "卡图缩略图" && !texRef20.IsAlternateArt && !texRef20.IsTokenOrMisc));
			bool flag90 = source19.All((string id) => index9.Textures.Any((TexRef texRef20) => texRef20.CardKey == id && texRef20.Category == "异画卡图" && texRef20.IsAlternateArt && !texRef20.IsTokenOrMisc));
			int result3;
			int num79 = index9.Textures.Count((TexRef texRef20) => int.TryParse(texRef20.CardKey, out result3) && result3 >= 30000 && result3 <= 30064 && texRef20.Category == "卡图缩略图");
			int num80 = index9.Textures.Count(delegate(TexRef texRef20)
			{
				bool flag94 = int.TryParse(texRef20.CardKey, out result3);
				if (flag94)
				{
					flag94 = ((result3 >= 3401 && (result3 <= 3899 || result3 == 19736 || result3 == 20040)) ? true : false);
				}
				return flag94 && texRef20.Category == "异画卡图";
			});
			Console.WriteLine($"version={index9.AlternateArtIndexVersion}; normalOk={flag89}; alternateOk={flag90}; forcedNormal={num79}; forcedAlternate={num80}");
			if (index9.AlternateArtIndexVersion != 4 || !flag89 || !flag90 || num79 < 2 || num80 < 4)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-index-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index10, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef texRef13 = index10.Textures.FirstOrDefault((TexRef texRef20) => texRef20.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			GameIndex gameIndex5 = new GameIndex
			{
				AlternateArtIndexVersion = index10.AlternateArtIndexVersion,
				Textures = index10.Textures.Where((TexRef texRef20) => texRef20.CardKey != args[2]).ToList()
			};
			gameIndex5.Textures.Add(new TexRef
			{
				BundlePath = texRef13.BundlePath,
				RelativeBundlePath = "diagnostic/retained-extra",
				PathId = long.MinValue,
				AssetFileName = texRef13.AssetFileName,
				Name = "diagnostic-extra",
				Width = 1,
				Height = 1,
				Category = "诊断",
				SourceKind = texRef13.SourceKind,
				CardKey = "999999"
			});
			if (!PortableIndexService.TryRepairFromBundled(args[1], gameIndex5, out GameIndex repaired, out string buildId5, out int retainedExtras))
			{
				throw new InvalidDataException("预绑定索引修复没有执行。");
			}
			bool flag91 = repaired.Textures.Any((TexRef texRef20) => texRef20.CardKey == args[2]);
			bool flag92 = repaired.Textures.Any((TexRef texRef20) => texRef20.PathId == long.MinValue);
			Console.WriteLine($"build={buildId5}; before={gameIndex5.Textures.Count}; after={repaired.Textures.Count}; restored={flag91}; retainedExtras={retainedExtras}; extraRetained={flag92}");
			if (!flag91 || !flag92 || retainedExtras != 1)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-texture-reference-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index11, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef texRef14 = index11.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			long pathId = texRef14.PathId;
			texRef14.PathId = long.MinValue;
			texRef14.AssetFileName = "stale-manual-mod-mapping";
			ModEngine modEngine5 = new ModEngine();
			TexRef texRef15 = modEngine5.ResolveTextureReference(texRef14) ?? throw new InvalidDataException("未能从当前 Bundle 重新定位 Texture2D。");
			texRef14.PathId = texRef15.PathId;
			texRef14.AssetFileName = texRef15.AssetFileName;
			byte[] array37 = modEngine5.DecodePng(texRef14, 512);
			Console.WriteLine($"card={args[2]}; expectedPathId={pathId}; resolvedPathId={texRef15.PathId}; assetFile={texRef15.AssetFileName}; pngBytes={array37.Length}");
			if (texRef15.PathId != pathId || array37.Length < 100)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--dump-animation-texture")
		{
			ModEngine modEngine6 = new ModEngine();
			MonsterAnimationAssetRef monsterAnimationAssetRef3 = modEngine6.ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).First((MonsterAnimationAssetRef asset) => asset.Kind == MonsterAnimationAssetKind.Texture);
			File.WriteAllBytes(Path.GetFullPath(args[3]), modEngine6.DecodePng(monsterAnimationAssetRef3.AsTexture()));
			Console.WriteLine($"{monsterAnimationAssetRef3.Name}; {new FileInfo(args[3]).Length} bytes; {Path.GetFullPath(args[3])}");
			return;
		}
		if (args.Length == 3 && args[0] == "--test-texture-bundle-relocation")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index12, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef texRef16 = index12.Textures.FirstOrDefault((TexRef x) => x.SourceKind == "本地卡图" && x.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			string path11 = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData。");
			TexRef texRef17 = new TexRef
			{
				BundlePath = Path.Combine(path11, "ff", "ffffffff"),
				RelativeBundlePath = Path.Combine("ff", "ffffffff"),
				PathId = long.MinValue,
				AssetFileName = "missing-bundle-mapping",
				Name = texRef16.Name,
				Width = texRef16.Width,
				Height = texRef16.Height,
				Category = texRef16.Category,
				SourceKind = texRef16.SourceKind,
				CardKey = texRef16.CardKey
			};
			byte[] array38 = new ModEngine().DecodePng(texRef17, 512);
			bool flag93 = File.Exists(texRef17.BundlePath) && string.Equals(texRef17.RelativeBundlePath, texRef16.RelativeBundlePath, StringComparison.OrdinalIgnoreCase);
			Console.WriteLine($"card={args[2]}; relocated={flag93}; bundle={texRef17.RelativeBundlePath}; pathId={texRef17.PathId}; pngBytes={array38.Length}");
			if (!flag93 || texRef17.PathId == long.MinValue || array38.Length < 100)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-texture-write-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index13, out buildId3))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef texRef18 = index13.Textures.FirstOrDefault((TexRef texRef20) => texRef20.SourceKind == "本地卡图" && texRef20.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			Directory.CreateDirectory(args[3]);
			string text28 = Path.Combine(args[3], "manual-mod.bundle");
			File.Copy(texRef18.BundlePath, text28, overwrite: true);
			TexRef texRef19 = new TexRef
			{
				BundlePath = text28,
				RelativeBundlePath = "manual-mod.bundle",
				PathId = long.MinValue,
				AssetFileName = "stale-manual-mod-mapping",
				Name = texRef18.Name,
				Width = texRef18.Width,
				Height = texRef18.Height,
				Category = texRef18.Category,
				SourceKind = texRef18.SourceKind,
				CardKey = texRef18.CardKey
			};
			ModEngine modEngine7 = new ModEngine();
			byte[] encodedImage;
			using (Image<Rgba32> source20 = SixLabors.ImageSharp.Image.Load<Rgba32>(modEngine7.DecodePng(texRef18)))
			{
				source20.Mutate(delegate(IImageProcessingContext source21)
				{
					source21.Resize(704, 1024);
				});
				using MemoryStream memoryStream6 = new MemoryStream();
				source20.SaveAsPng(memoryStream6);
				encodedImage = memoryStream6.ToArray();
			}
			modEngine7.Replace(texRef19, encodedImage, Path.Combine(args[3], "backup"));
			byte[] array39 = modEngine7.DecodePng(texRef19);
			ImageInfo imageInfo5 = SixLabors.ImageSharp.Image.Identify(array39) ?? throw new InvalidDataException("写回后的 PNG 无法识别。");
			Console.WriteLine($"card={args[2]}; resolvedPathId={texRef19.PathId}; assetFile={texRef19.AssetFileName}; result={imageInfo5.Width}x{imageInfo5.Height}; pngBytes={array39.Length}");
			if (texRef19.PathId == long.MinValue || texRef19.AssetFileName == "stale-manual-mod-mapping" || imageInfo5.Width != 704 || imageInfo5.Height != 1024)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--install-prebuilt-index")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index14, out string buildId6))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			IndexService.Save(args[1], index14);
			string localRoot = IndexService.FindLocalRoot(args[1]);
			Console.WriteLine($"build={buildId6}; textures={index14.Textures.Count}; cache={IndexService.CachePath(localRoot, IndexService.StreamingRoot(args[1]))}");
			return;
		}
		if (args.Length == 5 && args[0] == "--crop-image")
		{
			int num81 = int.Parse(args[3]);
			int num82 = int.Parse(args[4]);
			using Bitmap bitmap19 = ImageCropService.LoadPreview(args[1]);
			double num83 = (double)num81 / (double)num82;
			int num84 = bitmap19.Width;
			int num85 = (int)Math.Round((double)num84 / num83);
			if (num85 > bitmap19.Height)
			{
				num85 = bitmap19.Height;
				num84 = (int)Math.Round((double)num85 * num83);
			}
			System.Drawing.RectangleF sourceCrop = new System.Drawing.RectangleF((float)(bitmap19.Width - num84) / 2f, (float)(bitmap19.Height - num85) / 2f, num84, num85);
			File.WriteAllBytes(args[2], ImageCropService.CropAndResize(args[1], sourceCrop, num81, num82));
			Console.WriteLine($"{num81}x{num82}; {new FileInfo(args[2]).Length} bytes; {args[2]}");
			return;
		}
		Application.Run(new MainForm());
		static byte[] HashFile(string path12)
		{
			using FileStream source21 = File.OpenRead(path12);
			return SHA256.HashData(source21);
		}
		static byte[] PngBytes(Image<Rgba32> source21)
		{
			using MemoryStream memoryStream7 = new MemoryStream();
			source21.SaveAsPng(memoryStream7);
			return memoryStream7.ToArray();
		}
		Rgba32[] ReadPixels(TexRef texture6)
		{
			using Image<Rgba32> image22 = SixLabors.ImageSharp.Image.Load<Rgba32>(engine.DecodePng(texture6));
			Rgba32[] array40 = new Rgba32[image22.Width * image22.Height];
			image22.CopyPixelDataTo(array40);
			return array40;
		}
		static bool WaitFor(Func<bool> condition, int seconds = 20)
		{
			DateTime dateTime10 = DateTime.UtcNow.AddSeconds(seconds);
			while (DateTime.UtcNow < dateTime10)
			{
				Application.DoEvents();
				if (condition())
				{
					return true;
				}
				Thread.Sleep(20);
			}
			return condition();
		}
	}

	private static byte[] CreateTextureMappingPattern(int width, int height)
	{
		using Image<Rgba32> image = new Image<Rgba32>(width, height, new Rgba32(14, 23, 38, byte.MaxValue));
		int num = Math.Max(3, Math.Min(width, height) / 64);
		Rgba32 value = new Rgba32(246, 196, 64, byte.MaxValue);
		Rgba32 value2 = new Rgba32(226, 47, 84, byte.MaxValue);
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < width; j++)
			{
				if (j < num || j >= width - num || i < num || i >= height - num)
				{
					image[j, i] = value;
				}
			}
		}
		int num2 = Math.Max(16, Math.Min(width, height) / 10);
		int num3 = width / 2 - num2 / 2;
		int num4 = height / 2 - num2 / 2;
		for (int k = num4; k < num4 + num2; k++)
		{
			for (int l = num3; l < num3 + num2; l++)
			{
				image[l, k] = value2;
			}
		}
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder());
		return memoryStream.ToArray();
	}

	private static bool TextureMappingPatternReady(byte[] png, int width, int height)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		if (image.Width != width || image.Height != height)
		{
			return false;
		}
		Rgba32 rgba = image[width / 2, height / 2];
		Rgba32 rgba2 = image[1, 1];
		Rgba32 rgba3 = image[width - 2, height - 2];
		return rgba.R > 180 && rgba.G < 100 && rgba2.R > 180 && rgba2.G > 120 && rgba3.R > 180 && rgba3.G > 120;
	}

	private static bool PngHasSize(byte[] png, int width, int height)
	{
		ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(png);
		if (imageInfo.Width == width)
		{
			return imageInfo.Height == height;
		}
		return false;
	}

	private static byte[] CreateSplitTallTexture()
	{
		using Image<Rgba32> image = new Image<Rgba32>(512, 1024, new Rgba32(230, 32, 48, byte.MaxValue));
		for (int i = 512; i < 1024; i++)
		{
			for (int j = 0; j < 512; j++)
			{
				image[j, i] = new Rgba32(24, 72, 232, byte.MaxValue);
			}
		}
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, new PngEncoder());
		return memoryStream.ToArray();
	}

	private static TextureMappingLiveResult TestTextureMappingLiveCopy(TexRef source, GameTextureDisplayMapping mapping, string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		byte[] first = SHA256.HashData(File.ReadAllBytes(source.BundlePath));
		string text = Path.Combine(outputDirectory, Path.GetFileName(source.BundlePath) + ".mapping-test.bundle");
		File.Copy(source.BundlePath, text, overwrite: true);
		TexRef texture = new TexRef
		{
			BundlePath = text,
			RelativeBundlePath = Path.GetFileName(text),
			PathId = source.PathId,
			AssetFileName = source.AssetFileName,
			Name = source.Name,
			Width = source.Width,
			Height = source.Height,
			Category = source.Category,
			SourceKind = source.SourceKind,
			CardKey = source.CardKey
		};
		byte[] displayPng = CreateTextureMappingPattern(mapping.DisplayWidth, mapping.DisplayHeight);
		byte[] encodedImage = mapping.EncodeForStorage(displayPng);
		ModEngine modEngine = new ModEngine();
		modEngine.Replace(texture, encodedImage, Path.Combine(outputDirectory, "backup"));
		byte[] array = modEngine.DecodePng(texture);
		ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(array) ?? throw new InvalidDataException("临时 Bundle 写回后的 Texture2D 无法识别。");
		byte[] array2 = mapping.DecodeForDisplay(array);
		ImageInfo imageInfo2 = SixLabors.ImageSharp.Image.Identify(array2) ?? throw new InvalidDataException("临时 Bundle 的正常比例预览无法识别。");
		bool flag = TextureMappingPatternReady(array2, mapping.DisplayWidth, mapping.DisplayHeight);
		bool flag2 = first.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source.BundlePath)));
		return new TextureMappingLiveResult(imageInfo.Width == mapping.StorageWidth && imageInfo.Height == mapping.StorageHeight && imageInfo2.Width == mapping.DisplayWidth && imageInfo2.Height == mapping.DisplayHeight && flag && flag2, imageInfo.Width, imageInfo.Height, imageInfo2.Width, imageInfo2.Height, flag, flag2);
	}

	private static void TestVisualTextureWrite(TexRef source, string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		string text = Path.Combine(outputDirectory, Path.GetFileName(source.BundlePath) + ".visual-test.bundle");
		File.Copy(source.BundlePath, text, overwrite: true);
		TexRef texRef = new TexRef
		{
			BundlePath = text,
			RelativeBundlePath = Path.GetFileName(text),
			PathId = long.MinValue,
			AssetFileName = "stale-visual-mapping",
			Name = source.Name,
			Width = source.Width,
			Height = source.Height,
			Category = source.Category,
			SourceKind = source.SourceKind,
			CardKey = source.CardKey
		};
		ModEngine modEngine = new ModEngine();
		byte[] encodedImage = modEngine.DecodePng(source);
		modEngine.Replace(texRef, encodedImage, Path.Combine(outputDirectory, "backup"));
		byte[] array = modEngine.DecodePng(texRef);
		ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(array) ?? throw new InvalidDataException("写回后的视觉资源无法识别。");
		Console.WriteLine($"name={texRef.Name}; category={texRef.Category}; resolvedPathId={texRef.PathId}; assetFile={texRef.AssetFileName}; result={imageInfo.Width}x{imageInfo.Height}; pngBytes={array.Length}");
		if (texRef.PathId == long.MinValue || texRef.AssetFileName == "stale-visual-mapping" || imageInfo.Width != source.Width || imageInfo.Height != source.Height)
		{
			throw new InvalidDataException("视觉资源 Texture2D 写回回归失败：" + source.Name);
		}
	}

	private static IEnumerable<Control> Descendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control item in Descendants(child))
			{
				yield return item;
			}
		}
	}

	private static T GetPrivateField<T>(object instance, string fieldName) where T : class
	{
		Type type = instance.GetType();
		while (type != null)
		{
			if (type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) is T result)
			{
				return result;
			}
			type = type.BaseType;
		}
		throw new MissingFieldException(instance.GetType().Name, fieldName);
	}

	private static bool PumpTask(Task task, int timeoutMilliseconds)
	{
		bool num = PumpMessagesUntil(() => task.IsCompleted, timeoutMilliseconds);
		if (num)
		{
			task.GetAwaiter().GetResult();
		}
		return num;
	}

	private static bool PumpMessagesUntil(Func<bool> predicate, int timeoutMilliseconds)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
		{
			Application.DoEvents();
			if (predicate())
			{
				return true;
			}
			Thread.Sleep(12);
		}
		Application.DoEvents();
		return predicate();
	}

	private static void PumpMessagesFor(int milliseconds)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < milliseconds)
		{
			Application.DoEvents();
			Thread.Sleep(12);
		}
		Application.DoEvents();
	}

	private static void ExerciseRoundedButtonTransitions(Control root)
	{
		MethodInfo methodInfo = typeof(RoundedButton).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RoundedButton", "BeginTransition");
		RoundedButton[] array = (from button in Descendants(root).OfType<RoundedButton>()
			where button.Visible
			select button).ToArray();
		RoundedButton[] array2 = array;
		foreach (RoundedButton roundedButton in array2)
		{
			methodInfo.Invoke(roundedButton, new object[1] { roundedButton.HoverColor });
		}
		PumpMessagesFor(220);
		array2 = array;
		foreach (RoundedButton roundedButton2 in array2)
		{
			methodInfo.Invoke(roundedButton2, new object[1] { roundedButton2.NormalColor });
		}
		PumpMessagesFor(240);
	}

	private static bool EditorRenderSettled(OverFrameFrameEditorForm editor)
	{
		bool num = (bool)(editor.GetType().GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? ((object)true));
		bool flag = (bool)(editor.GetType().GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? ((object)true));
		byte[] array = editor.GetType().GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) as byte[];
		System.Windows.Forms.Timer privateField = GetPrivateField<System.Windows.Forms.Timer>(editor, "_renderTimer");
		if (!num && !flag && !privateField.Enabled)
		{
			if (array != null)
			{
				return array.Length > 0;
			}
			return false;
		}
		return false;
	}

	private static Bitmap CaptureClientFromScreen(Form form)
	{
		if (form.IsDisposed || !form.IsHandleCreated)
		{
			throw new InvalidOperationException("交互截图窗口已经关闭或尚未建立句柄。");
		}
		if (form.WindowState == FormWindowState.Minimized)
		{
			form.WindowState = FormWindowState.Normal;
			form.Show();
			PumpMessagesFor(250);
		}
		form.Activate();
		Cursor.Position = form.PointToScreen(new System.Drawing.Point(12, 12));
		Application.DoEvents();
		System.Drawing.Size clientSize = form.ClientSize;
		if (clientSize.Width <= 0 || clientSize.Height <= 0)
		{
			throw new InvalidOperationException($"交互截图客户区无效：client={clientSize}; bounds={form.Bounds}; restore={form.RestoreBounds}; state={form.WindowState}; visible={form.Visible}; disposed={form.IsDisposed}; handle={form.IsHandleCreated}。");
		}
		Bitmap bitmap = new Bitmap(clientSize.Width, clientSize.Height, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(form.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, clientSize, CopyPixelOperation.SourceCopy);
		return bitmap;
	}

	private static int CountDifferentPixels(Bitmap first, Bitmap second)
	{
		if (first.Size != second.Size)
		{
			return int.MaxValue;
		}
		System.Drawing.Rectangle rect = new System.Drawing.Rectangle(System.Drawing.Point.Empty, first.Size);
		BitmapData bitmapData = first.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		BitmapData bitmapData2 = second.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			int num = Math.Abs(bitmapData.Stride) * first.Height;
			byte[] array = new byte[num];
			byte[] array2 = new byte[num];
			Marshal.Copy(bitmapData.Scan0, array, 0, num);
			Marshal.Copy(bitmapData2.Scan0, array2, 0, num);
			int num2 = 0;
			for (int i = 0; i + 3 < num; i += 4)
			{
				if (array[i] != array2[i] || array[i + 1] != array2[i + 1] || array[i + 2] != array2[i + 2] || array[i + 3] != array2[i + 3])
				{
					num2++;
				}
			}
			return num2;
		}
		finally
		{
			first.UnlockBits(bitmapData);
			second.UnlockBits(bitmapData2);
		}
	}

	private static byte[] CreateInteractionBackground()
	{
		using Bitmap bitmap = new Bitmap(704, 1024, PixelFormat.Format32bppArgb);
		System.Drawing.Rectangle rect = new System.Drawing.Rectangle(System.Drawing.Point.Empty, bitmap.Size);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			using LinearGradientBrush brush = new LinearGradientBrush(rect, System.Drawing.Color.FromArgb(255, 20, 48, 80), System.Drawing.Color.FromArgb(255, 92, 28, 66), 35f);
			graphics.FillRectangle(brush, rect);
		}
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}
}
