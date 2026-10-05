using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed partial class MainForm
{
	private sealed record ModSortChoice(ModSortOrder Order, string Label)
	{
		public override string ToString() => Label;
	}

	private readonly ModernComboBox _modSortSelector = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};
	private Control? _modSortField;
	private Button? _exportSelectedModsButton;
	private bool _updatingModSort;
	private bool _updatingModContextHeight;
	private bool _exportingMods;

	private void ConfigureModManagementControls()
	{
		_exportSelectedModsButton = Button("", async delegate { await ExportManagedModsAsync(selectedOnly: true); }, ButtonTone.Primary);
		UiTheme.StyleComboBox(_modSortSelector);
		_modSortField = UiTheme.Field(_modSortSelector);
		_modSortField.Dock = DockStyle.None;
		_modSortField.Size = new Size(250, 38);
		_modSortField.Margin = new Padding(10, 4, 0, 4);
		UpdateModSortLabels();
		UpdateSelectedModAction();
		_modSortSelector.SelectedIndexChanged += delegate
		{
			if (!_updatingModSort && _modSortSelector.SelectedItem is ModSortChoice choice)
				SetModSortOrder(choice.Order, persist: true);
		};
		_list.ShowItemToolTips = true;
		_list.ColumnClick += delegate(object? _, ColumnClickEventArgs e)
		{
			if (_modsOnly && e.Column == 3)
				SetModSortOrder(_settings.ModSortOrder == ModSortOrder.NewestFirst ? ModSortOrder.OldestFirst : ModSortOrder.NewestFirst, persist: true);
		};
	}

	private void UpdateModSortLabels()
	{
		_updatingModSort = true;
		try
		{
			if (!Enum.IsDefined(_settings.ModSortOrder)) _settings.ModSortOrder = ModSortOrder.NewestFirst;
			_modSortSelector.Items.Clear();
			_modSortSelector.Items.AddRange(new object[]
			{
				new ModSortChoice(ModSortOrder.NewestFirst, Localizer.T("mods.sort.newest")),
				new ModSortChoice(ModSortOrder.OldestFirst, Localizer.T("mods.sort.oldest")),
				new ModSortChoice(ModSortOrder.Name, Localizer.T("mods.sort.name"))
			});
			_modSortSelector.SelectedIndex = (int)_settings.ModSortOrder;
			_modSortSelector.AccessibleName = Localizer.T("mods.sort.label");
			if (_list.Columns.Count == 5) UpdateModTimeHeading();
		}
		finally { _updatingModSort = false; }
		UpdateModContextHeight();
	}

	internal void SetModSortOrder(ModSortOrder order, bool persist)
	{
		_settings.ModSortOrder = Enum.IsDefined(order) ? order : ModSortOrder.NewestFirst;
		if (persist) AppSettingsStore.Save(_settings);
		UpdateModSortLabels();
		RenderList();
	}

	private IEnumerable<TexRef> OrderMods(IEnumerable<TexRef> textures)
	{
		IOrderedEnumerable<TexRef> sorted = _settings.ModSortOrder switch
		{
			ModSortOrder.OldestFirst => textures.OrderBy(x => x.ModModifiedAtUtc ?? DateTime.MaxValue),
			ModSortOrder.Name => textures.OrderBy(DisplayResourceName, StringComparer.CurrentCultureIgnoreCase),
			_ => textures.OrderByDescending(x => x.ModModifiedAtUtc ?? DateTime.MinValue)
		};
		return sorted.ThenBy(DisplayResourceName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(x => x.BundlePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.PathId);
	}

	private TexRef[] SelectedModTextures(bool includeUnmodified = false)
	{
		return _list.SelectedIndices.Cast<int>().Where(i => i >= 0 && i < _visibleTextures.Count)
			.Select(i => _visibleTextures[i]).Where(x => includeUnmodified || x.IsModded).ToArray();
	}

	private void UpdateSelectedModAction()
	{
		if (_exportSelectedModsButton == null) return;
		int count = SelectedModTextures().Length;
		_exportSelectedModsButton.Text = count == 0 ? Localizer.T("action.mods.export.selected") : Localizer.F("action.mods.export.selectedCount", count);
		_exportSelectedModsButton.AccessibleName = _exportSelectedModsButton.Text;
		_exportSelectedModsButton.Enabled = _gameRoot != null && count > 0 && !_exportingMods;
		UpdateModContextHeight();
	}

	private void UpdateModColumns()
	{
		_list.MultiSelect = _modsOnly;
		if (_modsOnly && _list.Columns.Count == 4)
			_list.Columns.Insert(3, Localizer.T("mods.column.modified"), 140);
		else if (!_modsOnly && _list.Columns.Count == 5)
			_list.Columns.RemoveAt(3);
		if (_modsOnly) UpdateModTimeHeading();
		ResizeResourceColumns();
		UpdateSelectedModAction();
	}

	private void UpdateModTimeHeading()
	{
		_list.Columns[3].Text = Localizer.T("mods.column.modified") + (_settings.ModSortOrder switch
		{
			ModSortOrder.NewestFirst => " ↓",
			ModSortOrder.OldestFirst => " ↑",
			_ => ""
		});
	}

	private void ResizeModColumns()
	{
		if (_list.ClientSize.Width <= 0) return;
		int width = Math.Max(5, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - UiTheme.Scale(_list, 3));
		double[] weights = { 0.30, 0.19, 0.10, 0.28, 0.13 };
		int remaining = width;
		for (int i = 0; i < weights.Length; i++)
		{
			int columnWidth = i == weights.Length - 1 ? remaining : Math.Max(1, (int)(width * weights[i]));
			_list.Columns[i].Width = columnWidth;
			remaining -= columnWidth;
		}
	}

	private void UpdateModContextHeight()
	{
		if (_updatingModContextHeight || !_modsOnly || _resourceContextRow == null ||
			!_resourceContextBar.Visible || _modContextActions.ClientSize.Width <= 0) return;
		_updatingModContextHeight = true;
		try
		{
			_modContextActions.PerformLayout();
			int bottom = _modContextActions.Controls.Cast<Control>().Where(x => x.Visible)
				.Select(x => x.Bottom + x.Margin.Bottom).DefaultIfEmpty(_modContextActions.Padding.Top).Max();
			int height = Math.Max(UiTheme.Scale(this, 52), bottom + _modContextActions.Padding.Bottom);
			if (Math.Abs(_resourceContextRow.Height - height) > 1)
				_resourceContextRow.Height = height;
		}
		finally { _updatingModContextHeight = false; }
	}

	private async Task ExportManagedModsAsync(bool selectedOnly)
	{
		if (_exportingMods) return;
		string? root = _gameRoot;
		if (root == null)
		{
			MessageBox.Show(this, Localizer.T("mods.export.chooseGame"), Text);
			return;
		}
		TexRef[] textures = _textures.ToArray();
		TexRef[] selected = selectedOnly ? SelectedModTextures() : Array.Empty<TexRef>();
		if (selectedOnly && selected.Length == 0)
		{
			MessageBox.Show(this, Localizer.T("mods.export.noSelection"), Text);
			return;
		}
		string title = Localizer.T(selectedOnly ? "mods.export.selectedTitle" : "mods.export.allTitle");
		if (selectedOnly && MessageBox.Show(this, Localizer.F("mods.export.selectedHint", selected.Length),
			title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
		using SaveFileDialog dialog = new()
		{
			Filter = Localizer.T("mods.export.filter"),
			FileName = $"MD_{(selectedOnly ? "Selected_Mods" : "Mods")}_{DateTime.Now:yyyyMMdd_HHmm}.zip",
			DefaultExt = "zip",
			Title = title
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		bool directReplacement = dialog.FilterIndex == 1;
		try
		{
			_exportingMods = true;
			Enabled = false; // Freeze profile and resource selection for the background snapshot.
			UseWaitCursor = true;
			UpdateSelectedModAction();
			_status.Text = Localizer.T(selectedOnly ? "mods.export.workingSelected" : "mods.export.workingAll");
			ModPackageInfo result = await Task.Run(() => selectedOnly
				? _mods.ExportSelected(root, textures, selected, dialog.FileName, directReplacement)
				: _mods.Export(root, textures, dialog.FileName, directReplacement));
			if (IsDisposed) return;
			_status.Text = Localizer.F("mods.export.status", result.BundleCount, dialog.FileName);
			MessageBox.Show(this, Localizer.F("mods.export.complete", result.BundleCount, FormatSize(result.TotalSize), dialog.FileName)
				+ (directReplacement ? "\n\n" + Localizer.T("mods.export.directHelp") : ""),
				title, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			if (!IsDisposed) MessageBox.Show(this, ex.Message, Localizer.T("mods.export.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			_exportingMods = false;
			if (!IsDisposed)
			{
				Enabled = true;
				UseWaitCursor = false;
				UpdateSelectedModAction();
			}
		}
	}
}
