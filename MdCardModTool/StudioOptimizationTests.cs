using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class StudioOptimizationTests
{
	public static void Run(string output)
	{
		Directory.CreateDirectory(output);
		Spine42PreviewRenderer.TestAtlasGeometry();
		using CropCanvas canvas = new(new Bitmap(400, 600), 704, 1024, overFrameEditing: true) { Size = new Size(900, 900) };
		canvas.SetFrame(new Bitmap(704, 1024), artWindow: new RectangleF(89, 191, 527, 528));
		ImageRenderSpec art = canvas.RenderSpec;
		using Bitmap source = new(960, 720);
		using (Graphics g = Graphics.FromImage(source))
		{
			g.Clear(Color.Teal);
			g.FillRectangle(Brushes.Gold, 0, 0, 480, 360);
			g.FillEllipse(Brushes.Fuchsia, 450, 250, 200, 200);
		}
		canvas.SetBackground(new Bitmap(source));
		byte[] before = canvas.RenderBackgroundToTarget()!;
		canvas.EditBackground(true);
		canvas.SetZoom(.7f, null);
		typeof(CropCanvas).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(canvas, [new KeyEventArgs(Keys.Right | Keys.Shift)]);
		ImageRenderSpec background = canvas.BackgroundRenderSpec;
		if (canvas.RenderSpec != art || background.OffsetX == 0) throw new InvalidDataException("Background changed art or did not move");
		int resizeChanges = 0;
		void CountResize(ImageRenderSpec _) => resizeChanges++;
		canvas.ViewChanged += CountResize;
		canvas.Size = new Size(720, 780);
		canvas.ViewChanged -= CountResize;
		if (resizeChanges != 0) throw new InvalidDataException("Viewport resize unnecessarily invalidated composition");
		if (Math.Abs(canvas.RenderSpec.ImageScale-art.ImageScale)>.001 || Math.Abs(canvas.RenderSpec.OffsetY-art.OffsetY)>.01
			|| canvas.BackgroundRenderSpec != background) throw new InvalidDataException("Resize changed layer composition");
		byte[] after = canvas.RenderBackgroundToTarget()!;
		if (before.SequenceEqual(after)) throw new InvalidDataException("Background output did not change");
		canvas.SetFrame(new Bitmap(704,1024), true, new RectangleF(89,191,527,528));
		if (canvas.BackgroundRenderSpec != background) throw new InvalidDataException("Frame selection reset background");
		canvas.EditBackground(false);
		canvas.SetZoom(.8f, null);
		if (canvas.BackgroundRenderSpec != background) throw new InvalidDataException("Art changed background");
		OverFrameArtStore.SaveBackground(output, 1, ToPng(source));
		OverFrameArtStore.SaveSettings(output, 1, new(BackgroundImageScale: background.ImageScale, BackgroundOffsetX: background.OffsetX, BackgroundOffsetY: background.OffsetY));
		var settings = OverFrameArtStore.ReadSettings(output, 1);
		using Bitmap restoredSource = FrameComposer.PreviewBitmap(File.ReadAllBytes(OverFrameArtStore.BackgroundPath(output, 1)));
		if (restoredSource.Size != source.Size) throw new InvalidDataException("Background was destructively cropped");
		canvas.SetBackground(new Bitmap(restoredSource));
		canvas.SetBackgroundRenderSpec(new(704,1024,settings.BackgroundImageScale,settings.BackgroundOffsetX,settings.BackgroundOffsetY));
		if (!after.SequenceEqual(canvas.RenderBackgroundToTarget()!)) throw new InvalidDataException("Background draft changed on reopen");
		File.WriteAllBytes(Path.Combine(output,"background-before.png"), before);
		File.WriteAllBytes(Path.Combine(output,"background-after.png"), after);
		canvas.EditBackground(true);
		using Bitmap screenshot = new(canvas.Width,canvas.Height);
		canvas.DrawToBitmap(screenshot,canvas.ClientRectangle);
		screenshot.Save(Path.Combine(output,"background-editor.png"));
		Console.WriteLine("atlasRotations=4; meshTrim=True; boneJoint=True; backgroundIndependent=True; originalSize=960x720; draftRoundtrip=True; ready=True");
	}

	public static void Picker(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
		{
			Localizer.SetLanguage(language);
			using AnimationDonorPicker picker = new(gameRoot, ["3801", "3413", "10001"])
			{ ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new Point(20,20) };
			picker.Show();
			Application.DoEvents();
			var search = (ImeAwareTextBox)typeof(AnimationDonorPicker).GetField("_search",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(picker)!;
			search.Text = "青眼";
			Application.DoEvents();
			if (picker.SelectedCardId != "3801") throw new InvalidDataException("Donor cross-language name search failed");
			if (language == AppLanguage.SimplifiedChinese)
			{
				// This CLI harness pumps messages without Application.Run. Install the
				// UI context a real modal dialog has, so await resumes on this UI thread
				// and its WinForms timer is not accidentally created on a pool thread.
				var previousContext = System.Threading.SynchronizationContext.Current;
				using WindowsFormsSynchronizationContext context = new();
				System.Threading.SynchronizationContext.SetSynchronizationContext(context);
				var task = (System.Threading.Tasks.Task)typeof(AnimationDonorPicker).GetMethod("PreviewAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,null)!;
				var wait = System.Diagnostics.Stopwatch.StartNew();
				while (!task.IsCompleted && wait.Elapsed.TotalSeconds<90) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
				if (!task.IsCompleted) throw new TimeoutException("Donor preview timed out");
				task.GetAwaiter().GetResult();
				System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
				var frames = (CurrentMonsterAnimationPreview?)typeof(AnimationDonorPicker).GetField("_frames",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(picker);
				if (frames?.Frames.Count is not > 1) throw new InvalidDataException("Donor preview unavailable");
				var playback = System.Diagnostics.Stopwatch.StartNew();
				while (playback.ElapsedMilliseconds < 450) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
				var previewCanvas = (AnimationPreviewCanvas)typeof(AnimationDonorPicker).GetField("_preview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(picker)!;
				if (previewCanvas.Frame == null || ReferenceEquals(previewCanvas.Frame,frames.Frames[0])) throw new InvalidDataException("Donor playback did not advance");
				using Bitmap picture = new(picker.Width,picker.Height);
				picker.DrawToBitmap(picture,new Rectangle(Point.Empty,picker.Size));
				picture.Save(Path.Combine(output,"animation-source-picker.png"));
			}
			picker.Close();
		}
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		Console.WriteLine("donorSearchLanguages=4; sourcePreview=True; close=True; gameWrites=False; ready=True");
	}

	public static void Transfer(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		var original = MonsterAnimationIndexService.Find(gameRoot,"10001");
		var donor = MonsterAnimationIndexService.Find(gameRoot,"3413");
		var originals = original.Assets.Concat(donor.Assets).DistinctBy(x=>x.BundlePath).ToDictionary(x=>x.BundlePath,x=>Hash(x.BundlePath));
		var target = new MonsterAnimationSet { CardId=original.CardId, Assets=original.Assets.Select(asset=>
		{
			string file = Path.Combine(output,"mirror",asset.StorageKind,asset.RelativeBundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			File.Copy(asset.BundlePath,file,true);
			return MonsterAnimationTransferService.AtPath(asset,file);
		}).ToList() };
		try
		{
			MonsterAnimationTransferService.Replace(output,target,donor,count=> { if(count==2) throw new IOException("Injected test failure"); });
			throw new InvalidDataException("Fault injection did not run");
		}
		catch(IOException ex) when(ex.Message=="Injected test failure") { }
		bool rollback = target.Assets.All(x=>Hash(x.BundlePath)==originals[original.Assets.First(a=>a.RelativeBundlePath==x.RelativeBundlePath).BundlePath]);
		if (!rollback) throw new InvalidDataException("Rollback mismatch");
		int count = MonsterAnimationTransferService.Replace(output,target,donor);
		using(var preview = Spine42PreviewRenderer.TryLoad(target, maxFrames: 20, previewMaxEdge: 512)
			?? throw new InvalidDataException("Transferred animation cannot render"))
			preview.Frames[preview.Frames.Count/2].Save(Path.Combine(output,"transferred-preview.png"));
		new MonsterAnimationService().Restore(output,target);
		bool restored = target.Assets.All(x=>Hash(x.BundlePath)==originals[original.Assets.First(a=>a.RelativeBundlePath==x.RelativeBundlePath).BundlePath]);
		bool untouched = originals.All(x=>Hash(x.Key)==x.Value);
		Console.WriteLine($"bundles={count}; failureRollback={rollback}; restore={restored}; originalsUnchanged={untouched}; gameWrites=False; ready={restored && untouched}");
		if (!restored || !untouched) throw new InvalidDataException("Restore or original resource hash mismatch");
	}
	private static string Hash(string path) { using var f=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
	private static byte[] ToPng(Bitmap image) { using var s = new MemoryStream(); image.Save(s,System.Drawing.Imaging.ImageFormat.Png); return s.ToArray(); }
}
