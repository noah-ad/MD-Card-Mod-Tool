using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal sealed class AnimationDonorPicker : Form
{
	private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
	private readonly ImeAwareTextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = Localizer.T("animation.donor.search") };
	private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, IntegralHeight = false };
	private readonly AnimationPreviewCanvas _preview = new() { Dock = DockStyle.Fill, StatusText = Localizer.T("animation.donor.idle") };
	private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
	private readonly System.Diagnostics.Stopwatch _clock = new();
	private readonly CancellationTokenSource _closed = new();
	private CancellationTokenSource? _previewCancellation;
	private CurrentMonsterAnimationPreview? _frames;
	private int _generation;
	private readonly string _gameRoot;
	private readonly CardCatalogService _catalog = CardCatalogService.LoadBestAvailable();
	private readonly HashSet<string> _ids;
	public string? SelectedCardId => (_list.SelectedItem as Choice)?.Id;

	public AnimationDonorPicker(string gameRoot, IEnumerable<string> ids)
	{
		_gameRoot = gameRoot;
		_ids = ids.ToHashSet(StringComparer.Ordinal);
		Text = Localizer.T("animation.donor.title");
		Size = new Size(1000, 650);
		MinimumSize = new Size(800, 520);
		AutoScaleMode = AutoScaleMode.Dpi;
		StartPosition = FormStartPosition.CenterParent;
		BackColor = UiTheme.Window;
		Font = new Font("Microsoft YaHei UI", 9f);
		UiTheme.ApplyDarkTitleBar(this);
		UiTheme.StyleTextBox(_search);
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 3 };
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
		layout.Controls.Add(_search, 0, 0);
		layout.SetColumnSpan(_search, 2);
		layout.Controls.Add(_list, 0, 1);
		layout.Controls.Add(_preview, 1, 1);
		var preview = UiTheme.Button(Localizer.T("animation.donor.preview"), async (_, _) => await PreviewAsync());
		var use = UiTheme.Button(Localizer.T("animation.donor.use"), (_, _) => { if (SelectedCardId != null) { DialogResult = DialogResult.OK; Close(); } }, ButtonTone.Gold);
		preview.Dock = use.Dock = DockStyle.Fill;
		layout.Controls.Add(preview, 0, 2);
		layout.Controls.Add(use, 1, 2);
		Controls.Add(layout);
		_search.TextChanged += (_, _) => { if (!_search.IsImeComposing) Filter(); };
		_search.ImeCompositionEnded += (_, _) => Filter();
		_list.SelectedIndexChanged += (_, _) => { _generation++; _previewCancellation?.Cancel(); ClearPreview(); };
		_list.DoubleClick += async (_, _) => await PreviewAsync();
		_timer.Tick += (_, _) =>
		{
			if (_frames == null || _frames.Frames.Count == 0) return;
			int i = (int)(_clock.Elapsed.TotalSeconds * _frames.FramesPerSecond) % _frames.Frames.Count;
			if (!ReferenceEquals(_preview.Frame, _frames.Frames[i])) { _preview.Frame = _frames.Frames[i]; _preview.Invalidate(); }
		};
		Filter();
	}

	private void Filter()
	{
		string query = _search.Text.Trim();
		IEnumerable<string> ids = query.Length == 0 ? _ids.OrderBy(x => int.Parse(x)) :
			_catalog.Search(query, _catalog.Count).Select(x => x.AnimationId.ToString()).Where(_ids.Contains)
			.Concat(_ids.Where(x => x.Contains(query, StringComparison.Ordinal))).Distinct();
		_list.BeginUpdate();
		try
		{
			_list.Items.Clear();
			foreach (string id in ids.Take(600))
				_list.Items.Add(new Choice(id, id + " · " + (_catalog.FindCardOrMrk(int.Parse(id))?.Name(Localizer.Language) ?? "动画资源")));
			if (_list.Items.Count > 0) _list.SelectedIndex = 0;
		}
		finally { _list.EndUpdate(); }
	}

	private async Task PreviewAsync()
	{
		string? id = SelectedCardId;
		if (id == null) return;
		int version = ++_generation;
		_previewCancellation?.Cancel();
		_previewCancellation?.Dispose();
		_previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
		CancellationToken token = _previewCancellation.Token;
		ClearPreview();
		_preview.StatusText = Localizer.T("animation.donor.loading");
		_preview.Invalidate();
		try
		{
			var frames = await Task.Run(() => Spine42PreviewRenderer.TryLoad(MonsterAnimationIndexService.Find(_gameRoot, id), previewMaxEdge: 512, cancellationToken: token));
			if (IsDisposed || version != _generation) { frames?.Dispose(); return; }
			_frames = frames;
			if (frames?.Frames.Count > 0) _preview.Frame = frames.Frames[0];
			_preview.StatusText = frames == null ? Localizer.T("animation.donor.unsupported") : "";
			_clock.Restart();
			_timer.Start();
			_preview.Invalidate();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex) { if (!IsDisposed) { _preview.StatusText = ex.Message; _preview.Invalidate(); } }
	}
	private void ClearPreview() { _timer.Stop(); _preview.Frame = null; _frames?.Dispose(); _frames = null; _preview.Invalidate(); }
	protected override void Dispose(bool disposing)
	{
		if (disposing) { _generation++; _closed.Cancel(); _previewCancellation?.Cancel(); _timer.Dispose(); _preview.Frame = null; _frames?.Dispose(); _frames = null; }
		base.Dispose(disposing);
	}
}
