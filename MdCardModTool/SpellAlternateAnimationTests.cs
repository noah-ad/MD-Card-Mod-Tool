using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class SpellAlternateAnimationTests
{
	internal static void Run(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		string fixtureRoot = Directory.GetDirectories(Path.Combine(gameRoot, "LocalData"))
			.Select(x => Path.Combine(x, "0000")).First(x => File.Exists(Path.Combine(x, IndexService.ResourceBundleRelativePath(SpellAlternateAnimationService.SettingPath))));
		IndexService.SetPreferredLocalRoot(gameRoot, fixtureRoot);
		var reader = new SpellAlternateAnimationService();
		var candidates = reader.Discover(gameRoot);
		Assert(candidates.Any(x => x.TargetId == 13619 && x.SourceId == 21233), "Called by the Grave discovery");
		Assert(candidates.Any(x => x.TargetId == 4842 && x.SourceId == 23491), "Monster Reborn discovery");
		Assert(candidates.Any(x => x.TargetId == 15057 && x.SourceId == 22188), "Fusion Deployment discovery");
		var live = reader.Inspect(gameRoot, 5328, 20040);
		var viable = candidates.Select(x => (Candidate: x, Inspection: reader.Inspect(gameRoot, x.TargetId, x.SourceId))).ToArray();
		File.WriteAllText(Path.Combine(output, "candidates.json"), JsonSerializer.Serialize(viable.Select(x => new { x.Candidate, x.Inspection }), new JsonSerializerOptions { WriteIndented = true }));
		var files = viable.SelectMany(x => x.Inspection.Dependencies).Append(live.BundlePath).Distinct().ToArray();
		var hashes = files.ToDictionary(x => x, SpellAlternateAnimationService.FileHash);
		string mirror = Path.Combine(Path.GetFullPath(output), "mirror-" + Guid.NewGuid().ToString("N"));
		string local = Path.Combine(mirror, "LocalData", "test-account", "0000");
		foreach (var source in files)
		{
			string relative = Path.GetRelativePath(source.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase) ? fixtureRoot : IndexService.StreamingRoot(gameRoot), source);
			string target = Path.Combine(local, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(source, target);
		}
		IndexService.SetPreferredLocalRoot(mirror, local);
		var service = new SpellAlternateAnimationService(mirror);
		var initial = service.Inspect(mirror, 5328, 20040);
		string path = initial.BundlePath;
		string originalHash = SpellAlternateAnimationService.FileHash(path);
		using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			Reject<IOException>(() => service.Apply(mirror, 5328, 20040));
		Assert(SpellAlternateAnimationService.FileHash(path) == originalHash, "Lock rejection changed file");
		Reject<IOException>(() => service.Change(mirror, 5328, 20040, false, false, () => throw new IOException("injected")));
		Assert(SpellAlternateAnimationService.FileHash(path) == originalHash, "Pre-commit failure changed file");
		int applied = 0;
		foreach (var (candidate, inspection) in viable)
		{
			if (!inspection.CanApply)
			{
				Assert(inspection.Diagnostic.Length > 0, "Missing rejection diagnostic");
				continue;
			}
			string[] before;
			using (var settings = new SpellAlternateAnimationService.Settings(path)) before = SpellAlternateAnimationService.Encoded(settings.Rows(candidate.TargetId));
			service.Apply(mirror, candidate.TargetId, candidate.SourceId, true);
			using (var settings = new SpellAlternateAnimationService.Settings(path))
			{
				var expected = settings.Rows(candidate.SourceId).Select(SpellAlternateAnimationService.Clone).ToArray();
				foreach (var row in expected) row["mrk"].AsInt = candidate.TargetId;
				Assert(SpellAlternateAnimationService.Encoded(settings.Rows(candidate.TargetId)).SequenceEqual(SpellAlternateAnimationService.Encoded(expected)), "Full player/timing row transfer: " + candidate);
			}
			service.Restore(mirror, candidate.TargetId, candidate.SourceId);
			using (var settings = new SpellAlternateAnimationService.Settings(path))
				Assert(SpellAlternateAnimationService.Encoded(settings.Rows(candidate.TargetId)).SequenceEqual(before), "Restore registrations: " + candidate);
			applied++;
		}
		Assert(applied >= 4, "Need multiple installed spell pairs");
		service.Apply(mirror, 5328, 20040);
		using (var settings = new SpellAlternateAnimationService.Settings(path))
			Assert(settings.Rows(5328).Single()["seLabel"].AsString == "SE_EV_039_NORMAL", "Default sound changed");
		Assert(SpellAlternateAnimationService.FileHash(initial.BackupPath) == originalHash, "First backup overwritten");
		Edit(path, settings => settings.Rows(11708).Single()["seLabel"].AsString = "UNRELATED_MOD_SOUND");
		string timeline = initial.Dependencies.First(x => new ModEngine().ReadAssetBundleContainerPaths(x).Any(c => c.EndsWith(".playable")));
		byte[] timelineBytes = File.ReadAllBytes(timeline);
		File.Delete(timeline);
		Assert(!service.Inspect(mirror, 5328, 20040).CanApply, "Missing dependency allowed write");
		service.Restore(mirror, 5328, 20040);
		using (var settings = new SpellAlternateAnimationService.Settings(path))
			Assert(settings.Rows(11708).Single()["seLabel"].AsString == "UNRELATED_MOD_SOUND", "Restore lost unrelated Mod");
		File.WriteAllBytes(timeline, timelineBytes);
		service.Apply(mirror, 5328, 20040);
		Edit(path, settings => settings.Rows(5328).Single()["centerEffectPath"].AsString = "Unknown/Effect");
		Assert(service.Inspect(mirror, 5328, 20040).State == "unknown", "Unknown target edit not detected");
		Reject<InvalidOperationException>(() => service.Apply(mirror, 5328, 20040));
		File.Copy(live.BundlePath, path, true);
		Reject<IOException>(() => service.Change(mirror, 5328, 20040, false, false,
			() => Edit(path, settings => settings.Rows(11708).Single()["seLabel"].AsString = "CONCURRENT_CHANGE")));
		using (var settings = new SpellAlternateAnimationService.Settings(path))
			Assert(settings.Rows(11708).Single()["seLabel"].AsString == "CONCURRENT_CHANGE", "Concurrent Mod lost");
		File.Copy(live.BundlePath, path, true);
		File.WriteAllText(initial.BackupPath, "corrupt");
		Reject<InvalidDataException>(() => service.Apply(mirror, 5328, 20040));
		File.Copy(live.BundlePath, initial.BackupPath, true);
		Reject<InvalidOperationException>(() => service.Apply(mirror, 5328, 23491));
		Reject<InvalidOperationException>(() => service.Apply(mirror, 13671, 3434)); // script-only source
		Assert(!Directory.GetFiles(Path.GetDirectoryName(path)!, "*.md-spell-*").Any(), "Temporary file leak");
		Application.EnableVisualStyles();
		foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
		{
			Localizer.SetLanguage(language);
			using var form = new SpellAlternateAnimationForm(mirror) { Opacity = 0, ShowInTaskbar = false };
			form.Show();
			Application.DoEvents();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			var layout = form.Controls.OfType<TableLayoutPanel>().Single();
			while (!layout.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().First().Enabled && watch.ElapsedMilliseconds < 15000)
			{
				Application.DoEvents();
				System.Threading.Thread.Sleep(10);
			}
			Assert(watch.ElapsedMilliseconds < 15000, "Form inspection timeout");
			Assert(layout.Controls.OfType<ComboBox>().Single().Items.Count > 2, "Form candidate discovery");
			Assert(layout.Controls.OfType<TextBox>().Single().Text.Contains(path), "Form did not finish inspection");
			using var bitmap = new Bitmap(form.Width, form.Height);
			form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
			bitmap.Save(Path.Combine(output, $"spell-{language}.png"));
		}
		foreach (var item in hashes) Assert(SpellAlternateAnimationService.FileHash(item.Key) == item.Value, "Real fixture changed");
		File.WriteAllText(Path.Combine(output, "result.txt"), $"discoveredPairs={candidates.Count}; appliedAndRestoredPairs={applied}; multiPlayerRows=True; addAndRemoveRegistration=True; sourceSound=True; preserveSound=True; unrelatedModsPreserved=True; backupImmutable=True; lockRejected=True; preCommitFailure=True; missingDependencyRejected=True; restoreWithoutDependency=True; unknownEditsRejected=True; concurrentChangePreserved=True; corruptBackupRejected=True; invalidPairRejected=True; scriptSourceRejected=True; fourLanguageForms=True; gameWrites=False\n");
	}
	private static void Edit(string path, Action<SpellAlternateAnimationService.Settings> edit)
	{
		string temp = path + ".fixture";
		using (var settings = new SpellAlternateAnimationService.Settings(path)) { edit(settings); settings.Save(temp); }
		File.Move(temp, path, true);
	}
	private static void Assert(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
	private static void Reject<T>(Action action) where T : Exception
	{
		try { action(); } catch (T) { return; }
		throw new InvalidDataException("Expected rejection: " + typeof(T).Name);
	}
}
