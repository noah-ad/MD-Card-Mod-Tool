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

	private readonly Dictionary<Control, string> _localizedControls = new Dictionary<Control, string>();

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

	private readonly Button _chooseDonor;

	private readonly Button _restore;

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _cardLookupDebounce = new System.Windows.Forms.Timer
	{
		Interval = 380
	};

	private readonly Stopwatch _playClock = new Stopwatch();

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

	public string CardQuery => _cardId.Text.Trim();

	public string? LocatedCardId => _set?.CardId;

	public string? PreviewSourceCardId => _previewSet?.CardId;

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
		base.DpiChanged += delegate
		{
			UiTheme.QueueStableRepaint(this);
		};
		base.ResizeEnd += delegate
		{
			UiTheme.QueueStableRepaint(this);
		};
		UiTheme.StyleTextBox(_cardId);
		UiTheme.StyleComboBox(_frameEdge);
		UiTheme.StyleComboBox(_atlasEdge);
		UiTheme.StyleComboBox(_animationSelector);
		_frameEdge.Items.AddRange(new object[8]
		{
			Localizer.T("animation.quality.auto"),
			"512",
			"768",
			"1024",
			"1280",
			"1600",
			"1920",
			"2048"
		});
		_frameEdge.SelectedIndex = 0;
		_atlasEdge.Items.AddRange(new object[3] { "2048", "4096", "8192" });
		_atlasEdge.SelectedItem = "4096";
		_animationSelector.Items.Add("animation");
		_animationSelector.SelectedIndex = 0;
		_animationSelector.SelectedIndexChanged += async delegate
		{
			if (!_updatingAnimationSelector && !_busy && _media == null && (_previewSet?.IsComplete ?? false))
			{
				await LoadCurrentAnimationPreviewAsync(_previewSet);
			}
		};
		_preview.ViewChanged += delegate
		{
			decimal num2 = Math.Clamp(_preview.ScalePercent, (int)_scale.Minimum, (int)_scale.Maximum);
			if (_scale.Value != num2)
			{
				_scale.Value = num2;
			}
		};
		_cardId.Text = ((initialCardId != null && initialCardId.All(char.IsAsciiDigit)) ? initialCardId : "");
		_cardLookupDebounce.Tick += async delegate
		{
			_cardLookupDebounce.Stop();
			string text = _cardId.Text.Trim();
			if (!_busy && !_cardId.IsImeComposing && text.Length > 0 && text.All(char.IsAsciiDigit))
			{
				await LocateAsync();
			}
		};
		_cardId.TextChanged += delegate
		{
			if (!_suppressCardLookup && !_cardId.IsImeComposing)
			{
				ScheduleAutomaticCardLookup();
			}
		};
		_cardId.ImeCompositionStarted += delegate
		{
			_cardLookupDebounce.Stop();
		};
		_cardId.ImeCompositionEnded += delegate
		{
			ScheduleAutomaticCardLookup();
		};
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
			IDataObject data = e.Data;
			e.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
		};
		base.DragDrop += async delegate(object? _, DragEventArgs e)
		{
			if (e.Data?.GetData(DataFormats.FileDrop) is string[] array2 && array2.Length != 0)
			{
				await LoadMediaAsync(array2.All(IsImageFile) ? array2 : new string[1] { array2[0] });
			}
		};
		Button control = Bind(UiTheme.Button("", async delegate
		{
			await LocateAsync();
		}, ButtonTone.Primary), "animation.action.locate");
		Button control2 = Bind(UiTheme.Button("", async delegate
		{
			await RebuildIndexAsync();
		}), "animation.action.rebuild");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18, 8, 18, 8),
			ColumnCount = 5,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(Bind(Label("", UiTheme.Gold), "animation.field.card"), 0, 0);
		tableLayoutPanel.Controls.Add(_cardId, 1, 0);
		tableLayoutPanel.Controls.Add(control, 2, 0);
		tableLayoutPanel.Controls.Add(_resourceStatus, 3, 0);
		tableLayoutPanel.Controls.Add(control2, 4, 0);
		_chooseMedia = Bind(UiTheme.Button("", async delegate
		{
			await ChooseMediaAsync();
		}, ButtonTone.Primary), "animation.action.choose");
		_chooseMedia.Enabled = false;
		_chooseDonor = Bind(UiTheme.Button("", async delegate
		{
			await ChooseDonorAsync();
		}, ButtonTone.Primary), "animation.donor.action");
		_chooseDonor.Enabled = false;
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
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			ColumnCount = 2,
			RowCount = 3,
			Padding = new Padding(0, 6, 0, 4),
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		Button[] array = new Button[5] { _chooseMedia, _chooseDonor, _play, _apply, _restore };
		foreach (Button obj in array)
		{
			obj.AutoSize = false;
			obj.Dock = DockStyle.Fill;
			obj.Margin = new Padding(3);
		}
		tableLayoutPanel2.Controls.Add(_chooseMedia, 0, 0);
		tableLayoutPanel2.Controls.Add(_chooseDonor, 1, 0);
		tableLayoutPanel2.Controls.Add(_play, 0, 1);
		tableLayoutPanel2.SetColumnSpan(_play, 2);
		tableLayoutPanel2.Controls.Add(_apply, 0, 2);
		tableLayoutPanel2.Controls.Add(_restore, 1, 2);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 277,
			ColumnCount = 2,
			RowCount = 8,
			Padding = new Padding(0, 4, 0, 4),
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		AddOption(tableLayoutPanel3, 0, "animation.field.name", _animationSelector);
		AddOption(tableLayoutPanel3, 1, "animation.field.fps", _fps);
		AddOption(tableLayoutPanel3, 2, "animation.field.start", _startSeconds);
		AddOption(tableLayoutPanel3, 3, "animation.field.frames", _maxFrames);
		AddOption(tableLayoutPanel3, 4, "animation.field.quality", _frameEdge);
		AddOption(tableLayoutPanel3, 5, "animation.field.atlas", _atlasEdge);
		AddOption(tableLayoutPanel3, 6, "animation.field.scale", _scale);
		AddOption(tableLayoutPanel3, 7, "animation.field.chroma", _removeGreenScreen);
		Bind(_removeGreenScreen, "animation.option.chroma");
		Label label = Bind(new Label
		{
			Dock = DockStyle.Top,
			Height = 104,
			ForeColor = UiTheme.Muted,
			Padding = new Padding(0, 10, 0, 0)
		}, "animation.note");
		DarkScrollPanel darkScrollPanel = new DarkScrollPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = Padding.Empty,
			Margin = Padding.Empty,
			ContentHeight = tableLayoutPanel3.Height + label.Height
		};
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = tableLayoutPanel3.Height + label.Height,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, tableLayoutPanel3.Height));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, label.Height));
		label.Dock = DockStyle.Fill;
		tableLayoutPanel4.Controls.Add(tableLayoutPanel3, 0, 0);
		tableLayoutPanel4.Controls.Add(label, 0, 1);
		darkScrollPanel.ContentPanel.Controls.Add(tableLayoutPanel4);
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 132f));
		tableLayoutPanel5.Controls.Add(darkScrollPanel, 0, 0);
		tableLayoutPanel5.Controls.Add(_sourceStatus, 0, 1);
		tableLayoutPanel5.Controls.Add(tableLayoutPanel2, 0, 2);
		borderPanel.Controls.Add(tableLayoutPanel5);
		TableLayoutPanel tableLayoutPanel6 = new TableLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 48,
			ColumnCount = 2,
			Padding = new Padding(8, 5, 8, 5),
			BackColor = UiTheme.SurfaceAlt
		};
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel6.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel6.Controls.Add(_timeline, 0, 0);
		tableLayoutPanel6.Controls.Add(_frameLabel, 1, 0);
		BorderPanel borderPanel2 = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(1)
		};
		borderPanel2.Controls.Add(_preview);
		borderPanel2.Controls.Add(tableLayoutPanel6);
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
		body.Panel1.Controls.Add(borderPanel2);
		body.Panel2.Controls.Add(borderPanel);
		GradientBanner gradientBanner = new GradientBanner
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(22, 6, 22, 6)
		};
		Label control3 = new Label
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
		Label control4 = new Label
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
		TableLayoutPanel tableLayoutPanel7 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = Color.Transparent
		};
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel7.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel7.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel7.Controls.Add(control3, 0, 0);
		tableLayoutPanel7.Controls.Add(control4, 0, 1);
		gradientBanner.Controls.Add(tableLayoutPanel7);
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
		root.Controls.Add(gradientBanner, 0, 0);
		root.Controls.Add(tableLayoutPanel, 0, 1);
		root.Controls.Add(body, 0, 2);
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
			root.Visible = true;
			root.PerformLayout();
			body.PerformLayout();
			int num2 = body.Width - 390 - body.SplitterWidth;
			if (num2 >= 500)
			{
				body.SplitterDistance = Math.Clamp(body.Width - 420, 500, num2);
				body.Panel1MinSize = 500;
				body.Panel2MinSize = 390;
			}
			body.PerformLayout();
			root.PerformLayout();
			root.Invalidate(invalidateChildren: true);
			UiTheme.QueueStableRepaint(this);
			if (_cardId.Text.Length > 0 && _set == null && !_busy)
			{
				await LocateAsync();
			}
		};
	}

	public async Task PreviewCardAsync(string cardId)
	{
		if (string.IsNullOrWhiteSpace(cardId) || !cardId.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("卡号必须是纯数字。", "cardId");
		}
		_cardLookupDebounce.Stop();
		if (!_busy || !string.Equals(CardQuery, cardId, StringComparison.Ordinal))
		{
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
	}

	private void ScheduleAutomaticCardLookup()
	{
		_cardLookupDebounce.Stop();
		string text = _cardId.Text.Trim();
		if (text.Length > 0 && text.All(char.IsAsciiDigit))
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
		foreach (KeyValuePair<Control, string> localizedControl in _localizedControls)
		{
			localizedControl.Deconstruct(out var key, out var value);
			Control control = key;
			string id = value;
			control.Text = Localizer.T(id);
		}
		_cardId.PlaceholderText = Localizer.T("animation.search.placeholder");
		bool num = _frameEdge.SelectedIndex == 0;
		_frameEdge.Items[0] = Localizer.T("animation.quality.auto");
		if (num)
		{
			_frameEdge.SelectedIndex = 0;
		}
		_play.Text = Localizer.T(_playing ? "animation.action.pause" : "animation.action.play");
		RefreshLocalizedStatuses();
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		ApplyLanguage();
	}

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
		if (_resourceStatusFactory != null)
		{
			_resourceStatus.Text = _resourceStatusFactory();
		}
		if (_sourceStatusFactory != null)
		{
			_sourceStatus.Text = _sourceStatusFactory();
		}
		if (_previewStatusFactory != null)
		{
			_preview.StatusText = _previewStatusFactory();
		}
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
		string text = _cardId.Text.Trim();
		CardCatalogEntry card = null;
		if (text.Length > 0 && text.All(char.IsAsciiDigit) && int.TryParse(text, out var result))
		{
			card = _catalog.FindCardOrMrk(result);
		}
		else if (text.Length > 0)
		{
			card = _catalog.Search(text, 1).FirstOrDefault();
		}
		string cardId = card?.CardId.ToString() ?? text;
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
			SetBusy(true, "animation.status.locating");
			_set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, cardId));
			_previewSet = _set;
			_legacyCreation = await Task.Run(() => _service.HasCreationTransaction(_gameRoot, cardId));
			if (!_set.IsComplete && (card?.IsMonster ?? false))
			{
				_previewSet = (await Task.Run(() => MonsterAnimationIndexService.FindEquivalentPreview(_gameRoot, card, _catalog))) ?? _set;
			}
			bool isComplete = _set.IsComplete;
			bool equivalentPreview = _previewSet.IsComplete && !string.Equals(_previewSet.CardId, _set.CardId, StringComparison.Ordinal);
			Func<string> cardSuffix = () => (!(card == null)) ? $" · {card.Name(Localizer.Language)} · 卡号 {card.CardId}" : "";
			if (!_previewSet.IsComplete)
			{
				_locatedResourceStatusFactory = (_legacyCreation ? ((Func<string>)(() => Localizer.F("animation.status.located.legacy", cardSuffix()))) : ((Func<string>)delegate
				{
					CardCatalogEntry cardCatalogEntry = card;
					return Localizer.F(((object)cardCatalogEntry != null && cardCatalogEntry.IsMonster) ? "animation.status.located.unsupported" : "animation.status.located.none", cardSuffix());
				}));
				SetResourceStatus(_locatedResourceStatusFactory);
			}
			_resourceStatus.ForeColor = (_previewSet.IsComplete ? UiTheme.Primary : Color.OrangeRed);
			_chooseMedia.Enabled = isComplete;
			_chooseDonor.Enabled = isComplete;
			_apply.Enabled = isComplete && _media != null;
			_restore.Enabled = _set.IsComplete || _legacyCreation;
			if (_previewSet.IsComplete)
			{
				MonsterAnimationTemplate monsterAnimationTemplate = await Task.Run(() => _service.ReadTemplate(_gameRoot, _previewSet));
				_updatingAnimationSelector = true;
				try
				{
					string text2 = _animationSelector.SelectedItem as string;
					_animationSelector.Items.Clear();
					_animationSelector.Items.AddRange(monsterAnimationTemplate.EffectiveAnimationNames.Cast<object>().ToArray());
					_animationSelector.SelectedItem = (monsterAnimationTemplate.EffectiveAnimationNames.Contains<string>(text2 ?? "", StringComparer.Ordinal) ? text2 : monsterAnimationTemplate.EffectiveAnimationNames[0]);
				}
				finally
				{
					_updatingAnimationSelector = false;
				}
				string animationNames = string.Join(" / ", monsterAnimationTemplate.EffectiveAnimationNames);
				_locatedResourceStatusFactory = (equivalentPreview ? ((Func<string>)(() => Localizer.F("animation.status.located.fallback", _previewSet.CardId, cardSuffix(), animationNames))) : (_legacyCreation ? ((Func<string>)(() => Localizer.F("animation.status.located.legacy", cardSuffix()))) : ((Func<string>)(() => Localizer.F("animation.status.located.complete", _set.CountSummary, cardSuffix(), animationNames)))));
				SetResourceStatus(_locatedResourceStatusFactory);
				if (_media == null)
				{
					await LoadCurrentAnimationPreviewAsync(_previewSet);
				}
			}
			else if (_media == null)
			{
				DisposeMedia();
				SetSourceStatus((card?.IsMonster ?? false) ? "animation.source.unsupported" : "animation.source.notapplicable");
				SetPreviewStatus((card?.IsMonster ?? false) ? "animation.preview.unsupported" : "animation.preview.notapplicable");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("animation.error.locate"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(false, null);
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
			SetBusy(true, "animation.status.scanning");
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
			SetBusy(false, null);
		}
	}

	private async Task ChooseMediaAsync()
	{
		MonsterAnimationSet? set = _set;
		if (set == null || !set.IsComplete)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.officialonly"), Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
				string[] fileNames = dialog.FileNames;
				await LoadMediaAsync(fileNames.All(IsImageFile) ? fileNames : new string[1] { fileNames[0] });
			}
		}
		finally
		{
			((IDisposable)dialog)?.Dispose();
		}
	}

	private Task LoadMediaAsync(string path)
	{
		return LoadMediaAsync(new _003C_003Ez__ReadOnlySingleElementList<string>(path));
	}

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
				SetBusy(true, "animation.status.probing");
				using ExtractedAnimation extractedAnimation = await ExtractSourceAsync(paths, 128, removeGreenScreen: false);
				using Bitmap bitmap = extractedAnimation.LoadFrame(0);
				resolvedFrameEdge = MonsterAnimationBuilder.ChooseAutomaticFrameEdge(extractedAnimation.FramePaths.Count, bitmap.Width, bitmap.Height, int.Parse(_atlasEdge.Text));
				SetBusy(true, "animation.status.extractauto", extractedAnimation.FramePaths.Count, resolvedFrameEdge);
			}
			else
			{
				resolvedFrameEdge = int.Parse(_frameEdge.Text);
				SetBusy(true, "animation.status.extractfixed", resolvedFrameEdge);
			}
			loadedMedia = await ExtractSourceAsync(paths, resolvedFrameEdge, removeGreenScreen);
			using (Bitmap bitmap2 = loadedMedia.LoadFrame(0))
			{
				mediaWidth = bitmap2.Width;
				mediaHeight = bitmap2.Height;
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
			_apply.Enabled = _set?.IsComplete ?? false;
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
			SetBusy(false, null);
		}
	}

	private Task<ExtractedAnimation> ExtractSourceAsync(IReadOnlyList<string> paths, int maxFrameEdge, bool removeGreenScreen)
	{
		if (!paths.All(IsImageFile))
		{
			return MonsterAnimationMedia.ExtractAsync(paths[0], (int)_fps.Value, (int)_maxFrames.Value, maxFrameEdge, (double)_startSeconds.Value, removeGreenScreen);
		}
		return MonsterAnimationMedia.ExtractSequenceAsync(paths, (int)_fps.Value, (int)_maxFrames.Value, maxFrameEdge, removeGreenScreen);
	}

	private static bool IsImageFile(string path)
	{
		string text = Path.GetExtension(path).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					char c = text[1];
					if (c != 'j')
					{
						if (c != 't')
						{
							if (c == 'w' && text == ".webp")
							{
								goto IL_00d1;
							}
						}
						else if (text == ".tiff")
						{
							goto IL_00d1;
						}
					}
					else if (text == ".jpeg")
					{
						goto IL_00d1;
					}
				}
			}
			else
			{
				char c = text[1];
				if ((uint)c <= 106u)
				{
					if (c != 'b')
					{
						if (c == 'j' && text == ".jpg")
						{
							goto IL_00d1;
						}
					}
					else if (text == ".bmp")
					{
						goto IL_00d1;
					}
				}
				else if (c != 'p')
				{
					if (c == 't' && text == ".tif")
					{
						goto IL_00d1;
					}
				}
				else if (text == ".png")
				{
					goto IL_00d1;
				}
			}
		}
		return false;
		IL_00d1:
		return true;
	}

	private void UpdateSourceStatus()
	{
		if (_media == null)
		{
			SetSourceStatus("animation.source.drop");
			return;
		}
		ExtractedAnimation media = _media;
		SetSourceStatus(delegate
		{
			string text = Localizer.F(_automaticQuality ? "animation.quality.resolved.auto" : "animation.quality.resolved.fixed", _resolvedFrameEdge);
			string text2 = (media.GreenScreenRemoved ? Localizer.T("animation.source.transparent") : "");
			return Localizer.F("animation.source.media", Path.GetFileName(media.SourcePath), media.FramePaths.Count, (int)_fps.Value, (double)media.FramePaths.Count / (double)_fps.Value, _mediaWidth, _mediaHeight, text, text2);
		});
	}

	private async Task LoadCurrentAnimationPreviewAsync(MonsterAnimationSet set)
	{
		int previewVersion = ++_animationPreviewVersion;
		_animationPreviewCancellation?.Cancel();
		_animationPreviewCancellation?.Dispose();
		CancellationTokenSource cancellation = (_animationPreviewCancellation = new CancellationTokenSource());
		CancellationToken cancellationToken = cancellation.Token;
		string animationName = _animationSelector.SelectedItem as string;
		Func<string> resourceBase = _locatedResourceStatusFactory ?? _resourceStatusFactory ?? ((Func<string>)(() => ""));
		SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.loading"));
		int streamedFrames = 0;
		CurrentMonsterAnimationPreview currentMonsterAnimationPreview;
		try
		{
			currentMonsterAnimationPreview = await Task.Run(delegate
			{
				cancellationToken.ThrowIfCancellationRequested();
				return MonsterAnimationCurrentPreview.TryLoad(set) ?? Spine42PreviewRenderer.TryLoad(set, animationName, 24, 120, 512, cancellationToken, ShowRenderedFrame);
			}, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		finally
		{
			if (_animationPreviewCancellation == cancellation)
			{
				_animationPreviewCancellation = null;
			}
			cancellation.Dispose();
		}
		if (previewVersion != _animationPreviewVersion || base.IsDisposed)
		{
			currentMonsterAnimationPreview?.Dispose();
			return;
		}
		if (currentMonsterAnimationPreview == null)
		{
			DisposeMedia();
			SetSourceStatus("animation.source.complex");
			SetPreviewStatus("animation.preview.complex");
			SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.complex"));
			return;
		}
		int framesPerSecond = currentMonsterAnimationPreview.FramesPerSecond;
		string animationName2 = currentMonsterAnimationPreview.AnimationName;
		int scalePercent = currentMonsterAnimationPreview.ScalePercent;
		if (streamedFrames == 0)
		{
			List<Bitmap> collection = currentMonsterAnimationPreview.Frames.ToList();
			currentMonsterAnimationPreview.Frames.Clear();
			currentMonsterAnimationPreview.Dispose();
			DisposeMedia();
			_previewFrames.AddRange(collection);
		}
		else
		{
			currentMonsterAnimationPreview.Dispose();
		}
		_scale.Value = scalePercent;
		SetPreviewRate(framesPerSecond);
		_timeline.Maximum = Math.Max(0, _previewFrames.Count - 1);
		_timeline.Value = 0;
		_timeline.Enabled = _previewFrames.Count > 1;
		SetPreviewStatus(() => "");
		ShowFrame(0);
		SetSourceStatus("animation.source.current", animationName2, _previewFrames.Count, framesPerSecond, (double)_previewFrames.Count / (double)framesPerSecond, scalePercent);
		SetResourceStatus(() => resourceBase() + Localizer.T("animation.status.preview.playing"));
		if (!_playing)
		{
			TogglePlay();
		}
		void ShowRenderedFrame(Bitmap frame, int frameIndex, int frameCount)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			Bitmap display = new Bitmap(frame);
			try
			{
				if (!base.IsDisposed && base.IsHandleCreated)
				{
					Invoke(delegate
					{
						if (display != null && !base.IsDisposed && previewVersion == _animationPreviewVersion && !cancellationToken.IsCancellationRequested)
						{
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
						}
					});
				}
			}
			catch (InvalidOperationException)
			{
			}
			finally
			{
				display?.Dispose();
			}
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
			int num = (_playStartFrame + (int)Math.Floor(_playClock.Elapsed.TotalSeconds * (double)_previewFramesPerSecond)) % _previewFrames.Count;
			if (num != _timeline.Value)
			{
				_updatingTimeline = true;
				_timeline.Value = num;
				_updatingTimeline = false;
				ShowFrame(_timeline.Value);
			}
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
		MonsterAnimationSet set = _set;
		MonsterAnimationSet monsterAnimationSet = set;
		if (monsterAnimationSet == null || !monsterAnimationSet.IsComplete || _media == null)
		{
			MessageBox.Show(this, Localizer.T("animation.prompt.officialonly"), Text);
			return;
		}
		string text = Localizer.T("animation.operation.modify");
		if (!EnsureGameClosed() || MessageBox.Show(this, Localizer.F("animation.confirm.apply.message", set.CardId, text), Localizer.T("animation.confirm.apply.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			SetBusy(true, "animation.status.building");
			await Task.Run(delegate
			{
				AnimationWriteLease.Preflight(set);
			});
			MonsterAnimationTemplate template = await Task.Run(() => _service.ReadTemplate(_gameRoot, set));
			MonsterAnimationBuildResult built = await Task.Run(() => MonsterAnimationBuilder.Build(_media.FramePaths, set.CardId, (int)_fps.Value, (int)_scale.Value, template, int.Parse(_atlasEdge.Text)));
			try
			{
				SetResourceStatus("animation.status.compressing", built.AtlasWidth, built.AtlasHeight);
				await Task.Run(delegate
				{
					_service.Apply(_gameRoot, set, built);
				});
				_resourceStatus.ForeColor = UiTheme.Primary;
				SetResourceStatus("animation.status.completed", built.FrameCount, built.FramesPerSecond, (int)_scale.Value, built.AtlasWidth, built.AtlasHeight);
				string text2 = Localizer.T("animation.message.launch.restart");
				MessageBox.Show(this, Localizer.F("animation.message.completed", text2), Localizer.T("animation.message.completed.title"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
			SetBusy(false, null);
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
			if (!EnsureGameClosed() || MessageBox.Show(this, Localizer.F("animation.confirm.restore.message", _set.CardId), Localizer.T("animation.confirm.restore.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
			{
				return;
			}
			try
			{
				SetBusy(true, "animation.status.restoring");
				int num = await Task.Run(() => _service.Restore(_gameRoot, _set));
				SetResourceStatus((num == 0) ? "animation.status.restore.none" : "animation.status.restore.count", num);
				MessageBox.Show(this, (num == 0) ? Localizer.T("animation.message.restore.none") : Localizer.F("animation.message.restore.count", num), Localizer.T("animation.confirm.restore.title"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				if (num > 0)
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
				SetBusy(false, null);
			}
		}
	}

	private async Task ChooseDonorAsync()
	{
		MonsterAnimationSet target = _set;
		if (_busy)
		{
			return;
		}
		MonsterAnimationSet monsterAnimationSet = target;
		if (monsterAnimationSet == null || !monsterAnimationSet.IsComplete)
		{
			return;
		}
		try
		{
			SetBusy(true, null);
			IReadOnlyList<string> source = await Task.Run(() => MonsterAnimationIndexService.FindInstalledCardIds(_gameRoot));
			using AnimationDonorPicker picker = new AnimationDonorPicker(_gameRoot, source.Where((string x) => x != target.CardId));
			if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedCardId == null)
			{
				return;
			}
			string donorId = picker.SelectedCardId;
			if (!EnsureGameClosed() || MessageBox.Show(this, $"使用卡号 {donorId} 的完整动画替换卡号 {target.CardId}？\n\n会一起替换 HD/SD 图集、骨骼和时间线，保留目标卡的触发动画名称。原文件会首次备份，可用“还原”恢复。\n仅修改本地资源；不为无原生动画卡增加触发。", "确认替换动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
			{
				return;
			}
			MonsterAnimationSet donor = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, donorId));
			int value = await Task.Run(() => MonsterAnimationTransferService.Replace(_gameRoot, target, donor));
			DisposeMedia();
			MessageBox.Show(this, $"已替换 {value} 个动画 Bundle。请重新启动游戏查看。", "动画替换完成");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "动画替换失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(false, null);
		}
		await LocateAsync();
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
		bool flag = !busy && (_set?.IsComplete ?? false);
		_chooseMedia.Enabled = flag;
		_chooseDonor.Enabled = flag;
		_apply.Enabled = flag && _media != null;
		Button restore = _restore;
		int enabled;
		if (!busy)
		{
			MonsterAnimationSet? set = _set;
			enabled = (((set != null && set.IsComplete) || _legacyCreation) ? 1 : 0);
		}
		else
		{
			enabled = 0;
		}
		restore.Enabled = (byte)enabled != 0;
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
