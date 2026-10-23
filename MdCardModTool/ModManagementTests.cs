using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class ModManagementTests
{
	internal static void Run(string gameRoot, string output)
	{
		IndexService.SetPreferredLocalRoot(gameRoot, Directory.GetDirectories(Path.Combine(gameRoot, "LocalData"))
			.Select(x => Path.Combine(x, "0000")).Where(Directory.Exists)
			.OrderByDescending(x => Directory.EnumerateFiles(x, "*", SearchOption.AllDirectories).Count()).First());
		Directory.CreateDirectory(output);
		ModEngine engine = new();
		MonsterAnimationAssetRef[] animation = MonsterAnimationIndexService.Find(gameRoot, "10001").Assets
			.DistinctBy(x => x.BundlePath, StringComparer.OrdinalIgnoreCase).ToArray();
		Assert(animation.Length >= 6, "Need installed official animation fixtures");
		TextAssetRef gate = new OverFrameService().FindCachedGate(gameRoot) ?? throw new InvalidDataException("Need cached original overframe gate fixture");
		Dictionary<string, string> originals = animation.Select(x => x.BundlePath).Append(gate.BundlePath)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(x => x, Hash, StringComparer.OrdinalIgnoreCase);
		byte[] original = File.ReadAllBytes(animation[0].BundlePath);
		byte[] modified = File.ReadAllBytes(animation[1].BundlePath);
		Assert(!original.SequenceEqual(modified), "Distinct valid Unity Bundle fixtures");

		string mirror = Path.Combine(output, "mirror_" + Guid.NewGuid().ToString("N"));
		string local = Path.Combine(mirror, "LocalData", "test001", "0000");
		Directory.CreateDirectory(local);
		IndexService.SetPreferredLocalRoot(mirror, local);
		DateTime old = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);
		DateTime recent = old.AddHours(3);
		List<TexRef> textures = new();
		TexRef cardA = Add("10001", "本地卡图", "12/12345678", old);
		TexRef cardB = Add("22524", "本地卡图", "34/34567890", old.AddHours(1));
		textures.Add(new TexRef
		{
			BundlePath = cardB.BundlePath, RelativeBundlePath = cardB.RelativeBundlePath, CardKey = cardB.CardKey,
			SourceKind = cardB.SourceKind, Name = "second texture", Width = 512, Height = 512, PathId = 2
		});
		TexRef streaming = Add("4007", "游戏内图片", "56/56789012", old.AddHours(2));
		TexRef restored = Add("7687", "本地卡图", "78/78901234", old, restored: true);
		TexRef visual = Add("10001", "视觉资源", "9a/9abcdef0", recent.AddMinutes(1));

		List<string> animationPaths = new();
		foreach (MonsterAnimationAssetRef asset in animation)
		{
			string root = asset.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(mirror) : local;
			string live = Path.Combine(root, asset.RelativeBundlePath);
			Write(live, modified);
			Write(Path.Combine(mirror, "_MD卡图备份", asset.ModSourceKind, asset.RelativeBundlePath), original);
			File.SetLastWriteTimeUtc(live, recent);
			animationPaths.Add(live);
		}

		string gateLive = Path.Combine(local, gate.RelativeBundlePath);
		Write(gateLive, File.ReadAllBytes(gate.BundlePath));
		TextAssetRef copiedGate = engine.FindTextAssetFast(gateLive, local, OverFrameService.GateName)
			?? throw new InvalidDataException("Copied gate");
		OverFrameService frames = new();
		List<OverFrameMapping> mappings = frames.Read(copiedGate);
		mappings.RemoveAll(x => x.CardId is 10001 or 22524);
		engine.ReplaceTextAsset(copiedGate, EncodeMappings(mappings), Path.Combine(mirror, "_fixtureGateBackup"));
		copiedGate = engine.FindTextAssetFast(gateLive, local, OverFrameService.GateName)!;
		mappings.Add(new OverFrameMapping(10001, 10001));
		mappings.Add(new OverFrameMapping(22524, 22524));
		engine.ReplaceTextAsset(copiedGate, EncodeMappings(mappings), Path.Combine(mirror, "_MD卡图备份", "超框开关"));
		File.SetLastWriteTimeUtc(gateLive, recent.AddDays(1)); // Must not lift old image-only cards to the top.

		ModPackageService service = new();
		ModChangeSummary summary = service.RefreshState(mirror, textures);
		Assert(summary.BundleCount == animationPaths.Count + 5, "Unique changed Bundles; restored resource excluded");
		Assert(summary.AnimationBundleCount == animationPaths.Count, "Animation summary");
		Assert(cardA.ModModifiedAtUtc == recent, "Associated animation modification time");
		Assert(cardB.ModModifiedAtUtc == old.AddHours(1), $"Shared gate must not override card edit time: actual={cardB.ModModifiedAtUtc:O}; expected={old.AddHours(1):O}");
		Assert(!restored.IsModded && restored.ModModifiedAtUtc == null, "Restored item has no timestamp");
		Assert(textures.Where(x => x.BundlePath == cardB.BundlePath).All(x => x.IsModded), "Shared texture flags");

		string[] aScope = animationPaths.Append(cardA.BundlePath).Append(gateLive).ToArray();
		foreach (bool direct in new[] { true, false })
		{
			string package = Path.Combine(output, direct ? "selected-direct.zip" : "selected.mdmod.zip");
			service.ExportSelected(mirror, textures, new[] { cardA }, package, direct);
			CheckScope(package, aScope);
			using ZipArchive zip = ZipFile.OpenRead(package);
			Assert(direct == zip.Entries.Any(x => x.FullName.StartsWith("LocalData/", StringComparison.Ordinal)), "Selected output format");
			ModPackageManifest manifest = ReadManifest(zip);
			Assert(manifest.Entries.Any(x => x.SourceKind == "超框开关"), "Selected overframe registration included");
			Assert(!manifest.Entries.Any(x => x.SourceKind == "视觉资源"), "Visual ID collision excluded");
		}
		string multiPackage = Path.Combine(output, "selected-multiple.zip");
		service.ExportSelected(mirror, textures, new[] { cardA, cardB, textures[2], cardA }, multiPackage, true);
		CheckScope(multiPackage, aScope.Append(cardB.BundlePath));
		string builtinPackage = Path.Combine(output, "selected-builtin.zip");
		service.ExportSelected(mirror, textures, new[] { streaming }, builtinPackage, true);
		CheckScope(builtinPackage, new[] { streaming.BundlePath });
		string visualPackage = Path.Combine(output, "selected-visual.zip");
		service.ExportSelected(mirror, textures, new[] { visual }, visualPackage, true);
		CheckScope(visualPackage, new[] { visual.BundlePath });
		ExpectNoExport(Array.Empty<TexRef>(), "empty.zip");
		ExpectNoExport(new[] { restored }, "restored.zip");
		string allPackage = Path.Combine(output, "all.zip");
		Assert(service.Export(mirror, textures, allPackage, true).BundleCount == summary.BundleCount, "Export All unchanged");
		Write(cardA.BundlePath, original);
		service.RefreshState(mirror, textures);
		Assert(cardA.IsModded && cardA.ModModifiedAtUtc == recent, "Animation-only card is still listed");
		var recoveryImages = service.ChangedTextureBundlePaths(mirror, textures);
		Assert(!recoveryImages.Contains(Path.GetFullPath(cardA.BundlePath)) && !recoveryImages.Contains(Path.GetFullPath(gateLive))
			&& animationPaths.All(x => !recoveryImages.Contains(Path.GetFullPath(x))) && recoveryImages.Contains(Path.GetFullPath(cardB.BundlePath)),
			"Recovery snapshots only changed image Bundles, not animation/gate-only card flags");
		string animationOnlyPackage = Path.Combine(output, "selected-animation-only.zip");
		service.ExportSelected(mirror, textures, new[] { cardA }, animationOnlyPackage, true);
		CheckScope(animationOnlyPackage, animationPaths.Append(gateLive));
		Write(cardA.BundlePath, modified);
		File.SetLastWriteTimeUtc(cardA.BundlePath, old);
		service.RefreshState(mirror, textures);

		// Re-import selected packages into a different temporary account, without touching the real game.
		string target = Path.Combine(output, "import_" + Guid.NewGuid().ToString("N"));
		string targetLocal = Path.Combine(target, "LocalData", "target002", "0000");
		Directory.CreateDirectory(targetLocal);
		IndexService.SetPreferredLocalRoot(target, targetLocal);
		using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(output, "selected-direct.zip")))
		{
			foreach (ModPackageEntry entry in ReadManifest(zip).Entries)
				Write(Path.Combine(entry.TargetKind == "StreamingAssets" ? IndexService.StreamingRoot(target) : targetLocal, entry.RelativePath), original);
		}
		foreach (string name in new[] { "selected-direct.zip", "selected.mdmod.zip" })
		{
			Assert(service.Import(target, Path.Combine(output, name)).BundleCount == aScope.Length, "Selected import round trip");
			Assert(!Directory.Exists(Path.Combine(target, "LocalData", "test001")), "Import maps selected account");
		}

		RunUi(mirror, textures, output, selection =>
		{
			Assert(selection.ToHashSet().SetEquals(new[] { visual, cardA }), "Export snapshot must use the mouse-selected Mods");
			foreach (bool direct in new[] { true, false })
			{
				string package = Path.Combine(output, direct ? "ui-selected-direct.zip" : "ui-selected.mdmod.zip");
				service.ExportSelected(mirror, textures, selection, package, direct);
				CheckScope(package, aScope.Append(visual.BundlePath));
			}
			Console.WriteLine("mouseSelectedExport=True; exactScope=True; formats=2");
		});
		Assert(originals.All(x => Hash(x.Key) == x.Value), "Original game hashes unchanged");
		AppSettings settings = new() { ModSortOrder = ModSortOrder.OldestFirst };
		Assert(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))?.ModSortOrder == ModSortOrder.OldestFirst, "Sort preference serialization");
		Console.WriteLine($"selected=True; multiSelect=True; deduplicated=True; direct=True; legacy=True; animationBundles={animationPaths.Count}; gate=True; builtin=True; visual=True; restoredExcluded=True; accountMapping=True; gameWrites=False; ready=True");

		TexRef Add(string id, string kind, string relative, DateTime time, bool restored = false)
		{
			string root = kind == "游戏内图片" ? IndexService.StreamingRoot(mirror) : local;
			TexRef texture = new()
			{
				BundlePath = Path.Combine(root, relative), RelativeBundlePath = relative, CardKey = id,
				SourceKind = kind, Name = "fixture-" + id, Width = 512, Height = 512,
				Category = kind == "视觉资源" ? "卡套" : "卡图缩略图"
			};
			Write(texture.BundlePath, restored ? original : modified);
			Write(Path.Combine(mirror, "_MD卡图备份", kind, relative), original);
			File.SetLastWriteTimeUtc(texture.BundlePath, time);
			textures.Add(texture);
			return texture;
		}
		void ExpectNoExport(TexRef[] selection, string name)
		{
			string destination = Path.Combine(output, name);
			try { service.ExportSelected(mirror, textures, selection, destination); }
			catch (InvalidOperationException) { Assert(!File.Exists(destination), "Empty scope must not create a ZIP"); return; }
			throw new InvalidDataException("Empty or restored-only selection exported");
		}
		void CheckScope(string package, IEnumerable<string> scope)
		{
			using ZipArchive zip = ZipFile.OpenRead(package);
			ModPackageManifest manifest = ReadManifest(zip);
			HashSet<string> paths = manifest.Entries.Select(x => Path.GetFullPath(Path.Combine(
				x.TargetKind == "StreamingAssets" ? IndexService.StreamingRoot(mirror) : x.TargetKind == "GameRoot" ? mirror : local, x.RelativePath)))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			HashSet<string> expected = scope.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
			Assert(paths.Count == manifest.Entries.Count && paths.SetEquals(expected),
				$"Exact selected export scope ({Path.GetFileName(package)}): missing={string.Join(",", expected.Except(paths, StringComparer.OrdinalIgnoreCase))}; extra={string.Join(",", paths.Except(expected, StringComparer.OrdinalIgnoreCase))}");
		}
	}

	private static void RunUi(string mirror, List<TexRef> textures, string output, Action<TexRef[]> verifyExport)
	{
		using MainForm form = new(null, backgroundRefresh: false) { ShowInTaskbar = false, Opacity = 0 };
		SetField(form, "_assetRoot", null); // Avoid automatic real-resource scans.
		SetField(form, "_gameRoot", mirror);
		GetField<List<TexRef>>(form, "_textures").AddRange(textures);
		form.Show();
		Application.DoEvents();
		Invoke(form, "SetModsOnly", true);
		form.SetModSortOrder(ModSortOrder.NewestFirst, persist: false);
		BufferedListView list = GetField<BufferedListView>(form, "_list");
		List<TexRef> visible = GetField<List<TexRef>>(form, "_visibleTextures");
		Button export = GetField<Button>(form, "_exportSelectedModsButton");
		Button recovery = GetField<Button>(form, "_recoverModsButton");
		FlowLayoutPanel bar = GetField<FlowLayoutPanel>(form, "_modContextActions");
		Assert(list.MultiSelect && list.ToggleSelectionOnClick && list.Columns.Count == 5, "Mod-only checklist multiselect and time column");
		Assert(visible[0].SourceKind == "视觉资源", "Newest first includes all Mod categories");
		Assert(!export.Enabled, "Empty selection disabled");
		Assert(recovery.Visible && recovery.Enabled, "Recovery available without selecting a Mod");
		ModSelectionInputTests.RunClicks(list, export);
		Button selectAll = GetField<Button>(form, "_selectAllModsButton");
		Button clear = GetField<Button>(form, "_clearModSelectionButton");
		int previewGeneration = GetField<int>(form, "_previewGeneration");
		selectAll.PerformClick();
		Application.DoEvents();
		Assert(list.SelectedIndices.Count == visible.Count && !selectAll.Enabled && clear.Enabled, "Select Visible button and feedback");
		Assert(GetField<int>(form, "_previewGeneration") - previewGeneration == 1, "Bulk selection coalesces preview updates");
		clear.PerformClick();
		Application.DoEvents();
		Assert(list.SelectedIndices.Count == 0 && selectAll.Enabled && !clear.Enabled && !export.Enabled, "Clear button and feedback");
		ModSelectionInputTests.ClickRow(list, 0);
		ModSelectionInputTests.ClickRow(list, 1);
		Application.DoEvents();
		TexRef[] selected = list.SelectedIndices.Cast<int>().Select(i => visible[i]).ToArray();
		Assert(export.Enabled && export.Text.Contains("2"), "Selection counter");
		Assert(GetField<Label>(form, "_previewHint").Text == Localizer.F("mods.selection.preview", 2), "Multi-selection preview explains export instead of claiming nothing selected");
		export.Focus();
		Application.DoEvents();
		TexRef[] exportSelection = (TexRef[])typeof(MainForm).GetMethod("SelectedModTextures", BindingFlags.Instance | BindingFlags.NonPublic)!
			.Invoke(form, new object[] { false })!;
		verifyExport(exportSelection);
		form.SetModSortOrder(ModSortOrder.OldestFirst, persist: false);
		Assert(visible[0].CardKey == "22524", "Oldest first");
		Assert(list.SelectedIndices.Cast<int>().Select(i => visible[i]).ToHashSet().SetEquals(selected), "Sort preserves selection");
		form.SetModSortOrder(ModSortOrder.Name, persist: false);
		Assert(list.SelectedIndices.Count == 2, "Name sort preserves multi-selection");
		string[] names = visible.Select(x => (string)typeof(MainForm).GetMethod("DisplayResourceName", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { x })!).ToArray();
		Assert(names.SequenceEqual(names.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)), "Localized name order");

		foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
		{
			Localizer.SetLanguage(language);
			Application.DoEvents();
			Assert(list.Columns[3].Text.StartsWith(Localizer.T("mods.column.modified"), StringComparison.Ordinal), "Localized timestamp header");
			Assert(export.Text == Localizer.F("action.mods.export.selectedCount", 2), "Localized selection action");
			Assert(selectAll.Text == Localizer.T("mods.selection.all") && clear.Text == Localizer.T("mods.selection.clear"), "Localized selection buttons");
			Assert(list.AccessibleDescription == Localizer.T("mods.selection.hint"), "Localized multi-selection instructions");
			Assert(GetField<Label>(form, "_previewHint").Text == Localizer.F("mods.selection.preview", 2), "Localized batch-selection preview hint");
			Assert(recovery.Text == Localizer.T("mods.recovery.action"), "Localized recovery action");
			Assert(GetField<ComboBox>(form, "_modSortSelector").Items.Count == 3, "Localized sort choices");
			CheckBar();
		}
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		form.SetModSortOrder(ModSortOrder.NewestFirst, persist: false);
		Application.DoEvents();
		using (Bitmap snapshot = new(form.ClientSize.Width, form.ClientSize.Height))
		{
			form.DrawToBitmap(snapshot, form.ClientRectangle);
			snapshot.Save(Path.Combine(output, "mod-manager.png"));
		}
		// Exercise 150% scaling while keeping the native list/selection intact.
		float systemScale = form.DeviceDpi / 96f;
		form.AutoScaleDimensions = new SizeF(form.DeviceDpi / 1.5f, form.DeviceDpi / 1.5f);
		form.PerformAutoScale();
		form.PerformLayout();
		Application.DoEvents();
		form.AutoScaleMode = AutoScaleMode.None;
		form.MinimumSize = new Size(1000, 600);
		form.Width = 1600;
		CheckBar();
		foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
		{
			Localizer.SetLanguage(language);
			Application.DoEvents();
			CheckBar();
		}
		Localizer.SetLanguage(AppLanguage.English);
		using (Bitmap snapshot = new(bar.Width, bar.Height))
		{
			bar.DrawToBitmap(snapshot, bar.ClientRectangle);
			snapshot.Save(Path.Combine(output, "mod-actions-150.png"));
		}
		Button[] actions = bar.Controls.OfType<Button>().ToArray();
		int firstRowBottom = actions.Min(x => x.Top) + actions.Max(x => x.Height);
		Assert(bar.Controls.Cast<Control>().Any(x => x.Top >= firstRowBottom), "Narrow action bar wraps");
		Console.WriteLine($"narrowFormWidth={form.Width}; actionWidth={bar.Width}; actionHeight={bar.Height}; secondRow=True");
		Localizer.SetLanguage(AppLanguage.SimplifiedChinese);
		TextBox search = GetField<TextBox>(form, "_search");
		search.Text = "7687";
		GetField<System.Windows.Forms.Timer>(form, "_searchDebounce").Stop();
		Invoke(form, "RenderList");
		Assert(visible.Count == 0, "Search in Mod Manager excludes restored cards");
		search.Clear();
		GetField<System.Windows.Forms.Timer>(form, "_searchDebounce").Stop();
		Invoke(form, "SetModsOnly", false);
		Invoke(form, "RenderList");
		Assert(!list.MultiSelect && !list.ToggleSelectionOnClick && list.Columns.Count == 4,
			$"Ordinary resource view unchanged: multiSelect={list.MultiSelect}; toggle={list.ToggleSelectionOnClick}; columns={list.Columns.Count}; modsOnly={GetField<bool>(form, "_modsOnly")}");
		list.SelectedIndices.Clear();
		ModSelectionInputTests.ClickRow(list, 0);
		ModSelectionInputTests.ClickRow(list, 1);
		Assert(list.SelectedIndices.Count == 1 && list.SelectedIndices[0] == 1, "Ordinary resource clicks remain single-selection");
		foreach (TexRef texture in textures) texture.IsModded = false;
		Invoke(form, "RefreshCategories");
		Invoke(form, "SelectGroup", "__mods__");
		Application.DoEvents();
		Assert(recovery.Visible && recovery.Enabled && visible.Count == 0, "Recovery remains available when the current Mod list is empty");
		CheckBar();
		CheckRecoveryProgress();
		Console.WriteLine($"ui=True; languages=4; selectionPreserved=True; sortChoices=3; scaleTest=150%; systemScale={systemScale}; wrapping=True; searchScoped=True; ready=True");
		form.Close();

		void CheckBar()
		{
			form.PerformLayout();
			bar.PerformLayout();
			Application.DoEvents();
			Control[] clipped = bar.Controls.Cast<Control>().Where(x => x.Visible && !bar.ClientRectangle.Contains(x.Bounds)).ToArray();
			if (clipped.Length > 0 && bar.Parent?.Parent is TableLayoutPanel layout)
				Console.WriteLine($"contextStyle={GetField<RowStyle>(form, "_resourceContextRow").Height}; actualStyle={layout.RowStyles[layout.GetRow(bar.Parent)].Height}; rowHeights={string.Join(",", layout.GetRowHeights())}; table={layout.ClientSize}; parent={bar.Parent.ClientSize}; preferred={bar.GetPreferredSize(new Size(bar.Width, 0))}; language={Localizer.Language}; rowType={layout.RowStyles[layout.GetRow(bar.Parent)].SizeType}");
			Assert(clipped.Length == 0, "Mod actions clipped: " + string.Join(", ", clipped.Select(x => $"{x.Text}:{x.Bounds} in {bar.ClientRectangle}")));
			Assert(bar.Parent!.ClientRectangle.Contains(bar.Bounds), $"Mod toolbar extends beyond its allocated table row: {bar.Bounds} in {bar.Parent.ClientRectangle}");
			Assert(list.Columns.Cast<ColumnHeader>().Sum(x => x.Width) <= list.ClientSize.Width, "Mod column widths fit");
		}
		void CheckRecoveryProgress()
		{
			using var progress = new ModRecoveryProgressForm { ShowInTaskbar = false, Opacity = 0 };
			progress.Show(form);
			progress.Report(1, 3, "4007");
			Application.DoEvents();
			CheckBounds();
			progress.AutoScaleDimensions = new SizeF(progress.DeviceDpi / 1.5f, progress.DeviceDpi / 1.5f);
			progress.PerformAutoScale();
			Application.DoEvents();
			CheckBounds();
			progress.Report(1, 3, "commit");
			Application.DoEvents();
			Assert(!GetField<Button>(progress, "_cancel").Enabled, "Commit disables cancellation");
			progress.Close();
			void CheckBounds()
			{
				var message = GetField<Label>(progress, "_message");
				var indicator = GetField<ProgressBar>(progress, "_bar");
				var cancel = GetField<Button>(progress, "_cancel");
				Assert(progress.Controls.Cast<Control>().All(x => progress.ClientRectangle.Contains(x.Bounds))
					&& message.Bottom <= indicator.Top && indicator.Bottom <= cancel.Top,
					"Recovery progress controls fit at normal and 150% scaling");
			}
		}
	}

	private static ModPackageManifest ReadManifest(ZipArchive zip)
	{
		using Stream stream = zip.GetEntry("manifest.json")!.Open();
		return JsonSerializer.Deserialize<ModPackageManifest>(stream)!;
	}
	private static byte[] EncodeMappings(List<OverFrameMapping> mappings)
	{
		byte[] data = new byte[mappings.Count * 4];
		for (int i = 0; i < mappings.Count; i++)
		{
			BitConverter.TryWriteBytes(data.AsSpan(i * 4, 2), mappings[i].CardId);
			BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2, 2), mappings[i].ArtId);
		}
		return data;
	}
	private static void Write(string path, byte[] data)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllBytes(path, data);
	}
	private static string Hash(string path)
	{
		using Stream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
	private static T GetField<T>(Form form, string name) => (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
	private static void SetField(MainForm form, string name, object? value) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);
	private static void Invoke(MainForm form, string name, params object?[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
	private static void Assert(bool success, string message)
	{
		if (!success) throw new InvalidDataException(message);
	}
}
