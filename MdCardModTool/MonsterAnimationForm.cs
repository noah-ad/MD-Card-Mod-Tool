using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class MonsterAnimationForm : Form
{
	private readonly string _gameRoot;

	private readonly Dictionary<Control, string> _localizedControls = new();

	private readonly MonsterAnimationService _service = new MonsterAnimationService();

	private CardCatalogService _catalog = CardCatalogService.LoadBestAvailable();

	private CardCatalogEntry? _resolvedCard;

	private readonly ImeAwareTextBox _cardId = new ImeAwareTextBox
	{
		Width = 220
	};

	private readonly Label _resourceStatus = new Label
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Muted,
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly Label _sourceStatus = new Label
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Text,
		Padding = new Padding(3, 7, 3, 7),
		TextAlign = ContentAlignment.MiddleLeft,
		AutoEllipsis = true,
		UseCompatibleTextRendering = false
	};

	private readonly AnimationPreviewCanvas _preview = new AnimationPreviewCanvas
	{
		Dock = DockStyle.Fill
	};

	private readonly TrackBar _timeline = new TrackBar
	{
		Dock = DockStyle.Fill,
		Minimum = 0,
		Maximum = 0,
		TickStyle = TickStyle.None,
		Enabled = false
	};

	private readonly NumericUpDown _fps = new ModernNumericUpDown
	{
		Minimum = 1m,
		Maximum = 60m,
		Value = 12m,
		Width = 88
	};

	private readonly NumericUpDown _maxFrames = new ModernNumericUpDown
	{
		Minimum = 10m,
		Maximum = 600m,
		Value = 60m,
		Increment = 10m,
		Width = 88
	};

	private readonly NumericUpDown _startSeconds = new ModernNumericUpDown
	{
		Minimum = 0m,
		Maximum = 3600m,
		Value = 0m,
		DecimalPlaces = 1,
		Increment = 0.5m,
		Width = 88
	};

	private readonly NumericUpDown _scale = new ModernNumericUpDown
	{
		Minimum = 10m,
		Maximum = 500m,
		Value = 100m,
		Increment = 5m,
		Width = 88
	};

	private readonly ModernComboBox _frameEdge = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 110
	};

	private readonly ModernComboBox _atlasEdge = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 110
	};

	private readonly ModernComboBox _animationSelector = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Width = 150
	};

	private readonly CheckBox _removeGreenScreen = new CheckBox
	{
		AutoSize = true,
		ForeColor = UiTheme.Text,
		BackColor = Color.Transparent
	};

	private readonly Label _frameLabel = new Label
	{
		AutoSize = true,
		ForeColor = UiTheme.Muted,
		Padding = new Padding(8, 8, 0, 0)
	};

	private readonly Button _play;

	private readonly Button _apply;

	private readonly Button _chooseMedia;

	private readonly Button _restore;

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _cardLookupDebounce = new System.Windows.Forms.Timer
	{
		Interval = 380
	};

	private readonly Stopwatch _playClock = new();

	private readonly List<Bitmap> _previewFrames = new List<Bitmap>();

	private ExtractedAnimation? _media;

	private MonsterAnimationSet? _set;

	private MonsterAnimationSet? _previewSet;

	private int _previewFramesPerSecond = 15;

	private int _playStartFrame;

	private bool _playing;

	private bool _busy;

	private bool _automaticQuality;

	private int _resolvedFrameEdge;

	private int _mediaWidth;

	private int _mediaHeight;

	private int _animationPreviewVersion;

	private CancellationTokenSource? _animationPreviewCancellation;

	private Func<string>? _resourceStatusFactory;

	private Func<string>? _locatedResourceStatusFactory;

	private Func<string>? _sourceStatusFactory;

	private Func<string>? _previewStatusFactory;

	private bool _updatingAnimationSelector;

	private bool _updatingTimeline;

	private bool _suppressCardLookup;

	private bool _legacyCreation;

	public MonsterAnimationForm(string gameRoot, string? initialCardId = null)
	{
		_gameRoot = gameRoot;
		UiTheme.ApplyDarkTitleBar(this);
		Text = Localizer.T("animation.title");
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new Size(1160, 820);
		MinimumSize = new Size(940, 680);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.KeyPreview = true;
		AllowDrop = true;
		DpiChanged += delegate { UiTheme.QueueStableRepaint(this); };
		ResizeEnd += delegate { UiTheme.QueueStableRepaint(this); };
		UiTheme.StyleTextBox(_cardId);
		UiTheme.StyleComboBox(_frameEdge);
		UiTheme.StyleComboBox(_atlasEdge);
		UiTheme.StyleComboBox(_animationSelector);
		_frameEdge.Items.AddRange(new object[8] { Localizer.T("animation.quality.auto"), "512", "768", "1024", "1280", "1600", "1920", "2048" });
		_frameEdge.SelectedIndex = 0;
		_atlasEdge.Items.AddRange(new object[2] { "2048", "4096" });
		_atlasEdge.SelectedItem = "4096";
		_animationSelector.Items.Add("animation");
		_animationSelector.SelectedIndex = 0;
		_animationSelector.SelectedIndexChanged += async delegate
		{
			if (!_updatingAnimationSelector && !_busy && _media == null && _previewSet?.IsComplete == true)
			{
				await LoadCurrentAnimationPreviewAsync(_previewSet);
			}
		};
		_preview.ViewChanged += delegate
		{
			decimal percent = Math.Clamp(_preview.ScalePercent, (int)_scale.Minimum, (int)_scale.Maximum);
			if (_scale.Value != percent) _scale.Value = percent;
		};
		_cardId.Text = ((initialCardId != null && initialCardId.All(char.IsAsciiDigit)) ? initialCardId : "");
		_cardLookupDebounce.Tick += async delegate
		{
			_cardLookupDebounce.Stop();
			string query = _cardId.Text.Trim();
			if (!_busy && !_cardId.IsImeComposing && query.Length > 0 && query.All(char.IsAsciiDigit))
			{
				await LocateAsync();
			}
		};
		_cardId.TextChanged += delegate
		{
			if (_suppressCardLookup || _cardId.IsImeComposing)
			{
				return;
			}
			ScheduleAutomaticCardLookup();
		};
		_cardId.ImeCompositionStarted += delegate { _cardLookupDebounce.Stop(); };
		_cardId.ImeCompositionEnded += delegate { ScheduleAutomaticCardLookup(); };
		_cardId.KeyDown += async delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				_cardLookupDebounce.Stop();
				e.SuppressKeyPress = true;
				await LocateAsync();
			}
		};
		_timeline.ValueChanged += delegate
		{
			if (!_updatingTimeline)
			{
				ShowFrame(_timeline.Value);
			}
		};
		_fps.ValueChanged += delegate
		{
			if (_media != null)
			{
				SetPreviewRate((int)_fps.Value);
			}
			UpdateSourceStatus();
		};
		_scale.ValueChanged += delegate
		{
			_preview.AnimationScale = (float)_scale.Value / 100f;
			_preview.ScalePercent = (int)_scale.Value;
			_preview.Invalidate();
		};
		_timer.Interval = 1000 / (int)_fps.Value;
		_timer.Tick += delegate
		{
			AdvanceFrame();
		};
		base.DragEnter += delegate(object? _, DragEventArgs e)
		{
			IDataObject? data = e.Data;
			e.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
		};
		base.DragDrop += async delegate(object? _, DragEventArgs e)
		{
			if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0)
			{
				await LoadMediaAsync(files.All(IsImageFile) ? files : [files[0]]);
			}
		};
		Button locate = Bind(UiTheme.Button("", async delegate
		{
			await LocateAsync();
		}, ButtonTone.Primary), "animation.action.locate");
		Button rebuild = Bind(UiTheme.Button("", async delegate
		{
			await RebuildIndexAsync();
		}), "animation.action.rebuild");
		TableLayoutPanel cardRow = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18, 8, 18, 8),
			ColumnCount = 5,
			RowCount = 1
		};
		cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		cardRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		cardRow.Controls.Add(Bind(Label("", UiTheme.Gold), "animation.field.card"), 0, 0);
		cardRow.Controls.Add(_cardId, 1, 0);
		cardRow.Controls.Add(locate, 2, 0);
		cardRow.Controls.Add(_resourceStatus, 3, 0);
		cardRow.Controls.Add(rebuild, 4, 0);
		_chooseMedia = Bind(UiTheme.Button("", async delegate
		{
			await ChooseMediaAsync();
		}, ButtonTone.Primary), "animation.action.choose");
		_chooseMedia.Enabled = false;
		_play = Bind(UiTheme.Button("", delegate
		{
			TogglePlay();
		}), "animation.action.play");
		_apply = Bind(UiTheme.Button("", async delegate
		{
			await ApplyAsync();
		}, ButtonTone.Gold), "animation.action.apply");
		_apply.Enabled = false;
		_restore = Bind(UiTheme.Button("", async delegate
		{
			await RestoreAsync();
		}, ButtonTone.Danger), "animation.action.restore");
		_restore.Enabled = false;
		TableLayoutPanel buttons = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			ColumnCount = 2,
			RowCount = 3,
			Padding = new Padding(0, 6, 0, 4),
			BackColor = UiTheme.Surface
		};
		buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		Button[] array = new Button[4] { _chooseMedia, _play, _apply, _restore };
		foreach (Button obj in array)
		{
			obj.AutoSize = false;
			obj.Dock = DockStyle.Fill;
			obj.Margin = new Padding(3);
		}
		buttons.Controls.Add(_chooseMedia, 0, 0);
		buttons.SetColumnSpan(_chooseMedia, 2);
		buttons.Controls.Add(_play, 0, 1);
		buttons.SetColumnSpan(_play, 2);
		buttons.Controls.Add(_apply, 0, 2);
		buttons.Controls.Add(_restore, 1, 2);
		TableLayoutPanel options = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 277,
			ColumnCount = 2,
			RowCount = 8,
			Padding = new Padding(0, 4, 0, 4),
			BackColor = UiTheme.Surface
		};
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
		options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		AddOption(options, 0, "animation.field.name", _animationSelector);
		AddOption(options, 1, "animation.field.fps", _fps);
		AddOption(options, 2, "animation.field.start", _startSeconds);
		AddOption(options, 3, "animation.field.frames", _maxFrames);
		AddOption(options, 4, "animation.field.quality", _frameEdge);
		AddOption(options, 5, "animation.field.atlas", _atlasEdge);
		AddOption(options, 6, "animation.field.scale", _scale);
		AddOption(options, 7, "animation.field.chroma", _removeGreenScreen);
		Bind(_removeGreenScreen, "animation.option.chroma");
		Label note = Bind(new Label
		{
			Dock = DockStyle.Top,
			Height = 104,
			ForeColor = UiTheme.Muted,
			Padding = new Padding(0, 10, 0, 0)
		}, "animation.note");
		DarkScrollPanel optionScroll = new DarkScrollPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = Padding.Empty,
			Margin = Padding.Empty,
			ContentHeight = options.Height + note.Height
		};
		TableLayoutPanel optionContent = new()
		{
			Dock = DockStyle.Top,
			Height = options.Height + note.Height,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiTheme.Surface
		};
		optionContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		optionContent.RowStyles.Add(new RowStyle(SizeType.Absolute, options.Height));
		optionContent.RowStyles.Add(new RowStyle(SizeType.Absolute, note.Height));
		note.Dock = DockStyle.Fill;
		optionContent.Controls.Add(options, 0, 0);
		optionContent.Controls.Add(note, 0, 1);
		optionScroll.ContentPanel.Controls.Add(optionContent);
		BorderPanel side = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		TableLayoutPanel sideLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiTheme.Surface
		};
		sideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132f));
		sideLayout.Controls.Add(optionScroll, 0, 0);
		sideLayout.Controls.Add(_sourceStatus, 0, 1);
		sideLayout.Controls.Add(buttons, 0, 2);
		side.Controls.Add(sideLayout);
		TableLayoutPanel timelineRow = new TableLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			ColumnCount = 2,
			Padding = new Padding(8, 5, 8, 5),
			BackColor = UiTheme.SurfaceAlt
		};
		timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		timelineRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		timelineRow.Controls.Add(_timeline, 0, 0);
		timelineRow.Controls.Add(_frameLabel, 1, 0);
		BorderPanel previewPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(1)
		};
		previewPanel.Controls.Add(_preview);
		previewPanel.Controls.Add(timelineRow);
		SplitContainer body = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			SplitterWidth = 8,
			FixedPanel = FixedPanel.Panel2,
			BackColor = UiTheme.Window
		};
		body.Panel1.Padding = new Padding(14, 14, 7, 14);
		body.Panel2.Padding = new Padding(7, 14, 14, 14);
		body.Panel1.Controls.Add(previewPanel);
		body.Panel2.Controls.Add(side);
		GradientBanner banner = new GradientBanner
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(22, 6, 22, 6)
		};
		Label bannerTitle = new Label
		{
			Name = "MonsterAnimationBannerTitle",
			Text = "MONSTER ANIMATION LAB",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Font = new Font("Segoe UI Semibold", 16f),
			ForeColor = UiTheme.Text,
			BackColor = Color.Transparent,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		Label bannerSubtitle = new Label
		{
			Name = "MonsterAnimationBannerSubtitle",
			Text = "GIF / VIDEO  →  SPINE SEQUENCE  →  MASTER DUEL",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			ForeColor = UiTheme.Primary,
			BackColor = Color.Transparent,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		TableLayoutPanel bannerText = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = Color.Transparent
		};
		bannerText.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		bannerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		bannerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		bannerText.Controls.Add(bannerTitle, 0, 0);
		bannerText.Controls.Add(bannerSubtitle, 0, 1);
		banner.Controls.Add(bannerText);
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			ColumnCount = 1,
			BackColor = UiTheme.Window
		};
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		root.Controls.Add(banner, 0, 0);
		root.Controls.Add(cardRow, 0, 1);
		root.Controls.Add(body, 0, 2);
		// Do not expose the design-time bounds. The embedded form is resized to its
		// host immediately before Load, and only that final layout is ever painted.
		root.Visible = false;
		base.Controls.Add(root);
		base.FormClosed += delegate
		{
			Localizer.LanguageChanged -= OnLanguageChanged;
			_animationPreviewCancellation?.Cancel();
			_animationPreviewCancellation?.Dispose();
			_animationPreviewCancellation = null;
			_cardLookupDebounce.Stop();
			_cardLookupDebounce.Dispose();
			DisposeMedia();
		};
		Localizer.LanguageChanged += OnLanguageChanged;
		SetSourceStatus("animation.source.drop");
		SetPreviewStatus("animation.preview.drop");
		ApplyLanguage();
		base.Load += async delegate
		{
			// Visible controls participate in SplitContainer and TableLayoutPanel
			// measurement. Load still runs before the form's first paint, so exposing
			// the root here gives us real bounds without showing a design-size frame.
			root.Visible = true;
			root.PerformLayout();
			body.PerformLayout();
			int maximum = body.Width - 390 - body.SplitterWidth;
			if (maximum >= 500)
			{
				body.SplitterDistance = Math.Clamp(body.Width - 420, 500, maximum);
				body.Panel1MinSize = 500;
				body.Panel2MinSize = 390;
			}
			body.PerformLayout();
			root.PerformLayout();
			root.Invalidate(true);
			UiTheme.QueueStableRepaint(this);
			if (_cardId.Text.Length > 0 && _set == null && !_busy)
			{
				await LocateAsync();
			}
		};
	}

	public string CardQuery => _cardId.Text.Trim();

	public string? LocatedCardId => _set?.CardId;

	/// <summary>
	/// Card id that owns the six assets currently shown in the read-only preview.
	/// This can differ from <see cref="LocatedCardId"/> for alternate-art/public ids
	/// such as 3899, whose equivalent official cut-in is stored under P13668.
	/// </summary>
	public string? PreviewSourceCardId => _previewSet?.CardId;

	public async Task PreviewCardAsync(string cardId)
	{
		if (string.IsNullOrWhiteSpace(cardId) || !cardId.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("卡号必须是纯数字。", nameof(cardId));
		}
		_cardLookupDebounce.Stop();
		if (_busy && string.Equals(CardQuery, cardId, StringComparison.Ordinal))
		{
			return;
		}
		_suppressCardLookup = true;
		try
		{
			_cardId.Text = cardId;
			_cardId.SelectionStart = _cardId.TextLength;
		}
		finally
		{
			_suppressCardLookup = false;
		}
		await LocateAsync();
	}

	private void ScheduleAutomaticCardLookup()
	{
		_cardLookupDebounce.Stop();
		string query = _cardId.Text.Trim();
		if (query.Length > 0 && query.All(char.IsAsciiDigit))
		{
			_cardLookupDebounce.Start();
		}
	}

	private T Bind<T>(T control, string resourceId) where T : Control
	{
		_localizedControls[control] = resourceId;
		control.Text = Localizer.T(resourceId);
		return control;
	}

	private void ApplyLanguage()
	{
		Text = Localizer.T("animation.title");
		foreach ((Control control, string id) in _localizedControls) control.Text = Localizer.T(id);
		_cardId.PlaceholderText = Localizer.T("animation.search.placeholder");
		bool automatic = _frameEdge.SelectedIndex == 0;
		_frameEdge.Items[0] = Localizer.T("animation.quality.auto");
		if (automatic) _frameEdge.SelectedIndex = 0;
		_play.Text = Localizer.T(_playing ? "animation.action.pause" : "animation.action.play");
		RefreshLocalizedStatuses();
	}

	private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

	private void SetResourceStatus(string resourceId, params object?[] values)
	{
		object?[] captured = values.ToArray();
		SetResourceStatus(() => Localizer.F(resourceId, captured));
	}

	private void SetResourceStatus(Func<string> factory)
	{
		_resourceStatusFactory = factory;
		_resourceStatus.Text = factory();
	}

	private void SetSourceStatus(string resourceId, params object?[] values)
	{
		object?[] captured = values.ToArray();
		SetSourceStatus(() => Localizer.F(resourceId, captured));
	}

	private void SetSourceStatus(Func<string> factory)
	{
		_sourceStatusFactory = factory;
		_sourceStatus.Text = factory();
	}

	private void SetPreviewStatus(string resourceId, params object?[] values)
	{
		object?[] captured = values.ToArray();
		SetPreviewStatus(() => Localizer.F(resourceId, captured));
	}

	private void SetPreviewStatus(Func<string> factory)
	{
		_previewStatusFactory = factory;
		_preview.StatusText = factory();
		_preview.Invalidate();
	}

	private void RefreshLocalizedStatuses()
	{
		if (_resourceStatusFactory != null) _resourceStatus.Text = _resourceStatusFactory();
		if (_sourceStatusFactory != null) _sourceStatus.Text = _sourceStatusFactory();
		if (_previewStatusFactory != null) _preview.StatusText = _previewStatusFactory();
		_preview.Invalidate();
	}

	private static Label Label(string text, Color color)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = color,
			Padding = new Padding(0, 7, 8, 0)
		};
	}

	private void AddOption(TableLayoutPanel panel, int row, string resourceId, Control control)
	{
		panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 33f));
		panel.Controls.Add(Bind(new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiTheme.Text
		}, resourceId), 0, row);
		control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
		panel.Controls.Add(control, 1, row);
	}

	private async Task LocateAsync()
	{
		_cardLookupDebounce.Stop();
		_catalog = CardCatalogService.LoadBestAvailable();
		string query = _cardId.Text.Trim();
		CardCatalogEntry? card = null;
		if (query.Length > 0 && query.All(char.IsAsciiDigit) && int.TryParse(query, out int numeric))
		{
			card = _catalog.FindCardOrMrk(numeric);
		}
		else if (query.Length > 0)
		{
			card = _catalog.Search(query, 1).FirstOrDefault();
		}
		string cardId = card?.CardId.ToString() ?? query;
		if (!cardId.All(char.IsAsciiDigit) || cardId.Length == 0)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.card"), Text);
			return;
		}
		try
		{
			_resolvedCard = card;
			_suppressCardLookup = true;
			try
			{
				_cardId.Text = cardId;
				_cardId.SelectionStart = _cardId.TextLength;
			}
			finally
			{
				_suppressCardLookup = false;
			}
			_animationPreviewCancellation?.Cancel();
			if (_media == null)
			{
				DisposeMedia();
			}
			_locatedResourceStatusFactory = null;
			SetBusy(busy: true, "animation.status.locating");
			_set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
			_previewSet = _set;
			_legacyCreation = await Task.Run(() => _service.HasCreationTransaction(_gameRoot, cardId));
			if (!_set.IsComplete && card?.IsMonster == true)
			{
				// Alternate/public card ids do not always match the P-number used by
				// MonsterCutIn.  Keep the selected id as the edit target, but resolve an
				// equivalent multilingual card name for read-only official preview.
				_previewSet = await Task.Run(() =>
					MonsterAnimationIndexService.FindEquivalentPreview(_gameRoot, card, _catalog)) ?? _set;
			}
			bool canReplaceSelected = _set.IsComplete;
			bool equivalentPreview = _previewSet.IsComplete
				&& !string.Equals(_previewSet.CardId, _set.CardId, StringComparison.Ordinal);
			Func<string> cardSuffix = () => card == null ? "" : $" · {card.Name(Localizer.Language)} · 卡号 {card.CardId}";
			if (!_previewSet.IsComplete)
			{
				_locatedResourceStatusFactory = _legacyCreation
					? () => Localizer.F("animation.status.located.legacy", cardSuffix())
					: () => Localizer.F(card?.IsMonster == true
						? "animation.status.located.unsupported"
						: "animation.status.located.none", cardSuffix());
				SetResourceStatus(_locatedResourceStatusFactory);
			}
			_resourceStatus.ForeColor = _previewSet.IsComplete ? UiTheme.Primary : Color.OrangeRed;
			_chooseMedia.Enabled = canReplaceSelected;
			_apply.Enabled = canReplaceSelected && _media != null;
			_restore.Enabled = _set.IsComplete || _legacyCreation;
			if (_previewSet.IsComplete)
			{
				MonsterAnimationTemplate template = await Task.Run(() => _service.ReadTemplate(_gameRoot, _previewSet));
				_updatingAnimationSelector = true;
				try
				{
					string? selectedAnimation = _animationSelector.SelectedItem as string;
					_animationSelector.Items.Clear();
					_animationSelector.Items.AddRange(template.EffectiveAnimationNames.Cast<object>().ToArray());
					_animationSelector.SelectedItem = template.EffectiveAnimationNames.Contains(selectedAnimation ?? "", StringComparer.Ordinal)
						? selectedAnimation : template.EffectiveAnimationNames[0];
				}
				finally { _updatingAnimationSelector = false; }
				string animationNames = string.Join(" / ", template.EffectiveAnimationNames);
				_locatedResourceStatusFactory = equivalentPreview
					? () => Localizer.F("animation.status.located.fallback", _previewSet.CardId, cardSuffix(), animationNames)
					: _legacyCreation
						? () => Localizer.F("animation.status.located.legacy", cardSuffix())
						: () => Localizer.F("animation.status.located.complete", _set.CountSummary, cardSuffix(), animationNames);
				SetResourceStatus(_locatedResourceStatusFactory);
				if (_media == null)
				{
					await LoadCurrentAnimationPreviewAsync(_previewSet);
				}
			}
			else if (_media == null)
			{
				DisposeMedia();
				SetSourceStatus(card?.IsMonster == true ? "animation.source.unsupported" : "animation.source.notapplicable");
				SetPreviewStatus(card?.IsMonster == true ? "animation.preview.unsupported" : "animation.preview.notapplicable");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("animation.error.locate"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private async Task RebuildIndexAsync()
	{
		if (MessageBox.Show(this, Localizer.T("animation.confirm.rebuild.message"), Localizer.T("animation.confirm.rebuild.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			SetBusy(busy: true, "animation.status.scanning");
			await Task.Run(() => MonsterAnimationIndexService.Rebuild(_gameRoot, delegate(int done, int total, int found)
			{
				if (!base.IsDisposed && base.IsHandleCreated)
				{
					BeginInvoke(delegate
					{
						SetResourceStatus("animation.status.scanprogress", done, total, found);
					});
				}
			}));
			await LocateAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("animation.error.rebuild"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private async Task ChooseMediaAsync()
	{
		if (_set?.IsComplete != true)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.officialonly"), Text,
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = Localizer.T("animation.dialog.media.title"),
			Filter = Localizer.T("animation.dialog.media.filter"),
			Multiselect = true
		};
		try
		{
			if (dialog.ShowDialog(this) == DialogResult.OK)
			{
				string[] selected = dialog.FileNames;
				await LoadMediaAsync(selected.All(IsImageFile) ? selected : [selected[0]]);
			}
		}
		finally
		{
			((IDisposable)(object)dialog)?.Dispose();
		}
	}

	private Task LoadMediaAsync(string path) => LoadMediaAsync([path]);

	private async Task LoadMediaAsync(IReadOnlyList<string> paths)
	{
		ExtractedAnimation loadedMedia = null;
		List<Bitmap> loadedFrames = null;
		bool resetToFullGameCanvas = _media == null;
		bool automaticQuality = _frameEdge.SelectedIndex == 0;
		bool removeGreenScreen = _removeGreenScreen.Checked;
		int resolvedFrameEdge = 0;
		int mediaWidth = 0;
		int mediaHeight = 0;
		try
		{
			if (automaticQuality)
			{
				SetBusy(busy: true, "animation.status.probing");
				using ExtractedAnimation probe = await ExtractSourceAsync(paths, 128, removeGreenScreen: false);
				using Bitmap probeFrame = probe.LoadFrame(0);
				resolvedFrameEdge = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(probe.FramePaths.Count, probeFrame.Width, probeFrame.Height, int.Parse(_atlasEdge.Text));
				SetBusy(busy: true, "animation.status.extractauto", probe.FramePaths.Count, resolvedFrameEdge);
			}
			else
			{
				resolvedFrameEdge = int.Parse(_frameEdge.Text);
				SetBusy(busy: true, "animation.status.extractfixed", resolvedFrameEdge);
			}
			loadedMedia = await ExtractSourceAsync(paths, resolvedFrameEdge, removeGreenScreen);
			using (Bitmap firstFrame = loadedMedia.LoadFrame(0))
			{
				mediaWidth = firstFrame.Width;
				mediaHeight = firstFrame.Height;
			}
			int previewEdge = Math.Min(512, resolvedFrameEdge);
			ExtractedAnimation mediaForPreview = loadedMedia;
			loadedFrames = await Task.Run(() => (from i in Enumerable.Range(0, mediaForPreview.FramePaths.Count)
				select mediaForPreview.LoadFrame(i, previewEdge)).ToList());
			DisposeMedia();
			_media = loadedMedia;
			loadedMedia = null;
			_automaticQuality = automaticQuality;
			_resolvedFrameEdge = resolvedFrameEdge;
			_mediaWidth = mediaWidth;
			_mediaHeight = mediaHeight;
			_previewFrames.AddRange(loadedFrames);
			loadedFrames = null;
			if (resetToFullGameCanvas)
			{
				_scale.Value = 100m;
			}
			SetPreviewRate((int)_fps.Value);
			_timeline.Maximum = Math.Max(0, _previewFrames.Count - 1);
			_timeline.Value = 0;
			_timeline.Enabled = _previewFrames.Count > 1;
			ShowFrame(0);
			UpdateSourceStatus();
			_apply.Enabled = _set?.IsComplete == true;
			if (!_playing)
			{
				TogglePlay();
			}
		}
		catch (Exception ex)
		{
			loadedMedia?.Dispose();
			if (loadedFrames != null)
			{
				foreach (Bitmap item in loadedFrames)
				{
					item.Dispose();
				}
			}
			MessageBox.Show(this, ex.Message, Localizer.T("animation.error.media"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private Task<ExtractedAnimation> ExtractSourceAsync(IReadOnlyList<string> paths, int maxFrameEdge, bool removeGreenScreen)
	{
		return paths.All(IsImageFile)
			? MonsterAnimationMedia.ExtractSequenceAsync(paths, (int)_fps.Value, (int)_maxFrames.Value, maxFrameEdge, removeGreenScreen)
			: MonsterAnimationMedia.ExtractAsync(paths[0], (int)_fps.Value, (int)_maxFrames.Value, maxFrameEdge, (double)_startSeconds.Value, removeGreenScreen);
	}

	private static bool IsImageFile(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff";
	}

	private void UpdateSourceStatus()
	{
		if (_media == null)
		{
			SetSourceStatus("animation.source.drop");
			return;
		}
		ExtractedAnimation media = _media;
		SetSourceStatus(() =>
		{
			string quality = Localizer.F(_automaticQuality ? "animation.quality.resolved.auto" : "animation.quality.resolved.fixed", _resolvedFrameEdge);
			string transparency = media.GreenScreenRemoved ? Localizer.T("animation.source.transparent") : "";
			return Localizer.F("animation.source.media", Path.GetFileName(media.SourcePath), media.FramePaths.Count,
				(int)_fps.Value, (double)media.FramePaths.Count / (double)_fps.Value, _mediaWidth, _mediaHeight, quality, transparency);
		});
	}

	private async Task LoadCurrentAnimationPreviewAsync(MonsterAnimationSet set)
	{
		int previewVersion = ++_animationPreviewVersion;
		_animationPreviewCancellation?.Cancel();
		_animationPreviewCancellation?.Dispose();
		CancellationTokenSource cancellation = new();
		_animationPreviewCancellation = cancellation;
		CancellationToken cancellationToken = cancellation.Token;
		string? animationName = _animationSelector.SelectedItem as string;
		Func<string> resourceBase = _locatedResourceStatusFactory ?? _resourceStatusFactory ?? (() => "");
		SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.loading"));
		int streamedFrames = 0;
		void ShowRenderedFrame(Bitmap frame, int frameIndex, int frameCount)
		{
			if (cancellationToken.IsCancellationRequested) return;
			Bitmap? display = new Bitmap(frame);
			try
			{
				if (base.IsDisposed || !base.IsHandleCreated) return;
				Invoke((Action)delegate
				{
					if (display == null || base.IsDisposed || previewVersion != _animationPreviewVersion
						|| cancellationToken.IsCancellationRequested) return;
					if (frameIndex == 0)
					{
						DisposeMedia();
						SetPreviewRate(24);
					}
					_previewFrames.Add(display);
					display = null;
					streamedFrames = _previewFrames.Count;
					_timeline.Maximum = Math.Max(0, _previewFrames.Count - 1);
					_timeline.Enabled = _previewFrames.Count > 1;
					if (frameIndex == 0)
					{
						_timeline.Value = 0;
						ShowFrame(0);
					}
					SetSourceStatus("animation.preview.rendering", streamedFrames, frameCount);
					if (_previewFrames.Count >= 2 && !_playing)
					{
						TogglePlay();
					}
				});
			}
			catch (InvalidOperationException)
			{
			}
			finally
			{
				display?.Dispose();
			}
		}
		CurrentMonsterAnimationPreview? current;
		try
		{
			current = await Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				return MonsterAnimationCurrentPreview.TryLoad(set)
					?? Spine42PreviewRenderer.TryLoad(set, animationName, cancellationToken: cancellationToken,
						frameRendered: ShowRenderedFrame);
			}, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			if (ReferenceEquals(_animationPreviewCancellation, cancellation))
			{
				_animationPreviewCancellation = null;
			}
			cancellation.Dispose();
		}
		if (previewVersion != _animationPreviewVersion || base.IsDisposed)
		{
			current?.Dispose();
			return;
		}
		if (current == null)
		{
			DisposeMedia();
			SetSourceStatus("animation.source.complex");
			SetPreviewStatus("animation.preview.complex");
			SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.complex"));
			return;
		}
		int fps = current.FramesPerSecond;
		string loadedAnimationName = current.AnimationName;
		int scalePercent = current.ScalePercent;
		if (streamedFrames == 0)
		{
			List<Bitmap> frames = current.Frames.ToList();
			current.Frames.Clear();
			current.Dispose();
			DisposeMedia();
			_previewFrames.AddRange(frames);
		}
		else
		{
			current.Dispose();
		}
		_scale.Value = scalePercent;
		SetPreviewRate(fps);
		_timeline.Maximum = Math.Max(0, _previewFrames.Count - 1);
		_timeline.Value = 0;
		_timeline.Enabled = _previewFrames.Count > 1;
		SetPreviewStatus(() => "");
		ShowFrame(0);
		SetSourceStatus("animation.source.current", loadedAnimationName, _previewFrames.Count, fps,
			(double)_previewFrames.Count / (double)fps, scalePercent);
		SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.playing"));
		if (!_playing)
		{
			TogglePlay();
		}
	}

	private void SetPreviewRate(int framesPerSecond)
	{
		_previewFramesPerSecond = Math.Clamp(framesPerSecond, 1, 60);
		_timer.Interval = 15;
		if (_playing)
		{
			_playStartFrame = _timeline.Value;
			_playClock.Restart();
		}
	}

	private void TogglePlay()
	{
		if (_previewFrames.Count >= 2)
		{
			_playing = !_playing;
			_play.Text = Localizer.T(_playing ? "animation.action.pause" : "animation.action.play");
			if (_playing)
			{
				_playStartFrame = _timeline.Value;
				_playClock.Restart();
			}
			else
			{
				_playClock.Stop();
			}
			_timer.Enabled = _playing;
		}
	}

	private void AdvanceFrame()
	{
		if (_previewFrames.Count != 0)
		{
			int frame = (_playStartFrame + (int)Math.Floor(_playClock.Elapsed.TotalSeconds * _previewFramesPerSecond)) % _previewFrames.Count;
			if (frame == _timeline.Value)
			{
				return;
			}
			_updatingTimeline = true;
			_timeline.Value = frame;
			_updatingTimeline = false;
			ShowFrame(_timeline.Value);
		}
	}

	private void ShowFrame(int index)
	{
		if (index >= 0 && index < _previewFrames.Count)
		{
			_preview.Frame = _previewFrames[index];
			_preview.Invalidate();
			_frameLabel.Text = $"{index + 1} / {_previewFrames.Count}";
		}
	}

	private async Task ApplyAsync()
	{
		MonsterAnimationSet? set = _set;
		if (set?.IsComplete != true || _media == null)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.officialonly"), Text);
		}
		else
		{
			string operation = Localizer.T("animation.operation.modify");
			if (!EnsureGameClosed() || MessageBox.Show(this, Localizer.F("animation.confirm.apply.message", set.CardId, operation),
				Localizer.T("animation.confirm.apply.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
			{
				return;
			}
			try
			{
				SetBusy(busy: true, "animation.status.building");
				MonsterAnimationTemplate template = await Task.Run(() => _service.ReadTemplate(_gameRoot, set));
				MonsterAnimationBuildResult built = await Task.Run(() => MonsterAnimationBuilder.Build(_media.FramePaths, set.CardId, (int)_fps.Value, (int)_scale.Value, template, int.Parse(_atlasEdge.Text)));
				try
				{
					SetResourceStatus("animation.status.compressing", built.AtlasWidth, built.AtlasHeight);
					await Task.Run(() => _service.Apply(_gameRoot, set, built));
					_resourceStatus.ForeColor = UiTheme.Primary;
					SetResourceStatus("animation.status.completed", built.FrameCount, built.FramesPerSecond,
						(int)_scale.Value, built.AtlasWidth, built.AtlasHeight);
					string launchNote = Localizer.T("animation.message.launch.restart");
					MessageBox.Show(this, Localizer.F("animation.message.completed", launchNote),
						Localizer.T("animation.message.completed.title"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				finally
				{
					if (built != null)
					{
						((IDisposable)built).Dispose();
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, Localizer.T("animation.error.replace"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				SetBusy(busy: false);
			}
		}
	}

	private async Task RestoreAsync()
	{
		if (_set == null)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.restore"), Text);
		}
		else
		{
			if (!EnsureGameClosed() || MessageBox.Show(this, Localizer.F("animation.confirm.restore.message", _set.CardId),
				Localizer.T("animation.confirm.restore.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
			{
				return;
			}
			try
			{
				SetBusy(busy: true, "animation.status.restoring");
				int count = await Task.Run(() => _service.Restore(_gameRoot, _set));
				SetResourceStatus(count == 0 ? "animation.status.restore.none" : "animation.status.restore.count", count);
				MessageBox.Show(this, count == 0 ? Localizer.T("animation.message.restore.none") : Localizer.F("animation.message.restore.count", count),
					Localizer.T("animation.confirm.restore.title"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				if (count > 0)
				{
					_legacyCreation = false;
					await LocateAsync();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, Localizer.T("animation.error.restore"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				SetBusy(busy: false);
			}
		}
	}

	private void SetBusy(bool busy, string? statusResourceId = null, params object?[] statusValues)
	{
		_busy = busy;
		base.UseWaitCursor = busy;
		if (statusResourceId != null)
		{
			SetResourceStatus(statusResourceId, statusValues);
		}
		_cardId.Enabled = !busy;
		bool officialAnimation = !busy && _set?.IsComplete == true;
		_chooseMedia.Enabled = officialAnimation;
		_apply.Enabled = officialAnimation && _media != null;
		_restore.Enabled = !busy && (_set?.IsComplete == true || _legacyCreation);
	}

	private bool EnsureGameClosed()
	{
		try
		{
			if (Process.GetProcessesByName("masterduel").Length == 0)
			{
				return true;
			}
		}
		catch
		{
			return true;
		}
		MessageBox.Show(this, Localizer.T("animation.error.game.running"), Localizer.T("animation.error.game.title"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		return false;
	}

	private void DisposeMedia()
	{
		_timer.Stop();
		_playing = false;
		_play.Text = Localizer.T("animation.action.play");
		_preview.Frame = null;
		foreach (Bitmap previewFrame in _previewFrames)
		{
			previewFrame.Dispose();
		}
		_previewFrames.Clear();
		_media?.Dispose();
		_media = null;
		_timeline.Value = 0;
		_timeline.Maximum = 0;
		_timeline.Enabled = false;
		_frameLabel.Text = "";
	}
}
