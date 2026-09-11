using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace MdCardModTool;

internal static class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		if (args.Length == 3 && args[0] == "--test-mod-card-identity") { ModCardIdentityTests.Run(args[1],args[2]); return; }
		if (args.Length == 2 && args[0] == "--test-resource-preview-scroll") { ResourcePreviewScrollTests.Run(args[1]); return; }
		if (args.Length > 0 && args[0].StartsWith("--test-", StringComparison.Ordinal))
		{
			// CLI UI tests use DoEvents instead of Application.Run. Keep async form
			// continuations on the STA message-pump thread, as in the real application.
			// DoEvents tears down auto-installed contexts at the end of each pump.
			// Own this one explicitly for the full CLI test, including actions between pumps.
			WindowsFormsSynchronizationContext.AutoInstall = false;
			SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
			Control.CheckForIllegalCrossThreadCalls = true;
		}
		if (args.Length == 3 && args[0] == "--test-animation-write-lock") { AnimationWriteTests.Run(args[1], args[2]); return; }
		if (args.Length == 3 && args[0] == "--test-animation-atlas-8192") { AnimationWriteTests.Run(args[1], args[2], largeAtlas: true); return; }
		if (args.Length == 3 && args[0] == "--test-card-catalog-refresh") { CardCatalogRefreshTests.Run(args[1], args[2]); return; }
		if (args.Length == 2 && args[0] == "--test-sidebar-dpi") { SidebarDpiTests.Run(args[1]); return; }
		if (args.Length == 3 && args[0] == "--test-new-animation-links") { AnimationLinkTests.Run(args[1], args[2]); return; }
		if (args.Length == 3 && args[0] == "--test-mod-packages") { ModPackageTests.Run(args[1], args[2]); return; }
		if (args.Length == 2 && args[0] == "--test-studio-optimizations") { StudioOptimizationTests.Run(args[1]); return; }
		if (args.Length == 3 && args[0] == "--test-animation-transfer") { StudioOptimizationTests.Transfer(args[1],args[2]); return; }
		if (args.Length == 3 && args[0] == "--test-animation-donor-picker") { StudioOptimizationTests.Picker(args[1],args[2]); return; }
		if (args.Length == 3 && args[0] == "--build-card-catalog")
		{
			int count = CardCatalogService.GenerateFromAstellarCsv(args[1], args[2]);
			Console.WriteLine($"cards={count:N0}; output={Path.GetFullPath(args[2])}; bytes={new FileInfo(args[2]).Length:N0}");
			return;
		}
		if (args.Length == 2 && args[0] == "--test-card-catalog")
		{
			List<CardCatalogEntry> entries = CardCatalogService.Read(args[1]);
			CardCatalogService catalog = new(entries);
			CardCatalogEntry? chinese = catalog.Search("青眼白龙", 5).FirstOrDefault();
			CardCatalogEntry? traditional = catalog.Search("青眼白龍", 5).FirstOrDefault();
			CardCatalogEntry? english = catalog.Search("Blue-Eyes White Dragon", 5).FirstOrDefault();
			CardCatalogEntry? japanese = catalog.Search("青眼の白龍", 5).FirstOrDefault();
			IReadOnlyList<CardCatalogEntry> unicorn = catalog.Search("独角", 50);
			bool containsSalamangreat = unicorn.Any(entry => entry.CardId == 14338);
			Console.WriteLine($"cards={catalog.Count}; zh-cn={chinese?.CardId}; zh-tw={traditional?.CardId}; en={english?.CardId}; ja={japanese?.CardId}; 独角-results={unicorn.Count}; has-14338={containsSalamangreat}");
			if (catalog.Count < 10000 || chinese == null || traditional == null || english == null || japanese == null
				|| chinese.CardId != traditional.CardId || chinese.CardId != english.CardId || chinese.CardId != japanese.CardId
				|| !containsSalamangreat)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-card-preview-fallback")
		{
			using Bitmap art = new(512, 512);
			using (Graphics graphics = Graphics.FromImage(art))
			{
				graphics.Clear(System.Drawing.Color.MediumPurple);
			}
			using Bitmap invalidFrame = new(128, 128);
			using (Graphics graphics = Graphics.FromImage(invalidFrame))
			{
				graphics.Clear(System.Drawing.Color.Transparent);
			}
			using MemoryStream artStream = new();
			using MemoryStream frameStream = new();
			art.Save(artStream, System.Drawing.Imaging.ImageFormat.Png);
			invalidFrame.Save(frameStream, System.Drawing.Imaging.ImageFormat.Png);
			using Bitmap preview = CardPreviewRenderer.Render(artStream.ToArray(), frameStream.ToArray(), fullArt: false,
				out string? warning);
			bool ready = preview.Width == 512 && preview.Height == 512 && !string.IsNullOrWhiteSpace(warning);
			Console.WriteLine($"preview={preview.Width}x{preview.Height}; fallbackWarning={warning}; ready={ready}");
			if (!ready)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-card-preview-raw")
		{
			byte[] png;
			using (SixLabors.ImageSharp.Image<Rgba32> art = new(512, 1024,
				new Rgba32(System.Drawing.Color.MediumPurple.R, System.Drawing.Color.MediumPurple.G,
					System.Drawing.Color.MediumPurple.B, 255)))
			using (MemoryStream stream = new())
			{
				art[10, 10] = new Rgba32(19, 143, 227, 0);
				art.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder
				{
					ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha,
					TransparentColorMode = SixLabors.ImageSharp.Formats.Png.PngTransparentColorMode.Preserve
				});
				png = stream.ToArray();
			}
			using Bitmap preview = CardPreviewRenderer.RenderRaw(png);
			using Bitmap shaderPreview = CardPreviewRenderer.RenderRaw(png, showTransparentRgb: true);
			System.Drawing.Color center = preview.GetPixel(preview.Width / 2, preview.Height / 2);
			System.Drawing.Color hidden = preview.GetPixel(10, 10);
			System.Drawing.Color projected = shaderPreview.GetPixel(10, 10);
			bool ready = preview.Width == 512 && preview.Height == 1024
				&& center.R == System.Drawing.Color.MediumPurple.R
				&& center.G == System.Drawing.Color.MediumPurple.G
				&& center.B == System.Drawing.Color.MediumPurple.B
				&& hidden.A == 0 && hidden.R == 19 && hidden.G == 143 && hidden.B == 227
				&& projected.A == 255 && projected.R == hidden.R && projected.G == hidden.G
				&& projected.B == hidden.B;
			Console.WriteLine($"preview={preview.Width}x{preview.Height}; center={center.R},{center.G},{center.B}; hidden={hidden.R},{hidden.G},{hidden.B},{hidden.A}; projected={projected.R},{projected.G},{projected.B},{projected.A}; frameComposed=False; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-texture-display-mapping")
		{
			TexRef sleeveTexture = new()
			{
				BundlePath = "sleeve",
				RelativeBundlePath = "sleeve",
				Name = "ProtectorIcon1070005",
				Width = 512,
				Height = 1024,
				Category = "卡套",
				SourceKind = "视觉资源"
			};
			TexRef pendulumTexture = new()
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
			TexRef unrelatedTallTexture = new()
			{
				BundlePath = "wallpaper",
				RelativeBundlePath = "wallpaper",
				Name = "UnrelatedTallTexture",
				Width = 512,
				Height = 1024,
				Category = "壁纸／大厅背景",
				SourceKind = "视觉资源"
			};

			GameTextureDisplayMapping sleeve = GameTextureDisplayMapping.Resolve(sleeveTexture);
			GameTextureDisplayMapping pendulum = GameTextureDisplayMapping.Resolve(pendulumTexture, "card_frame14");
			TexRef existingOverFramePendulum = new()
			{
				BundlePath = "pendulum-overframe",
				RelativeBundlePath = "pendulum-overframe",
				Name = "20486",
				Width = 704,
				Height = 1024,
				Category = "灵摆卡图",
				SourceKind = "本地卡图",
				CardKey = "20486"
			};
			GameTextureDisplayMapping legacyPendulumDraft = GameTextureDisplayMapping.ResolveCanvas(
				existingOverFramePendulum, 512, 1024, "card_frame14");
			GameTextureDisplayMapping native = GameTextureDisplayMapping.Resolve(unrelatedTallTexture);
			byte[] sleeveDisplay = CreateTextureMappingPattern(sleeve.DisplayWidth, sleeve.DisplayHeight);
			byte[] pendulumDisplay = CreateTextureMappingPattern(pendulum.DisplayWidth, pendulum.DisplayHeight);
			byte[] sleeveStored = sleeve.EncodeForStorage(sleeveDisplay);
			byte[] pendulumStored = pendulum.EncodeForStorage(pendulumDisplay);
			byte[] sleeveRoundTrip = sleeve.DecodeForDisplay(sleeveStored);
			byte[] pendulumRoundTrip = pendulum.DecodeForDisplay(pendulumStored);

			bool sleeveStorage = PngHasSize(sleeveStored, 512, 1024);
			bool pendulumStorage = PngHasSize(pendulumStored, 512, 1024);
			bool sleeveGeometry = TextureMappingPatternReady(sleeveRoundTrip,
				sleeve.DisplayWidth, sleeve.DisplayHeight);
			bool pendulumGeometry = TextureMappingPatternReady(pendulumRoundTrip,
				pendulum.DisplayWidth, pendulum.DisplayHeight);

			byte[] tallArt = CreateSplitTallTexture();
			byte[] transparentFrame;
			using (Bitmap frame = new(704, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
			using (Graphics graphics = Graphics.FromImage(frame))
			using (MemoryStream stream = new())
			{
				graphics.Clear(System.Drawing.Color.Transparent);
				frame.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
				transparentFrame = stream.ToArray();
			}
			using Bitmap composed = FrameComposer.BitmapFrom(
				CardFrameRenderer.ComposeStoredArtPreview(tallArt, transparentFrame));
			System.Drawing.Color composedBottom = composed.GetPixel(composed.Width / 2, composed.Height - 80);
			bool fullCanvasUsed = composedBottom.B > 180 && composedBottom.R < 80;

			using CropCanvas canvas = new(new Bitmap(512, 683), 512, 683)
			{
				Size = new System.Drawing.Size(900, 720)
			};
			canvas.CreateControl();
			canvas.SetFrame(new Bitmap(704, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb));
			System.Drawing.RectangleF cardRectangle = (System.Drawing.RectangleF)(typeof(CropCanvas)
				.GetProperty("CardRectangle", BindingFlags.Instance | BindingFlags.NonPublic)
					?.GetValue(canvas) ?? System.Drawing.RectangleF.Empty);
			bool frameAspect = Math.Abs(cardRectangle.Width / cardRectangle.Height - 704f / 1024f) < 0.001f;

			bool cropUiReady = true;
			string? cropScreenshotRoot = Environment.GetEnvironmentVariable("MDCT_TEXTURE_MAPPING_SCREENSHOT_ROOT");
			if (!string.IsNullOrWhiteSpace(cropScreenshotRoot))
			{
				cropScreenshotRoot = Path.GetFullPath(cropScreenshotRoot);
				Directory.CreateDirectory(cropScreenshotRoot);
				string sourceRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool",
					"TextureMappingUi", Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(sourceRoot);
				try
				{
					string sleeveSource = Path.Combine(sourceRoot, "sleeve-display.png");
					string pendulumSource = Path.Combine(sourceRoot, "pendulum-display.png");
					File.WriteAllBytes(sleeveSource, sleeveDisplay);
					File.WriteAllBytes(pendulumSource, pendulumDisplay);

					bool CaptureCrop(ImageCropForm form, string outputName, bool needsFrame,
						string expectedMappingText)
					{
						form.StartPosition = FormStartPosition.Manual;
						form.Location = new System.Drawing.Point(-32000, -32000);
						form.ShowInTaskbar = false;
						form.Show();
						CropCanvas cropCanvas = (CropCanvas)(typeof(ImageCropForm)
							.GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)
							?.GetValue(form) ?? throw new MissingFieldException(nameof(ImageCropForm), "_canvas"));
						Label mappingLabel = (Label)(typeof(ImageCropForm)
							.GetField("_mapping", BindingFlags.Instance | BindingFlags.NonPublic)
							?.GetValue(form) ?? throw new MissingFieldException(nameof(ImageCropForm), "_mapping"));
						Stopwatch wait = Stopwatch.StartNew();
						while (needsFrame && !cropCanvas.HasFrame && wait.ElapsedMilliseconds < 15000)
						{
							Application.DoEvents();
							Thread.Sleep(15);
						}
						form.PerformLayout();
						Application.DoEvents();
						Button[] confirmationButtons = Descendants(form).OfType<Button>()
							.Where(button => button.Text is "取消" or "按预览效果替换")
							.ToArray();
						bool confirmationButtonsVisible = confirmationButtons.Length == 2
							&& confirmationButtons.All(button => button.Visible
								&& form.ClientRectangle.Contains(form.RectangleToClient(
									button.RectangleToScreen(button.ClientRectangle))));
						using Bitmap screenshot = new(form.ClientSize.Width, form.ClientSize.Height);
						form.DrawToBitmap(screenshot, form.ClientRectangle);
						screenshot.Save(Path.Combine(cropScreenshotRoot, outputName),
							System.Drawing.Imaging.ImageFormat.Png);
						bool formReady = (!needsFrame || cropCanvas.HasFrame)
							&& mappingLabel.Visible
							&& mappingLabel.Text.Contains(expectedMappingText,
								StringComparison.Ordinal)
							&& confirmationButtonsVisible
							&& form.AutoScaleMode == AutoScaleMode.Dpi;
						Console.WriteLine($"crop={outputName}; client={form.ClientSize}; "
							+ $"mapping={mappingLabel.Bounds}; buttons={confirmationButtonsVisible}:"
							+ string.Join(",", confirmationButtons.Select(button =>
								$"{button.Text}@{form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))}")));
						form.Close();
						return formReady;
					}

					using (ImageCropForm sleeveForm = new(sleeveSource, sleeve.DisplayWidth,
						sleeve.DisplayHeight, "卡套正常比例回归", displayMapping: sleeve))
					{
						cropUiReady &= CaptureCrop(sleeveForm, "sleeve-crop-normal-ratio.png",
							needsFrame: false, "正常预览 704×1024");
					}
					using (ImageCropForm pendulumForm = new(pendulumSource, pendulum.DisplayWidth,
						pendulum.DisplayHeight, "灵摆卡图正常比例回归",
						BuiltInCardFrameCatalog.Load(), "card_frame14", displayMapping: pendulum))
					{
						cropUiReady &= CaptureCrop(pendulumForm,
							"pendulum-crop-normal-ratio.png", needsFrame: true,
							"正常预览 512×683");
					}
				}
				finally
				{
					if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
				}
			}

			bool ready = sleeve.Kind == TextureDisplayMappingKind.CardSleeve
				&& sleeve.DisplayWidth == 704 && sleeve.DisplayHeight == 1024
				&& pendulum.Kind == TextureDisplayMappingKind.PendulumCardArt
				&& pendulum.DisplayWidth == 512 && pendulum.DisplayHeight == 683
				&& legacyPendulumDraft.Kind == TextureDisplayMappingKind.PendulumCardArt
				&& legacyPendulumDraft.DisplayWidth == 512
				&& legacyPendulumDraft.DisplayHeight == 683
				&& native.Kind == TextureDisplayMappingKind.Native && !native.RequiresMapping
				&& sleeveStorage && pendulumStorage && sleeveGeometry && pendulumGeometry
				&& fullCanvasUsed && frameAspect && cropUiReady;
			Console.WriteLine($"sleeve={sleeve.DisplayWidth}x{sleeve.DisplayHeight}->{sleeve.StorageWidth}x{sleeve.StorageHeight}:{sleeveGeometry}; pendulum={pendulum.DisplayWidth}x{pendulum.DisplayHeight}->{pendulum.StorageWidth}x{pendulum.StorageHeight}:{pendulumGeometry}; legacyOverFrameDraft={legacyPendulumDraft.Kind}:{legacyPendulumDraft.DisplayWidth}x{legacyPendulumDraft.DisplayHeight}; unrelated={native.Kind}; fullCanvas={fullCanvasUsed}:{composedBottom.R},{composedBottom.G},{composedBottom.B}; frameAspect={cardRectangle.Width:0.0}x{cardRectangle.Height:0.0}:{frameAspect}; cropUi={cropUiReady}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-texture-display-mapping-live")
		{
			string gameRoot = Path.GetFullPath(args[1]);
			if (!PortableIndexService.TryLoadBundled(gameRoot, out GameIndex gameIndex, out string mappingBuildId))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef pendulumSource = gameIndex.Textures.FirstOrDefault(texture =>
				texture.SourceKind == "本地卡图" && texture.Width == 512 && texture.Height == 1024
				&& (texture.CardKey == "20486" || texture.Category.Contains("灵摆", StringComparison.OrdinalIgnoreCase)))
				?? throw new InvalidDataException("预绑定索引中没有可用于只读回归的灵摆卡图。");
			VisualAssetScanResult visualIndex = VisualAssetIndexService.Scan(gameRoot);
			TexRef sleeveSource = visualIndex.Textures.FirstOrDefault(texture =>
				texture.Width == 512 && texture.Height == 1024
				&& texture.Category.Contains("卡套", StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidDataException("当前游戏资源中没有可用于只读回归的卡套。");

			string testRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool",
				"TextureDisplayMappingLiveTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testRoot);
			try
			{
				TextureMappingLiveResult sleeveResult = TestTextureMappingLiveCopy(sleeveSource,
					GameTextureDisplayMapping.Resolve(sleeveSource), Path.Combine(testRoot, "sleeve"));
				TextureMappingLiveResult pendulumResult = TestTextureMappingLiveCopy(pendulumSource,
					GameTextureDisplayMapping.Resolve(pendulumSource, "card_frame14"),
					Path.Combine(testRoot, "pendulum"));
				bool ready = sleeveResult.Ready && pendulumResult.Ready;
				Console.WriteLine($"build={mappingBuildId}; sleeve={sleeveSource.Name}:{sleeveResult}; pendulum={pendulumSource.CardKey}:{pendulumResult}; temporaryCopies=True; gameWrites=False; ready={ready}");
				if (!ready) Environment.ExitCode = 2;
			}
			finally
			{
				string fullTestRoot = Path.GetFullPath(testRoot);
				string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool",
					"TextureDisplayMappingLiveTests")).TrimEnd(Path.DirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;
				if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
					&& Directory.Exists(fullTestRoot))
				{
					Directory.Delete(fullTestRoot, recursive: true);
				}
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-packaged-card-frames")
		{
			IReadOnlyList<TexRef> frames = BuiltInCardFrameCatalog.Load();
			ModEngine engine = new();
			int valid = 0;
			foreach (TexRef frame in frames)
			{
				using Bitmap bitmap = FrameComposer.BitmapFrom(engine.DecodePng(frame));
				System.Drawing.RectangleF window = CardFrameRenderer.FindArtWindow(bitmap);
				if (bitmap.Width == 704 && bitmap.Height == 1024 && window.Width > 100 && window.Height > 100) valid++;
			}
			string link = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 22747,
				Type = "怪獸",
				SubType = "連結"
			}, 704, 1024);
			string pendulumEffect = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 20486,
				Type = "怪獸",
				SubType = "效果=靈擺"
			}, 512, 1024);
			string normalMonster = CardFrameCatalog.RecommendedKey(new CardCatalogEntry
			{
				CardId = 1,
				Type = "怪獸",
				SubType = "通常"
			}, 512, 512);
			CardCatalogService installedCatalog = CardCatalogService.LoadBestAvailable();
			string catalogLink = CardFrameCatalog.RecommendedKey(installedCatalog.Find(22747), 704, 1024);
			string catalogPendulum = CardFrameCatalog.RecommendedKey(installedCatalog.Find(20486), 512, 1024);
			int normal = frames.Count(BuiltInCardFrameCatalog.IsNormalFrame);
			int transparent = frames.Count(BuiltInCardFrameCatalog.IsTransparentFrame);
			int transparentGradient = frames.Count(BuiltInCardFrameCatalog.IsTransparentGradientFrame);
			int gradient = frames.Count(BuiltInCardFrameCatalog.IsGradientFrame);
			TexRef normalEffect = frames.Single(frame => frame.Name == "card_frame01");
			TexRef transparentEffect = frames.Single(frame => frame.Name == "transparent_card_frame01");
			TexRef transparentGradientEffect = frames.Single(frame =>
				frame.Name == "transparent_gradient_card_frame01");
			TexRef gradientEffect = frames.Single(frame => frame.Name == "gradient_card_frame01");
			Rgba32[] ReadPixels(TexRef frame)
			{
				using SixLabors.ImageSharp.Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(engine.DecodePng(frame));
				Rgba32[] pixels = new Rgba32[image.Width * image.Height];
				image.CopyPixelDataTo(pixels);
				return pixels;
			}
			Rgba32[] normalPixels = ReadPixels(normalEffect);
			Rgba32[] transparentPixels = ReadPixels(transparentEffect);
			Rgba32[] transparentGradientPixels = ReadPixels(transparentGradientEffect);
			Rgba32[] gradientPixels = ReadPixels(gradientEffect);
			int normalVisible = normalPixels.Count(pixel => pixel.A > 0);
			int transparentVisible = transparentPixels.Count(pixel => pixel.A > 0);
			int transparentRgb = transparentPixels.Count(pixel => pixel.A == 0 && (pixel.R != 0 || pixel.G != 0 || pixel.B != 0));
			int transparentGradientVisible = transparentGradientPixels.Count(pixel => pixel.A > 0);
			int transparentGradientRgb = transparentGradientPixels.Count(pixel => pixel.A == 0
				&& (pixel.R != 0 || pixel.G != 0 || pixel.B != 0));
			int transparentGradientColorDifference = transparentPixels.Zip(transparentGradientPixels)
				.Count(pair => pair.First.R != pair.Second.R || pair.First.G != pair.Second.G
					|| pair.First.B != pair.Second.B);
			int transparentGradientAlphaDifference = gradientPixels.Zip(transparentGradientPixels)
				.Count(pair => pair.First.A != pair.Second.A);
			int gradientDifference = normalPixels.Zip(gradientPixels)
				.Count(pair => pair.First.R != pair.Second.R || pair.First.G != pair.Second.G
					|| pair.First.B != pair.Second.B || pair.First.A != pair.Second.A);
			bool ready = frames.Count == 64 && valid == frames.Count
				&& normal == 16 && transparent == 16 && transparentGradient == 16 && gradient == 16
				&& transparentVisible + 100000 < normalVisible && transparentRgb > 10000
				&& transparentGradientVisible + 100000 < normalVisible
				&& transparentGradientRgb > 10000
				&& transparentGradientColorDifference > 10000
				&& transparentGradientAlphaDifference > 10000
				&& gradientDifference > 10000
				&& frames.Any(frame => frame.Name == "card_frame18")
				&& frames.Any(frame => frame.Name == "transparent_card_frame18")
				&& frames.Any(frame => frame.Name == "transparent_gradient_card_frame18")
				&& frames.Any(frame => frame.Name == "gradient_card_frame18")
				&& frames.All(BuiltInCardFrameCatalog.IsPackagedFrame)
				&& link == "card_frame18" && pendulumEffect == "card_frame14" && normalMonster == "card_frame00"
				&& catalogLink == "card_frame18" && catalogPendulum == "card_frame14";
			Console.WriteLine($"frames={frames.Count}; normal={normal}:{normalVisible}; transparent={transparent}:{transparentVisible}:rgb0={transparentRgb}; transparentGradient={transparentGradient}:{transparentGradientVisible}:rgb0={transparentGradientRgb}:colorDiff={transparentGradientColorDifference}:alphaDiff={transparentGradientAlphaDifference}; gradient={gradient}:diff={gradientDifference}; artWindows={valid}; link={link}/{catalogLink}; pendulumEffect={pendulumEffect}/{catalogPendulum}; normalMonster={normalMonster}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-overframe-workflow")
		{
			string testRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool",
				"OverFrameWorkflowTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testRoot);
			try
			{
				static byte[] PngBytes(SixLabors.ImageSharp.Image<Rgba32> image)
				{
					using MemoryStream stream = new();
					image.SaveAsPng(stream);
					return stream.ToArray();
				}

				bool WaitFor(Func<bool> condition, int seconds = 20)
				{
					DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
					while (DateTime.UtcNow < deadline)
					{
						Application.DoEvents();
						if (condition()) return true;
						Thread.Sleep(20);
					}
					return condition();
				}

				// A regular 512x512 card-art source with a transparent subject. This is the
				// entry that previously opened ImageCropForm before the real OF editor.
				byte[] sourcePng;
				using (SixLabors.ImageSharp.Image<Rgba32> sourceImage = new(512, 512,
					new Rgba32(0, 0, 0, 0)))
				{
					for (int y = 22; y < 500; y++)
					for (int x = 176; x < 346; x++)
					{
						sourceImage[x, y] = new Rgba32(24, 112, 208, 255);
					}
					sourcePng = PngBytes(sourceImage);
				}
				byte[] backgroundPng;
				using (SixLabors.ImageSharp.Image<Rgba32> backgroundImage = new(FrameComposer.Width,
					FrameComposer.Height, new Rgba32(217, 45, 81, 255)))
				{
					backgroundPng = PngBytes(backgroundImage);
				}

				TexRef[] packagedFrames = BuiltInCardFrameCatalog.Load().ToArray();
				TexRef fakeCard = new()
				{
					BundlePath = Path.Combine(testRoot, "not-written.bundle"),
					RelativeBundlePath = "not-written.bundle",
					Name = "3899",
					Width = 512,
					Height = 512,
					SourceKind = "本地卡图",
					CardKey = "3899"
				};
				int[] editorCounts = new int[4];
				bool editorUiReady;
				bool alphaPreviewReady = false;
				ImageRenderSpec movedSpec;
				using (OverFrameFrameEditorForm editor = new(testRoot, fakeCard, packagedFrames,
					sourcePng, "transparent_card_frame01", backgroundPng,
					replaceStoredBackground: true)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				})
				{
					editor.Show();
					ModernComboBox mode = (ModernComboBox)(typeof(OverFrameFrameEditorForm)
						.GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_mode"));
					ModernComboBox frameChoices = (ModernComboBox)(typeof(OverFrameFrameEditorForm)
						.GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_frames"));
					CropCanvas canvas = (CropCanvas)(typeof(OverFrameFrameEditorForm)
						.GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_canvas"));
					Label status = (Label)(typeof(OverFrameFrameEditorForm)
						.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_status"));
					Label layerStatus = (Label)(typeof(OverFrameFrameEditorForm)
						.GetField("_layerStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_layerStatus"));
					FieldInfo outputField = typeof(OverFrameFrameEditorForm).GetField("_outputBytes",
						BindingFlags.Instance | BindingFlags.NonPublic)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_outputBytes");
					FieldInfo previewField = typeof(OverFrameFrameEditorForm).GetField("_previewBytes",
						BindingFlags.Instance | BindingFlags.NonPublic)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_previewBytes");
					bool initialReady = WaitFor(() => outputField.GetValue(editor) is byte[]);
					ImageRenderSpec initialSpec = canvas.RenderSpec;
					movedSpec = new ImageRenderSpec(FrameComposer.Width, FrameComposer.Height,
						initialSpec.ImageScale * 1.28f, initialSpec.OffsetX + 37f,
						initialSpec.OffsetY - 24f);
					canvas.SetRenderSpec(movedSpec);
					bool movedReady = WaitFor(() => outputField.GetValue(editor) is byte[]
						&& Math.Abs(canvas.RenderSpec.OffsetX - movedSpec.OffsetX) < 1f);

					string[] expectedModes = ["透明卡框", "透明炫彩卡框", "炫彩卡框", "普通卡框"];
					bool modesReady = initialReady && movedReady;
					for (int index = 0; index < expectedModes.Length; index++)
					{
						mode.SelectedIndex = index;
						bool generated = WaitFor(() => outputField.GetValue(editor) is byte[]
							&& status.Text.StartsWith(expectedModes[index], StringComparison.Ordinal));
						editorCounts[index] = frameChoices.Items.Count;
						Console.WriteLine($"mode={index}; generated={generated}; status={status.Text}");
						byte[]? output = outputField.GetValue(editor) as byte[];
						ImageInfo? info = output == null ? null : SixLabors.ImageSharp.Image.Identify(output);
						ImageRenderSpec kept = canvas.RenderSpec;
						modesReady &= generated && editorCounts[index] == 16 && output != null
							&& info?.Width == FrameComposer.Width && info.Height == FrameComposer.Height
							&& Math.Abs(kept.ImageScale - movedSpec.ImageScale) < 0.02f
							&& Math.Abs(kept.OffsetX - movedSpec.OffsetX) < 1f
							&& Math.Abs(kept.OffsetY - movedSpec.OffsetY) < 1f;
					}
					// Supersede an in-flight render several times without pumping in between.
					mode.SelectedIndex = 1;
					mode.SelectedIndex = 2;
					mode.SelectedIndex = 3;
					mode.SelectedIndex = 0;
					modesReady &= WaitFor(() => outputField.GetValue(editor) is byte[]
						&& status.Text.StartsWith(expectedModes[0], StringComparison.Ordinal));
					if (outputField.GetValue(editor) is byte[] alphaOutput
						&& previewField.GetValue(editor) is byte[] alphaPreview)
					{
						using SixLabors.ImageSharp.Image<Rgba32> outputImage =
							SixLabors.ImageSharp.Image.Load<Rgba32>(alphaOutput);
						using SixLabors.ImageSharp.Image<Rgba32> previewImage =
							SixLabors.ImageSharp.Image.Load<Rgba32>(alphaPreview);
						int transparentPixels = 0;
						int mismatches = 0;
						for (int y = 0; y < outputImage.Height; y++)
						for (int x = 0; x < outputImage.Width; x++)
						{
							Rgba32 outputPixel = outputImage[x, y];
							Rgba32 previewPixel = previewImage[x, y];
							if (outputPixel.A == 0) transparentPixels++;
							if (outputPixel != previewPixel) mismatches++;
						}
						alphaPreviewReady = transparentPixels > 10000 && mismatches == 0;
						Console.WriteLine($"alphaPixels={transparentPixels}; mismatches={mismatches}; status={status.Text}");
					}
					string[] editorButtons = Descendants(editor).OfType<Button>()
						.Select(button => button.Text).ToArray();
					string[] modeLabels = mode.Items.Cast<object>().Select(item => item.ToString() ?? "").ToArray();
					editorUiReady = modesReady && canvas.IsOverFrameEditing
						&& canvas.ShowingRenderedPreview
						&& modeLabels.SequenceEqual(expectedModes)
						&& layerStatus.Text.Contains("已添加背景", StringComparison.Ordinal)
						&& layerStatus.Text.Contains("主体可越过卡框", StringComparison.Ordinal)
						&& editorButtons.Contains("更换卡图")
						&& editorButtons.Contains("添加叠底背景")
						&& editorButtons.Contains("清除背景")
						&& alphaPreviewReady
						&& editorButtons.Contains("真实 Alpha 预览")
						&& editorButtons.Contains("构图编辑")
						&& editorButtons.Contains("导出最终 PNG")
						&& editorButtons.Contains("铺满插图区")
						&& editorButtons.Contains("显示整张图");
					string? screenshotPath = Environment.GetEnvironmentVariable("MDCT_OVERFRAME_SCREENSHOT");
					if (!string.IsNullOrWhiteSpace(screenshotPath))
					{
						if (int.TryParse(Environment.GetEnvironmentVariable("MDCT_OVERFRAME_SCREENSHOT_MODE"),
							out int screenshotMode) && screenshotMode >= 0
							&& screenshotMode < expectedModes.Length)
						{
							mode.SelectedIndex = screenshotMode;
							_ = WaitFor(() => outputField.GetValue(editor) is byte[]
								&& status.Text.StartsWith(expectedModes[screenshotMode],
									StringComparison.Ordinal));
						}
						Application.DoEvents();
						using Bitmap screenshot = new(editor.Width, editor.Height);
						editor.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0,
							screenshot.Width, screenshot.Height));
						screenshot.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
					}
					editor.Close();
				}

				bool reopenRestored;
				using (OverFrameFrameEditorForm reopened = new(testRoot, fakeCard, packagedFrames)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				})
				{
					reopened.Show();
					CropCanvas reopenedCanvas = (CropCanvas)(typeof(OverFrameFrameEditorForm)
						.GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reopened)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_canvas"));
					FieldInfo reopenedOutput = typeof(OverFrameFrameEditorForm).GetField("_outputBytes",
						BindingFlags.Instance | BindingFlags.NonPublic)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_outputBytes");
					reopenRestored = WaitFor(() => reopenedOutput.GetValue(reopened) is byte[])
						&& reopenedCanvas.IsOverFrameEditing
						&& reopenedCanvas.ShowingRenderedPreview
						&& Math.Abs(reopenedCanvas.RenderSpec.ImageScale - movedSpec.ImageScale) < 0.02f
						&& Math.Abs(reopenedCanvas.RenderSpec.OffsetX - movedSpec.OffsetX) < 1f
						&& Math.Abs(reopenedCanvas.RenderSpec.OffsetY - movedSpec.OffsetY) < 1f;
						reopened.Close();
					}

				// A draft saved by older builds can still be the native 512×1024
				// Pendulum canvas even after its live card has become a 704×1024 OF
				// texture. Reopening the unified editor must migrate that source using
				// the card/frame metadata instead of the current live dimensions.
				const ushort legacyPendulumCardId = 20486;
				OverFrameArtStore.SaveSource(testRoot, legacyPendulumCardId,
					CreateTextureMappingPattern(512, 1024));
				OverFrameArtStore.SaveSettings(testRoot, legacyPendulumCardId,
					new OverFrameFrameSettings("card_frame14", UserSelected: true));
				TexRef existingPendulumOverFrame = new()
				{
					BundlePath = Path.Combine(testRoot, "not-written-pendulum.bundle"),
					RelativeBundlePath = "not-written-pendulum.bundle",
					Name = legacyPendulumCardId.ToString(),
					Width = 704,
					Height = 1024,
					Category = "灵摆卡图",
					SourceKind = "本地卡图",
					CardKey = legacyPendulumCardId.ToString()
				};
				bool legacyPendulumDraftMigrated;
				using (OverFrameFrameEditorForm legacyEditor = new(testRoot,
					existingPendulumOverFrame, packagedFrames)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				})
				{
					legacyEditor.Show();
					FieldInfo legacyOutput = typeof(OverFrameFrameEditorForm).GetField("_outputBytes",
						BindingFlags.Instance | BindingFlags.NonPublic)
						?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_outputBytes");
					bool rendered = WaitFor(() => legacyOutput.GetValue(legacyEditor) is byte[]);
					ImageInfo? migratedSource = SixLabors.ImageSharp.Image.Identify(
						OverFrameArtStore.SourcePath(testRoot, legacyPendulumCardId));
					legacyPendulumDraftMigrated = rendered
						&& migratedSource?.Width == GameTextureDisplayMapping.PendulumDisplayWidth
						&& migratedSource.Height == GameTextureDisplayMapping.PendulumDisplayHeight
						&& !File.Exists(existingPendulumOverFrame.BundlePath);
					legacyEditor.Close();
				}

				// Pixel-level order assertion: subject must win over frame chrome, while a
				// transparent subject/frame pixel must expose the optional background.
				byte[] placedSubject;
				using (SixLabors.ImageSharp.Image<Rgba32> artImage = new(FrameComposer.Width,
					FrameComposer.Height, new Rgba32(0, 0, 0, 0)))
				{
					artImage[100, 100] = new Rgba32(239, 31, 47, 255);
					placedSubject = PngBytes(artImage);
				}
				byte[] testFrame;
				using (SixLabors.ImageSharp.Image<Rgba32> frameImage = new(FrameComposer.Width,
					FrameComposer.Height, new Rgba32(0, 0, 0, 0)))
				{
					frameImage[100, 100] = new Rgba32(20, 230, 61, 255);
					testFrame = PngBytes(frameImage);
				}
				byte[] layered = AstellarOverFrameComposer.ComposeFlatFrame(placedSubject,
					testFrame, backgroundPng);
				using SixLabors.ImageSharp.Image<Rgba32> layeredImage =
					SixLabors.ImageSharp.Image.Load<Rgba32>(layered);
				Rgba32 subjectOverFrame = layeredImage[100, 100];
				Rgba32 backgroundThrough = layeredImage[352, 512];
				string storedBackgroundPath = OverFrameArtStore.BackgroundPath(testRoot, 3899);
				ImageInfo? storedBackground = File.Exists(storedBackgroundPath)
					? SixLabors.ImageSharp.Image.Identify(storedBackgroundPath)
					: null;
				ImageInfo? storedSource = SixLabors.ImageSharp.Image.Identify(
					OverFrameArtStore.SourcePath(testRoot, 3899));
				OverFrameFrameSettings saved = OverFrameArtStore.ReadSettings(testRoot, 3899);
				bool layeredReady = subjectOverFrame.R == 239 && subjectOverFrame.G == 31
					&& subjectOverFrame.B == 47 && subjectOverFrame.A == 255
					&& backgroundThrough.R == 217 && backgroundThrough.G == 45
					&& backgroundThrough.B == 81 && backgroundThrough.A == 255;
				bool transformSaved = Math.Abs(saved.ArtImageScale - movedSpec.ImageScale) < 0.02f
					&& Math.Abs(saved.ArtOffsetX - movedSpec.OffsetX) < 1f
					&& Math.Abs(saved.ArtOffsetY - movedSpec.OffsetY) < 1f;
				bool ready = editorUiReady && reopenRestored && legacyPendulumDraftMigrated
					&& layeredReady && transformSaved
					&& storedBackground?.Width == FrameComposer.Width
					&& storedBackground.Height == FrameComposer.Height
					&& storedSource?.Width == 512 && storedSource.Height == 512
					&& !File.Exists(fakeCard.BundlePath);
				Console.WriteLine($"singleEditor=True; reopenRestored={reopenRestored}; legacyPendulumDraftMigrated={legacyPendulumDraftMigrated}; alphaPreview={alphaPreviewReady}; source={storedSource?.Width}x{storedSource?.Height}; editorModes={string.Join(',', editorCounts)}; dragScale={saved.ArtImageScale:0.000}; dragOffset={saved.ArtOffsetX:0.0},{saved.ArtOffsetY:0.0}; subjectOverFrame={subjectOverFrame.R},{subjectOverFrame.G},{subjectOverFrame.B},{subjectOverFrame.A}; backgroundThrough={backgroundThrough.R},{backgroundThrough.G},{backgroundThrough.B},{backgroundThrough.A}; gameWrites=False; ready={ready}");
				if (!ready) Environment.ExitCode = 2;
			}
			finally
			{
				string fullTestRoot = Path.GetFullPath(testRoot);
				string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool",
					"OverFrameWorkflowTests")).TrimEnd(Path.DirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;
				if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
					&& Directory.Exists(fullTestRoot))
				{
					Directory.Delete(fullTestRoot, recursive: true);
				}
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-astellar-overframe")
		{
			string key = args[1];
			AstellarOverFrameTemplate template = AstellarOverFrameTemplateCatalog.Load(key);
			bool[] dirtyMask = new bool[FrameComposer.Width * FrameComposer.Height];
			using SixLabors.ImageSharp.Image<Rgba32> effectBox =
				SixLabors.ImageSharp.Image.Load<Rgba32>(template.Layers["EffBox"]);
			foreach (string layerName in new[] { "PeriFrame", "ArtFrame", "EffFrame" })
			{
				using SixLabors.ImageSharp.Image<Rgba32> layer =
					SixLabors.ImageSharp.Image.Load<Rgba32>(template.Layers[layerName]);
				for (int y = 0; y < layer.Height; y++)
				for (int x = 0; x < layer.Width; x++)
				{
					if (layer[x, y].A == 0) continue;
					if (layerName != "PeriFrame" && effectBox[x, y].A > 0) continue;
					dirtyMask[y * FrameComposer.Width + x] = true;
				}
			}
			int subjectIndex = Array.FindIndex(dirtyMask, value => value);
			if (subjectIndex < 0) throw new InvalidDataException($"透明边缘模板 {key} 没有 Dirty Alpha 几何。");
			System.Drawing.Point subjectPoint = new(subjectIndex % FrameComposer.Width,
				subjectIndex / FrameComposer.Width);
			byte[] artPng;
			using (SixLabors.ImageSharp.Image<Rgba32> art = new(FrameComposer.Width, FrameComposer.Height,
				new Rgba32(0, 0, 0, 0)))
			using (MemoryStream stream = new())
			{
				for (int y = Math.Max(0, subjectPoint.Y - 5); y <= Math.Min(art.Height - 1, subjectPoint.Y + 5); y++)
				for (int x = Math.Max(0, subjectPoint.X - 5); x <= Math.Min(art.Width - 1, subjectPoint.X + 5); x++)
				{
					art[x, y] = new Rgba32(239, 31, 47, 255);
				}
				art.SaveAsPng(stream);
				artPng = stream.ToArray();
			}
			AstellarOverFrameComposition composition = AstellarOverFrameComposer.Compose(artPng, template);
			using SixLabors.ImageSharp.Image<Rgba32> game = SixLabors.ImageSharp.Image.Load<Rgba32>(composition.GamePng);
			using SixLabors.ImageSharp.Image<Rgba32> preview = SixLabors.ImageSharp.Image.Load<Rgba32>(composition.PreviewPng);
			using SixLabors.ImageSharp.Image<Rgba32> diagnostic = SixLabors.ImageSharp.Image.Load<Rgba32>(
				AstellarOverFrameComposer.CreateVisibleRgbPreview(composition.GamePng));
			int transparentRgb = 0;
			int transparentPreview = 0;
			int diagnosticVisible = 0;
			int dirtyPixels = 0;
			int dirtyAlphaFailures = 0;
			int previewMismatches = 0;
			int visibleOutsideDirty = 0;
			for (int y = 0; y < game.Height; y++)
			for (int x = 0; x < game.Width; x++)
			{
				int index = y * game.Width + x;
				Rgba32 pixel = game[x, y];
				Rgba32 previewPixel = preview[x, y];
				bool carriesTransparentRgb = pixel.A == 0
					&& (pixel.R != 0 || pixel.G != 0 || pixel.B != 0);
				if (previewPixel != pixel) previewMismatches++;
				if (carriesTransparentRgb)
				{
					transparentRgb++;
					if (previewPixel.A == 0) transparentPreview++;
					if (diagnostic[x, y].A == 255) diagnosticVisible++;
				}
				if (dirtyMask[index])
				{
					dirtyPixels++;
					if (pixel.A != 0) dirtyAlphaFailures++;
				}
				else if (pixel.A > 0)
				{
					visibleOutsideDirty++;
				}
			}
			Rgba32 subject = game[subjectPoint.X, subjectPoint.Y];
			bool ready = template.Layers.Count == 6 && game.Width == 704 && game.Height == 1024
				&& transparentRgb > 10000 && transparentPreview == transparentRgb
				&& diagnosticVisible == transparentRgb && previewMismatches == 0
				&& dirtyPixels > 10000 && dirtyAlphaFailures == 0
				&& visibleOutsideDirty > 10000
				&& composition.TransparentEdgePixels == transparentRgb
				&& subject.R == 239 && subject.G == 31 && subject.B == 47 && subject.A == 0
				&& preview[subjectPoint.X, subjectPoint.Y].A == 0;
			Console.WriteLine($"template={key}; layers={template.Layers.Count}; transparentRgb={transparentRgb}/{composition.TransparentEdgePixels}; previewTransparent={transparentPreview}; diagnosticVisible={diagnosticVisible}; previewMismatches={previewMismatches}; dirty={dirtyPixels}; dirtyAlphaFailures={dirtyAlphaFailures}; visibleOutsideDirty={visibleOutsideDirty}; subjectOverFrame={subject.R},{subject.G},{subject.B},{subject.A}@{subjectPoint.X},{subjectPoint.Y}; gameBytes={composition.GamePng.Length}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-astellar-overframe-draft")
		{
			string draftRoot = Path.GetFullPath(args[1]);
			string settingsPath = Path.Combine(draftRoot, "卡框设置.json");
			string artPath = Path.Combine(draftRoot, "透明原画.png");
			string backgroundPath = Path.Combine(draftRoot, "叠底背景.png");
			OverFrameFrameSettings settings = JsonSerializer.Deserialize<OverFrameFrameSettings>(
				File.ReadAllText(settingsPath)) ?? throw new InvalidDataException("无法读取超框草稿设置。");
			string frameKey = settings.FrameKey.StartsWith("transparent_", StringComparison.Ordinal)
				? settings.FrameKey["transparent_".Length..]
				: settings.FrameKey;
			AstellarOverFrameComposition composition = AstellarOverFrameComposer.Compose(
				File.ReadAllBytes(artPath),
				AstellarOverFrameTemplateCatalog.Load(frameKey),
				File.Exists(backgroundPath) ? File.ReadAllBytes(backgroundPath) : null);
			using SixLabors.ImageSharp.Image<Rgba32> game =
				SixLabors.ImageSharp.Image.Load<Rgba32>(composition.GamePng);
			using SixLabors.ImageSharp.Image<Rgba32> preview =
				SixLabors.ImageSharp.Image.Load<Rgba32>(composition.PreviewPng);
			using SixLabors.ImageSharp.Image<Rgba32> diagnostic =
				SixLabors.ImageSharp.Image.Load<Rgba32>(
					AstellarOverFrameComposer.CreateVisibleRgbPreview(composition.GamePng));
			int zeroAlpha = 0;
			int hiddenRgb = 0;
			int opaque = 0;
			int previewTransparentRgb = 0;
			int diagnosticVisibleRgb = 0;
			int previewMismatches = 0;
			for (int y = 0; y < game.Height; y++)
			for (int x = 0; x < game.Width; x++)
			{
				Rgba32 pixel = game[x, y];
				Rgba32 previewPixel = preview[x, y];
				bool carriesTransparentRgb = pixel.A == 0
					&& (pixel.R != 0 || pixel.G != 0 || pixel.B != 0);
				if (pixel.A == 0)
				{
					zeroAlpha++;
					if (carriesTransparentRgb)
					{
						hiddenRgb++;
						if (previewPixel.A == 0) previewTransparentRgb++;
						if (diagnostic[x, y].A == 255) diagnosticVisibleRgb++;
					}
				}
				if (pixel.A == 255) opaque++;
				if (previewPixel != pixel) previewMismatches++;
			}
			bool ready = game.Width == FrameComposer.Width && game.Height == FrameComposer.Height
				&& zeroAlpha > 100000 && hiddenRgb > 100000 && opaque > 100000
				&& previewTransparentRgb == hiddenRgb && diagnosticVisibleRgb == hiddenRgb
				&& previewMismatches == 0
				&& composition.TransparentEdgePixels == hiddenRgb;
			Console.WriteLine($"draft={draftRoot}; frame={frameKey}; zeroAlpha={zeroAlpha}; hiddenRgb={hiddenRgb}/{composition.TransparentEdgePixels}; opaque={opaque}; previewTransparentRgb={previewTransparentRgb}; diagnosticVisibleRgb={diagnosticVisibleRgb}; previewMismatches={previewMismatches}; gameBytes={composition.GamePng.Length}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 4 && args[0] == "--test-texture-rgba-roundtrip")
		{
			string gameRoot = Path.GetFullPath(args[1]);
			string cardId = args[2];
			string draftRoot = Path.GetFullPath(args[3]);
			if (!PortableIndexService.TryLoadBundled(gameRoot, out GameIndex currentIndex, out _))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef indexed = currentIndex.Textures.FirstOrDefault(texture =>
				texture.SourceKind == "本地卡图" && texture.CardKey == cardId)
				?? throw new InvalidDataException("预绑定索引不含卡号 " + cardId + "。");
			ModEngine engine = new();
			TexRef resolved = engine.ResolveTextureReference(indexed)
				?? throw new InvalidDataException("无法重新定位卡号 " + cardId + " 的 Texture2D。");
			string realBundle = Path.GetFullPath(resolved.ActiveBundlePath);
			byte[] HashFile(string path)
			{
				using FileStream stream = File.OpenRead(path);
				return SHA256.HashData(stream);
			}
			byte[] realHashBefore = HashFile(realBundle);

			OverFrameFrameSettings settings = JsonSerializer.Deserialize<OverFrameFrameSettings>(
				File.ReadAllText(Path.Combine(draftRoot, "卡框设置.json")))
				?? throw new InvalidDataException("无法读取超框草稿设置。");
			string frameKey = settings.FrameKey.StartsWith("transparent_", StringComparison.Ordinal)
				? settings.FrameKey["transparent_".Length..]
				: settings.FrameKey;
			string backgroundPath = Path.Combine(draftRoot, "叠底背景.png");
			AstellarOverFrameComposition composition = AstellarOverFrameComposer.Compose(
				File.ReadAllBytes(Path.Combine(draftRoot, "透明原画.png")),
				AstellarOverFrameTemplateCatalog.Load(frameKey),
				File.Exists(backgroundPath) ? File.ReadAllBytes(backgroundPath) : null);

			string testRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool",
				"TextureRoundTripTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testRoot);
			try
			{
				string temporaryBundle = Path.Combine(testRoot, Path.GetFileName(realBundle));
				File.Copy(realBundle, temporaryBundle);
				TexRef temporary = new()
				{
					BundlePath = temporaryBundle,
					RelativeBundlePath = Path.GetFileName(temporaryBundle),
					PathId = resolved.PathId,
					AssetFileName = resolved.AssetFileName,
					Name = resolved.Name,
					Width = resolved.Width,
					Height = resolved.Height,
					Category = resolved.Category,
					SourceKind = resolved.SourceKind,
					CardKey = resolved.CardKey
				};
				engine.Replace(temporary, composition.GamePng, Path.Combine(testRoot, "backup"));
				byte[] decoded = engine.DecodePng(temporary);
				using SixLabors.ImageSharp.Image<Rgba32> expected =
					SixLabors.ImageSharp.Image.Load<Rgba32>(composition.GamePng);
				using SixLabors.ImageSharp.Image<Rgba32> actual =
					SixLabors.ImageSharp.Image.Load<Rgba32>(decoded);
				Rgba32[] expectedPixels = new Rgba32[expected.Width * expected.Height];
				Rgba32[] actualPixels = new Rgba32[actual.Width * actual.Height];
				expected.CopyPixelDataTo(expectedPixels);
				actual.CopyPixelDataTo(actualPixels);
				int mismatches = expectedPixels.Length == actualPixels.Length
					? expectedPixels.Zip(actualPixels).Count(pair => pair.First != pair.Second)
					: Math.Max(expectedPixels.Length, actualPixels.Length);
				int hiddenRgb = actualPixels.Count(pixel => pixel.A == 0
					&& (pixel.R != 0 || pixel.G != 0 || pixel.B != 0));
				bool realUnchanged = realHashBefore.SequenceEqual(HashFile(realBundle));
				bool ready = actual.Width == expected.Width && actual.Height == expected.Height
					&& mismatches == 0 && hiddenRgb == composition.TransparentEdgePixels
					&& realUnchanged;
				Console.WriteLine($"card={cardId}; temporaryBundle=True; expected={expected.Width}x{expected.Height}; decoded={actual.Width}x{actual.Height}; rgbaMismatches={mismatches}; hiddenRgb={hiddenRgb}/{composition.TransparentEdgePixels}; realBundleUnchanged={realUnchanged}; ready={ready}");
				if (!ready) Environment.ExitCode = 2;
			}
			finally
			{
				string fullTestRoot = Path.GetFullPath(testRoot);
				string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool",
					"TextureRoundTripTests")).TrimEnd(Path.DirectorySeparatorChar)
					+ Path.DirectorySeparatorChar;
				if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
					&& Directory.Exists(fullTestRoot))
				{
					Directory.Delete(fullTestRoot, recursive: true);
				}
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-overframe-editor-current")
		{
			string gameRoot = args[1];
			string cardId = args[2];
			if (!PortableIndexService.TryLoadBundled(gameRoot, out GameIndex currentIndex, out _))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef art = currentIndex.Textures.FirstOrDefault(texture =>
				texture.SourceKind == "本地卡图" && texture.CardKey == cardId)
				?? throw new InvalidDataException("预绑定索引不含卡号 " + cardId + "。");
			byte[] decoded = new ModEngine().DecodePng(art);
			string testRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameEditorTests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testRoot);
			try
			{
				TexRef[] frames = BuiltInCardFrameCatalog.Load().ToArray();
				CardCatalogEntry? card = CardCatalogService.LoadBestAvailable().Find(cardId);
				string recommended = CardFrameCatalog.RecommendedKey(card, art.Width, art.Height);
				using OverFrameFrameEditorForm form = new(testRoot, art, frames, decoded, recommended)
				{
					Opacity = 0.0,
					ShowInTaskbar = false
				};
				form.Show();
				ModernComboBox mode = (ModernComboBox)(typeof(OverFrameFrameEditorForm)
					.GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_mode"));
				ModernComboBox frameChoices = (ModernComboBox)(typeof(OverFrameFrameEditorForm)
					.GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_frames"));
				CropCanvas canvas = (CropCanvas)(typeof(OverFrameFrameEditorForm)
					.GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_canvas"));
				Label status = (Label)(typeof(OverFrameFrameEditorForm)
					.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_status"));
				FieldInfo outputField = typeof(OverFrameFrameEditorForm).GetField("_outputBytes",
					BindingFlags.Instance | BindingFlags.NonPublic)
					?? throw new MissingFieldException(nameof(OverFrameFrameEditorForm), "_outputBytes");
				DateTime initialDeadline = DateTime.UtcNow.AddSeconds(20);
				while (DateTime.UtcNow < initialDeadline
					&& (outputField.GetValue(form) is not byte[] || mode.SelectedIndex != 0
						|| !status.Text.StartsWith("透明卡框", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(20);
				}
				string[] expectedStatus = ["透明卡框", "透明炫彩卡框", "炫彩卡框", "普通卡框"];
				int[] choiceCounts = new int[expectedStatus.Length];
				bool defaultOverframe = outputField.GetValue(form) is byte[]
					&& mode.SelectedIndex == 0
					&& status.Text.StartsWith(expectedStatus[0], StringComparison.Ordinal);
				bool modesReady = defaultOverframe;
				for (int index = 0; index < expectedStatus.Length; index++)
				{
					mode.SelectedIndex = index;
					DateTime deadline = DateTime.UtcNow.AddSeconds(20);
					while (DateTime.UtcNow < deadline &&
						(outputField.GetValue(form) is not byte[] || !status.Text.StartsWith(expectedStatus[index], StringComparison.Ordinal)))
					{
						Application.DoEvents();
						Thread.Sleep(20);
					}
					choiceCounts[index] = frameChoices.Items.Count;
					byte[]? output = outputField.GetValue(form) as byte[];
					bool dimensions = false;
					if (output != null)
					{
						using SixLabors.ImageSharp.Image<Rgba32> rendered = SixLabors.ImageSharp.Image.Load<Rgba32>(output);
						dimensions = rendered.Width == FrameComposer.Width && rendered.Height == FrameComposer.Height;
					}
					modesReady &= output != null && dimensions && frameChoices.Items.Count == 16
						&& status.Text.StartsWith(expectedStatus[index], StringComparison.Ordinal);
				}
				mode.SelectedIndex = 0;
				DateTime finalPreviewDeadline = DateTime.UtcNow.AddSeconds(20);
				while (DateTime.UtcNow < finalPreviewDeadline &&
					(outputField.GetValue(form) is not byte[]
						|| !status.Text.StartsWith(expectedStatus[0], StringComparison.Ordinal)
						|| !canvas.ShowingRenderedPreview))
				{
					Application.DoEvents();
					Thread.Sleep(20);
				}
				modesReady &= outputField.GetValue(form) is byte[]
					&& status.Text.StartsWith(expectedStatus[0], StringComparison.Ordinal)
					&& canvas.ShowingRenderedPreview;
				string[] editorButtons = Descendants(form).OfType<Button>()
					.Select(button => button.Text).ToArray();
				bool ready = decoded.Length > 0 && canvas.IsOverFrameEditing && canvas.HasFrame
					&& canvas.ShowingRenderedPreview && modesReady
					&& editorButtons.Contains("真实 Alpha 预览")
					&& editorButtons.Contains("构图编辑")
					&& editorButtons.Contains("导出最终 PNG")
					&& frames.Count(BuiltInCardFrameCatalog.IsNormalFrame) == 16
					&& frames.Count(BuiltInCardFrameCatalog.IsTransparentFrame) == 16
					&& frames.Count(BuiltInCardFrameCatalog.IsTransparentGradientFrame) == 16
					&& frames.Count(BuiltInCardFrameCatalog.IsGradientFrame) == 16;
				ImageRenderSpec spec = canvas.RenderSpec;
				Console.WriteLine($"card={cardId}; source={art.Width}x{art.Height}; decoded={decoded.Length}; defaultOverframe={defaultOverframe}; modes={string.Join(',', choiceCounts)}; status={status.Text}; directCanvas={canvas.IsOverFrameEditing}; transform={spec.ImageScale:0.000}@{spec.OffsetX:0.0},{spec.OffsetY:0.0}; gameWrites=False; ready={ready}");
				string? screenshotPath = Environment.GetEnvironmentVariable("MDCT_OVERFRAME_CURRENT_SCREENSHOT");
				if (!string.IsNullOrWhiteSpace(screenshotPath))
				{
					using Bitmap screenshot = new(form.Width, form.Height);
					form.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0,
						screenshot.Width, screenshot.Height));
					screenshot.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
				}
				form.Close();
				if (!ready) Environment.ExitCode = 2;
			}
			finally
			{
				string fullTestRoot = Path.GetFullPath(testRoot);
				string safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MDCardModTool", "OverFrameEditorTests"))
					.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (fullTestRoot.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullTestRoot))
				{
					Directory.Delete(fullTestRoot, recursive: true);
				}
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-overframe-theme")
		{
			using OverFrameForm form = new(AppContext.BaseDirectory, null);
			form.Size = new System.Drawing.Size(1180, 700);
			form.CreateControl();
			form.PerformLayout();
			OverFrameMappingTable mappings = (OverFrameMappingTable)(typeof(OverFrameForm)
				.GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_mappings"));
			mappings.SetMappings(Enumerable.Range(1, 80).Select(index =>
				new OverFrameMapping((ushort)(3000 + index), (ushort)(index % 3 == 0 ? 0 : 3000 + index))));
			TextBox cardId = (TextBox)(typeof(OverFrameForm)
				.GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_cardId"));
			TextBox artId = (TextBox)(typeof(OverFrameForm)
				.GetField("_artId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_artId"));
			Button[] actions = Descendants(form).OfType<Button>().ToArray();
			bool ready = mappings.BackColor == UiTheme.Surface && mappings.ForeColor == UiTheme.Text
				&& mappings.MappingCount == 80 && mappings.HasVerticalScrollIndicator && !mappings.HasHorizontalScrollBar
				&& cardId.BorderStyle == BorderStyle.None && artId.BorderStyle == BorderStyle.None
				&& cardId.Parent is RoundedField && artId.Parent is RoundedField
				&& actions.Length == 4 && actions.All(button => button is RoundedButton);
			Console.WriteLine($"customTable={mappings.GetType().Name}; list={mappings.BackColor}; rows={mappings.MappingCount}; vertical={mappings.HasVerticalScrollIndicator}; horizontal={mappings.HasHorizontalScrollBar}; roundedFields={cardId.Parent is RoundedField && artId.Parent is RoundedField}; roundedButtons={actions.Count(button => button is RoundedButton)}/{actions.Length}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-card-catalog-merge")
		{
			CardCatalogEntry bundled = new()
			{
				CardId = 100,
				SimplifiedChineseName = "旧简中名",
				TraditionalChineseName = "繁中保留",
				JapaneseName = "日本語保持",
				EnglishName = "English Kept",
				Type = "Monster",
				SubType = "Fusion"
			};
			CardCatalogEntry installedLanguage = new()
			{
				CardId = 100,
				SimplifiedChineseName = "游戏内新名称",
				Type = "Monster",
				SubType = "0x01"
			};
			CardCatalogEntry newCard = new() { CardId = 101, SimplifiedChineseName = "更新后新卡", Type = "Monster" };
			IReadOnlyList<CardCatalogEntry> merged = CardCatalogService.MergeGameCatalogs([bundled], [installedLanguage, newCard]);
			CardCatalogEntry existing = merged.Single(entry => entry.CardId == 100);
			Console.WriteLine($"cards={merged.Count}; zh-cn={existing.SimplifiedChineseName}; zh-tw={existing.TraditionalChineseName}; type={existing.SubType}; new={merged.Any(entry => entry.CardId == 101)}");
			if (merged.Count != 2 || existing.SimplifiedChineseName != "游戏内新名称"
				|| existing.TraditionalChineseName != "繁中保留" || existing.JapaneseName != "日本語保持"
				|| existing.EnglishName != "English Kept" || existing.SubType != "Fusion")
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--extract-game-card-catalog")
		{
			IReadOnlyList<CardCatalogEntry> entries = GameCardCatalogUpdater.Extract(args[1], delegate(int done, int total, int found)
			{
				if (done % 250 == 0 || done == total)
				{
					Console.WriteLine($"{done:N0}/{total:N0}; located={found}/9");
				}
			});
			CardCatalogService.Write(args[2], entries);
			CardCatalogService catalog = new(entries);
			int zhCn = entries.Count(entry => entry.SimplifiedChineseName.Length > 0);
			int zhTw = entries.Count(entry => entry.TraditionalChineseName.Length > 0);
			int ja = entries.Count(entry => entry.JapaneseName.Length > 0);
			int en = entries.Count(entry => entry.EnglishName.Length > 0);
			Console.WriteLine($"cards={entries.Count:N0}; zh-cn={zhCn:N0}; zh-tw={zhTw:N0}; ja-jp={ja:N0}; en-us={en:N0}; output={Path.GetFullPath(args[2])}; bytes={new FileInfo(args[2]).Length:N0}");
			if (entries.Count < 10000 || Math.Max(Math.Max(zhCn, zhTw), Math.Max(ja, en)) < 10000)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-game-discovery")
		{
			GameInstallation installation = SteamGameDiscovery.FromPath(args[1]);
			Console.WriteLine($"root={installation.GameRoot}; build={installation.BuildId}; profiles={installation.Profiles.Count}");
			foreach (LocalDataProfile profile in installation.Profiles)
			{
				Console.WriteLine($"{profile.AccountId}; {profile.RootPath}; {profile.LastWriteTimeUtc:O}");
			}
			if (installation.Profiles.Count == 0)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-ui-interaction-render")
		{
			string gameRoot = Path.GetFullPath(args[1]);
			string outputRoot = Path.GetFullPath(args[2]);
			Directory.CreateDirectory(outputRoot);
			List<string> report = [];
			bool animationReady = false;
			bool overFrameReady = false;
			int animationRepaintDifference = int.MaxValue;
			int overFrameRepaintDifference = int.MaxValue;
			Point originalCursor = Cursor.Position;
			try
			{
				Cursor.Position = new Point(SystemInformation.VirtualScreen.Left + 2,
					SystemInformation.VirtualScreen.Top + 2);
				using (MainForm form = new MainForm
				{
					StartPosition = FormStartPosition.Manual,
					Location = new Point(8, 8),
					Size = new Size(1504, 912),
					ShowInTaskbar = false,
					TopMost = true
				})
				{
					typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, gameRoot);
					typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
					typeof(MainForm).GetField("_streamingRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
					TextBox gameFolder = GetPrivateField<TextBox>(form, "_gameFolder");
					gameFolder.Text = gameRoot;
					form.Show();
					PumpMessagesFor(300);

					IReadOnlyCollection<NavigationButton> navigation =
						GetPrivateField<IReadOnlyCollection<NavigationButton>>(form, "_navigationButtons");
					NavigationButton animationNavigation = navigation.Single(button => button.Page == WorkspacePage.Animation);
					NavigationButton cardsNavigation = navigation.Single(button => button.Page == WorkspacePage.Cards);
					animationNavigation.PerformClick();
					bool embeddedReady = PumpMessagesUntil(() =>
						typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
							is MonsterAnimationForm, 15000);
					MonsterAnimationForm? embedded = typeof(MainForm)
						.GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
						as MonsterAnimationForm;
					if (embeddedReady && embedded != null)
					{
						Task previewTask = embedded.PreviewCardAsync("3899");
						bool previewCompleted = PumpTask(previewTask, 60000);
						AnimationPreviewCanvas preview = GetPrivateField<AnimationPreviewCanvas>(embedded, "_preview");
						Button play = GetPrivateField<Button>(embedded, "_play");
						animationReady = previewCompleted && previewTask.IsCompletedSuccessfully
							&& embedded.LocatedCardId == "3899" && embedded.PreviewSourceCardId == "13668"
							&& preview.Frame != null;
						if (animationReady)
						{
							play.PerformClick();
							PumpMessagesFor(1450);
							play.PerformClick();
							PumpMessagesFor(120);
							if ((bool)(typeof(MonsterAnimationForm).GetField("_playing",
								BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded) ?? false))
							{
								play.PerformClick();
								PumpMessagesFor(120);
							}
							ExerciseRoundedButtonTransitions(form);
							form.Size = new Size(1440, 860);
							PumpMessagesFor(180);
							// Final capture stays above shell thumbnail overlays; larger layouts
							// were already exercised before this final resize.
							form.Size = new Size(1504, 800);
							PumpMessagesFor(260);
							cardsNavigation.PerformClick();
							PumpMessagesFor(180);
							animationNavigation.PerformClick();
							PumpMessagesFor(650);
							if ((bool)(typeof(MonsterAnimationForm).GetField("_playing",
								BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded) ?? false))
							{
								play.PerformClick();
								PumpMessagesFor(120);
							}
							play.Focus();
							PumpMessagesFor(350);
							using Bitmap interaction = CaptureClientFromScreen(form);
							interaction.Save(Path.Combine(outputRoot, "animation-3899-after-interactions.png"),
								System.Drawing.Imaging.ImageFormat.Png);
							form.PerformLayout();
							form.Invalidate(true);
							form.Update();
							PumpMessagesFor(80);
							using Bitmap forced = CaptureClientFromScreen(form);
							forced.Save(Path.Combine(outputRoot, "animation-3899-after-forced-repaint.png"),
								System.Drawing.Imaging.ImageFormat.Png);
							animationRepaintDifference = CountDifferentPixels(interaction, forced);
						}
					}
					report.Add($"animationReady={animationReady}; source={embedded?.PreviewSourceCardId}; repaintDifference={animationRepaintDifference}");
					form.Close();
				}

				string temporaryGameRoot = Path.Combine(Path.GetTempPath(),
					"MDCardModTool-ui-interaction-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(temporaryGameRoot);
				try
				{
					if (!PortableIndexService.TryLoadBundled(gameRoot, out GameIndex index, out _))
					{
						throw new InvalidDataException("交互回归无法读取随包卡图索引。");
					}
					TexRef art = index.Textures.First(texture => texture.SourceKind == "本地卡图"
						&& texture.CardKey == "3899");
					byte[] artBytes = new ModEngine().DecodePng(art);
					byte[] backgroundBytes = CreateInteractionBackground();
					TexRef[] frames = BuiltInCardFrameCatalog.Load().ToArray();
					TexRef transparentFrame = frames.First(BuiltInCardFrameCatalog.IsTransparentFrame);
					using OverFrameFrameEditorForm editor = new(temporaryGameRoot, art, frames,
						artBytes, transparentFrame.Name, backgroundBytes, replaceStoredBackground: true)
					{
						StartPosition = FormStartPosition.Manual,
						Location = new Point(8, 8),
						Size = new Size(1120, 900),
						ShowInTaskbar = false,
						TopMost = true
					};
					editor.Show();
					bool initiallyRendered = PumpMessagesUntil(() => EditorRenderSettled(editor), 60000);
					ModernComboBox mode = GetPrivateField<ModernComboBox>(editor, "_mode");
					ModernComboBox frame = GetPrivateField<ModernComboBox>(editor, "_frames");
					TrackBar zoom = GetPrivateField<TrackBar>(editor, "_zoom");
					RoundedButton finalPreview = GetPrivateField<RoundedButton>(editor, "_finalPreviewButton");
					RoundedButton editCanvas = GetPrivateField<RoundedButton>(editor, "_editCanvasButton");
					int initialZoom = zoom.Value;
					int initialFrame = frame.SelectedIndex;
					if (initiallyRendered && mode.Items.Count >= 4)
					{
						foreach (int modeIndex in new[] { 1, 2, 3, 0 })
						{
							mode.SelectedIndex = modeIndex;
							if (!PumpMessagesUntil(() => EditorRenderSettled(editor), 60000)) break;
						}
						editCanvas.PerformClick();
						zoom.Value = Math.Clamp(initialZoom + 25, zoom.Minimum, zoom.Maximum);
						PumpMessagesFor(220);
						if (frame.Items.Count > 1)
						{
							frame.SelectedIndex = (initialFrame + 1) % frame.Items.Count;
							PumpMessagesUntil(() => EditorRenderSettled(editor), 60000);
							frame.SelectedIndex = initialFrame;
							PumpMessagesUntil(() => EditorRenderSettled(editor), 60000);
						}
						zoom.Value = initialZoom;
						finalPreview.PerformClick();
						PumpMessagesUntil(() => EditorRenderSettled(editor), 60000);
						ExerciseRoundedButtonTransitions(editor);
						editor.Size = new Size(1040, 820);
						PumpMessagesFor(180);
						editor.Size = new Size(1120, 800);
						PumpMessagesFor(500);
						finalPreview.Focus();
						PumpMessagesFor(250);
						overFrameReady = EditorRenderSettled(editor);
						using Bitmap interaction = CaptureClientFromScreen(editor);
						interaction.Save(Path.Combine(outputRoot, "overframe-3899-after-interactions.png"),
							System.Drawing.Imaging.ImageFormat.Png);
						editor.PerformLayout();
						editor.Invalidate(true);
						editor.Update();
						PumpMessagesFor(80);
						using Bitmap forced = CaptureClientFromScreen(editor);
						forced.Save(Path.Combine(outputRoot, "overframe-3899-after-forced-repaint.png"),
							System.Drawing.Imaging.ImageFormat.Png);
						overFrameRepaintDifference = CountDifferentPixels(interaction, forced);
					}
					report.Add($"overFrameReady={overFrameReady}; modes={mode.Items.Count}; frames={frame.Items.Count}; background=True; repaintDifference={overFrameRepaintDifference}");
					editor.Close();
				}
				finally
				{
					if (Directory.Exists(temporaryGameRoot)) Directory.Delete(temporaryGameRoot, recursive: true);
				}
			}
			catch (Exception error)
			{
				report.Add("error=" + error);
			}
			finally
			{
				Cursor.Position = originalCursor;
			}

			bool ready = animationReady && overFrameReady
				&& animationRepaintDifference <= 64 && overFrameRepaintDifference <= 64;
			report.Add("ready=" + ready);
			File.WriteAllLines(Path.Combine(outputRoot, "interaction-report.txt"), report);
			foreach (string line in report) Console.WriteLine(line);
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-ui-render-layout")
		{
			bool captureScreen = string.Equals(Environment.GetEnvironmentVariable("MDCARDMODTOOL_SCREEN_CAPTURE"), "1",
				StringComparison.Ordinal);
			using MainForm form = new MainForm
			{
				StartPosition = FormStartPosition.Manual,
				Location = captureScreen ? new System.Drawing.Point(8, 8) : new System.Drawing.Point(-32000, -32000),
				Size = new System.Drawing.Size(1504, 912),
				ShowInTaskbar = false,
				TopMost = captureScreen
			};
			string temporaryRoot = Path.GetTempPath();
			typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, temporaryRoot);
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
			typeof(MainForm).GetField("_streamingRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
			TextBox gameFolder = (TextBox)(typeof(MainForm).GetField("_gameFolder", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_gameFolder"));
			gameFolder.Text = temporaryRoot;
			form.Show();
			Application.DoEvents();
			IReadOnlyCollection<NavigationButton> navigation = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm)
				.GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_navigationButtons"));
			navigation.Single(button => button.Page == WorkspacePage.Animation).PerformClick();
			Stopwatch wait = Stopwatch.StartNew();
			MonsterAnimationForm? embedded = null;
			while (wait.ElapsedMilliseconds < 5000 && embedded == null)
			{
				Application.DoEvents();
				embedded = typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					as MonsterAnimationForm;
				Thread.Sleep(10);
			}
			if (embedded == null)
			{
				throw new InvalidOperationException("Embedded animation workspace did not load.");
			}
			form.Size = new System.Drawing.Size(1460, 880);
			Application.DoEvents();
			form.Size = new System.Drawing.Size(1504, 912);
			UiTheme.QueueStableRepaint(form);
			Stopwatch settle = Stopwatch.StartNew();
			while (settle.ElapsedMilliseconds < 350)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}

			Control bannerTitle = Descendants(embedded).Single(control => control.Name == "MonsterAnimationBannerTitle");
			Control bannerSubtitle = Descendants(embedded).Single(control => control.Name == "MonsterAnimationBannerSubtitle");
			Button chooseMedia = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_chooseMedia"));
			Button play = (Button)(typeof(MonsterAnimationForm).GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_play"));
			Button apply = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_apply"));
			Button restore = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_restore"));
			Label sourceStatus = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_sourceStatus"));
			TableLayoutPanel buttonGrid = chooseMedia.Parent as TableLayoutPanel
				?? throw new InvalidOperationException("Animation button grid missing.");
			Button[] animationButtons = [chooseMedia, play, apply, restore];
			Control? bannerParent = bannerSubtitle.Parent;
			bool bannerClean = bannerParent != null && bannerTitle.Parent == bannerParent
				&& bannerTitle.Bottom <= bannerSubtitle.Top
				&& bannerSubtitle.Bottom <= bannerParent.ClientSize.Height;
			bool actionGridClean = animationButtons.All(button => buttonGrid.ClientRectangle.Contains(button.Bounds))
				&& sourceStatus.Parent == buttonGrid.Parent && sourceStatus.Bottom <= buttonGrid.Top;
			bool navigationClean = navigation.All(button => button.Parent != null
				&& button.Parent.ClientRectangle.Contains(button.Bounds) && button.Height >= 40);
			bool buttonBuffersClean = Descendants(form).OfType<RoundedButton>()
				.All(button => !button.UsesSharedOptimizedBuffer);

			string output = Path.GetFullPath(args[1]);
			Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
			using Bitmap bitmap = new(form.ClientSize.Width, form.ClientSize.Height);
			if (captureScreen)
			{
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.CopyFromScreen(form.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty,
					form.ClientSize, CopyPixelOperation.SourceCopy);
			}
			else
			{
				form.DrawToBitmap(bitmap, form.ClientRectangle);
			}
			bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
			bool ready = bannerClean && actionGridClean && navigationClean && buttonBuffersClean;
			Console.WriteLine($"capture={output}; banner={bannerClean}:{bannerTitle.Bounds}/{bannerSubtitle.Bounds}/{bannerParent?.ClientSize}; actions={actionGridClean}; navigation={navigationClean}; button-buffers={buttonBuffersClean}; ready={ready}");
			form.Close();
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-layout")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Size = new System.Drawing.Size(1180, 760);
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
			form.Show();
			Application.DoEvents();
			form.PerformLayout();
			Button visualButton = (Button)(typeof(MainForm).GetField("_visualAssetsButton", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_visualAssetsButton"));
			ComboBox profiles = (ComboBox)(typeof(MainForm).GetField("_profileSelector", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_profileSelector"));
			ComboBox languages = (ComboBox)(typeof(MainForm).GetField("_languageSelector", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_languageSelector"));
			Control cardSearchField = (Control)(typeof(MainForm).GetField("_cardSearchField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_cardSearchField"));
			PictureBox resourcePreview = (PictureBox)(typeof(MainForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_preview"));
			TableLayoutPanel visualShortcuts = (TableLayoutPanel)(typeof(MainForm).GetField("_visualShortcutBar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_visualShortcutBar"));
			IReadOnlyCollection<NavigationButton> navigation = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm).GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_navigationButtons"));
			Panel resourcePage = (Panel)(typeof(MainForm).GetField("_resourcePage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_resourcePage"));
			Panel animationPage = (Panel)(typeof(MainForm).GetField("_animationPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_animationPage"));
			Panel animationHost = (Panel)(typeof(MainForm).GetField("_animationHost", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_animationHost"));
			Panel settingsPage = (Panel)(typeof(MainForm).GetField("_settingsPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_settingsPage"));
			TableLayoutPanel resourceActionGrid = (TableLayoutPanel)(typeof(MainForm).GetField("_resourceActionGrid", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_resourceActionGrid"));
			string[] resourceActions = Descendants(resourcePage).OfType<Button>().Select(button => button.Text).ToArray();
			string[] animationActions = Descendants(animationPage).OfType<Button>().Select(button => button.Text).ToArray();
			string[] settingsActions = Descendants(settingsPage).OfType<Button>().Select(button => button.Text).ToArray();
			bool clipped = form.Controls.Cast<Control>().Any(control => control.Right > form.ClientSize.Width || control.Bottom > form.ClientSize.Height);
			NavigationButton animationNavigation = navigation.Single(button => button.Page == WorkspacePage.Animation);
			NavigationButton cardsNavigation = navigation.Single(button => button.Page == WorkspacePage.Cards);
			Stopwatch pageSwitchClock = Stopwatch.StartNew();
			animationNavigation.PerformClick();
			pageSwitchClock.Stop();
			long firstSwitchMilliseconds = pageSwitchClock.ElapsedMilliseconds;
			bool animationLoadingFrame = animationPage.Visible && animationHost.Controls.OfType<Label>()
				.Any(label => label.Text == Localizer.T("page.animation.loading"));
			bool cardSearchHiddenOnAnimation = !cardSearchField.Visible;
			Application.DoEvents();
			bool animationPageSwitch = animationPage.Visible && !resourcePage.Visible && animationNavigation.Selected;
			MonsterAnimationForm? embeddedAnimation = typeof(MainForm).GetField("_embeddedAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) as MonsterAnimationForm;
			Control[] embeddedControls = embeddedAnimation == null ? [] : Descendants(embeddedAnimation).ToArray();
			bool animationLayoutReady = embeddedAnimation == null ||
				(embeddedControls.OfType<NumericUpDown>().All(control => !control.Visible || (control.Width >= 80 && control.Height >= 20))
				&& embeddedControls.OfType<Button>().All(control => !control.Visible || control.Height >= 24));
			cardsNavigation.PerformClick();
			Application.DoEvents();
			bool cardsPageSwitch = resourcePage.Visible && !animationPage.Visible && cardsNavigation.Selected
				&& cardSearchField.Visible;
			pageSwitchClock.Restart();
			animationNavigation.PerformClick();
			pageSwitchClock.Stop();
			long returnSwitchMilliseconds = pageSwitchClock.ElapsedMilliseconds;
			Application.DoEvents();
			bool animationReturnSwitch = animationPage.Visible && animationNavigation.Selected;
			using NavigationButton paletteProbe = new()
			{
				Page = WorkspacePage.Cards,
				Size = new System.Drawing.Size(180, 44)
			};
			paletteProbe.CreateControl();
			paletteProbe.Selected = true;
			MethodInfo beginTransition = typeof(RoundedButton).GetMethod("BeginTransition", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(nameof(RoundedButton), "BeginTransition");
			beginTransition.Invoke(paletteProbe, [paletteProbe.HoverColor]);
			paletteProbe.Selected = false;
			Stopwatch paletteClock = Stopwatch.StartNew();
			while (paletteClock.ElapsedMilliseconds < 220)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			bool navigationPaletteStable = paletteProbe.BackColor.ToArgb() == paletteProbe.NormalColor.ToArgb();
			bool ready = Application.HighDpiMode == HighDpiMode.PerMonitorV2
				&& visualButton.Parent != null && form.Text.Contains("2.0", StringComparison.Ordinal)
				&& profiles.Parent != null && languages.Parent != null && languages.Items.Count == 4
				&& resourcePreview is AlphaPreviewBox
				&& navigation.Count == 4 && !navigation.Any(button => button.Page is WorkspacePage.Frames or WorkspacePage.Mods)
				&& visualShortcuts.ColumnCount == 8 && !visualShortcuts.AutoScroll && !clipped
				&& resourceActionGrid.ColumnCount == 2 && resourceActionGrid.RowCount == 4
				&& resourceActionGrid.Controls.OfType<Button>().Count() == 8 && !resourceActionGrid.AutoScroll
				&& resourceActions.Contains("制作超框")
				&& resourceActions.Contains("管理超框登记") && resourceActions.Contains("一键导出全部 Mod")
				&& resourceActions.Contains("预览怪兽动画") && animationPageSwitch && animationLoadingFrame && animationLayoutReady && cardsPageSwitch
				&& cardSearchHiddenOnAnimation && animationReturnSwitch && navigationPaletteStable
				&& firstSwitchMilliseconds < 750 && returnSwitchMilliseconds < 750
				&& !animationActions.Any(text => text.Contains("运行时扩展", StringComparison.Ordinal))
				&& !settingsActions.Any(text => text.Contains("运行时扩展", StringComparison.Ordinal));
			Console.WriteLine($"title={form.Text}; dpi={Application.HighDpiMode}; alphaPreview={resourcePreview is AlphaPreviewBox}; visualButton={visualButton.Text}; profiles={profiles.Items.Count}; languages={languages.Items.Count}; navigation={navigation.Count}; cardSearchHiddenOnAnimation={cardSearchHiddenOnAnimation}; navigationPaletteStable={navigationPaletteStable}; shortcutColumns={visualShortcuts.ColumnCount}; shortcutScroll={visualShortcuts.AutoScroll}; resourceGrid={resourceActionGrid.ColumnCount}x{resourceActionGrid.RowCount}:{resourceActionGrid.Controls.Count}; resourceScroll={resourceActionGrid.AutoScroll}; animationSwitch={animationPageSwitch}; animationLoading={animationLoadingFrame}; animationLayout={animationLayoutReady}; firstSwitchMs={firstSwitchMilliseconds}; returnSwitchMs={returnSwitchMilliseconds}; cardsSwitch={cardsPageSwitch}; cardFrameActions={resourceActions.Count(text => text.Contains("框", StringComparison.Ordinal))}; runtimeOnAnimation={animationActions.Any(text => text.Contains("运行时扩展", StringComparison.Ordinal))}; runtimeInSettings={settingsActions.Any(text => text.Contains("运行时扩展", StringComparison.Ordinal))}; clipped={clipped}; ready={ready}");
			form.Close();
			if (!ready)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-search")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Show();
			TextBox search = (TextBox)(typeof(MainForm).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_search"));
			ListBox suggestions = (ListBox)(typeof(MainForm).GetField("_searchSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_searchSuggestions"));
			search.Focus();
			search.Text = "独角";
			search.SelectionStart = search.TextLength;
			Stopwatch debounce = Stopwatch.StartNew();
			while (debounce.ElapsedMilliseconds < 800 && suggestions.Items.Count == 0)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			object? first = suggestions.Items.Cast<object>().FirstOrDefault();
			string primary = first?.GetType().GetProperty("Primary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(first)?.ToString() ?? "";
			CardCatalogEntry? salamangreat = suggestions.Items.Cast<object>()
				.Select(item => item.GetType().GetProperty("Entry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) as CardCatalogEntry)
				.FirstOrDefault(entry => entry?.CardId == 14338);
			bool ready = suggestions.Items.Count > 0 && salamangreat != null && search.Focused
				&& search.SelectionStart == search.TextLength;
			Console.WriteLine($"query={search.Text}; suggestions={suggestions.Items.Count}; first={primary}; has14338={salamangreat != null}; focused={search.Focused}; caret={search.SelectionStart}; ready={ready}");
			form.Close();
			if (!ready)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-frames")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Size = new System.Drawing.Size(1180, 760);
			form.Show();
			Application.DoEvents();
			form.PerformLayout();
			IReadOnlyCollection<NavigationButton> navigation = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm)
				.GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_navigationButtons"));
			Panel resourcePage = (Panel)(typeof(MainForm).GetField("_resourcePage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_resourcePage"));
			Control searchField = (Control)(typeof(MainForm).GetField("_cardSearchField", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_cardSearchField"));
			Panel contextBar = (Panel)(typeof(MainForm).GetField("_resourceContextBar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_resourceContextBar"));
			FlowLayoutPanel modContext = (FlowLayoutPanel)(typeof(MainForm).GetField("_modContextActions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_modContextActions"));
			FlowLayoutPanel overFrameContext = (FlowLayoutPanel)(typeof(MainForm).GetField("_overFrameContextActions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_overFrameContextActions"));
			TreeView groups = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_groups"));
			List<TexRef> textures = (List<TexRef>)(typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_textures"));
			textures.Clear();
			textures.Add(new TexRef
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
			string[] frameCategories =
			[
				BuiltInCardFrameCatalog.TransparentCategory,
				BuiltInCardFrameCatalog.TransparentGradientCategory,
				BuiltInCardFrameCatalog.GradientCategory,
				BuiltInCardFrameCatalog.NormalCategory
			];
			for (int index = 0; index < frameCategories.Length; index++)
			{
				textures.Add(new TexRef
				{
					BundlePath = $"frame-{index}.png",
					RelativeBundlePath = $"frame-{index}.png",
					Name = $"diagnostic_frame_{index}",
					Width = FrameComposer.Width,
					Height = FrameComposer.Height,
					SourceKind = BuiltInCardFrameCatalog.SourceKind,
					Category = frameCategories[index]
				});
			}
			MethodInfo refreshCategories = typeof(MainForm).GetMethod("RefreshCategories", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(nameof(MainForm), "RefreshCategories");
			refreshCategories.Invoke(form, null);
			TreeNode? frameSourceNode = groups.Nodes.Cast<TreeNode>().FirstOrDefault(node =>
				node.Nodes.Cast<TreeNode>().Any(child =>
					(child.Tag as string)?.StartsWith(BuiltInCardFrameCatalog.SourceKind + "|",
						StringComparison.Ordinal) == true));
			string[] actualFrameCategoryOrder = frameSourceNode?.Nodes.Cast<TreeNode>()
				.Select(node => (node.Tag as string ?? "").Split('|').Last()).ToArray() ?? [];
			TreeNode? overFrameNode = groups.Nodes.Cast<TreeNode>().SelectMany(node => node.Nodes.Cast<TreeNode>())
				.FirstOrDefault(node => string.Equals(node.Tag as string, "本地卡图|超框卡图", StringComparison.Ordinal));
			groups.SelectedNode = overFrameNode;
			Application.DoEvents();
			bool overFrameContextVisible = contextBar.Visible && overFrameContext.Visible && !modContext.Visible;
			groups.SelectedNode = groups.Nodes.Cast<TreeNode>().FirstOrDefault(node => string.Equals(node.Tag as string, "__mods__", StringComparison.Ordinal));
			Application.DoEvents();
			bool modContextVisible = contextBar.Visible && modContext.Visible && !overFrameContext.Visible;
			bool contextActions = Descendants(resourcePage).OfType<Button>().Any(button => button.Text == "管理超框登记")
				&& Descendants(resourcePage).OfType<Button>().Any(button => button.Text == "一键导出全部 Mod");

			using OverFrameForm manager = new(AppContext.BaseDirectory, null);
			manager.Size = new System.Drawing.Size(1180, 700);
			manager.CreateControl();
			manager.PerformLayout();
			OverFrameMappingTable mappings = (OverFrameMappingTable)(typeof(OverFrameForm)
				.GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_mappings"));
			mappings.SetMappings(Enumerable.Range(1, 90).Select(index =>
				new OverFrameMapping((ushort)(3000 + index), (ushort)(index % 4 == 0 ? 0 : 3000 + index))));
			TextBox cardId = (TextBox)(typeof(OverFrameForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_cardId"));
			Button[] managerActions = Descendants(manager).OfType<Button>().ToArray();
			bool ready = navigation.Count == 4 && !navigation.Any(button => button.Page is WorkspacePage.Frames or WorkspacePage.Mods)
				&& searchField.Visible && contextActions && overFrameContextVisible && modContextVisible && mappings.MappingCount == 90
				&& actualFrameCategoryOrder.SequenceEqual(frameCategories)
				&& mappings.HasVerticalScrollIndicator && !mappings.HasHorizontalScrollBar
				&& cardId.Parent is RoundedField && managerActions.Length == 4
				&& managerActions.All(button => button is RoundedButton);
			Console.WriteLine($"navigation={navigation.Count}; standaloneFrames={navigation.Any(button => button.Page == WorkspacePage.Frames)}; standaloneMods={navigation.Any(button => button.Page == WorkspacePage.Mods)}; cardSearch={searchField.Visible}; frameCategories={string.Join('>', actualFrameCategoryOrder)}; overFrameContext={overFrameContextVisible}; modContext={modContextVisible}; contextActions={contextActions}; table={mappings.GetType().Name}:{mappings.MappingCount}; vertical={mappings.HasVerticalScrollIndicator}; horizontal={mappings.HasHorizontalScrollBar}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-main-form-frames-live-scan")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			typeof(MainForm).GetField("_assetRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, null);
			form.Show();
			Application.DoEvents();
			typeof(MainForm).GetField("_gameRoot", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, args[1]);
			typeof(MainForm).GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(form, new GameIndex());
			FieldInfo cancellationField = typeof(MainForm).GetField("_backgroundRefreshCancellation", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingFieldException(nameof(MainForm), "_backgroundRefreshCancellation");
			FieldInfo taskField = typeof(MainForm).GetField("_backgroundRefreshTask", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingFieldException(nameof(MainForm), "_backgroundRefreshTask");
			CancellationTokenSource cancellation = new();
			int scanDone = 0;
			int scanTotal = 0;
			Exception? scanError = null;
			Task scanTask = Task.Run(() =>
			{
				try
				{
					GameCardCatalogUpdater.Extract(args[1], (done, total, _) =>
					{
						Volatile.Write(ref scanDone, done);
						Volatile.Write(ref scanTotal, total);
					}, cancellation.Token);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception error)
				{
					scanError = error;
				}
			});
			cancellationField.SetValue(form, cancellation);
			taskField.SetValue(form, scanTask);
			Stopwatch warmup = Stopwatch.StartNew();
			while (Volatile.Read(ref scanDone) < 100 && !scanTask.IsCompleted && warmup.ElapsedMilliseconds < 20000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			bool scanWasActive = Volatile.Read(ref scanDone) >= 100 && !scanTask.IsCompleted;
			MethodInfo openManager = typeof(MainForm).GetMethod("OpenOverFrameTable", BindingFlags.Instance | BindingFlags.NonPublic)
				?? throw new MissingMethodException(nameof(MainForm), "OpenOverFrameTable");
			Stopwatch openClock = Stopwatch.StartNew();
			openManager.Invoke(form, null);
			openClock.Stop();
			long maxPumpMilliseconds = 0;
			Stopwatch completion = Stopwatch.StartNew();
			OverFrameForm? manager = null;
			while ((!scanTask.IsCompleted || manager == null) && completion.ElapsedMilliseconds < 10000)
			{
				Stopwatch pump = Stopwatch.StartNew();
				Application.DoEvents();
				pump.Stop();
				maxPumpMilliseconds = Math.Max(maxPumpMilliseconds, pump.ElapsedMilliseconds);
				manager = typeof(MainForm).GetField("_overFrameManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) as OverFrameForm;
				Thread.Sleep(10);
			}
			OverFrameMappingTable? mappings = manager == null ? null : typeof(OverFrameForm)
				.GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager) as OverFrameMappingTable;
			bool ready = scanWasActive && scanError == null && cancellation.IsCancellationRequested && scanTask.IsCompleted
				&& openClock.ElapsedMilliseconds < 250 && maxPumpMilliseconds < 750
				&& manager is { Visible: true } && mappings != null && !mappings.HasHorizontalScrollBar;
			Console.WriteLine($"scan={scanDone}/{scanTotal}; active={scanWasActive}; cancelled={cancellation.IsCancellationRequested}; complete={scanTask.IsCompleted}; openMs={openClock.ElapsedMilliseconds}; maxPumpMs={maxPumpMilliseconds}; manager={manager?.Visible}; customTable={mappings?.GetType().Name}; error={scanError?.Message}; ready={ready}");
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-main-form-frames-live-scan")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Size = new System.Drawing.Size(1180, 760);
			form.Show();
			Application.DoEvents();
			FieldInfo cancellationField = typeof(MainForm).GetField("_backgroundRefreshCancellation",
				BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(nameof(MainForm), "_backgroundRefreshCancellation");
			FieldInfo taskField = typeof(MainForm).GetField("_backgroundRefreshTask",
				BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(nameof(MainForm), "_backgroundRefreshTask");
			FieldInfo indexField = typeof(MainForm).GetField("_index",
				BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(nameof(MainForm), "_index");
			Stopwatch initialLoad = Stopwatch.StartNew();
			while ((indexField.GetValue(form) == null || taskField.GetValue(form) == null) && initialLoad.ElapsedMilliseconds < 15000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			(cancellationField.GetValue(form) as CancellationTokenSource)?.Cancel();
			Task? previousTask = taskField.GetValue(form) as Task;
			bool initialReady = indexField.GetValue(form) != null && previousTask != null;
			Stopwatch settle = Stopwatch.StartNew();
			while (previousTask is { IsCompleted: false } && settle.ElapsedMilliseconds < 10000)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			int scanDone = 0;
			int scanTotal = 0;
			int scanFound = 0;
			Exception? scanError = null;
			CancellationTokenSource scanCancellation = new();
			Task scanTask = Task.Run(() =>
			{
				try
				{
					GameCardCatalogUpdater.Extract(args[1], (done, total, found) =>
					{
						Volatile.Write(ref scanDone, done);
						Volatile.Write(ref scanTotal, total);
						Volatile.Write(ref scanFound, found);
					}, scanCancellation.Token);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					scanError = ex;
				}
			});
			cancellationField.SetValue(form, scanCancellation);
			taskField.SetValue(form, scanTask);
			long maxPumpMilliseconds = 0;
			Stopwatch scanWarmup = Stopwatch.StartNew();
			while (Volatile.Read(ref scanDone) < 100 && !scanTask.IsCompleted && scanWarmup.ElapsedMilliseconds < 20000)
			{
				Stopwatch pump = Stopwatch.StartNew();
				Application.DoEvents();
				pump.Stop();
				maxPumpMilliseconds = Math.Max(maxPumpMilliseconds, pump.ElapsedMilliseconds);
				Thread.Sleep(10);
			}
			bool scanWasActive = Volatile.Read(ref scanDone) >= 100 && !scanTask.IsCompleted;
			IReadOnlyCollection<NavigationButton> navigation = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm)
				.GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_navigationButtons"));
			Panel framesPage = (Panel)(typeof(MainForm).GetField("_framesPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_framesPage"));
			NavigationButton framesNavigation = navigation.Single(button => button.Page == WorkspacePage.Frames);
			Stopwatch switchClock = Stopwatch.StartNew();
			framesNavigation.PerformClick();
			switchClock.Stop();
			bool loadingFrame = framesPage.Visible && framesPage.Controls.Cast<Control>()
				.SelectMany(Descendants).OfType<Label>().Any(label => label.Text == Localizer.T("page.frames.loading"));
			Stopwatch completion = Stopwatch.StartNew();
			OverFrameForm? embedded = null;
			while (completion.ElapsedMilliseconds < 5000 && (embedded == null || !scanTask.IsCompleted))
			{
				Stopwatch pump = Stopwatch.StartNew();
				Application.DoEvents();
				pump.Stop();
				maxPumpMilliseconds = Math.Max(maxPumpMilliseconds, pump.ElapsedMilliseconds);
				embedded = typeof(MainForm).GetField("_embeddedFrames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) as OverFrameForm;
				Thread.Sleep(10);
			}
			bool ready = initialReady && previousTask is not { IsCompleted: false } && scanWasActive && scanError == null
				&& scanCancellation.IsCancellationRequested && scanTask.IsCompleted
				&& switchClock.ElapsedMilliseconds < 250 && maxPumpMilliseconds < 750
				&& loadingFrame && framesPage.Visible && framesNavigation.Selected && embedded is { Visible: true };
			Console.WriteLine($"initialReady={initialReady}; scan={scanDone}/{scanTotal}:{scanFound}; active={scanWasActive}; cancelled={scanCancellation.IsCancellationRequested}; scanComplete={scanTask.IsCompleted}; switchMs={switchClock.ElapsedMilliseconds}; maxPumpMs={maxPumpMilliseconds}; loading={loadingFrame}; embedded={embedded?.Visible}; error={scanError?.Message}; ready={ready}");
			form.Close();
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-frames")
		{
			using MainForm form = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Size = new System.Drawing.Size(1180, 760);
			form.Show();
			Application.DoEvents();
			IReadOnlyCollection<NavigationButton> navigation = (IReadOnlyCollection<NavigationButton>)(typeof(MainForm)
				.GetField("_navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_navigationButtons"));
			Panel framesPage = (Panel)(typeof(MainForm).GetField("_framesPage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_framesPage"));
			FieldInfo backgroundCancellationField = typeof(MainForm).GetField("_backgroundRefreshCancellation",
				BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(nameof(MainForm), "_backgroundRefreshCancellation");
			CancellationTokenSource? backgroundBefore = backgroundCancellationField.GetValue(form) as CancellationTokenSource;
			if (backgroundBefore == null)
			{
				backgroundBefore = new CancellationTokenSource();
				backgroundCancellationField.SetValue(form, backgroundBefore);
			}
			NavigationButton framesNavigation = navigation.Single(button => button.Page == WorkspacePage.Frames);
			Stopwatch clock = Stopwatch.StartNew();
			framesNavigation.PerformClick();
			clock.Stop();
			bool framesLoadingFrame = framesPage.Visible && framesPage.Controls.Cast<Control>()
				.SelectMany(Descendants).OfType<Label>().Any(label => label.Text == Localizer.T("page.frames.loading"));
			bool backgroundPaused = backgroundBefore.IsCancellationRequested;
			Application.DoEvents();
			ToolStripStatusLabel status = (ToolStripStatusLabel)(typeof(MainForm)
				.GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_status"));
			int statusUpdates = 0;
			status.TextChanged += delegate { statusUpdates++; };
			Stopwatch sustained = Stopwatch.StartNew();
			long maxPumpMilliseconds = 0;
			long maxLoopGapMilliseconds = 0;
			long previousLoopAt = sustained.ElapsedMilliseconds;
			while (sustained.ElapsedMilliseconds < 2500)
			{
				Stopwatch pump = Stopwatch.StartNew();
				Application.DoEvents();
				pump.Stop();
				maxPumpMilliseconds = Math.Max(maxPumpMilliseconds, pump.ElapsedMilliseconds);
				long loopAt = sustained.ElapsedMilliseconds;
				maxLoopGapMilliseconds = Math.Max(maxLoopGapMilliseconds, loopAt - previousLoopAt);
				previousLoopAt = loopAt;
				Thread.Sleep(10);
			}
			OverFrameForm embedded = (OverFrameForm)(typeof(MainForm).GetField("_embeddedFrames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException(nameof(MainForm), "_embeddedFrames"));
			ListView mappings = (ListView)(typeof(OverFrameForm).GetField("_mappings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_mappings"));
			TextBox cardId = (TextBox)(typeof(OverFrameForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(embedded)
				?? throw new MissingFieldException(nameof(OverFrameForm), "_cardId"));
			Button[] embeddedActions = Descendants(embedded).OfType<Button>().ToArray();
			bool controlsInside = embeddedActions.All(button => button.Bottom <= embedded.ClientSize.Height)
				&& cardId.Parent is RoundedField field && field.Height is >= 32 and <= 60;
			bool responsive = maxPumpMilliseconds < 750 && maxLoopGapMilliseconds < 1000 && statusUpdates < 80;
			bool ready = framesPage.Visible && framesNavigation.Selected && embedded.Visible
				&& clock.ElapsedMilliseconds < 750 && framesLoadingFrame && backgroundPaused && responsive && controlsInside
				&& mappings.OwnerDraw && mappings.BackColor == UiTheme.Surface
				&& embeddedActions.Length == 4 && embeddedActions.All(button => button is RoundedButton);
			Console.WriteLine($"switchMs={clock.ElapsedMilliseconds}; maxPumpMs={maxPumpMilliseconds}; maxGapMs={maxLoopGapMilliseconds}; statusUpdates={statusUpdates}; loadingFrame={framesLoadingFrame}; backgroundPaused={backgroundPaused}; page={framesPage.Visible}; embedded={embedded.Visible}; inputHeight={cardId.Parent?.Height}; actions={embeddedActions.Length}; controlsInside={controlsInside}; ownerDraw={mappings.OwnerDraw}; responsive={responsive}; ready={ready}");
			form.Close();
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form-list")
		{
			using MainForm form = new MainForm();
			form.CreateControl();
			List<TexRef> textures = (List<TexRef>)(typeof(MainForm).GetField("_textures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_textures"));
			ListView list = (ListView)(typeof(MainForm).GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_list"));
			TreeView groups = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_groups"));
			ComboBox category = (ComboBox)(typeof(MainForm).GetField("_category", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_category"));
			Label resultCount = (Label)(typeof(MainForm).GetField("_resultCount", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_resultCount"));
			MethodInfo refreshCategories = typeof(MainForm).GetMethod("RefreshCategories", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RefreshCategories");
			MethodInfo renderList = typeof(MainForm).GetMethod("RenderList", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("RenderList");
			MethodInfo selectTexture = typeof(MainForm).GetMethod("SelectTexture", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("SelectTexture");
			MethodInfo selectedTexture = typeof(MainForm).GetMethod("Selected", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("Selected");
			List<TexRef> visibleTextures = (List<TexRef>)(typeof(MainForm).GetField("_visibleTextures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) ?? throw new MissingFieldException("_visibleTextures"));
			for (int i = 0; i < 18000; i++)
			{
				textures.Add(new TexRef
				{
					BundlePath = $"diagnostic/{i:D5}",
					RelativeBundlePath = $"diagnostic/{i:D5}",
					Name = $"Card_{i:D5}",
					CardKey = (10000 + i).ToString(),
					Width = 512,
					Height = i % 2 == 0 ? 512 : 1024,
					SourceKind = "本地卡图",
					Category = i % 2 == 0 ? "卡图缩略图" : "灵摆卡图"
				});
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			refreshCategories.Invoke(form, null);
			renderList.Invoke(form, null);
			int allCount = list.Items.Count;
			category.SelectedIndex = 1;
			int filteredCount = list.Items.Count;
			_ = list.Handle;
			TexRef expectedSelection = visibleTextures[Math.Min(123, visibleTextures.Count - 1)];
			selectTexture.Invoke(form, [expectedSelection]);
			Application.DoEvents();
			TexRef? actualSelection = selectedTexture.Invoke(form, null) as TexRef;
			bool virtualRowReady = ReferenceEquals(list.Items[Math.Min(123, list.Items.Count - 1)].Tag, expectedSelection);
			stopwatch.Stop();
			bool ready = allCount == 18000 && filteredCount == 9000 && groups.Nodes.Count >= 2
				&& category.Items.Count >= 3 && ReferenceEquals(actualSelection, expectedSelection) && virtualRowReady
				&& !resultCount.Text.Contains(Localizer.T("list.updating"), StringComparison.Ordinal);
			Console.WriteLine($"all={allCount}; filtered={filteredCount}; groups={groups.Nodes.Count}; categories={category.Items.Count}; virtualSelection={ReferenceEquals(actualSelection, expectedSelection)}; result={resultCount.Text}; elapsedMs={stopwatch.ElapsedMilliseconds}; ready={ready}");
			if (!ready)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--scan-visual-assets")
		{
			VisualAssetScanResult result = VisualAssetIndexService.Scan(args[1], delegate(int done, int total, int found)
			{
				if (done % 250 == 0 || done == total)
				{
					Console.WriteLine($"{done}/{total}; visual textures={found}");
				}
			});
			foreach (IGrouping<string, TexRef> category in result.Textures.GroupBy((TexRef x) => x.Category).OrderBy((IGrouping<string, TexRef> x) => x.Key))
			{
				Console.WriteLine($"{category.Key}={category.Count()}");
			}
			Console.WriteLine($"catalog={result.CatalogEntries}; candidates={result.CandidateBundles}; installed={result.InstalledBundles}; textures={result.Textures.Count}");
			return;
		}
		if (args.Length == 3 && args[0] == "--test-visual-write-suite")
		{
			VisualAssetScanResult visual = VisualAssetIndexService.Scan(args[1]);
			string[] names = new string[3]
			{
				"ShopBGBase02",
				"Mat_002_05_BaseColor_near",
				"WallPaper0001_1"
			};
			foreach (string name in names)
			{
				TexRef source = visual.Textures.FirstOrDefault((TexRef x) => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("视觉资源目录中找不到 " + name + "。");
				TestVisualTextureWrite(source, Path.Combine(args[2], name));
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-visual-texture-write")
		{
			VisualAssetScanResult visual = VisualAssetIndexService.Scan(args[1]);
			TexRef source = visual.Textures.FirstOrDefault((TexRef x) => string.Equals(x.Name, args[2], StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("视觉资源目录中找不到 " + args[2] + "。");
			TestVisualTextureWrite(source, args[3]);
			return;
		}
		if (args.Length == 3 && args[0] == "--build-animation-index")
		{
			PortableMonsterAnimationIndex index = MonsterAnimationIndexService.Rebuild(args[1], delegate(int done, int total, int found)
			{
				Console.WriteLine($"{done}/{total}; animation assets={found}");
			});
			MonsterAnimationIndexService.Export(args[1], index, args[2]);
			Console.WriteLine($"build={index.GameBuildId}; assets={index.Assets.Count}; cards={index.Assets.Select((MonsterAnimationAssetRef monsterAnimationAssetRef) => monsterAnimationAssetRef.CardId).Distinct().Count()}; {args[2]}");
			return;
		}
		if ((args.Length == 2 || args.Length == 3) && args[0] == "--scan-spine42-compat")
		{
			string gameRoot = Path.GetFullPath(args[1]);
			PortableMonsterAnimationIndex animationIndex = MonsterAnimationIndexService.EnsureCurrentIndex(gameRoot,
				(done, total, found) =>
				{
					if (done % 500 == 0 || done == total)
						Console.WriteLine($"index {done:N0}/{total:N0}; assets={found:N0}");
				});
			List<MonsterAnimationAssetRef> installed = MonsterAnimationIndexService.LoadBestAvailable(gameRoot, out string compatibilityBuildId)
				.Where(asset => File.Exists(asset.BundlePath)).ToList();
			List<MonsterAnimationSet> sets = installed.GroupBy(asset => asset.CardId, StringComparer.Ordinal)
				.Select(group => new MonsterAnimationSet { CardId = group.Key, Assets = group.ToList() })
				.Where(set => set.IsComplete)
				.OrderBy(set => int.TryParse(set.CardId, out int value) ? value : int.MaxValue).ToList();
			Spine42CompatibilityResult[] probeResults = new Spine42CompatibilityResult[sets.Count];
			int completedProbes = 0;
			Parallel.For(0, sets.Count, new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6)
			}, i =>
			{
				Spine42CompatibilityResult result = Spine42PreviewRenderer.Probe(sets[i]);
				probeResults[i] = result;
				int completed = Interlocked.Increment(ref completedProbes);
				if (!result.Success || result.UnsupportedFeatures.Count > 0 || completed % 25 == 0 || completed == sets.Count)
					Console.WriteLine($"probe {completed:N0}/{sets.Count:N0}; card={result.CardId}; success={result.Success}; opaque={result.OpaquePixels:N0}; diagnostics={string.Join('|', result.UnsupportedFeatures)}; {result.Message}");
			});
			List<Spine42CompatibilityResult> results = probeResults.ToList();
			if (args.Length == 3)
			{
				string reportPath = Path.GetFullPath(args[2]);
				Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
				File.WriteAllText(reportPath, JsonSerializer.Serialize(new
				{
					FormatVersion = 1,
					GameBuildId = compatibilityBuildId.Length > 0 ? compatibilityBuildId : animationIndex.GameBuildId,
					GeneratedUtc = DateTimeOffset.UtcNow,
					Results = results
				}, new JsonSerializerOptions { WriteIndented = true }));
			}
			int failures = results.Count(result => !result.Success);
			int warnings = results.Count(result => result.Success && result.UnsupportedFeatures.Count > 0);
			int blank = results.Count(result => result.Success && result.OpaquePixels == 0);
			Console.WriteLine($"build={compatibilityBuildId}; cards={results.Count:N0}; passed={results.Count - failures:N0}; failures={failures:N0}; diagnostics={warnings:N0}; blank={blank:N0}");
			if (results.Count == 0 || failures > 0) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--list-animation-cards")
		{
			foreach (string item in MonsterAnimationIndexService.FindInstalledCardIds(args[1]))
			{
				Console.WriteLine(item);
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-raw-animation-roundtrip")
		{
			MonsterAnimationSet set = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationRawAssetService service = new MonsterAnimationRawAssetService();
			RawAnimationManifest manifest = service.ExportAll(set, args[3]);
			Dictionary<string, string> liveHashes = set.Assets
				.Select(asset => asset.BundlePath)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToDictionary(path => path,
					path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
					StringComparer.OrdinalIgnoreCase);
			string mirrorRoot = Path.Combine(Path.GetTempPath(), "MDCardModTool",
				"raw_animation_roundtrip_" + Guid.NewGuid().ToString("N"));
			int imported;
			try
			{
				string mirrorGame = Path.Combine(mirrorRoot, "Yu-Gi-Oh!  Master Duel");
				string mirrorLocal = Path.Combine(mirrorGame, "LocalData", "test-profile", "0000");
				List<MonsterAnimationAssetRef> mirrorAssets = new();
				foreach (MonsterAnimationAssetRef asset in set.Assets)
				{
					string mirrorBundle = Path.Combine(mirrorLocal,
						asset.RelativeBundlePath.Replace('/', Path.DirectorySeparatorChar));
					Directory.CreateDirectory(Path.GetDirectoryName(mirrorBundle)!);
					if (!File.Exists(mirrorBundle))
					{
						File.Copy(asset.BundlePath, mirrorBundle);
					}
					mirrorAssets.Add(new MonsterAnimationAssetRef
					{
						BundlePath = mirrorBundle,
						RelativeBundlePath = asset.RelativeBundlePath,
						AssetFileName = asset.AssetFileName,
						PathId = asset.PathId,
						Name = asset.Name,
						CardId = asset.CardId,
						Kind = asset.Kind,
						StorageKind = asset.StorageKind
					});
				}
				MonsterAnimationSet mirrorSet = new()
				{
					CardId = set.CardId,
					Assets = mirrorAssets
				};
				imported = service.ImportAll(mirrorGame, mirrorSet, args[3]);
			}
			finally
			{
				try
				{
					if (Directory.Exists(mirrorRoot)) Directory.Delete(mirrorRoot, recursive: true);
				}
				catch
				{
				}
			}
			bool liveUnchanged = liveHashes.All(pair => File.Exists(pair.Key)
				&& string.Equals(pair.Value,
					Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pair.Key))),
					StringComparison.Ordinal));
			string[] profiles = set.Assets.Select((MonsterAnimationAssetRef asset5) => service.ResolveProfile(asset5).DisplayName).Distinct().ToArray();
			Dictionary<string, int> extensions = (from rawAnimationManifestEntry in manifest.Files
				group rawAnimationManifestEntry by Path.GetExtension(rawAnimationManifestEntry.FileName).ToLowerInvariant()).ToDictionary((IGrouping<string, RawAnimationManifestEntry> grouping) => grouping.Key, (IGrouping<string, RawAnimationManifestEntry> source4) => source4.Count());
			Console.WriteLine($"complete={set.IsComplete}; exported={manifest.Files.Count}; imported={imported}; liveUnchanged={liveUnchanged}; profiles={string.Join(" / ", profiles)}; files={string.Join(',', extensions.Select((KeyValuePair<string, int> keyValuePair) => $"{keyValuePair.Key}:{keyValuePair.Value}"))}");
			if (!set.IsComplete || manifest.Files.Count < 6 || imported != manifest.Files.Count || !liveUnchanged || extensions.GetValueOrDefault(".png") < 2 || extensions.GetValueOrDefault(".atlas") < 2 || extensions.GetValueOrDefault(".json") < 2)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-languages")
		{
			AppLanguage originalLanguage = Localizer.Language;
			using MonsterAnimationForm animationForm = new MonsterAnimationForm(args[1], args[2]);
			using MonsterAnimationRawAssetsForm rawForm = new MonsterAnimationRawAssetsForm(args[1], args[2]);
			animationForm.CreateControl();
			rawForm.CreateControl();
			Dictionary<Control, string> animationBindings = (Dictionary<Control, string>)(typeof(MonsterAnimationForm)
				.GetField("_localizedControls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(animationForm)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_localizedControls"));
			Dictionary<Control, string> rawBindings = (Dictionary<Control, string>)(typeof(MonsterAnimationRawAssetsForm)
				.GetField("_localizedControls", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rawForm)
				?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_localizedControls"));
			ComboBox frameEdge = (ComboBox)(typeof(MonsterAnimationForm).GetField("_frameEdge", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(animationForm)
				?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_frameEdge"));
			ListView rawAssets = (ListView)(typeof(MonsterAnimationRawAssetsForm).GetField("_assets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rawForm)
				?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_assets"));
			string[] columnIds = ["raw.column.profile", "raw.column.kind", "raw.column.name", "raw.column.pathid", "raw.column.bundle"];
			bool passed = true;
			foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
			{
				Localizer.SetLanguage(language);
				Application.DoEvents();
				bool animationOk = animationForm.Text == Localizer.T("animation.title")
					&& animationBindings.All(pair => pair.Key.Text == Localizer.T(pair.Value))
					&& Equals(frameEdge.Items[0], Localizer.T("animation.quality.auto"));
				bool rawOk = rawForm.Text == Localizer.F("raw.title", args[2])
					&& rawBindings.All(pair => pair.Key.Text == Localizer.T(pair.Value))
					&& rawAssets.Columns.Cast<ColumnHeader>().Select(column => column.Text)
						.SequenceEqual(columnIds.Select(Localizer.T));
				Console.WriteLine($"language={language}; animation={animationOk}; raw={rawOk}; animationTitle={animationForm.Text}; rawTitle={rawForm.Text}");
				passed &= animationOk && rawOk;
			}
			Localizer.SetLanguage(originalLanguage);
			if (!passed) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 3 && args[0] == "--test-raw-animation-form")
		{
			using (MonsterAnimationRawAssetsForm form = new MonsterAnimationRawAssetsForm(args[1], args[2])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				form.Show();
				ListView list = (ListView)(typeof(MonsterAnimationRawAssetsForm).GetField("_assets", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form));
				PictureBox image = (PictureBox)(typeof(MonsterAnimationRawAssetsForm).GetField("_imagePreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form));
				TextBox text = (TextBox)(typeof(MonsterAnimationRawAssetsForm).GetField("_textPreview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form));
				List<Button> buttons = (List<Button>)(typeof(MonsterAnimationRawAssetsForm).GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_buttons"));
				HashSet<Button> mutationButtons = (HashSet<Button>)(typeof(MonsterAnimationRawAssetsForm).GetField("_mutationButtons", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_mutationButtons"));
				Label status = (Label)(typeof(MonsterAnimationRawAssetsForm).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_status"));
				FieldInfo setField = typeof(MonsterAnimationRawAssetsForm).GetField("_set", BindingFlags.Instance | BindingFlags.NonPublic)
					?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_set");
				FieldInfo readOnlyField = typeof(MonsterAnimationRawAssetsForm).GetField("_readOnlyEquivalent", BindingFlags.Instance | BindingFlags.NonPublic)
					?? throw new MissingFieldException(nameof(MonsterAnimationRawAssetsForm), "_readOnlyEquivalent");
				DateTime deadline = DateTime.UtcNow.AddSeconds(60.0);
				while (DateTime.UtcNow < deadline && ((list?.Items.Count ?? 0) < 6 || (image?.Image == null && string.IsNullOrWhiteSpace(text?.Text))))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				string[] labels = (from ListViewItem listViewItem in list?.Items
					select listViewItem.SubItems[0].Text).Distinct().ToArray() ?? Array.Empty<string>();
				bool previewLoaded = image?.Image != null || !string.IsNullOrWhiteSpace(text?.Text);
				MonsterAnimationSet? loadedSet = setField.GetValue(form) as MonsterAnimationSet;
				bool readOnlyEquivalent = (bool)(readOnlyField.GetValue(form) ?? false);
				bool mutationDisabled = mutationButtons.All(button => !button.Enabled);
				bool exportEnabled = buttons.Where(button => !mutationButtons.Contains(button)).All(button => button.Enabled);
				bool equivalent3899 = args[2] != "3899" || readOnlyEquivalent
					&& loadedSet?.CardId == "13668" && status.Text.Contains("13668", StringComparison.Ordinal)
					&& mutationDisabled && exportEnabled;
				Console.WriteLine($"items={list?.Items.Count}; preview={previewLoaded}; profiles={string.Join(" / ", labels)}; source={loadedSet?.CardId}; readOnly={readOnlyEquivalent}; mutationDisabled={mutationDisabled}; exportEnabled={exportEnabled}; status={status.Text}");
				bool num = (list?.Items.Count ?? 0) >= 6 && previewLoaded && labels.Any((string text3) => text3.Contains("SD", StringComparison.OrdinalIgnoreCase)) && labels.Any((string text3) => text3.Contains("HighEnd_HD", StringComparison.OrdinalIgnoreCase))
					&& equivalent3899;
				form.Close();
				if (!num)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-animation-form-sequence")
		{
			using MonsterAnimationForm form = new(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Show();
			string[] sequence = ["3899", "13668", "3899"];
			List<string> results = [];
			bool ready = true;
			foreach (string cardId in sequence)
			{
				Task load = form.PreviewCardAsync(cardId);
				DateTime deadline = DateTime.UtcNow.AddSeconds(45);
				while (!load.IsCompleted && DateTime.UtcNow < deadline)
				{
					Application.DoEvents();
					Thread.Sleep(20);
				}
				if (!load.IsCompleted)
				{
					ready = false;
					results.Add(cardId + ":timeout");
					break;
				}
				load.GetAwaiter().GetResult();
				Application.DoEvents();
				AnimationPreviewCanvas preview = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm)
					.GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_preview"));
				Label source = (Label)(typeof(MonsterAnimationForm)
					.GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
					?? throw new MissingFieldException(nameof(MonsterAnimationForm), "_sourceStatus"));
				bool cleanText = source.Text.IndexOf('\0') < 0 && source.Parent is TableLayoutPanel
					&& Descendants(form).OfType<RoundedButton>().All(button => button.Region == null);
				string expectedSource = cardId == "3899" ? "13668" : cardId;
				bool stepReady = form.CardQuery == cardId && form.LocatedCardId == cardId
					&& form.PreviewSourceCardId == expectedSource && preview.Frame != null && cleanText;
				ready &= stepReady;
				results.Add($"{cardId}->{form.PreviewSourceCardId}:frame={preview.Frame != null}:clean={cleanText}");
			}
			Stopwatch settle = Stopwatch.StartNew();
			while (settle.ElapsedMilliseconds < 750)
			{
				Application.DoEvents();
				Thread.Sleep(15);
			}
			ready &= form.CardQuery == "3899" && form.LocatedCardId == "3899" && form.PreviewSourceCardId == "13668";
			Console.WriteLine($"sequence={string.Join(" | ", results)}; final={form.CardQuery}/{form.LocatedCardId}->{form.PreviewSourceCardId}; ready={ready}");
			form.Close();
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 2 && args[0] == "--test-animation-form")
		{
			using (MonsterAnimationForm form2 = new MonsterAnimationForm(args[1]))
			{
				form2.Opacity = 0.0;
				form2.ShowInTaskbar = false;
				form2.Show();
				Application.DoEvents();
				Button[] buttons = Descendants(form2).OfType<Button>().ToArray();
				Button choose = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form2)
					?? throw new MissingFieldException("MonsterAnimationForm._chooseMedia"));
				Button play = (Button)(typeof(MonsterAnimationForm).GetField("_play", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form2)
					?? throw new MissingFieldException("MonsterAnimationForm._play"));
				Button apply = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form2)
					?? throw new MissingFieldException("MonsterAnimationForm._apply"));
				Button restore = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form2)
					?? throw new MissingFieldException("MonsterAnimationForm._restore"));
				Label source = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form2)
					?? throw new MissingFieldException("MonsterAnimationForm._sourceStatus"));
				TableLayoutPanel buttonGrid = choose.Parent as TableLayoutPanel
					?? throw new InvalidOperationException("Animation button grid missing.");
				bool ready = buttonGrid.RowCount == 3 && buttonGrid.GetColumnSpan(choose) == 2
					&& buttonGrid.GetColumnSpan(play) == 2 && buttonGrid.GetRow(apply) == 2 && buttonGrid.GetRow(restore) == 2
					&& source.AutoEllipsis && buttons.OfType<RoundedButton>().All(button => button.Region == null);
				Console.WriteLine($"shown={form2.ClientSize.Width}x{form2.ClientSize.Height}; grid={buttonGrid.ColumnCount}x{buttonGrid.RowCount}; sourceEllipsis={source.AutoEllipsis}; rectangularWindows={buttons.OfType<RoundedButton>().All(button => button.Region == null)}; ready={ready}");
				form2.Close();
				if (!ready) Environment.ExitCode = 2;
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-current")
		{
			using (MonsterAnimationForm form3 = new MonsterAnimationForm(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				form3.Show();
				Label label = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				Label resourceStatus = (Label)(typeof(MonsterAnimationForm).GetField("_resourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				Button chooseMedia = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				AnimationPreviewCanvas preview = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				TextBox cardInput = (TextBox)(typeof(MonsterAnimationForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				cardInput.Text = args[2];
				cardInput.SelectionStart = cardInput.TextLength;
				DateTime deadline2 = DateTime.UtcNow.AddSeconds(30.0);
				while (DateTime.UtcNow < deadline2 && preview?.Frame == null && (label == null || !label.Text.Contains("原版多骨骼", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				int initialScale = preview?.ScalePercent ?? 0;
				bool num2 = initialScale >= 10 && initialScale <= 500 && Math.Abs((preview?.AnimationScale ?? 0f) - (float)initialScale / 100f) < 0.001f;
				NumericUpDown scale = (NumericUpDown)(typeof(MonsterAnimationForm).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form3));
				if (scale != null)
				{
					scale.Value = 35m;
				}
				Application.DoEvents();
				bool realtimeScale = preview != null && preview.ScalePercent == 35 && Math.Abs(preview.AnimationScale - 0.35f) < 0.001f;
				bool autoLocated = string.Equals(form3.LocatedCardId, args[2], StringComparison.Ordinal);
				bool equivalent3899 = args[2] != "3899"
					|| string.Equals(form3.PreviewSourceCardId, "13668", StringComparison.Ordinal)
						&& (resourceStatus?.Text.Contains("P13668", StringComparison.Ordinal) ?? false)
						&& chooseMedia?.Enabled == false;
				bool num3 = num2 && realtimeScale && autoLocated && equivalent3899
					&& (preview?.Frame != null || (label?.Text.Contains("原版多骨骼", StringComparison.Ordinal) ?? false));
				Console.WriteLine($"status={resourceStatus?.Text.Replace(Environment.NewLine, " | ")}; source={label?.Text.Replace(Environment.NewLine, " | ")}; frame={preview?.Frame != null}; located={form3.LocatedCardId}; previewSource={form3.PreviewSourceCardId}; replaceEnabled={chooseMedia?.Enabled}; initialScale={initialScale}; realtimeScale={preview?.ScalePercent}");
				form3.Close();
				if (!num3)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--test-animation-form-unsupported")
		{
			using MonsterAnimationForm form = new(args[1])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			};
			form.Show();
			TextBox cardInput = (TextBox)(typeof(MonsterAnimationForm).GetField("_cardId", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException("MonsterAnimationForm._cardId"));
			Label sourceStatus = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException("MonsterAnimationForm._sourceStatus"));
			Button chooseMedia = (Button)(typeof(MonsterAnimationForm).GetField("_chooseMedia", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException("MonsterAnimationForm._chooseMedia"));
			Button apply = (Button)(typeof(MonsterAnimationForm).GetField("_apply", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException("MonsterAnimationForm._apply"));
			Button restore = (Button)(typeof(MonsterAnimationForm).GetField("_restore", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form)
				?? throw new MissingFieldException("MonsterAnimationForm._restore"));
			cardInput.Text = args[2];
			cardInput.SelectionStart = cardInput.TextLength;
			DateTime deadline = DateTime.UtcNow.AddSeconds(30.0);
			string expectedStatus = Localizer.T("animation.source.unsupported");
			while (DateTime.UtcNow < deadline
				&& (!string.Equals(form.LocatedCardId, args[2], StringComparison.Ordinal)
					|| !string.Equals(sourceStatus.Text, expectedStatus, StringComparison.Ordinal)))
			{
				Application.DoEvents();
				Thread.Sleep(25);
			}
			bool ready = string.Equals(form.LocatedCardId, args[2], StringComparison.Ordinal)
				&& string.Equals(sourceStatus.Text, expectedStatus, StringComparison.Ordinal)
				&& !chooseMedia.Enabled && !apply.Enabled && !restore.Enabled;
			Console.WriteLine($"card={args[2]}; status={sourceStatus.Text}; chooseMedia={chooseMedia.Enabled}; apply={apply.Enabled}; restore={restore.Enabled}; ready={ready}");
			form.Close();
			if (!ready) Environment.ExitCode = 2;
			return;
		}
		bool flag = args.Length == 4;
		string buildId;
		if (flag)
		{
			buildId = args[0];
			bool flag2 = ((buildId == "--test-animation-form-media" || buildId == "--test-animation-form-chroma") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			using (MonsterAnimationForm form4 = new MonsterAnimationForm(args[1], args[2])
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				form4.Show();
				AnimationPreviewCanvas preview2 = (AnimationPreviewCanvas)(typeof(MonsterAnimationForm).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4));
				Label source = (Label)(typeof(MonsterAnimationForm).GetField("_sourceStatus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4));
				CheckBox removeGreenScreen = (CheckBox)(typeof(MonsterAnimationForm).GetField("_removeGreenScreen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4));
				if (args[0] == "--test-animation-form-chroma" && removeGreenScreen != null)
				{
					removeGreenScreen.Checked = true;
				}
				DateTime deadline3 = DateTime.UtcNow.AddSeconds(30.0);
				while (DateTime.UtcNow < deadline3 && preview2?.Frame == null && (source == null || !source.Text.Contains("原版多骨骼", StringComparison.Ordinal)))
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				Task task = ((Task)(typeof(MonsterAnimationForm).GetMethod("LoadMediaAsync", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException("LoadMediaAsync")).Invoke(form4, new object[1] { args[3] })) ?? throw new InvalidOperationException("媒体加载任务没有启动。");
				deadline3 = DateTime.UtcNow.AddSeconds(60.0);
				while (!task.IsCompleted && DateTime.UtcNow < deadline3)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				task.GetAwaiter().GetResult();
				NumericUpDown scale2 = (NumericUpDown)(typeof(MonsterAnimationForm).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form4));
				int num6;
				if (task.IsCompletedSuccessfully && preview2?.Frame != null)
				{
					decimal? num4 = scale2?.Value;
					decimal num5 = 100;
					if (((num4.GetValueOrDefault() == num5) & num4.HasValue) && preview2.ScalePercent == 100)
					{
						num6 = ((args[0] != "--test-animation-form-chroma" || (source?.Text.Contains("绿幕已透明", StringComparison.Ordinal) ?? false)) ? 1 : 0);
						goto IL_0d15;
					}
				}
				num6 = 0;
				goto IL_0d15;
				IL_0d15:
				Console.WriteLine($"media={source?.Text.Replace(Environment.NewLine, " | ")}; frame={preview2?.Frame != null}; fullCanvasScale={scale2?.Value}");
				form4.Close();
				if (num6 == 0)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--test-animation-catalog")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index2, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			HashSet<string> ids = MonsterAnimationIndexService.LoadBundledCardIds();
			TexRef[] tagged = index2.Textures.Where((TexRef texRef) => texRef.SourceKind == "本地卡图" && ids.Contains(texRef.CardKey)).ToArray();
			Console.WriteLine($"ids={ids.Count}; taggedTextures={tagged.Length}; distinctCards={tagged.Select((TexRef texRef) => texRef.CardKey).Distinct().Count()}");
			return;
		}
		if (args.Length == 1 && args[0] == "--test-main-form")
		{
			using (MainForm form5 = new MainForm
			{
				Opacity = 0.0,
				ShowInTaskbar = false
			})
			{
				form5.Show();
				TreeView groups = (TreeView)(typeof(MainForm).GetField("_groups", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form5));
				DateTime deadline4 = DateTime.UtcNow.AddSeconds(30.0);
				TreeNode animationNode = null;
				while (DateTime.UtcNow < deadline4 && animationNode == null)
				{
					Application.DoEvents();
					animationNode = groups?.Nodes.Cast<TreeNode>().SelectMany((TreeNode treeNode) => treeNode.Nodes.Cast<TreeNode>()).FirstOrDefault((TreeNode treeNode) => string.Equals(treeNode.Tag as string, "local-card|animation", StringComparison.Ordinal));
					if (animationNode == null)
					{
						Thread.Sleep(25);
					}
				}
				Console.WriteLine((animationNode == null) ? "animationCategory=missing" : ("animationCategory=" + animationNode.Text));
				form5.Close();
				if (animationNode == null)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--inspect-animation-card")
		{
			MonsterAnimationSet set2 = MonsterAnimationIndexService.Find(args[1], args[2]);
			Console.WriteLine($"card={set2.CardId}; complete={set2.IsComplete}; {set2.CountSummary}");
			{
				foreach (MonsterAnimationAssetRef asset in set2.Assets)
				{
					Console.WriteLine($"{asset.Kind}; {asset.Name}; PathID={asset.PathId}; {asset.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 4 && args[0] == "--dump-animation-card")
		{
			MonsterAnimationSet set3 = MonsterAnimationIndexService.Find(args[1], args[2]);
			Directory.CreateDirectory(args[3]);
			ModEngine engine = new ModEngine();
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + "JS.json"), engine.ReadTextAsset(set3.Skeletons[0]).Data);
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + ".atlas"), engine.ReadTextAsset(set3.Atlases[0]).Data);
			string root = ((set3.Textures[0].StorageKind == "StreamingAssets") ? IndexService.StreamingRoot(args[1]) : IndexService.FindLocalRoot(args[1]));
			foreach (TexRef texture in engine.ScanBundle(set3.Textures[0].BundlePath, root, set3.Textures[0].ModSourceKind, includeDependencies: false).Textures)
			{
				File.WriteAllBytes(Path.Combine(args[3], texture.Name + ".png"), engine.DecodePng(texture));
			}
			Console.WriteLine(args[3]);
			return;
		}
		if (args.Length == 4 && args[0] == "--test-current-animation-preview")
		{
			using (CurrentMonsterAnimationPreview preview3 = MonsterAnimationCurrentPreview.TryLoad(MonsterAnimationIndexService.Find(args[1], args[2])) ?? throw new InvalidDataException("当前动画不是可逐帧还原的单槽序列动画。"))
			{
				Directory.CreateDirectory(args[3]);
				preview3.Frames[0].Save(Path.Combine(args[3], "frame-0001.png"));
				Console.WriteLine($"frames={preview3.Frames.Count}; fps={preview3.FramesPerSecond}; animation={preview3.AnimationName}");
				return;
			}
		}
		if (args.Length == 4 && args[0] == "--test-spine42-preview")
		{
			using CurrentMonsterAnimationPreview preview = Spine42PreviewRenderer.TryLoad(MonsterAnimationIndexService.Find(args[1], args[2]))
				?? throw new InvalidDataException("Spine 4.2 preview could not be rendered.");
			Directory.CreateDirectory(args[3]);
			int[] samples = [0, preview.Frames.Count / 2, preview.Frames.Count - 1];
			for (int i = 0; i < samples.Length; i++) preview.Frames[samples[i]].Save(Path.Combine(args[3], $"spine42-{i + 1}.png"));
			Console.WriteLine($"frames={preview.Frames.Count}; fps={preview.FramesPerSecond}; animation={preview.AnimationName}; output={Path.GetFullPath(args[3])}");
			return;
		}
		if (args.Length == 3 && args[0] == "--diagnose-spine42")
		{
			Console.WriteLine(Spine42PreviewRenderer.Diagnose(MonsterAnimationIndexService.Find(args[1], args[2])));
			return;
		}
		if (args.Length == 3 && args[0] == "--probe-spine42")
		{
			Spine42CompatibilityResult result = Spine42PreviewRenderer.Probe(MonsterAnimationIndexService.Find(args[1], args[2]));
			Console.WriteLine(JsonSerializer.Serialize(result));
			if (!result.Success) Environment.ExitCode = 2;
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-pairing")
		{
			MonsterAnimationSet pairingSet = MonsterAnimationIndexService.Find(args[1], args[2]);
			IReadOnlyList<MonsterAnimationAssetTriplet> pairs = MonsterAnimationAssetPairing.FindComplete(pairingSet);
			foreach (MonsterAnimationAssetTriplet pair in pairs)
			{
				Console.WriteLine($"{pair.Key}; texture={pair.Texture.RelativeBundlePath}; atlas={pair.Atlas.RelativeBundlePath}; skeleton={pair.Skeleton.RelativeBundlePath}");
			}
			if (!pairs.Any(pair => pair.Tier == "HighEnd_HD") || !pairs.Any(pair => pair.Tier == "SD"))
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-animation-bundle")
		{
			foreach (MonsterAnimationAssetRef asset2 in new ModEngine().ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])))
			{
				Console.WriteLine($"{asset2.Kind}; {asset2.Name}; PathID={asset2.PathId}; {asset2.RelativeBundlePath}");
				if (asset2.Kind == MonsterAnimationAssetKind.Texture)
				{
					AnimationTextureMetadata metadata = new ModEngine().ReadAnimationTextureMetadata(asset2);
					Console.WriteLine($"texture={metadata.Width}x{metadata.Height}; format={metadata.TextureFormat}; colorSpace={metadata.ColorSpace}; mip={metadata.MipCount}; complete={metadata.CompleteImageSize}; inline={metadata.InlineDataSize}; stream={metadata.StreamSize}:{metadata.StreamPath}");
				}
				else
				{
					byte[] data = new ModEngine().ReadTextAsset(asset2).Data;
					string text2 = Encoding.UTF8.GetString(data).TrimEnd('\0');
					Console.WriteLine(text2.Substring(0, Math.Min(text2.Length, 3000)));
				}
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--dump-animation-text")
		{
			ModEngine engine2 = new ModEngine();
			MonsterAnimationAssetRef asset3 = engine2.ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).First((MonsterAnimationAssetRef monsterAnimationAssetRef) => monsterAnimationAssetRef.Kind != MonsterAnimationAssetKind.Texture);
			File.WriteAllBytes(Path.GetFullPath(args[3]), engine2.ReadTextAsset(asset3).Data);
			Console.WriteLine($"{asset3.Kind}; {asset3.Name}; {new FileInfo(args[3]).Length} bytes; {Path.GetFullPath(args[3])}");
			return;
		}
		if (args.Length == 2 && args[0] == "--bundle-containers")
		{
			foreach (string item2 in new ModEngine().ReadAssetBundleContainerPaths(Path.GetFullPath(args[1])))
			{
				Console.WriteLine(item2);
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-media")
		{
			Directory.CreateDirectory(args[3]);
			Console.WriteLine("extracting");
			using ExtractedAnimation media = MonsterAnimationMedia.ExtractAsync(args[1], 12, 48, 256).GetAwaiter().GetResult();
			Console.WriteLine($"extracted {media.FramePaths.Count}");
			using MonsterAnimationBuildResult built = MonsterAnimationBuilder.Build(media.FramePaths, args[2], 12, 100, 4096);
			Console.WriteLine($"built {built.AtlasWidth}x{built.AtlasHeight}");
			built.AtlasImage.SaveAsPng(Path.Combine(args[3], "P" + args[2] + ".png"));
			File.WriteAllText(Path.Combine(args[3], "P" + args[2] + ".atlas.txt"), built.AtlasText);
			File.WriteAllBytes(Path.Combine(args[3], "P" + args[2] + "JS.json"), built.SkeletonJson);
			Console.WriteLine("encoding bc7");
			AnimationAtlasTextureData encoded = new ModEngine().EncodeAnimationAtlas(built.AtlasImage);
			Console.WriteLine($"frames={built.FrameCount}; fps={built.FramesPerSecond}; atlas={built.AtlasWidth}x{built.AtlasHeight}; bc7={encoded.Data.Length}");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-hd-media")
		{
			Directory.CreateDirectory(args[3]);
			using ExtractedAnimation media2 = MonsterAnimationMedia.ExtractAsync(args[1], 15, 28, 1920).GetAwaiter().GetResult();
			using Bitmap first = media2.LoadFrame(0);
			using MonsterAnimationBuildResult built2 = MonsterAnimationBuilder.Build(media2.FramePaths, args[2], 15, 100);
			AnimationAtlasTextureData encoded2 = new ModEngine().EncodeAnimationAtlas(built2.AtlasImage);
			Console.WriteLine($"frames={built2.FrameCount}; frame={first.Width}x{first.Height}; atlas={built2.AtlasWidth}x{built2.AtlasHeight}; bc7={encoded2.Data.Length}");
			return;
		}
		if (args.Length == 1 && args[0] == "--test-animation-quality-plan")
		{
			int shortAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(22, 16, 9, 8192);
			int mediumAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(60, 16, 9, 8192);
			int longAnimation = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(180, 16, 9, 8192);
			Console.WriteLine($"22frames={shortAnimation}; 60frames={mediumAnimation}; 180frames={longAnimation}");
			if (shortAnimation != 1920 || mediumAnimation != 1280 || longAnimation != 768)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-animation-chroma-key")
		{
			using (ExtractedAnimation keyed = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512, 0.0, removeGreenScreen: true).GetAwaiter().GetResult())
			{
				using ExtractedAnimation plain = MonsterAnimationMedia.ExtractAsync(args[1], 12, 12, 512).GetAwaiter().GetResult();
				using Bitmap keyedFrame = keyed.LoadFrame(0);
				using Bitmap plainFrame = plain.LoadFrame(0);
				System.Drawing.Color keyedBackground = keyedFrame.GetPixel(8, 8);
				System.Drawing.Color keyedSubject = keyedFrame.GetPixel(keyedFrame.Width / 2, keyedFrame.Height / 2);
				System.Drawing.Color plainBackground = plainFrame.GetPixel(8, 8);
				keyedFrame.Save(args[2]);
				Console.WriteLine($"keyedBackground={keyedBackground}; keyedSubject={keyedSubject}; plainBackground={plainBackground}; saved={args[2]}");
				if (!keyed.GreenScreenRemoved || keyedBackground.A > 16 || keyedSubject.A < 240 || plainBackground.A < 240)
				{
					Environment.ExitCode = 2;
				}
				return;
			}
		}
		if (args.Length == 5 && args[0] == "--test-animation-texture")
		{
			ModEngine engine3 = new ModEngine();
			MonsterAnimationAssetRef asset4 = engine3.ScanAnimationAssetsFast(args[1], args[2]).First((MonsterAnimationAssetRef monsterAnimationAssetRef) => monsterAnimationAssetRef.Kind == MonsterAnimationAssetKind.Texture);
			using Image<Rgba32> atlas = SixLabors.ImageSharp.Image.Load<Rgba32>(args[3]);
			engine3.ReplaceAnimationAtlas(asset4, atlas, Path.Combine(args[2], "backup"));
			File.WriteAllBytes(args[4], engine3.DecodePng(asset4.AsTexture()));
			Console.WriteLine($"roundtrip={atlas.Width}x{atlas.Height}; {new FileInfo(args[1]).Length} bytes");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-apply")
		{
			MonsterAnimationSet set4 = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationService service2 = new MonsterAnimationService();
			MonsterAnimationTemplate template = service2.ReadTemplate(args[1], set4);
			using ExtractedAnimation media3 = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 128).GetAwaiter().GetResult();
			using MonsterAnimationBuildResult built3 = MonsterAnimationBuilder.Build(media3.FramePaths, args[2], 12, 100, template, 4096);
			service2.Apply(args[1], set4, built3);
			ModEngine engine4 = new ModEngine();
			IEnumerable<string> dimensions = set4.Textures.Select(delegate(MonsterAnimationAssetRef monsterAnimationAssetRef)
			{
				using SixLabors.ImageSharp.Image image3 = SixLabors.ImageSharp.Image.Load(engine4.DecodePng(monsterAnimationAssetRef.AsTexture()));
				return $"{image3.Width}x{image3.Height}";
			});
			IEnumerable<int> texts = from asset5 in set4.Atlases.Concat(set4.Skeletons)
				select engine4.ReadTextAsset(asset5).Data.Length;
			Console.WriteLine($"complete={set4.IsComplete}; textures={string.Join(',', dimensions)}; textBytes={string.Join(',', texts)}");
			return;
		}
		if (args.Length == 4 && args[0] == "--test-animation-build-profile")
		{
			MonsterAnimationSet set5 = MonsterAnimationIndexService.Find(args[1], args[2]);
			MonsterAnimationTemplate template2 = new MonsterAnimationService().ReadTemplate(args[1], set5);
			ExtractedAnimation media4 = MonsterAnimationMedia.ExtractAsync(args[3], 12, 24, 256).GetAwaiter().GetResult();
			try
			{
				using MonsterAnimationBuildResult built4 = MonsterAnimationBuilder.Build(media4.FramePaths, args[2], 12, 100, template2, 4096);
				using MonsterAnimationBuildResult built35 = MonsterAnimationBuilder.Build(media4.FramePaths, args[2], 12, 35, template2, 4096);
				using JsonDocument document = JsonDocument.Parse(built4.SkeletonJson);
				string[] animationNames = (from jsonProperty in document.RootElement.GetProperty("animations").EnumerateObject()
					select jsonProperty.Name).ToArray();
				int[] timelineCounts = (from animation in document.RootElement.GetProperty("animations").EnumerateObject()
					select animation.Value.GetProperty("slots").EnumerateObject().First()
						.Value.GetProperty("attachment").GetArrayLength()).ToArray();
				bool timelinesValid = timelineCounts.All((int num8) => num8 == media4.FramePaths.Count);
				Console.WriteLine($"display100={built4.DisplayWidth:0.##}x{built4.DisplayHeight:0.##}; display35={built35.DisplayWidth:0.##}x{built35.DisplayHeight:0.##}; template={string.Join(',', template2.EffectiveAnimationNames)}; generated={string.Join(',', animationNames)}; timelines={string.Join(',', timelineCounts)}");
				if (Math.Abs(built4.DisplayWidth - 6720.0) > 0.1 || Math.Abs(built4.DisplayHeight - 3780.0) > 0.1 || Math.Abs(built35.DisplayWidth - 2352.0) > 0.1 || Math.Abs(built35.DisplayHeight - 1323.0) > 0.1 || !template2.EffectiveAnimationNames.SequenceEqual(animationNames) || !timelinesValid)
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
			MonsterAnimationSet set6 = MonsterAnimationIndexService.Find(args[1], args[2]);
			int restored = new MonsterAnimationService().Restore(args[1], set6);
			Console.WriteLine($"restored={restored}");
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
			string cache = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex index3 = (File.Exists(cache) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache)) ?? new GameIndex()) : new GameIndex());
			MissingCardScanResult result = IndexService.ScanMissingLocalCard(args[1], index3, args[2], delegate(int done, int total, int added)
			{
				Console.WriteLine($"{done}/{total}; added={added}");
			});
			HashSet<string> known = index3.Textures.Select((TexRef texRef) => $"{texRef.BundlePath}\0{texRef.AssetFileName}\0{texRef.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			index3.Textures.AddRange(result.Textures.Where((TexRef texRef) => known.Add($"{texRef.BundlePath}\0{texRef.AssetFileName}\0{texRef.PathId}")));
			IndexService.Save(args[1], index3);
			{
				foreach (TexRef x in index3.Textures.Where((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey == args[2]))
				{
					Console.WriteLine($"FOUND {x.Name}; {x.Width}x{x.Height}; {x.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 2 && args[0] == "--enrich-local-card-index")
		{
			string cache2 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex index4 = (File.Exists(cache2) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache2)) ?? new GameIndex()) : new GameIndex());
			MissingCardScanResult missingCardScanResult = IndexService.ScanMissingLocalCard(args[1], index4, "0", delegate(int done, int total, int added)
			{
				Console.WriteLine($"{done}/{total}; added={added}");
			});
			HashSet<string> known2 = index4.Textures.Select((TexRef texRef) => $"{texRef.BundlePath}\0{texRef.AssetFileName}\0{texRef.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<TexRef> additions = missingCardScanResult.Textures.Where((TexRef texRef) => known2.Add($"{texRef.BundlePath}\0{texRef.AssetFileName}\0{texRef.PathId}")).ToList();
			YgoCdbCardCatalog.ClassifyTexturesAsync(additions).GetAwaiter().GetResult();
			index4.Textures.AddRange(additions);
			IndexService.Save(args[1], index4);
			Console.WriteLine($"added={additions.Count}; total={index4.Textures.Count}");
			return;
		}
		if (args.Length == 2 && args[0] == "--sanitize-card-index")
		{
			string cache3 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex index5 = (File.Exists(cache3) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache3)) ?? new GameIndex()) : new GameIndex());
			int removed = IndexService.RemoveSpineAtlasParts(index5);
			removed += IndexService.RemoveNonCardLocalTextures(index5);
			index5.AlternateArtIndexVersion = 0;
			YgoCdbCardCatalog.ClassifyAlternateArtsAsync(index5).GetAwaiter().GetResult();
			IndexService.Save(args[1], index5);
			Console.WriteLine($"removed={removed}; total={index5.Textures.Count}");
			return;
		}
		if (args.Length == 2 && args[0] == "--find-card-frame")
		{
			string game = args[1];
			string[] array = new string[2]
			{
				Path.Combine(game, "masterduel_Data", "data.unity3d"),
				IndexService.StreamingRoot(game)
			};
			foreach (string target in array)
			{
				IEnumerable<string> enumerable2;
				if (!File.Exists(target))
				{
					if (!Directory.Exists(target))
					{
						IEnumerable<string> enumerable = Array.Empty<string>();
						enumerable2 = enumerable;
					}
					else
					{
						enumerable2 = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories);
					}
				}
				else
				{
					IEnumerable<string> enumerable = new string[1] { target };
					enumerable2 = enumerable;
				}
				foreach (string file in enumerable2)
				{
					try
					{
						foreach (TexRef x2 in from texRef in new ModEngine().ListTextures(file, game, "游戏内图片")
							where texRef.Name.Contains("card", StringComparison.OrdinalIgnoreCase) && texRef.Name.Contains("frame", StringComparison.OrdinalIgnoreCase)
							select texRef)
						{
							Console.WriteLine($"{x2.Name}\t{x2.Width}x{x2.Height}\t{x2.RelativeBundlePath}\tPathID={x2.PathId}");
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
			string cache4 = IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1]));
			GameIndex index6 = (File.Exists(cache4) ? (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cache4)) ?? new GameIndex()) : new GameIndex());
			ModPackageInfo info = new ModPackageService().Export(args[1], index6.Textures, args[2], args[0] == "--export-mods-direct");
			Console.WriteLine($"{info.BundleCount} bundles; {info.TotalSize} bytes; {args[2]}");
			return;
		}
		if (args.Length == 2 && args[0] == "--inspect-mod")
		{
			ModPackageInfo info2 = new ModPackageService().Inspect(args[1]);
			Console.WriteLine($"{info2.Name}; {info2.BundleCount} bundles; {info2.TotalSize} bytes");
			return;
		}
		if (args.Length == 3 && args[0] == "--import-mods")
		{
			ModImportResult result2 = new ModPackageService().Import(args[1], args[2]);
			Console.WriteLine($"{result2.BundleCount} bundles imported");
			return;
		}
		if (args.Length == 4 && args[0] == "--export-card")
		{
			TexRef texture2 = (JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1])))) ?? new GameIndex()).Textures.FirstOrDefault((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey == args[2]) ?? throw new FileNotFoundException("索引中没有卡号 " + args[2] + "。");
			byte[] stored = new ModEngine().DecodePng(texture2);
			CardCatalogEntry? card = CardCatalogService.LoadBestAvailable().Find(texture2.CardKey);
			string frameKey = CardFrameCatalog.RecommendedKey(card, texture2.Width, texture2.Height);
			GameTextureDisplayMapping mapping = GameTextureDisplayMapping.Resolve(texture2, frameKey);
			byte[] exported = mapping.RequiresMapping ? mapping.DecodeForDisplay(stored) : stored;
			File.WriteAllBytes(args[3], exported);
			Console.WriteLine($"{args[3]}; {mapping.EditorSummary}");
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-bundle")
		{
			string bundle = Path.GetFullPath(args[1]);
			string root2 = Path.GetFullPath(args[2]);
			{
				foreach (TexRef texture3 in new ModEngine().ScanBundle(bundle, root2, "诊断", includeDependencies: false).Textures)
				{
					Console.WriteLine($"{texture3.Name}; {texture3.Width}x{texture3.Height}; PathID={texture3.PathId}; file={texture3.AssetFileName}; {texture3.RelativeBundlePath}");
				}
				return;
			}
		}
		if (args.Length == 3 && args[0] == "--export-portable-index")
		{
			GameIndex index7 = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(IndexService.CachePath(IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。"), IndexService.StreamingRoot(args[1])))) ?? throw new InvalidDataException("本地索引无法读取。");
			PortableIndexService.Export(args[1], index7, args[2]);
			PortableGameIndex portable = PortableIndexService.Read(args[2]);
			Console.WriteLine($"build={portable.GameBuildId}; textures={portable.Textures.Count}; bytes={new FileInfo(args[2]).Length}");
			return;
		}
		if (args.Length == 2 && args[0] == "--inspect-portable-index")
		{
			PortableGameIndex portable2 = PortableIndexService.Read(args[1]);
			Console.WriteLine($"format={portable2.FormatVersion}; build={portable2.GameBuildId}; textures={portable2.Textures.Count}; alternateVersion={portable2.AlternateArtIndexVersion}");
			return;
		}
		if (args.Length == 3 && args[0] == "--inspect-portable-card")
		{
			foreach (PortableTextureEntry x3 in PortableIndexService.Read(args[1]).Textures.Where((PortableTextureEntry portableTextureEntry) => portableTextureEntry.CardKey == args[2]))
			{
				Console.WriteLine($"{x3.CardKey}; {x3.Width}x{x3.Height}; {x3.Category}; {x3.RelativeBundlePath}");
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--test-prebuilt-index")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index8, out string buildId2))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			TexRef first2 = index8.Textures.FirstOrDefault((TexRef texRef) => texRef.SourceKind == "本地卡图");
			Console.WriteLine($"build={buildId2}; textures={index8.Textures.Count}; first={first2?.BundlePath}");
			return;
		}
		if (args.Length == 2 && args[0] == "--test-classification-overrides")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index9, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			index9.AlternateArtIndexVersion = 3;
			YgoCdbCardCatalog.ClassifyAlternateArtsAsync(index9).GetAwaiter().GetResult();
			string[] normalIds = new string[2] { "30000", "30064" };
			string[] source2 = new string[4] { "3401", "3899", "19736", "20040" };
			bool normalOk = normalIds.All((string id) => index9.Textures.Any((TexRef texRef) => texRef.CardKey == id && texRef.Category == "卡图缩略图" && !texRef.IsAlternateArt && !texRef.IsTokenOrMisc));
			bool alternateOk = source2.All((string id) => index9.Textures.Any((TexRef texRef) => texRef.CardKey == id && texRef.Category == "异画卡图" && texRef.IsAlternateArt && !texRef.IsTokenOrMisc));
			int result3;
			int forcedNormal = index9.Textures.Count((TexRef texRef) => int.TryParse(texRef.CardKey, out result3) && result3 >= 30000 && result3 <= 30064 && texRef.Category == "卡图缩略图");
			int forcedAlternate = index9.Textures.Count(delegate(TexRef texRef)
			{
				bool flag3 = int.TryParse(texRef.CardKey, out result3);
				if (flag3)
				{
					bool flag4 = ((result3 >= 3401 && (result3 <= 3899 || result3 == 19736 || result3 == 20040)) ? true : false);
					flag3 = flag4;
				}
				return flag3 && texRef.Category == "异画卡图";
			});
			Console.WriteLine($"version={index9.AlternateArtIndexVersion}; normalOk={normalOk}; alternateOk={alternateOk}; forcedNormal={forcedNormal}; forcedAlternate={forcedAlternate}");
			if (index9.AlternateArtIndexVersion != 4 || !normalOk || !alternateOk || forcedNormal < 2 || forcedAlternate < 4)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-index-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex complete, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef expected = complete.Textures.FirstOrDefault((TexRef texRef) => texRef.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			GameIndex incomplete = new GameIndex
			{
				AlternateArtIndexVersion = complete.AlternateArtIndexVersion,
				Textures = complete.Textures.Where((TexRef texRef) => texRef.CardKey != args[2]).ToList()
			};
			incomplete.Textures.Add(new TexRef
			{
				BundlePath = expected.BundlePath,
				RelativeBundlePath = "diagnostic/retained-extra",
				PathId = long.MinValue,
				AssetFileName = expected.AssetFileName,
				Name = "diagnostic-extra",
				Width = 1,
				Height = 1,
				Category = "诊断",
				SourceKind = expected.SourceKind,
				CardKey = "999999"
			});
			if (!PortableIndexService.TryRepairFromBundled(args[1], incomplete, out GameIndex repaired, out string buildId3, out int retainedExtras))
			{
				throw new InvalidDataException("预绑定索引修复没有执行。");
			}
			bool restored2 = repaired.Textures.Any((TexRef texRef) => texRef.CardKey == args[2]);
			bool extraRetained = repaired.Textures.Any((TexRef texRef) => texRef.PathId == long.MinValue);
			Console.WriteLine($"build={buildId3}; before={incomplete.Textures.Count}; after={repaired.Textures.Count}; restored={restored2}; retainedExtras={retainedExtras}; extraRetained={extraRetained}");
			if (!restored2 || !extraRetained || retainedExtras != 1)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 3 && args[0] == "--test-texture-reference-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index10, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef texture4 = index10.Textures.FirstOrDefault((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			long expectedPathId = texture4.PathId;
			texture4.PathId = long.MinValue;
			texture4.AssetFileName = "stale-manual-mod-mapping";
			ModEngine modEngine = new ModEngine();
			TexRef resolved = modEngine.ResolveTextureReference(texture4) ?? throw new InvalidDataException("未能从当前 Bundle 重新定位 Texture2D。");
			texture4.PathId = resolved.PathId;
			texture4.AssetFileName = resolved.AssetFileName;
			byte[] png = modEngine.DecodePng(texture4, 512);
			Console.WriteLine($"card={args[2]}; expectedPathId={expectedPathId}; resolvedPathId={resolved.PathId}; assetFile={resolved.AssetFileName}; pngBytes={png.Length}");
			if (resolved.PathId != expectedPathId || png.Length < 100)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--dump-animation-texture")
		{
			ModEngine textureEngine = new();
			MonsterAnimationAssetRef textureAsset = textureEngine.ScanAnimationAssetsFast(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])).First(asset => asset.Kind == MonsterAnimationAssetKind.Texture);
			File.WriteAllBytes(Path.GetFullPath(args[3]), textureEngine.DecodePng(textureAsset.AsTexture()));
			Console.WriteLine($"{textureAsset.Name}; {new FileInfo(args[3]).Length} bytes; {Path.GetFullPath(args[3])}");
			return;
		}
		if (args.Length == 3 && args[0] == "--test-texture-bundle-relocation")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex relocationIndex, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef source = relocationIndex.Textures.FirstOrDefault((TexRef x) => x.SourceKind == "本地卡图" && x.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			string localRoot = IndexService.FindLocalRoot(args[1]) ?? throw new DirectoryNotFoundException("未找到 LocalData。");
			TexRef stale = new TexRef
			{
				BundlePath = Path.Combine(localRoot, "ff", "ffffffff"),
				RelativeBundlePath = Path.Combine("ff", "ffffffff"),
				PathId = long.MinValue,
				AssetFileName = "missing-bundle-mapping",
				Name = source.Name,
				Width = source.Width,
				Height = source.Height,
				Category = source.Category,
				SourceKind = source.SourceKind,
				CardKey = source.CardKey
			};
			byte[] png = new ModEngine().DecodePng(stale, 512);
			bool relocated = File.Exists(stale.BundlePath) && string.Equals(stale.RelativeBundlePath, source.RelativeBundlePath, StringComparison.OrdinalIgnoreCase);
			Console.WriteLine($"card={args[2]}; relocated={relocated}; bundle={stale.RelativeBundlePath}; pathId={stale.PathId}; pngBytes={png.Length}");
			if (!relocated || stale.PathId == long.MinValue || png.Length < 100)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 4 && args[0] == "--test-texture-write-repair")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index11, out buildId))
			{
				throw new FileNotFoundException("缺少卡图预绑定索引。");
			}
			TexRef source3 = index11.Textures.FirstOrDefault((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey == args[2]) ?? throw new InvalidDataException("预绑定索引不含卡号 " + args[2] + "。");
			Directory.CreateDirectory(args[3]);
			string copy = Path.Combine(args[3], "manual-mod.bundle");
			File.Copy(source3.BundlePath, copy, overwrite: true);
			TexRef target2 = new TexRef
			{
				BundlePath = copy,
				RelativeBundlePath = "manual-mod.bundle",
				PathId = long.MinValue,
				AssetFileName = "stale-manual-mod-mapping",
				Name = source3.Name,
				Width = source3.Width,
				Height = source3.Height,
				Category = source3.Category,
				SourceKind = source3.SourceKind,
				CardKey = source3.CardKey
			};
			ModEngine engine5 = new ModEngine();
			byte[] overFrame;
			using (Image<Rgba32> image2 = SixLabors.ImageSharp.Image.Load<Rgba32>(engine5.DecodePng(source3)))
			{
				image2.Mutate(delegate(IImageProcessingContext source4)
				{
					source4.Resize(704, 1024);
				});
				using MemoryStream stream = new MemoryStream();
				image2.SaveAsPng(stream);
				overFrame = stream.ToArray();
			}
			engine5.Replace(target2, overFrame, Path.Combine(args[3], "backup"));
			byte[] after = engine5.DecodePng(target2);
			ImageInfo info3 = SixLabors.ImageSharp.Image.Identify(after) ?? throw new InvalidDataException("写回后的 PNG 无法识别。");
			Console.WriteLine($"card={args[2]}; resolvedPathId={target2.PathId}; assetFile={target2.AssetFileName}; result={info3.Width}x{info3.Height}; pngBytes={after.Length}");
			if (target2.PathId == long.MinValue || target2.AssetFileName == "stale-manual-mod-mapping" || info3.Width != 704 || info3.Height != 1024)
			{
				Environment.ExitCode = 2;
			}
			return;
		}
		if (args.Length == 2 && args[0] == "--install-prebuilt-index")
		{
			if (!PortableIndexService.TryLoadBundled(args[1], out GameIndex index12, out string buildId4))
			{
				throw new FileNotFoundException("程序目录没有随包预绑定索引。", PortableIndexService.BundledPath);
			}
			IndexService.Save(args[1], index12);
			string local = IndexService.FindLocalRoot(args[1]);
			Console.WriteLine($"build={buildId4}; textures={index12.Textures.Count}; cache={IndexService.CachePath(local, IndexService.StreamingRoot(args[1]))}");
			return;
		}
		if (args.Length == 5 && args[0] == "--crop-image")
		{
			int targetWidth = int.Parse(args[3]);
			int targetHeight = int.Parse(args[4]);
			using Bitmap preview4 = ImageCropService.LoadPreview(args[1]);
			double targetAspect = (double)targetWidth / (double)targetHeight;
			int cropWidth = preview4.Width;
			int cropHeight = (int)Math.Round((double)cropWidth / targetAspect);
			if (cropHeight > preview4.Height)
			{
				cropHeight = preview4.Height;
				cropWidth = (int)Math.Round((double)cropHeight * targetAspect);
			}
			System.Drawing.RectangleF crop = new System.Drawing.RectangleF((float)(preview4.Width - cropWidth) / 2f, (float)(preview4.Height - cropHeight) / 2f, cropWidth, cropHeight);
			File.WriteAllBytes(args[2], ImageCropService.CropAndResize(args[1], crop, targetWidth, targetHeight));
			Console.WriteLine($"{targetWidth}x{targetHeight}; {new FileInfo(args[2]).Length} bytes; {args[2]}");
			return;
		}
		Application.Run(new MainForm());
	}

	private static byte[] CreateTextureMappingPattern(int width, int height)
	{
		using SixLabors.ImageSharp.Image<Rgba32> image = new(width, height, new Rgba32(14, 23, 38, 255));
		int border = Math.Max(3, Math.Min(width, height) / 64);
		Rgba32 gold = new(246, 196, 64, 255);
		Rgba32 red = new(226, 47, 84, 255);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				if (x < border || x >= width - border || y < border || y >= height - border)
				{
					image[x, y] = gold;
				}
			}
		}
		int marker = Math.Max(16, Math.Min(width, height) / 10);
		int markerLeft = width / 2 - marker / 2;
		int markerTop = height / 2 - marker / 2;
		for (int y = markerTop; y < markerTop + marker; y++)
		{
			for (int x = markerLeft; x < markerLeft + marker; x++) image[x, y] = red;
		}
		using MemoryStream output = new();
		image.Save(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
		return output.ToArray();
	}

	private static bool TextureMappingPatternReady(byte[] png, int width, int height)
	{
		using SixLabors.ImageSharp.Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		if (image.Width != width || image.Height != height) return false;
		Rgba32 center = image[width / 2, height / 2];
		Rgba32 topLeft = image[1, 1];
		Rgba32 bottomRight = image[width - 2, height - 2];
		return center.R > 180 && center.G < 100
			&& topLeft.R > 180 && topLeft.G > 120
			&& bottomRight.R > 180 && bottomRight.G > 120;
	}

	private static bool PngHasSize(byte[] png, int width, int height)
	{
		SixLabors.ImageSharp.ImageInfo info = SixLabors.ImageSharp.Image.Identify(png);
		return info.Width == width && info.Height == height;
	}

	private static byte[] CreateSplitTallTexture()
	{
		using SixLabors.ImageSharp.Image<Rgba32> image = new(512, 1024, new Rgba32(230, 32, 48, 255));
		for (int y = 512; y < 1024; y++)
		{
			for (int x = 0; x < 512; x++) image[x, y] = new Rgba32(24, 72, 232, 255);
		}
		using MemoryStream output = new();
		image.Save(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
		return output.ToArray();
	}

	private sealed record TextureMappingLiveResult(bool Ready, int StoredWidth, int StoredHeight,
		int DisplayWidth, int DisplayHeight, bool GeometryReady, bool OriginalUnchanged)
	{
		public override string ToString() =>
			$"{DisplayWidth}x{DisplayHeight}->{StoredWidth}x{StoredHeight}:geometry={GeometryReady}:original={OriginalUnchanged}";
	}

	private static TextureMappingLiveResult TestTextureMappingLiveCopy(TexRef source,
		GameTextureDisplayMapping mapping, string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		byte[] originalHash = SHA256.HashData(File.ReadAllBytes(source.BundlePath));
		string copy = Path.Combine(outputDirectory, Path.GetFileName(source.BundlePath) + ".mapping-test.bundle");
		File.Copy(source.BundlePath, copy, overwrite: true);
		TexRef target = new()
		{
			BundlePath = copy,
			RelativeBundlePath = Path.GetFileName(copy),
			PathId = source.PathId,
			AssetFileName = source.AssetFileName,
			Name = source.Name,
			Width = source.Width,
			Height = source.Height,
			Category = source.Category,
			SourceKind = source.SourceKind,
			CardKey = source.CardKey
		};

		byte[] displayPattern = CreateTextureMappingPattern(mapping.DisplayWidth, mapping.DisplayHeight);
		byte[] storedPattern = mapping.EncodeForStorage(displayPattern);
		ModEngine engine = new();
		engine.Replace(target, storedPattern, Path.Combine(outputDirectory, "backup"));
		byte[] decodedStorage = engine.DecodePng(target);
		ImageInfo storedInfo = SixLabors.ImageSharp.Image.Identify(decodedStorage)
			?? throw new InvalidDataException("临时 Bundle 写回后的 Texture2D 无法识别。");
		byte[] decodedDisplay = mapping.DecodeForDisplay(decodedStorage);
		ImageInfo displayInfo = SixLabors.ImageSharp.Image.Identify(decodedDisplay)
			?? throw new InvalidDataException("临时 Bundle 的正常比例预览无法识别。");
		bool geometryReady = TextureMappingPatternReady(decodedDisplay,
			mapping.DisplayWidth, mapping.DisplayHeight);
		bool originalUnchanged = originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source.BundlePath)));
		bool ready = storedInfo.Width == mapping.StorageWidth && storedInfo.Height == mapping.StorageHeight
			&& displayInfo.Width == mapping.DisplayWidth && displayInfo.Height == mapping.DisplayHeight
			&& geometryReady && originalUnchanged;
		return new TextureMappingLiveResult(ready, storedInfo.Width, storedInfo.Height,
			displayInfo.Width, displayInfo.Height, geometryReady, originalUnchanged);
	}

	private static void TestVisualTextureWrite(TexRef source, string outputDirectory)
	{
		Directory.CreateDirectory(outputDirectory);
		string copy = Path.Combine(outputDirectory, Path.GetFileName(source.BundlePath) + ".visual-test.bundle");
		File.Copy(source.BundlePath, copy, overwrite: true);
		TexRef target = new TexRef
		{
			BundlePath = copy,
			RelativeBundlePath = Path.GetFileName(copy),
			PathId = long.MinValue,
			AssetFileName = "stale-visual-mapping",
			Name = source.Name,
			Width = source.Width,
			Height = source.Height,
			Category = source.Category,
			SourceKind = source.SourceKind,
			CardKey = source.CardKey
		};
		ModEngine engine = new ModEngine();
		byte[] png = engine.DecodePng(source);
		engine.Replace(target, png, Path.Combine(outputDirectory, "backup"));
		byte[] after = engine.DecodePng(target);
		ImageInfo image = SixLabors.ImageSharp.Image.Identify(after) ?? throw new InvalidDataException("写回后的视觉资源无法识别。");
		Console.WriteLine($"name={target.Name}; category={target.Category}; resolvedPathId={target.PathId}; assetFile={target.AssetFileName}; result={image.Width}x{image.Height}; pngBytes={after.Length}");
		if (target.PathId == long.MinValue || target.AssetFileName == "stale-visual-mapping" || image.Width != source.Width || image.Height != source.Height)
		{
			throw new InvalidDataException("视觉资源 Texture2D 写回回归失败：" + source.Name);
		}
	}

	private static IEnumerable<Control> Descendants(Control root)
	{
		foreach (Control child in root.Controls)
		{
			yield return child;
			foreach (Control nested in Descendants(child)) yield return nested;
		}
	}

	private static T GetPrivateField<T>(object instance, string fieldName) where T : class
	{
		Type? type = instance.GetType();
		while (type != null)
		{
			FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			if (field?.GetValue(instance) is T value) return value;
			type = type.BaseType;
		}
		throw new MissingFieldException(instance.GetType().Name, fieldName);
	}

	private static bool PumpTask(Task task, int timeoutMilliseconds)
	{
		bool completed = PumpMessagesUntil(() => task.IsCompleted, timeoutMilliseconds);
		if (completed) task.GetAwaiter().GetResult();
		return completed;
	}

	private static bool PumpMessagesUntil(Func<bool> predicate, int timeoutMilliseconds)
	{
		Stopwatch timeout = Stopwatch.StartNew();
		while (timeout.ElapsedMilliseconds < timeoutMilliseconds)
		{
			Application.DoEvents();
			if (predicate()) return true;
			Thread.Sleep(12);
		}
		Application.DoEvents();
		return predicate();
	}

	private static void PumpMessagesFor(int milliseconds)
	{
		Stopwatch wait = Stopwatch.StartNew();
		while (wait.ElapsedMilliseconds < milliseconds)
		{
			Application.DoEvents();
			Thread.Sleep(12);
		}
		Application.DoEvents();
	}

	private static void ExerciseRoundedButtonTransitions(Control root)
	{
		MethodInfo transition = typeof(RoundedButton).GetMethod("BeginTransition",
			BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(RoundedButton), "BeginTransition");
		RoundedButton[] buttons = Descendants(root).OfType<RoundedButton>().Where(button => button.Visible).ToArray();
		foreach (RoundedButton button in buttons) transition.Invoke(button, [button.HoverColor]);
		PumpMessagesFor(220);
		foreach (RoundedButton button in buttons) transition.Invoke(button, [button.NormalColor]);
		PumpMessagesFor(240);
	}

	private static bool EditorRenderSettled(OverFrameFrameEditorForm editor)
	{
		bool loading = (bool)(editor.GetType().GetField("_loading", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? true);
		bool rendering = (bool)(editor.GetType().GetField("_rendering", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) ?? true);
		byte[]? output = editor.GetType().GetField("_outputBytes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(editor) as byte[];
		System.Windows.Forms.Timer timer = GetPrivateField<System.Windows.Forms.Timer>(editor, "_renderTimer");
		return !loading && !rendering && !timer.Enabled && output is { Length: > 0 };
	}

	private static Bitmap CaptureClientFromScreen(Form form)
	{
		if (form.IsDisposed || !form.IsHandleCreated)
		{
			throw new InvalidOperationException("交互截图窗口已经关闭或尚未建立句柄。");
		}
		// Desktop automation or focus-stealing by the calling terminal can minimize
		// a taskbar-hidden test window. Restore the same form before taking the two
		// repaint samples; otherwise the test would fail in Bitmap construction and
		// never inspect the UI pixels it is intended to validate.
		if (form.WindowState == FormWindowState.Minimized)
		{
			form.WindowState = FormWindowState.Normal;
			form.Show();
			PumpMessagesFor(250);
		}
		form.Activate();
		// Keep the pointer away from the shell taskbar thumbnail strip. The test
		// compares this window, not an unrelated application thumbnail fading above it.
		Cursor.Position = form.PointToScreen(new Point(12, 12));
		Application.DoEvents();
		Size clientSize = form.ClientSize;
		if (clientSize.Width <= 0 || clientSize.Height <= 0)
		{
			throw new InvalidOperationException($"交互截图客户区无效：client={clientSize}; "
				+ $"bounds={form.Bounds}; restore={form.RestoreBounds}; state={form.WindowState}; "
				+ $"visible={form.Visible}; disposed={form.IsDisposed}; handle={form.IsHandleCreated}。");
		}
		Bitmap bitmap = new(clientSize.Width, clientSize.Height,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, clientSize,
			CopyPixelOperation.SourceCopy);
		return bitmap;
	}

	private static int CountDifferentPixels(Bitmap first, Bitmap second)
	{
		if (first.Size != second.Size) return int.MaxValue;
		Rectangle bounds = new(Point.Empty, first.Size);
		System.Drawing.Imaging.BitmapData firstData = first.LockBits(bounds,
			System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		System.Drawing.Imaging.BitmapData secondData = second.LockBits(bounds,
			System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try
		{
			int length = Math.Abs(firstData.Stride) * first.Height;
			byte[] a = new byte[length];
			byte[] b = new byte[length];
			System.Runtime.InteropServices.Marshal.Copy(firstData.Scan0, a, 0, length);
			System.Runtime.InteropServices.Marshal.Copy(secondData.Scan0, b, 0, length);
			int different = 0;
			for (int offset = 0; offset + 3 < length; offset += 4)
			{
				if (a[offset] != b[offset] || a[offset + 1] != b[offset + 1]
					|| a[offset + 2] != b[offset + 2] || a[offset + 3] != b[offset + 3])
				{
					different++;
				}
			}
			return different;
		}
		finally
		{
			first.UnlockBits(firstData);
			second.UnlockBits(secondData);
		}
	}

	private static byte[] CreateInteractionBackground()
	{
		using Bitmap bitmap = new(FrameComposer.Width, FrameComposer.Height,
			System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		Rectangle bounds = new(Point.Empty, bitmap.Size);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		using (System.Drawing.Drawing2D.LinearGradientBrush gradient = new(bounds,
			Color.FromArgb(255, 20, 48, 80), Color.FromArgb(255, 92, 28, 66), 35f))
		{
			graphics.FillRectangle(gradient, bounds);
		}
		using MemoryStream stream = new();
		bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
		return stream.ToArray();
	}
}
