using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class DuelResourceTests
{
	internal static void Run(string realRoot, string output)
	{
		output = Path.GetFullPath(output); Directory.CreateDirectory(output);
		string realLocal = Directory.GetDirectories(Path.Combine(realRoot, "LocalData")).Select(x => Path.Combine(x, "0000"))
			.Where(Directory.Exists).OrderByDescending(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Count()).First();
		IndexService.SetPreferredLocalRoot(realRoot, realLocal);
		var installedFields = DuelResourceCatalog.Fields(realRoot);
		var installedMusic = DuelResourceCatalog.Music(realRoot);
		Console.WriteLine($"READONLY catalog fields={installedFields.Count}; models={installedFields.Sum(x => x.Models.Count)}; previews={installedFields.Count(x => x.Preview != null)}; music={installedMusic.Count}");
		string mirror = Path.Combine(output, "mirror-" + Guid.NewGuid().ToString("N"));
		string local = Path.Combine(mirror, "LocalData", "test", "0000"); Directory.CreateDirectory(local);
		var hashes = new Dictionary<string, string>();
		void Require(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
		void Finish(System.Threading.Tasks.Task task)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(60);
			while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
			if (!task.IsCompleted) throw new TimeoutException("UI test task timed out");
			task.GetAwaiter().GetResult();
		}
		void CopyLogical(string logical)
		{
			string? original = DuelResourceCatalog.Locate(realRoot, logical); if (original == null) return;
			hashes[original] = DuelResourceService.Hash(original);
			string dest = Path.Combine(local, IndexService.ResourceBundleRelativePath(logical));
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(original, dest, true);
		}
		foreach (string language in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" }) CopyLogical("Texts/" + language + "/IDS_ITEM");
		CopyLogical("Duel/ScriptableObject/Bg/BgMatModelSetting");
		foreach (int id in new[] { 1, 2, 4 })
		{
			foreach (string side in new[] { "near", "far" }) CopyLogical($"Duel/BG/Mat/Mat_{id:000}/SD/Mat_{id:000}_{side}");
			foreach (string quality in new[] { "HighEnd_HD", "SD" }) CopyLogical($"Images/Field/{quality}/FieldIcon{(id == 2 ? 1090001 : id == 1 ? 1090003 : 1090004)}");
		}
		foreach (string phase in new[] { "NORMAL", "KEYCARD", "CLIMAX" })
		foreach (int id in new[] { 1, 9 }) CopyLogical($"Sound/AudioClip/BGM/BGM_DUEL_{phase}_{id:00}");
		var fields = DuelResourceCatalog.Fields(mirror);
		var target = fields.Single(x => x.Id == 1); var source = fields.Single(x => x.Id == 4);
		string targetName = target.Names.Split(" / ")[0];
		Require(fields.Single(x => x.Id == 2).Matches("森林") && target.Matches("1090003") && target.Preview != null, "Chinese names, ID, thumbnail");
		Require(new ModEngine().DecodePng(target.Preview!).Length > 100, "thumbnail decodes");
		var music = DuelResourceCatalog.Music(mirror);
		Require(music.Count == 6, "music discovery");
		Require(target.ItemId == 1090003 && target.Models.Count > 0 && target.Models.All(x => x.LogicalPath.Contains("Mat_001")), "field item to model mapping");
		Console.WriteLine("ANNOTATION " + music.Single(x => x.Name == "BGM_DUEL_NORMAL_01"));
		Console.WriteLine("ANNOTATION " + music.Single(x => x.Name == "BGM_DUEL_NORMAL_09"));
		Require(music.Single(x => x.Name == "BGM_DUEL_NORMAL_01").ToString().Contains("（森林") && music.Count(x => x.Matches("森林")) == 3, "Chinese field annotations and music search");
		Require(music.Single(x => x.Name == "BGM_DUEL_NORMAL_01").Annotation.Contains("共用曲目"), "shared music annotation");
		Require(DuelMusicMetadata.Describe("BGM_DUEL_F02_PHASE_A", music[0].Slot, new Dictionary<int, string>()).Annotation.Contains("未确认"), "unknown field binding is not guessed");
		var originalHashes = target.Models.ToDictionary(x => x.BundlePath, x => DuelResourceService.Hash(x.BundlePath));
		var service = new DuelResourceService(mirror);
		service.ReplaceField(mirror, target, source);
		Require(target.Models.All(x => DuelResourceService.State(mirror, x) == "applied"), "field variants committed");
		foreach (var slot in target.Models)
		{
			using var actual = new DuelBundleDocument(slot.BundlePath, slot.Container);
			using var donor = new DuelBundleDocument(source.Models.Single(x => x.Key == slot.Key).BundlePath);
			Require(actual.RootId == donor.RootId && actual.Assets.name != donor.Assets.name, "donor root / destination CAB");
			var textures = new ModEngine().ScanBundle(slot.BundlePath, local, "视觉资源", false).Textures;
			Require(textures.Count > 0 && new ModEngine().DecodePng(textures.First()).Length > 100, "replaced model texture decodes");
		}
		service.Restore(mirror, target.Models);
		Require(originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "field restore byte exact");
		using (var main = new MainForm(_ => { }))
		{
			var matches = typeof(MainForm).GetMethod("MatchesSearch", BindingFlags.Static | BindingFlags.NonPublic)!;
			Require((bool)matches.Invoke(null, new object?[] { target.Preview!, targetName, null })!, "main visual Chinese search");
			Require((bool)matches.Invoke(null, new object?[] { target.Preview!, "003", null })!, "main visual numeric field search");
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(MainForm).GetField("_currentPage", flags)!.SetValue(main, WorkspacePage.Visuals);
			var textures = (List<TexRef>)typeof(MainForm).GetField("_textures", flags)!.GetValue(main)!;
			textures.Clear(); textures.Add(target.Preview!); textures.Add(source.Preview!);
			((TextBox)typeof(MainForm).GetField("_search", flags)!.GetValue(main)!).Text = targetName;
			typeof(MainForm).GetMethod("RenderList", flags)!.Invoke(main, null);
			var visible = (List<TexRef>)typeof(MainForm).GetField("_visibleTextures", flags)!.GetValue(main)!;
			Require(visible.Count == 1 && visible[0] == target.Preview, "main visual rendered search results");
		}
		var incomplete = source with { Models = source.Models.Take(1).ToArray() };
		bool rejected = false; try { service.ReplaceField(mirror, target, incomplete); } catch (InvalidOperationException) { rejected = true; }
		Require(rejected && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "incomplete donor rejected atomically");
		rejected = false;
		try { service.ReplaceField(mirror, target, source, () => throw new IOException("injected")); } catch (IOException) { rejected = true; }
		Require(rejected && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "precommit failure preserves targets");
		rejected = false;
		try { new DuelResourceService(mirror, _ => throw new IOException("injected after first commit")).ReplaceField(mirror, target, source); }
		catch (AggregateException) { rejected = true; }
		Require(rejected && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "partial commit rollback byte exact");
		string changedTarget = target.Models[0].BundlePath;
		byte[] saved = File.ReadAllBytes(changedTarget);
		rejected = false;
		try { service.ReplaceField(mirror, target, source, () => { using var stream = new FileStream(changedTarget, FileMode.Append); stream.WriteByte(42); }); }
		catch (IOException) { rejected = true; }
		Require(rejected && File.ReadAllBytes(changedTarget).Last() == 42, "concurrent external change preserved");
		File.WriteAllBytes(changedTarget, saved);
		using (var locked = new FileStream(changedTarget, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			rejected = false; try { service.ReplaceField(mirror, target, source); } catch (IOException) { rejected = true; }
			Require(rejected && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "locked target rejected");
		}
		string backup = DuelResourceService.BackupPath(mirror, target.Models[0]);
		byte[] savedBackup = File.ReadAllBytes(backup);
		using (var stream = new FileStream(backup, FileMode.Append)) stream.WriteByte(1);
		rejected = false; try { service.Restore(mirror, target.Models); } catch (InvalidDataException) { rejected = true; }
		Require(rejected && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "corrupt backup rejected");
		File.WriteAllBytes(backup, savedBackup);
		var audioTarget = music.Single(x => x.Name == "BGM_DUEL_NORMAL_01"); var audioSource = music.Single(x => x.Name == "BGM_DUEL_NORMAL_09");
		var groupPlan = DuelMusicMetadata.PlanGroup(music, audioTarget, audioSource);
		Require(groupPlan.Length == 3 && groupPlan.All(x => x.Target.Phase == x.Source.Phase), "group phase pairing");
		var groupHashes = groupPlan.ToDictionary(x => x.Target.Slot.BundlePath, x => DuelResourceService.Hash(x.Target.Slot.BundlePath));
		service.ReplaceMusicGroup(mirror, music, audioTarget, audioSource);
		foreach (var pair in groupPlan)
		{
			using var actual = new DuelBundleDocument(pair.Target.Slot.BundlePath);
			using var donor = new DuelBundleDocument(pair.Source.Slot.BundlePath);
			Require(actual.AudioBytes().SequenceEqual(donor.AudioBytes()), "group donor audio " + pair.Target.Phase);
		}
		service.Restore(mirror, groupPlan.Select(x => x.Target.Slot).ToArray());
		Require(groupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "music group restore byte exact");
		rejected = false;
		try { service.ReplaceMusicGroup(mirror, music.Where(x => x != audioSource).ToArray(), audioTarget, audioSource); } catch (InvalidOperationException) { rejected = true; }
		Require(rejected && groupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "missing group phase rejects atomically");
		rejected = false;
		try { service.ReplaceMusicGroup(mirror, music.Select(x => x == audioSource ? x with { Phase = "PHASE_A" } : x).ToArray(), audioTarget, audioSource); } catch (InvalidOperationException) { rejected = true; }
		Require(rejected && groupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "incompatible group phases reject atomically");
		rejected = false;
		try { new DuelResourceService(mirror, _ => throw new IOException("injected group failure")).ReplaceMusicGroup(mirror, music, audioTarget, audioSource); } catch (AggregateException) { rejected = true; }
		Require(rejected && groupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "music group partial commit rollback");
		string audioHash = DuelResourceService.Hash(audioTarget.Slot.BundlePath);
		service.ReplaceMusic(mirror, audioTarget, audioSource);
		using (var actual = new DuelBundleDocument(audioTarget.Slot.BundlePath, audioTarget.Slot.Container))
		using (var donor = new DuelBundleDocument(audioSource.Slot.BundlePath))
			Require(actual.AudioBytes().SequenceEqual(donor.AudioBytes()) && actual.Audio["m_Name"].AsString == audioTarget.Name, "donor music bytes / target identity");
		service.Restore(mirror, new[] { audioTarget.Slot });
		Require(DuelResourceService.Hash(audioTarget.Slot.BundlePath) == audioHash, "music restore byte exact");
		using (var originalAudio = new DuelBundleDocument(audioTarget.Slot.BundlePath))
		{
			File.WriteAllBytes(Path.Combine(output, "original.fsb"), originalAudio.AudioBytes());
			DuelMusicAudio.Convert(Path.Combine(output, "original.fsb"), Path.Combine(output, "original.wav"), false);
			Require(new FileInfo(Path.Combine(output, "original.wav")).Length > 44, "original Vorbis FSB5 decode");
		}
		byte[] pcm = new byte[48000 * 4];
		for (int i = 0; i < 48000; i++) { short value = (short)(Math.Sin(i * 440 * 2 * Math.PI / 48000) * 3000); BitConverter.GetBytes(value).CopyTo(pcm, i * 4); BitConverter.GetBytes(value).CopyTo(pcm, i * 4 + 2); }
		byte[] fsb = DuelMusicAudio.BuildPcmBank(pcm);
		string bank = Path.Combine(output, "sine.fsb"), wav = Path.Combine(output, "sine.wav");
		File.WriteAllBytes(bank, fsb); DuelMusicAudio.Convert(bank, wav, false);
		service.ImportMusic(mirror, audioTarget, wav);
		using (var actual = new DuelBundleDocument(audioTarget.Slot.BundlePath, audioTarget.Slot.Container))
		{
			Require(actual.AudioBytes().SequenceEqual(fsb), "custom PCM FSB5 roundtrip");
			File.WriteAllBytes(Path.Combine(output, "reopened.fsb"), actual.AudioBytes());
			DuelMusicAudio.Convert(Path.Combine(output, "reopened.fsb"), Path.Combine(output, "reopened.wav"), false);
		}
		service.Restore(mirror, new[] { audioTarget.Slot });
		Require(DuelResourceService.Hash(audioTarget.Slot.BundlePath) == audioHash, "import restore / immutable first backup");
		service.ReplaceField(mirror, target, source);
		service.ReplaceMusicGroup(mirror, music, audioTarget, audioSource);
		var managed = DuelModInventory.Read(mirror);
		Require(managed.Count == 4 && managed.Single(x => x.Kind == "场地模型").Slots.Count == 2 && managed.All(x => x.Verified), "existing field and music records unified into MOD inventory");
		var rows = managed.Select(x => x.Row(mirror)).ToArray();
		var packageService = new ModPackageService();
		string managedPackage = Path.Combine(output, "duel-mod-backup.zip");
		Require(packageService.ExportSelected(mirror, Array.Empty<TexRef>(), rows, managedPackage).BundleCount == 5, "selected MOD backup includes both field variants and all music");
		var modifiedHashes = managed.SelectMany(x => x.Slots).ToDictionary(x => x.BundlePath, x => DuelResourceService.Hash(x.BundlePath));
		var originalBackupHashes = managed.SelectMany(x => x.Slots).ToDictionary(x => DuelResourceService.BackupPath(mirror, x), x => DuelResourceService.Hash(DuelResourceService.BackupPath(mirror, x)));
		using (var main = new MainForm(_ => { }, backgroundRefresh: false))
		{
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(MainForm).GetField("_gameRoot", flags)!.SetValue(main, mirror);
			typeof(MainForm).GetField("_assetRoot", flags)!.SetValue(main, local);
			var mainTextures = (List<TexRef>)typeof(MainForm).GetField("_textures", flags)!.GetValue(main)!;
			mainTextures.Clear(); mainTextures.Add(target.Preview!);
			Finish((System.Threading.Tasks.Task)typeof(MainForm).GetMethod("RefreshModFlagsAsync", flags)!.Invoke(main, null)!);
			typeof(MainForm).GetMethod("SetModsOnly", flags)!.Invoke(main, new object[] { true });
			typeof(MainForm).GetMethod("RenderList", flags)!.Invoke(main, null);
			var visible = (List<TexRef>)typeof(MainForm).GetField("_visibleTextures", flags)!.GetValue(main)!;
			Require(visible.Count == 4 && visible.All(x => x.IsModded && x.ManagedDuel != null), "MOD management shows one field row and music rows without duplicate preview");
			Require(target.Preview!.IsModded, "field preview receives yellow MOD flag");
			string previewBackup = Path.Combine(mirror, "_MD卡图备份", target.Preview!.SourceKind, target.Preview.RelativeBundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(previewBackup)!); File.Copy(target.Preview.BundlePath, previewBackup);
			File.Copy(source.Preview!.BundlePath, target.Preview.BundlePath, true);
			Finish((System.Threading.Tasks.Task)typeof(MainForm).GetMethod("RefreshModFlagsAsync", flags)!.Invoke(main, null)!);
			typeof(MainForm).GetMethod("RenderList", flags)!.Invoke(main, null);
			Require(visible.Count == 5 && visible.Contains(target.Preview!), "independent modified field preview remains in MOD management");
			var resolved = (TexRef[])typeof(MainForm).GetMethod("ResolveManagedSelection", flags)!.Invoke(main, new object[] { new[] { target.Preview! } })!;
			Require(resolved.Single().ManagedDuel == null, "independent field preview restores as an image");
			new TextureModRestoreService().Restore(mirror, resolved);
			Finish((System.Threading.Tasks.Task)typeof(MainForm).GetMethod("RefreshModFlagsAsync", flags)!.Invoke(main, null)!);
			typeof(MainForm).GetMethod("RenderList", flags)!.Invoke(main, null);
			main.Show(); Application.DoEvents();
			using var capture = new System.Drawing.Bitmap(main.Width, main.Height);
			main.DrawToBitmap(capture, new System.Drawing.Rectangle(0, 0, main.Width, main.Height));
			capture.Save(Path.Combine(output, "mod-manager-ui.png"));
		}
		using (var form = new DuelResourceForm(mirror))
		{
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			Finish((System.Threading.Tasks.Task)typeof(DuelResourceForm).GetMethod("RefreshModColors", flags)!.Invoke(form, null)!);
			Require(form.ItemColor(target) == UiTheme.Gold && form.ItemColor(audioTarget) == UiTheme.Gold && form.ItemColor(audioSource) == UiTheme.Text, "modified field and music text yellow; original remains normal");
		}
		string imagePath = Path.Combine(local, "ab", "abcd1234");
		string imageBackup = Path.Combine(mirror, "_MD卡图备份", "视觉资源", "ab", "abcd1234");
		Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!); Directory.CreateDirectory(Path.GetDirectoryName(imageBackup)!);
		File.Copy(DuelResourceService.BackupPath(mirror, target.Models[0]), imageBackup);
		File.Copy(source.Models[0].BundlePath, imagePath);
		var imageRow = new ModEngine().ScanBundle(imagePath, local, "视觉资源", false).Textures.First();
		var mixedRows = rows.Append(imageRow).ToArray();
		modifiedHashes[imagePath] = DuelResourceService.Hash(imagePath);
		var restoreService = new TextureModRestoreService();
		rejected = false;
		try { restoreService.Restore(mirror, mixedRows, _ => throw new IOException("batch restore failure")); } catch (IOException) { rejected = true; }
		Require(rejected && modifiedHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "batch restore rollback preserves all modified resources");
		restoreService.Restore(mirror, mixedRows);
		Require(DuelResourceService.Hash(imagePath) == DuelResourceService.Hash(imageBackup), "mixed image field music batch restore");
		modifiedHashes.Remove(imagePath);
		Require(DuelModInventory.Read(mirror).Count == 0 && originalHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value) && groupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "MOD batch restore returns all originals and clears inventory");
		rejected = false;
		try { packageService.Import(mirror, managedPackage, afterCommit: _ => throw new IOException("import failure")); } catch (IOException) { rejected = true; }
		Require(rejected && DuelModInventory.Read(mirror).Count == 0, "MOD backup import partial failure rolls back");
		Require(packageService.Import(mirror, managedPackage).BundleCount == 5 && modifiedHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "MOD backup import reapplies field and music with managed records");
		Require(originalBackupHashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "MOD backup import keeps immutable first originals");
		byte[] acceptedMusic = File.ReadAllBytes(audioTarget.Slot.BundlePath);
		using (var edit = new FileStream(audioTarget.Slot.BundlePath, FileMode.Append)) edit.WriteByte(42);
		var unknown = DuelModInventory.Read(mirror).Single(x => x.Slots.Any(s => s.BundlePath == audioTarget.Slot.BundlePath));
		Require(!unknown.Verified && unknown.Row(mirror).IsModded, "unknown external edit remains visible and flagged");
		rejected = false; try { restoreService.Restore(mirror, new[] { unknown.Row(mirror) }); } catch (IOException) { rejected = true; }
		Require(rejected && File.ReadAllBytes(audioTarget.Slot.BundlePath).Last() == 42, "unknown external edit protected from restore");
		rejected = false; try { packageService.ExportSelected(mirror, Array.Empty<TexRef>(), new[] { unknown.Row(mirror) }, Path.Combine(output, "unknown.zip")); } catch (IOException) { rejected = true; }
		Require(rejected, "unknown external edit protected from export");
		File.WriteAllBytes(audioTarget.Slot.BundlePath, acceptedMusic);
		restoreService.Restore(mirror, DuelModInventory.Read(mirror).Select(x => x.Row(mirror)).ToArray());
		string badPackage = Path.Combine(output, "wrong-build.zip"); File.Copy(managedPackage, badPackage);
		using (var zip = System.IO.Compression.ZipFile.Open(badPackage, System.IO.Compression.ZipArchiveMode.Update))
		{
			var entry = zip.GetEntry("manifest.json")!;
			System.Text.Json.Nodes.JsonNode manifest;
			using (var stream = entry.Open()) manifest = System.Text.Json.Nodes.JsonNode.Parse(stream)!;
			foreach (var item in manifest["Entries"]!.AsArray()) item!["GameBuild"] = "incompatible-test-build";
			entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()); writer.Write(manifest.ToJsonString());
		}
		rejected = false; try { packageService.Import(mirror, badPackage); } catch (InvalidDataException) { rejected = true; }
		Require(rejected && DuelModInventory.Read(mirror).Count == 0, "different game build rejected without writes");
		string otherRoot = Path.Combine(output, "other-account"), otherLocal = Path.Combine(otherRoot, "LocalData", "different-user", "0000");
		foreach (string file in Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories))
		{
			string destination = Path.Combine(otherLocal, Path.GetRelativePath(local, file));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
		}
		IndexService.SetPreferredLocalRoot(otherRoot, otherLocal);
		Require(packageService.Import(otherRoot, managedPackage).BundleCount == 5 && DuelModInventory.Read(otherRoot).Count == 4, "MOD backup maps to current different account");
		restoreService.Restore(otherRoot, DuelModInventory.Read(otherRoot).Select(x => x.Row(otherRoot)).ToArray());
		Require(DuelModInventory.Read(otherRoot).Count == 0, "imported account has usable original backups");
		using (var form = new DuelResourceForm(mirror))
		{
			Finish((System.Threading.Tasks.Task)typeof(DuelResourceForm).GetMethod("RefreshModColors", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!);
			Require(form.ItemColor(target) == UiTheme.Text && form.ItemColor(audioTarget) == UiTheme.Text, "restored field and music return to normal text");
		}
		foreach (var language in Enum.GetValues<AppLanguage>())
		{
			Localizer.SetLanguage(language);
			using var form = new DuelResourceForm(mirror); form.CreateControl();
			Require(form.Text == Localizer.T("duel.title"), "localized form " + language);
		}
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		using (var form = new DuelResourceForm(mirror))
		{
			form.Show();
			var busy = typeof(DuelResourceForm).GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
			DateTime deadline = DateTime.UtcNow.AddSeconds(40);
			Application.DoEvents();
			while ((bool)busy.GetValue(form)! && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
			Require(!(bool)busy.GetValue(form)!, "UI catalog load finishes");
			var search = (TextBox)typeof(DuelResourceForm).GetField("_targetSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
			var list = (ListBox)typeof(DuelResourceForm).GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
			search.Text = targetName; Application.DoEvents(); Require(list.Items.Count == 1, "UI Chinese search");
			search.Text = ""; Application.DoEvents();
			var sources = (ListBox)typeof(DuelResourceForm).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
			sources.SelectedIndex = 1;
			var picture = (PictureBox)typeof(DuelResourceForm).GetField("_targetPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
			var sourcePicture = (PictureBox)typeof(DuelResourceForm).GetField("_sourcePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
			while ((picture.Image == null || sourcePicture.Image == null) && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
			Require(picture.Image != null && sourcePicture.Image != null, "both field previews loaded");
			using var screenshot = new System.Drawing.Bitmap(form.Width, form.Height);
			form.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
			screenshot.Save(Path.Combine(output, "duel-ui.png"));
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var mode = (ComboBox)typeof(DuelResourceForm).GetField("_mode", flags)!.GetValue(form)!;
			var player = (System.Media.SoundPlayer)typeof(DuelResourceForm).GetField("_player", flags)!.GetValue(form)!;
			var stop = (Button)typeof(DuelResourceForm).GetField("_stop", flags)!.GetValue(form)!;
			mode.SelectedIndex = 1;
			search.Text = "森林"; Application.DoEvents();
			Require(list.Items.Count == 3 && list.Items.Cast<DuelMusic>().All(x => x.Group == "DUEL_01"), "UI music Chinese field filter");
			using (var musicScreenshot = new System.Drawing.Bitmap(form.Width, form.Height))
			{
				form.DrawToBitmap(musicScreenshot, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
				musicScreenshot.Save(Path.Combine(output, "duel-music-ui.png"));
			}
			search.Text = "";
			stop.PerformClick(); stop.PerformClick();
			Require(player.SoundLocation == "", "stop before playback is repeatable");
			for (int attempt = 0; attempt < 2; attempt++)
			{
				player.SoundLocation = wav; player.Load(); player.Play();
				System.Threading.Thread.Sleep(100);
				stop.PerformClick(); stop.PerformClick();
				Require(player.SoundLocation == wav, "stop during playback / restart " + attempt);
			}
			player.Play(); mode.SelectedIndex = 0;
			Require(player.SoundLocation == wav, "mode switch stops loaded playback safely");
			mode.SelectedIndex = 1; player.Play();
			form.Close();
			Require(!form.Visible, "close during playback succeeds");
		}
		Require(hashes.All(x => DuelResourceService.Hash(x.Key) == x.Value), "real game files unchanged");
		File.WriteAllText(Path.Combine(output, "summary.txt"), "Chinese names / thumbnails / model variants / donor audio / PCM FSB5 / exact restores / precommit failure / incomplete donor / four-language forms: PASS. gameWrites=False");
	}
}
