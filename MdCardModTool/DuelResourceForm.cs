using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal sealed class DuelResourceForm : Form
{
	private readonly string _root;
	private readonly DuelResourceService _service = new();
	private readonly ComboBox _mode = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
	private readonly ImeAwareTextBox _targetSearch = new() { Dock = DockStyle.Fill };
	private readonly ImeAwareTextBox _sourceSearch = new() { Dock = DockStyle.Fill };
	private readonly ListBox _targets = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
	private readonly ListBox _sources = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
	private readonly PictureBox _targetPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
	private readonly PictureBox _sourcePreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
	private readonly Label _targetLabel = new() { AutoSize = true };
	private readonly Label _sourceLabel = new() { AutoSize = true };
	private readonly TextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
	private readonly Button _replace = UiTheme.Button("", (_, _) => { }, ButtonTone.Gold);
	private readonly Button _replaceGroup = UiTheme.Button("", (_, _) => { }, ButtonTone.Primary);
	private readonly Button _import = UiTheme.Button("", (_, _) => { }, ButtonTone.Primary);
	private readonly Button _restore = UiTheme.Button("", (_, _) => { });
	private readonly Button _playTarget = UiTheme.Button("", (_, _) => { });
	private readonly Button _playSource = UiTheme.Button("", (_, _) => { });
	private readonly Button _stop = UiTheme.Button("", (_, _) => { });
	private readonly Button _refresh = UiTheme.Button("", (_, _) => { });
	private readonly SoundPlayer _player = new();
	private readonly string _temporary = Path.Combine(Path.GetTempPath(), "MDCardModTool", "listen-" + Guid.NewGuid().ToString("N"));
	private List<DuelField> _fields = new();
	private List<DuelMusic> _music = new();
	private bool _busy;
	private int _targetGeneration, _sourceGeneration;
	internal DuelResourceForm(string gameRoot)
	{
		_root = gameRoot;
		Size = new Size(1100, 780); MinimumSize = new Size(850, 640);
		StartPosition = FormStartPosition.CenterParent;
		BackColor = UiTheme.Window; ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9.5f);
		UiTheme.ApplyDarkTitleBar(this);
		UiTheme.StyleComboBox(_mode);
		UiTheme.StyleTextBox(_targetSearch); UiTheme.StyleTextBox(_sourceSearch);
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 4 };
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
		var header = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
		header.Controls.AddRange(new Control[] { _mode, _refresh });
		var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
		columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		columns.Controls.Add(Column(_targetLabel, _targetSearch, _targets, _targetPreview), 0, 0);
		columns.Controls.Add(Column(_sourceLabel, _sourceSearch, _sources, _sourcePreview), 1, 0);
		var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
		actions.Controls.AddRange(new Control[] { _replace, _replaceGroup, _import, _restore, _playTarget, _playSource, _stop });
		layout.Controls.Add(header, 0, 0); layout.Controls.Add(columns, 0, 1); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(_details, 0, 3);
		foreach (Control c in new Control[] { _targets, _sources, _targetSearch, _sourceSearch, _details, _mode }) { c.BackColor = UiTheme.Surface; c.ForeColor = UiTheme.Text; }
		Controls.Add(layout);
		_targetSearch.TextChanged += (_, _) => Filter(true);
		_sourceSearch.TextChanged += (_, _) => Filter(false);
		_targets.SelectedIndexChanged += async (_, _) => await Selected(true);
		_sources.SelectedIndexChanged += async (_, _) => await Selected(false);
		_mode.SelectedIndexChanged += (_, _) => { Stop(); Filter(true); Filter(false); UpdateActions(); };
		_refresh.Click += async (_, _) => await LoadCatalog();
		_replace.Click += async (_, _) => await Write(false, null);
		_replaceGroup.Click += async (_, _) => await ReplaceGroup();
		_restore.Click += async (_, _) => await Write(true, null);
		_import.Click += async (_, _) =>
		{
			using var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aac;*.wma|All files|*.*" };
			if (dialog.ShowDialog(this) == DialogResult.OK) await Write(false, dialog.FileName);
		};
		_playTarget.Click += async (_, _) => await Listen(true);
		_playSource.Click += async (_, _) => await Listen(false);
		_stop.Click += (_, _) => Stop();
		Shown += async (_, _) => await LoadCatalog();
		FormClosing += (_, e) => { if (_busy) e.Cancel = true; else Stop(); };
		Localizer.LanguageChanged += LanguageChanged;
		ApplyLanguage();
		_mode.AccessibleName = Localizer.T("duel.fields") + " / " + Localizer.T("duel.music");
	}
	private static Control Column(Label label, TextBox search, ListBox list, PictureBox preview)
	{
		var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
		panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
		panel.Controls.Add(label, 0, 0); panel.Controls.Add(UiTheme.Field(search), 0, 1); panel.Controls.Add(list, 0, 2); panel.Controls.Add(preview, 0, 3);
		return panel;
	}
	private void LanguageChanged(object? sender, EventArgs e) => ApplyLanguage();
	private void ApplyLanguage()
	{
		Text = Localizer.T("duel.title");
		_targetLabel.Text = Localizer.T("duel.target"); _sourceLabel.Text = Localizer.T("duel.source");
		_targetSearch.PlaceholderText = _sourceSearch.PlaceholderText = Localizer.T("duel.search");
		_replace.Text = Localizer.T("duel.replace"); _import.Text = Localizer.T("duel.import"); _restore.Text = Localizer.T("duel.restore");
		_replaceGroup.Text = Localizer.T("duel.replaceGroup");
		_playTarget.Text = Localizer.T("duel.listenTarget"); _playSource.Text = Localizer.T("duel.listenSource"); _stop.Text = Localizer.T("duel.stop"); _refresh.Text = Localizer.T("duel.refresh");
		int selected = Math.Max(0, _mode.SelectedIndex);
		_mode.Items.Clear(); _mode.Items.AddRange(new object[] { Localizer.T("duel.fields"), Localizer.T("duel.music") }); _mode.SelectedIndex = selected;
	}
	private async Task LoadCatalog()
	{
		if (_busy) return;
		SetBusy(true); _details.Text = Localizer.T("duel.loading");
		try
		{
			var data = await Task.Run(() => (Fields: DuelResourceCatalog.Fields(_root), Music: DuelResourceCatalog.Music(_root)));
			_fields = data.Fields; _music = data.Music;
			Filter(true); Filter(false);
			_details.Text = Localizer.F("duel.loaded", _fields.Count, _music.Count) + Environment.NewLine + Localizer.T("duel.boundary");
		}
		catch (Exception ex) { _details.Text = ex.Message; }
		finally { SetBusy(false); }
	}
	private void Filter(bool target)
	{
		var list = target ? _targets : _sources;
		string query = (target ? _targetSearch : _sourceSearch).Text.Trim();
		object? selected = list.SelectedItem;
		list.BeginUpdate(); list.Items.Clear();
		list.Items.AddRange(_mode.SelectedIndex == 0 ? _fields.Where(x => x.Matches(query)).Cast<object>().ToArray()
			: _music.Where(x => x.Matches(query)).Cast<object>().ToArray());
		if (selected != null && list.Items.Contains(selected)) list.SelectedItem = selected;
		else if (list.Items.Count > 0) list.SelectedIndex = 0;
		list.EndUpdate();
	}
	private async Task Selected(bool target)
	{
		int generation = target ? ++_targetGeneration : ++_sourceGeneration;
		var picture = target ? _targetPreview : _sourcePreview;
		picture.Image?.Dispose(); picture.Image = null;
		object? selected = (target ? _targets : _sources).SelectedItem;
		UpdateActions();
		if (!_busy && selected != null)
		{
			IEnumerable<DuelBundleSlot> slots = selected is DuelField f ? f.Models : new[] { ((DuelMusic)selected).Slot };
			_details.Text = (target ? _targetLabel.Text : _sourceLabel.Text) + " · " + selected + Environment.NewLine
				+ string.Join(Environment.NewLine, slots.Select(x => x.LogicalPath + " · " + Localizer.T("duel.state." + DuelResourceService.State(_root, x))))
				+ (selected is DuelMusic ? Environment.NewLine + Localizer.T("duel.musicNotes") : "")
				+ Environment.NewLine + Localizer.T("duel.boundary");
		}
		if (selected is not DuelField field || field.Preview == null) return;
		try
		{
			byte[] bytes = await Task.Run(() => new ModEngine().DecodePng(field.Preview, 1024));
			if (IsDisposed || generation != (target ? _targetGeneration : _sourceGeneration)) return;
			picture.Image = FrameComposer.BitmapFrom(bytes);
		}
		catch (Exception ex) { if (!IsDisposed) _details.Text = Localizer.T("duel.previewFailed") + ex.Message; }
	}
	private void SetBusy(bool busy)
	{
		_busy = busy; _mode.Enabled = _refresh.Enabled = _targets.Enabled = _sources.Enabled = _targetSearch.Enabled = _sourceSearch.Enabled = !busy;
		UpdateActions();
	}
	private void UpdateActions()
	{
		bool music = _mode.SelectedIndex == 1;
		_import.Visible = _playTarget.Visible = _playSource.Visible = _stop.Visible = music;
		_replaceGroup.Visible = music;
		_replaceGroup.Enabled = !_busy && music && _targets.SelectedItem is DuelMusic && _sources.SelectedItem is DuelMusic;
		_replace.Enabled = !_busy && (_targets.SelectedItem is DuelField t && _sources.SelectedItem is DuelField s && t.Id != s.Id && t.Models.Count > 0 && t.Models.All(x => s.Models.Any(y => x.Key == y.Key))
			|| _targets.SelectedItem is DuelMusic tm && _sources.SelectedItem is DuelMusic sm && tm != sm);
		_import.Enabled = !_busy && _targets.SelectedItem is DuelMusic;
		_restore.Enabled = !_busy && (_targets.SelectedItem is DuelField f && f.Models.Count > 0 || _targets.SelectedItem is DuelMusic);
		_playTarget.Enabled = !_busy && _targets.SelectedItem is DuelMusic;
		_playSource.Enabled = !_busy && _sources.SelectedItem is DuelMusic;
		_stop.Enabled = !_busy;
	}
	private async Task ReplaceGroup()
	{
		if (_busy || _targets.SelectedItem is not DuelMusic target || _sources.SelectedItem is not DuelMusic source) return;
		try
		{
			var pairs = DuelMusicMetadata.PlanGroup(_music, target, source);
			string selection = string.Join(Environment.NewLine, pairs.Select(x => x.Target + Environment.NewLine + "← " + x.Source));
			if (MessageBox.Show(this, selection + Environment.NewLine + Localizer.T("duel.groupConfirm"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
			Stop(); SetBusy(true); _details.Text = Localizer.T("duel.working");
			await Task.Run(() => _service.ReplaceMusicGroup(_root, _music, target, source));
			_details.Text = Localizer.T("duel.done") + Environment.NewLine + selection;
		}
		catch (Exception ex) { _details.Text = ex.Message; }
		finally { SetBusy(false); }
	}
	private async Task Write(bool restore, string? input)
	{
		if (_busy || _targets.SelectedItem == null) return;
		object target = _targets.SelectedItem; object? source = _sources.SelectedItem;
		string selection = target + (restore ? "" : Environment.NewLine + "← " + (input ?? source?.ToString()));
		if (MessageBox.Show(this, selection + Environment.NewLine + Localizer.T("duel.confirm"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
		Stop(); SetBusy(true); _details.Text = Localizer.T("duel.working");
		try
		{
			await Task.Run(() =>
			{
				if (restore) _service.Restore(_root, target is DuelField f ? f.Models : new[] { ((DuelMusic)target).Slot });
				else if (target is DuelField t && source is DuelField s) _service.ReplaceField(_root, t, s);
				else if (target is DuelMusic tm && input != null) _service.ImportMusic(_root, tm, input);
				else if (target is DuelMusic mt && source is DuelMusic ms) _service.ReplaceMusic(_root, mt, ms);
				else throw new InvalidOperationException(Localizer.T("duel.selection"));
			});
			_details.Text = Localizer.T("duel.done") + Environment.NewLine + selection + Environment.NewLine + Localizer.T("duel.boundary");
		}
		catch (Exception ex) { _details.Text = ex.ToString(); }
		finally { SetBusy(false); }
	}
	private async Task Listen(bool target)
	{
		if (_busy || (target ? _targets : _sources).SelectedItem is not DuelMusic music) return;
		Stop(); SetBusy(true); _details.Text = Localizer.T("duel.working");
		try
		{
			Directory.CreateDirectory(_temporary);
			string wave = Path.Combine(_temporary, "preview.wav");
			await Task.Run(() =>
			{
				using var document = new DuelBundleDocument(music.Slot.BundlePath, music.Slot.Container);
				string bank = Path.Combine(_temporary, "preview.fsb");
				File.WriteAllBytes(bank, document.AudioBytes());
				DuelMusicAudio.Convert(bank, wave, false);
			});
			_player.SoundLocation = wave; _player.Load(); _player.Play();
			_details.Text = music.ToString();
		}
		catch (Exception ex) { _details.Text = Localizer.T("duel.listenFailed") + ex.Message; }
		finally { SetBusy(false); }
	}
	private void Stop() { _player.Stop(); }
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Localizer.LanguageChanged -= LanguageChanged;
			_targetGeneration++; _sourceGeneration++;
			_player.Stop(); _player.Dispose();
			_targetPreview.Image?.Dispose(); _sourcePreview.Image?.Dispose();
			try { if (Directory.Exists(_temporary)) Directory.Delete(_temporary, true); } catch (IOException) { }
		}
		base.Dispose(disposing);
	}
}
