using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class StudioOptimizationTests
{
	public static void Run(string output)
	{
		Directory.CreateDirectory(output);
		Spine42PreviewRenderer.TestAtlasGeometry();
		int resizeChanges;
		using (CropCanvas cropCanvas = new CropCanvas(new Bitmap(400, 600), 704, 1024, fullCardOverlay: false, overFrameEditing: true)
		{
			Size = new Size(900, 900)
		})
		{
			cropCanvas.SetFrame(new Bitmap(704, 1024), preserveView: false, new RectangleF(89f, 191f, 527f, 528f));
			ImageRenderSpec renderSpec = cropCanvas.RenderSpec;
			using Bitmap bitmap = new Bitmap(960, 720);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.Teal);
				graphics.FillRectangle(Brushes.Gold, 0, 0, 480, 360);
				graphics.FillEllipse(Brushes.Fuchsia, 450, 250, 200, 200);
			}
			cropCanvas.SetBackground(new Bitmap(bitmap));
			byte[] array = cropCanvas.RenderBackgroundToTarget();
			cropCanvas.EditBackground(enabled: true);
			cropCanvas.SetZoom(0.7f, null);
			typeof(CropCanvas).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cropCanvas, new object[1]
			{
				new KeyEventArgs(Keys.Right | Keys.Shift)
			});
			ImageRenderSpec backgroundRenderSpec = cropCanvas.BackgroundRenderSpec;
			if (cropCanvas.RenderSpec != renderSpec || backgroundRenderSpec.OffsetX == 0f)
			{
				throw new InvalidDataException("Background changed art or did not move");
			}
			resizeChanges = 0;
			cropCanvas.ViewChanged += CountResize;
			cropCanvas.Size = new Size(720, 780);
			cropCanvas.ViewChanged -= CountResize;
			if (resizeChanges != 0)
			{
				throw new InvalidDataException("Viewport resize unnecessarily invalidated composition");
			}
			if ((double)Math.Abs(cropCanvas.RenderSpec.ImageScale - renderSpec.ImageScale) > 0.001 || (double)Math.Abs(cropCanvas.RenderSpec.OffsetY - renderSpec.OffsetY) > 0.01 || cropCanvas.BackgroundRenderSpec != backgroundRenderSpec)
			{
				throw new InvalidDataException("Resize changed layer composition");
			}
			byte[] array2 = cropCanvas.RenderBackgroundToTarget();
			if (array.SequenceEqual(array2))
			{
				throw new InvalidDataException("Background output did not change");
			}
			cropCanvas.SetFrame(new Bitmap(704, 1024), preserveView: true, new RectangleF(89f, 191f, 527f, 528f));
			if (cropCanvas.BackgroundRenderSpec != backgroundRenderSpec)
			{
				throw new InvalidDataException("Frame selection reset background");
			}
			cropCanvas.EditBackground(enabled: false);
			cropCanvas.SetZoom(0.8f, null);
			if (cropCanvas.BackgroundRenderSpec != backgroundRenderSpec)
			{
				throw new InvalidDataException("Art changed background");
			}
			OverFrameArtStore.SaveBackground(output, 1, ToPng(bitmap));
			OverFrameArtStore.SaveSettings(output, 1, new OverFrameFrameSettings("card_frame01", UsesCustomFrame: false, UserSelected: false, "AstellarTransparent", 0f, 0f, 0f, backgroundRenderSpec.ImageScale, backgroundRenderSpec.OffsetX, backgroundRenderSpec.OffsetY));
			OverFrameFrameSettings overFrameFrameSettings = OverFrameArtStore.ReadSettings(output, 1);
			using Bitmap bitmap2 = FrameComposer.PreviewBitmap(File.ReadAllBytes(OverFrameArtStore.BackgroundPath(output, 1)));
			if (bitmap2.Size != bitmap.Size)
			{
				throw new InvalidDataException("Background was destructively cropped");
			}
			cropCanvas.SetBackground(new Bitmap(bitmap2));
			cropCanvas.SetBackgroundRenderSpec(new ImageRenderSpec(704f, 1024f, overFrameFrameSettings.BackgroundImageScale, overFrameFrameSettings.BackgroundOffsetX, overFrameFrameSettings.BackgroundOffsetY));
			if (!array2.SequenceEqual(cropCanvas.RenderBackgroundToTarget()))
			{
				throw new InvalidDataException("Background draft changed on reopen");
			}
			File.WriteAllBytes(Path.Combine(output, "background-before.png"), array);
			File.WriteAllBytes(Path.Combine(output, "background-after.png"), array2);
			cropCanvas.EditBackground(enabled: true);
			using Bitmap bitmap3 = new Bitmap(cropCanvas.Width, cropCanvas.Height);
			cropCanvas.DrawToBitmap(bitmap3, cropCanvas.ClientRectangle);
			bitmap3.Save(Path.Combine(output, "background-editor.png"));
			Console.WriteLine("atlasRotations=4; meshTrim=True; boneJoint=True; backgroundIndependent=True; originalSize=960x720; draftRoundtrip=True; ready=True");
		}
		void CountResize(ImageRenderSpec _)
		{
			resizeChanges++;
		}
	}

	public static void Picker(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		AppLanguage[] values = Enum.GetValues<AppLanguage>();
		foreach (AppLanguage appLanguage in values)
		{
			Localizer.SetLanguage(appLanguage);
			using AnimationDonorPicker animationDonorPicker = new AnimationDonorPicker(gameRoot, new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "3801", "3413", "10001" }))
			{
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.Manual,
				Location = new Point(20, 20)
			};
			animationDonorPicker.Show();
			Application.DoEvents();
			((ImeAwareTextBox)typeof(AnimationDonorPicker).GetField("_search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animationDonorPicker)).Text = "青眼";
			Application.DoEvents();
			if (animationDonorPicker.SelectedCardId != "3801")
			{
				throw new InvalidDataException("Donor cross-language name search failed");
			}
			if (appLanguage == AppLanguage.SimplifiedChinese)
			{
				SynchronizationContext current = SynchronizationContext.Current;
				using WindowsFormsSynchronizationContext synchronizationContext = new WindowsFormsSynchronizationContext();
				SynchronizationContext.SetSynchronizationContext(synchronizationContext);
				Task task = (Task)typeof(AnimationDonorPicker).GetMethod("PreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(animationDonorPicker, null);
				Stopwatch stopwatch = Stopwatch.StartNew();
				while (!task.IsCompleted && stopwatch.Elapsed.TotalSeconds < 90.0)
				{
					Application.DoEvents();
					Thread.Sleep(15);
				}
				if (!task.IsCompleted)
				{
					throw new TimeoutException("Donor preview timed out");
				}
				task.GetAwaiter().GetResult();
				SynchronizationContext.SetSynchronizationContext(current);
				CurrentMonsterAnimationPreview currentMonsterAnimationPreview = (CurrentMonsterAnimationPreview)typeof(AnimationDonorPicker).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animationDonorPicker);
				int? num = currentMonsterAnimationPreview?.Frames.Count;
				if (!num.HasValue || num.GetValueOrDefault() <= 1)
				{
					throw new InvalidDataException("Donor preview unavailable");
				}
				Stopwatch stopwatch2 = Stopwatch.StartNew();
				while (stopwatch2.ElapsedMilliseconds < 450)
				{
					Application.DoEvents();
					Thread.Sleep(15);
				}
				AnimationPreviewCanvas animationPreviewCanvas = (AnimationPreviewCanvas)typeof(AnimationDonorPicker).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animationDonorPicker);
				if (animationPreviewCanvas.Frame == null || animationPreviewCanvas.Frame == currentMonsterAnimationPreview.Frames[0])
				{
					throw new InvalidDataException("Donor playback did not advance");
				}
				using Bitmap bitmap = new Bitmap(animationDonorPicker.Width, animationDonorPicker.Height);
				animationDonorPicker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, animationDonorPicker.Size));
				bitmap.Save(Path.Combine(output, "animation-source-picker.png"));
			}
			animationDonorPicker.Close();
		}
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		Console.WriteLine("donorSearchLanguages=4; sourcePreview=True; close=True; gameWrites=False; ready=True");
	}

	public static void Transfer(string gameRoot, string output, string targetCardId = "10001")
	{
		Directory.CreateDirectory(output);
		MonsterAnimationSet original = MonsterAnimationIndexService.Find(gameRoot, targetCardId);
		MonsterAnimationSet monsterAnimationSet = MonsterAnimationIndexService.Find(gameRoot, "3413");
		Dictionary<string, string> originals = original.Assets.Concat(monsterAnimationSet.Assets).DistinctBy((MonsterAnimationAssetRef x) => x.BundlePath).ToDictionary((MonsterAnimationAssetRef x) => x.BundlePath, (MonsterAnimationAssetRef x) => Hash(x.BundlePath));
		MonsterAnimationSet monsterAnimationSet2 = new MonsterAnimationSet
		{
			CardId = original.CardId,
			Assets = original.Assets.Select(delegate(MonsterAnimationAssetRef asset)
			{
				string text = Path.Combine(output, "mirror", asset.StorageKind, asset.RelativeBundlePath);
				Directory.CreateDirectory(Path.GetDirectoryName(text));
				File.Copy(asset.BundlePath, text, overwrite: true);
				return MonsterAnimationTransferService.AtPath(asset, text);
			}).ToList()
		};
		try
		{
			MonsterAnimationTransferService.Replace(output, monsterAnimationSet2, monsterAnimationSet, delegate(int count)
			{
				if (count == 2)
				{
					throw new IOException("Injected test failure");
				}
			});
			throw new InvalidDataException("Fault injection did not run");
		}
		catch (IOException ex) when (ex.Message == "Injected test failure")
		{
		}
		bool flag = monsterAnimationSet2.Assets.All((MonsterAnimationAssetRef x) => Hash(x.BundlePath) == originals[original.Assets.First((MonsterAnimationAssetRef a) => a.RelativeBundlePath == x.RelativeBundlePath).BundlePath]);
		if (!flag)
		{
			throw new InvalidDataException("Rollback mismatch");
		}
		int value = MonsterAnimationTransferService.Replace(output, monsterAnimationSet2, monsterAnimationSet);
		using (CurrentMonsterAnimationPreview currentMonsterAnimationPreview = Spine42PreviewRenderer.TryLoad(monsterAnimationSet2, null, 24, 20, 512) ?? throw new InvalidDataException("Transferred animation cannot render"))
		{
			currentMonsterAnimationPreview.Frames[currentMonsterAnimationPreview.Frames.Count / 2].Save(Path.Combine(output, "transferred-preview.png"));
		}
		new MonsterAnimationService().Restore(output, monsterAnimationSet2);
		bool flag2 = monsterAnimationSet2.Assets.All((MonsterAnimationAssetRef x) => Hash(x.BundlePath) == originals[original.Assets.First((MonsterAnimationAssetRef a) => a.RelativeBundlePath == x.RelativeBundlePath).BundlePath]);
		bool flag3 = originals.All<KeyValuePair<string, string>>((KeyValuePair<string, string> x) => Hash(x.Key) == x.Value);
		Console.WriteLine($"bundles={value}; failureRollback={flag}; restore={flag2}; originalsUnchanged={flag3}; gameWrites=False; ready={flag2 && flag3}");
		if (!flag2 || !flag3)
		{
			throw new InvalidDataException("Restore or original resource hash mismatch");
		}
	}

	private static string Hash(string path)
	{
		using FileStream source = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static byte[] ToPng(Bitmap image)
	{
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}
}
