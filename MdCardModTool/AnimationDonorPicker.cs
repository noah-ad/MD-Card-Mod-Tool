using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal sealed class AnimationDonorPicker : Form
{
	private sealed record Choice(string Id, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private readonly ImeAwareTextBox _search = new ImeAwareTextBox
	{
		Dock = DockStyle.Top,
		PlaceholderText = Localizer.T("animation.donor.search")
	};

	private readonly ListBox _list = new ListBox
	{
		Dock = DockStyle.Fill,
		BorderStyle = BorderStyle.None,
		BackColor = UiTheme.Surface,
		ForeColor = UiTheme.Text,
		IntegralHeight = false
	};

	private readonly AnimationPreviewCanvas _preview = new AnimationPreviewCanvas
	{
		Dock = DockStyle.Fill,
		StatusText = Localizer.T("animation.donor.idle")
	};

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer
	{
		Interval = 16
	};

	private readonly Stopwatch _clock = new Stopwatch();

	private readonly CancellationTokenSource _closed = new CancellationTokenSource();

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
		_ids = ids.ToHashSet<string>(StringComparer.Ordinal);
		Text = Localizer.T("animation.donor.title");
		base.Size = new Size(1000, 650);
		MinimumSize = new Size(800, 520);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = UiTheme.Window;
		Font = new Font("Microsoft YaHei UI", 9f);
		UiTheme.ApplyDarkTitleBar(this);
		UiTheme.StyleTextBox(_search);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(16),
			ColumnCount = 2,
			RowCount = 3
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
		tableLayoutPanel.Controls.Add(_search, 0, 0);
		tableLayoutPanel.SetColumnSpan(_search, 2);
		tableLayoutPanel.Controls.Add(_list, 0, 1);
		tableLayoutPanel.Controls.Add(_preview, 1, 1);
		Button button = UiTheme.Button(Localizer.T("animation.donor.preview"), async delegate
		{
			await PreviewAsync();
		});
		Button button2 = UiTheme.Button(Localizer.T("animation.donor.use"), delegate
		{
			if (SelectedCardId != null)
			{
				base.DialogResult = DialogResult.OK;
				Close();
			}
		}, ButtonTone.Gold);
		button.Dock = (button2.Dock = DockStyle.Fill);
		tableLayoutPanel.Controls.Add(button, 0, 2);
		tableLayoutPanel.Controls.Add(button2, 1, 2);
		base.Controls.Add(tableLayoutPanel);
		_search.TextChanged += delegate
		{
			if (!_search.IsImeComposing)
			{
				Filter();
			}
		};
		_search.ImeCompositionEnded += delegate
		{
			Filter();
		};
		_list.SelectedIndexChanged += delegate
		{
			_generation++;
			_previewCancellation?.Cancel();
			ClearPreview();
		};
		_list.DoubleClick += async delegate
		{
			await PreviewAsync();
		};
		_timer.Tick += delegate
		{
			if (_frames != null && _frames.Frames.Count != 0)
			{
				int index = (int)(_clock.Elapsed.TotalSeconds * (double)_frames.FramesPerSecond) % _frames.Frames.Count;
				if (_preview.Frame != _frames.Frames[index])
				{
					_preview.Frame = _frames.Frames[index];
					_preview.Invalidate();
				}
			}
		};
		Filter();
	}

	private void Filter()
	{
		string query = _search.Text.Trim();
		IEnumerable<string> enumerable;
		if (query.Length != 0)
		{
			enumerable = (from x in _catalog.Search(query, _catalog.Count)
				select x.AnimationId.ToString()).Where(_ids.Contains).Concat(_ids.Where((string x) => x.Contains(query, StringComparison.Ordinal))).Distinct();
		}
		else
		{
			IEnumerable<string> enumerable2 = _ids.OrderBy((string x) => int.Parse(x));
			enumerable = enumerable2;
		}
		IEnumerable<string> source = enumerable;
		_list.BeginUpdate();
		try
		{
			_list.Items.Clear();
			foreach (string item in source.Take(600))
			{
				_list.Items.Add(new Choice(item, item + " · " + (_catalog.FindCardOrMrk(int.Parse(item))?.Name(Localizer.Language) ?? "动画资源")));
			}
			if (_list.Items.Count > 0)
			{
				_list.SelectedIndex = 0;
			}
		}
		finally
		{
			_list.EndUpdate();
		}
	}

	private async Task PreviewAsync()
	{
		string id = SelectedCardId;
		if (id == null)
		{
			return;
		}
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
			CurrentMonsterAnimationPreview currentMonsterAnimationPreview = await Task.Run(() => Spine42PreviewRenderer.TryLoad(MonsterAnimationIndexService.Find(_gameRoot, id), null, 24, 120, 512, token));
			if (base.IsDisposed || version != _generation)
			{
				currentMonsterAnimationPreview?.Dispose();
				return;
			}
			_frames = currentMonsterAnimationPreview;
			if (currentMonsterAnimationPreview != null && currentMonsterAnimationPreview.Frames.Count > 0)
			{
				_preview.Frame = currentMonsterAnimationPreview.Frames[0];
			}
			_preview.StatusText = ((currentMonsterAnimationPreview == null) ? Localizer.T("animation.donor.unsupported") : "");
			_clock.Restart();
			_timer.Start();
			_preview.Invalidate();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!base.IsDisposed)
			{
				_preview.StatusText = ex2.Message;
				_preview.Invalidate();
			}
		}
	}

	private void ClearPreview()
	{
		_timer.Stop();
		_preview.Frame = null;
		_frames?.Dispose();
		_frames = null;
		_preview.Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_generation++;
			_closed.Cancel();
			_previewCancellation?.Cancel();
			_timer.Dispose();
			_preview.Frame = null;
			_frames?.Dispose();
			_frames = null;
		}
		base.Dispose(disposing);
	}
}
