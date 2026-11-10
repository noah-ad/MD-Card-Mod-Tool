using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MdCardModTool;

internal static class WorkspaceRegressionTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	internal static void Run(string output)
	{
		Directory.CreateDirectory(output);
		var settings = AppSettingsStore.Load();
		try
		{
			foreach (int dpi in new[] { 96, 144, 192 })
			{
				using var form = new MainForm(f =>
				{
					FieldInfo field = typeof(Control).GetField("_deviceDpi", Private)!;
					foreach (Control child in Walk(f).Prepend(f)) field.SetValue(child, dpi);
				}, backgroundRefresh: false) { ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
				Set(form, "_assetRoot", null); // No real installation scans or writes.
				Set(form, "_gameRoot", null);
				form.Show();
				Get<List<TexRef>>(form, "_textures").AddRange(BuiltInCardFrameCatalog.Load());
				Invoke(form, "RenderList");
				float scale = dpi / 96f;
				foreach (int width in new[] { 1100, 1360, 1680, 1180 })
				{
					form.ClientSize = new Size((int)(width * scale), (int)(760 * scale));
					foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
					{
						Localizer.SetLanguage(language);
						Get<ModernComboBox>(form, "_languageSelector").SelectedIndex = (int)language;
						Invoke(form, "ShowPage", WorkspacePage.Cards);
						Pump(form);
						AssertFields(form);
						foreach (WorkspacePage page in new[] { WorkspacePage.Settings, WorkspacePage.Visuals, WorkspacePage.Animation, WorkspacePage.Cards })
						{
							Invoke(form, "ShowPage", page);
							Pump(form);
							AssertFields(form);
							Check(Get<Control>(form, "_cardSearchField").Visible == (page == WorkspacePage.Cards), "card search scope");
						}
						Invoke(form, "SetModsOnly", true);
						Pump(form);
						var actions = Get<FlowLayoutPanel>(form, "_modContextActions");
						Check(actions.Controls.Cast<Control>().Where(c => c.Visible).All(c => actions.ClientRectangle.Contains(c.Bounds)), "wrapped Mod actions fit");
						Invoke(form, "SetModsOnly", false);
					}
					Console.WriteLine($"PASS injectedDpi={dpi}; logicalWidth={width}; fourLanguages; repeatedPages; toolbarAncestors; fieldText; modActions");
				}
				using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
				form.DrawToBitmap(bitmap, form.ClientRectangle);
				bitmap.Save(Path.Combine(output, $"workspace-layout-{dpi}.png"));
				form.Close();
			}
		}
		finally { Localizer.SetLanguage(settings.Language); AppSettingsStore.Save(settings); }
		Console.WriteLine("PASS workspace geometry regression; DPI injection is a simulation, not an OS monitor switch; gameWrites=False");
	}

	internal static void Review(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		string mirror = Path.Combine(output, "review-game");
		string local = Path.Combine(mirror, "LocalData", "review", "0000");
		Directory.CreateDirectory(local);
		IndexService.SetPreferredLocalRoot(mirror, local);
		PortableIndexService.TryLoadBundled(gameRoot, out var seed, out _);
		var copied = new List<TexRef>();
		var engine = new ModEngine();
		foreach (var source in seed.Textures.Where(CardTextureRules.IsCardArt).Where(t => File.Exists(t.BundlePath)).Take(45))
		{
			string destination = Path.Combine(local, source.RelativeBundlePath);
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			File.Copy(source.BundlePath, destination, true);
			var items = engine.ScanBundle(destination, local, "本地卡图", false).Textures.Where(CardTextureRules.IsCardArt);
			copied.AddRange(items);
		}
		using var form = new MainForm(null, backgroundRefresh: false);
		Set(form, "_assetRoot", null);
		form.Shown += (_, _) =>
		{
			Set(form, "_gameRoot", mirror); Set(form, "_assetRoot", local); Set(form, "_streamingRoot", IndexService.StreamingRoot(mirror));
			Get<TextBox>(form, "_gameFolder").Text = mirror;
			Set(form, "_index", new GameIndex { Textures = copied });
			Get<List<TexRef>>(form, "_textures").AddRange(copied.Concat(BuiltInCardFrameCatalog.Load()));
			Invoke(form, "RefreshCategories"); Invoke(form, "RenderList");
		};
		Application.Run(form);
	}

	private static void AssertFields(MainForm form)
	{
		Control toolbar = Walk(form).Single(c => c.Name == "WorkspaceToolbar");
		Check(VisibleThroughAncestors(toolbar), "toolbar fits every ancestor");
		var workspace = Get<SplitContainer>(form, "_workspaceSplit");
		Check(workspace.Panel2.Width >= UiTheme.Scale(form, 270), "inspector must not collapse while parent is being constructed");
		Check(Get<Control>(form, "_resourceSearchBar").Height <= UiTheme.Scale(form, 72), "search row must measure its fields, not default Panel height");
		var list = Get<ListView>(form, "_list");
		if (list.Visible && list.Columns.Count == 4)
			Check(list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) <= list.ClientSize.Width, "virtual row changes must not leave a horizontal scrollbar");
		foreach (string name in new[] { "_gameFolder", "_profileSelector", "_languageSelector", "_search", "_category" })
		{
			Control field = Get<Control>(form, name);
			if (!field.Visible) continue;
			Check(VisibleThroughAncestors(field), name + " must not be clipped by wrappers/rows");
			int preferred = field is TextBox text ? text.PreferredHeight : field.GetPreferredSize(new Size(field.Width, 0)).Height;
			Check(field.Height >= preferred, $"{name} native text height: actual={field.Height}, preferred={preferred}, font={field.Font.Height}, parent={field.Parent?.Bounds}");
		}
		foreach (Button button in toolbar.Controls.OfType<Button>())
		{
			Check(VisibleThroughAncestors(button), "toolbar button bounds");
			Check(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width + 8, $"toolbar button full label '{button.Text}': width={button.Width}, preferred={TextRenderer.MeasureText(button.Text, button.Font).Width + 8}, dpi={form.DeviceDpi}, toolbar={toolbar.Size}");
		}
	}

	private static bool VisibleThroughAncestors(Control control)
	{
		Rectangle visible = control.ClientRectangle;
		for (Control? current = control; current?.Parent != null; current = current.Parent)
		{
			visible.Offset(current.Left, current.Top);
			if (!current.Parent.ClientRectangle.Contains(visible)) return false;
		}
		return true;
	}
	private static void Pump(Form form) { form.PerformLayout(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents(); }
	private static IEnumerable<Control> Walk(Control root) => root.Controls.Cast<Control>().SelectMany(c => Walk(c).Prepend(c));
	private static T Get<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, Private)!.GetValue(form)!;
	private static void Set(MainForm form, string name, object? value) => typeof(MainForm).GetField(name, Private)!.SetValue(form, value);
	private static object? Invoke(MainForm form, string name, params object[] values) => typeof(MainForm).GetMethod(name, Private)!.Invoke(form, values);
	private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
