using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class SpellAlternateAnimationForm : Form
{
	private readonly string _gameRoot;
	private readonly SpellAlternateAnimationService _service = new();
	private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
	private readonly Button _inspect = new() { AutoSize = true };
	private readonly Button _apply = new() { AutoSize = true, Enabled = false };
	private readonly Button _restore = new() { AutoSize = true, Enabled = false };
	private readonly CheckBox _sound = new() { AutoSize = true };
	private readonly Label _description = new() { AutoSize = true, MaximumSize = new Size(760, 0) };
	private readonly ComboBox _candidates = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 740 };
	private bool _busy;

	public SpellAlternateAnimationForm(string gameRoot)
	{
		_gameRoot = gameRoot;
		StartPosition = FormStartPosition.CenterParent;
		Size = new Size(850, 560);
		MinimumSize = new Size(650, 420);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 10);
		UiTheme.ApplyDarkTitleBar(this);
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 12, 0, 12) };
		actions.Controls.AddRange(new Control[] { _inspect, _apply, _restore, _sound });
		_details.BackColor = UiTheme.Surface;
		_details.ForeColor = UiTheme.Text;
		layout.Controls.Add(_description, 0, 0);
		layout.Controls.Add(_candidates, 0, 1);
		layout.Controls.Add(actions, 0, 2);
		layout.Controls.Add(_details, 0, 3);
		Controls.Add(layout);
		_inspect.Click += async (_, _) => await Run(false, false);
		_apply.Click += async (_, _) => await Run(true, false);
		_restore.Click += async (_, _) => await Run(true, true);
		_candidates.SelectionChangeCommitted += async (_, _) => await Run(false, false);
		Shown += async (_, _) => await Run(false, false);
		FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
		Localizer.LanguageChanged += LanguageChanged;
		ApplyLanguage();
	}
	private void LanguageChanged(object? sender, EventArgs e) => ApplyLanguage();
	private void ApplyLanguage()
	{
		Text = Localizer.T("spell.title");
		_description.Text = Localizer.T("spell.description");
		_inspect.Text = Localizer.T("spell.inspect");
		_apply.Text = Localizer.T("spell.apply");
		_restore.Text = Localizer.T("spell.restore");
		_sound.Text = Localizer.T("spell.sound");
	}
	private async Task Run(bool write, bool restore)
	{
		if (_busy) return;
		if (write && MessageBox.Show(this, Localizer.T(restore ? "spell.confirmRestore" : "spell.confirmApply"), Text,
			MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
		_busy = true;
		_inspect.Enabled = _apply.Enabled = _restore.Enabled = _sound.Enabled = _candidates.Enabled = false;
		_details.Text = Localizer.T("spell.working");
		bool sound = _sound.Checked;
		try
		{
			if (_candidates.Items.Count == 0)
			{
				var candidates = await Task.Run(() => _service.Discover(_gameRoot));
				_candidates.Items.AddRange(candidates.Cast<object>().ToArray());
				_candidates.SelectedItem = candidates.FirstOrDefault(x => x.TargetId == 5328 && x.SourceId == 20040) ?? candidates.FirstOrDefault();
			}
			if (_candidates.SelectedItem is not SpellAlternateAnimationService.Candidate candidate)
				throw new InvalidOperationException(Localizer.T("spell.empty"));
			var result = await Task.Run(() =>
			{
				if (write) { if (restore) _service.Restore(_gameRoot, candidate.TargetId, candidate.SourceId); else _service.Apply(_gameRoot, candidate.TargetId, candidate.SourceId, sound); }
				return _service.Inspect(_gameRoot, candidate.TargetId, candidate.SourceId);
			});
			_details.Text = (write ? Localizer.T("spell.done") + Environment.NewLine : "")
				+ Localizer.T("spell.state." + result.State) + Environment.NewLine
				+ Localizer.F("spell.paths", result.LocalRoot, result.BundlePath, result.BackupPath)
				+ Environment.NewLine + result.Diagnostic + Environment.NewLine + string.Join(Environment.NewLine, result.Dependencies);
			_apply.Enabled = result.CanApply;
			_restore.Enabled = result.CanRestore;
		}
		catch (Exception ex) { _details.Text = Localizer.T("spell.failed") + Environment.NewLine + ex.Message; }
		finally { _busy = false; _inspect.Enabled = _sound.Enabled = _candidates.Enabled = true; }
	}
	protected override void Dispose(bool disposing)
	{
		if (disposing) Localizer.LanguageChanged -= LanguageChanged;
		base.Dispose(disposing);
	}
}
