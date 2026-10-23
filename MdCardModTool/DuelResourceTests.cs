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
		void CopyLogical(string logical)
		{
			string? original = DuelResourceCatalog.Locate(realRoot, logical); if (original == null) return;
			hashes[original] = DuelResourceService.Hash(original);
			string dest = Path.Combine(local, IndexService.ResourceBundleRelativePath(logical));
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(original, dest, true);
		}
		foreach (string language in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" }) CopyLogical("Texts/" + language + "/IDS_ITEM");
		foreach (int id in new[] { 1, 4 })
		{
			foreach (string side in new[] { "near", "far" }) CopyLogical($"Duel/BG/Mat/Mat_{id:000}/SD/Mat_{id:000}_{side}");
			foreach (string quality in new[] { "HighEnd_HD", "SD" }) CopyLogical($"Images/Field/{quality}/FieldIcon{1090000 + id}");
		}
		foreach (string name in new[] { "BGM_DUEL_NORMAL_01", "BGM_DUEL_NORMAL_09" }) CopyLogical("Sound/AudioClip/BGM/" + name);
		var fields = DuelResourceCatalog.Fields(mirror);
		var target = fields.Single(x => x.Id == 1); var source = fields.Single(x => x.Id == 4);
		Require(target.Matches("森林") && target.Matches("1090001") && target.Preview != null, "Chinese names, ID, thumbnail");
		Require(new ModEngine().DecodePng(target.Preview!).Length > 100, "thumbnail decodes");
		var music = DuelResourceCatalog.Music(mirror);
		Require(music.Count == 2, "music discovery");
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
			Require((bool)matches.Invoke(null, new object?[] { target.Preview!, "森林", null })!, "main visual Chinese search");
			Require((bool)matches.Invoke(null, new object?[] { target.Preview!, "001", null })!, "main visual numeric field search");
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(MainForm).GetField("_currentPage", flags)!.SetValue(main, WorkspacePage.Visuals);
			var textures = (List<TexRef>)typeof(MainForm).GetField("_textures", flags)!.GetValue(main)!;
			textures.Clear(); textures.Add(target.Preview!); textures.Add(source.Preview!);
			((TextBox)typeof(MainForm).GetField("_search", flags)!.GetValue(main)!).Text = "森林";
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
		var audioTarget = music[0]; var audioSource = music[1];
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
			search.Text = "森林"; Application.DoEvents(); Require(list.Items.Count == 1, "UI Chinese search");
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
