using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class MainForm : Form
{
	private sealed record LanguageChoice(AppLanguage Language, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private sealed record SearchSuggestion(CardCatalogEntry? Entry, string Primary, string Secondary)
	{
		public override string ToString()
		{
			return Primary;
		}
	}

	private sealed record VisualShortcut(string Key, string ResourceId, string[] Categories, RoundedButton Button);

	private enum CategoryFilterKind
	{
		All,
		Animation,
		Category
	}

	private sealed record CategoryFilter(CategoryFilterKind Kind, string Key, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private const string ModGroupKey = "__mods__";

	private const string AnimationGroupKey = "本地卡图|有怪兽动画";

	private readonly ModEngine _engine = new ModEngine();

	private readonly OverFrameService _overFrames = new OverFrameService();

	private readonly ModPackageService _mods = new ModPackageService();

	private readonly TextBox _gameFolder = new TextBox
	{
		ReadOnly = true,
		Dock = DockStyle.Fill
	};

	private readonly ImeAwareTextBox _search = new ImeAwareTextBox
	{
		PlaceholderText = "搜索卡号、贴图名、分类或 Bundle…",
		Dock = DockStyle.Fill
	};

	private readonly ListBox _searchSuggestions = new ListBox
	{
		BorderStyle = BorderStyle.None,
		DrawMode = DrawMode.OwnerDrawFixed,
		ItemHeight = 52,
		IntegralHeight = false,
		TabStop = false
	};

	private readonly Panel _searchSuggestionPopup = new Panel
	{
		BackColor = UiTheme.Elevated,
		Padding = new Padding(1),
		Visible = false,
		TabStop = false
	};

	private readonly ModernComboBox _category = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly TreeView _groups = new TreeView
	{
		Dock = DockStyle.Fill,
		HideSelection = false
	};

	private readonly BufferedListView _list = new BufferedListView
	{
		Dock = DockStyle.Fill,
		View = View.Details,
		VirtualMode = true,
		FullRowSelect = true,
		MultiSelect = false,
		HideSelection = false,
		AllowDrop = true
	};

	private readonly PictureBox _preview = new AlphaPreviewBox
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = UiTheme.Surface
	};

	private readonly Label _previewHint = new Label
	{
		Dock = DockStyle.Fill,
		Text = "SELECT A RESOURCE\n\n选择一张卡图查看预览",
		TextAlign = ContentAlignment.MiddleCenter,
		ForeColor = UiTheme.Muted,
		BackColor = UiTheme.Surface
	};

	private readonly Label _resultCount = new Label
	{
		Dock = DockStyle.Right,
		AutoSize = false,
		Width = 160,
		TextAlign = ContentAlignment.MiddleRight,
		ForeColor = UiTheme.Muted,
		Padding = new Padding(0, 0, 12, 0)
	};

	private readonly Label _info = new Label
	{
		Dock = DockStyle.Fill,
		Padding = new Padding(14, 10, 14, 8),
		ForeColor = UiTheme.Text,
		BackColor = UiTheme.SurfaceAlt
	};

	private readonly Label _modSummary = new Label
	{
		Dock = DockStyle.Fill,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Muted,
		Padding = new Padding(8, 0, 8, 0)
	};

	private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel
	{
		Text = "选择游戏目录后扫描；首次扫描会建立缓存。"
	};

	private readonly List<TexRef> _textures = new List<TexRef>();

	private readonly List<TexRef> _visibleTextures = new List<TexRef>();

	private Button _modsOnlyButton;

	private Button _scanMissingButton;

	private Button _visualAssetsButton;

	private readonly ModernComboBox _profileSelector = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly ModernComboBox _languageSelector = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly Panel _pageHost = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Label _pageTitle = new Label
	{
		Dock = DockStyle.Top,
		Height = 31,
		ForeColor = UiTheme.Text,
		Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly Label _pageDescription = new Label
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Muted,
		Font = new Font("Microsoft YaHei UI", 9f),
		TextAlign = ContentAlignment.TopLeft
	};

	private readonly Label _catalogBadge = new Label
	{
		Dock = DockStyle.Right,
		Width = 220,
		ForeColor = UiTheme.Primary,
		Font = new Font("Segoe UI Semibold", 9f),
		TextAlign = ContentAlignment.MiddleRight
	};

	private readonly TableLayoutPanel _visualShortcutBar = new TableLayoutPanel
	{
		Dock = DockStyle.Fill,
		ColumnCount = 8,
		RowCount = 1,
		BackColor = UiTheme.SurfaceAlt,
		Padding = new Padding(12, 7, 12, 7),
		Margin = Padding.Empty
	};

	private readonly TableLayoutPanel _resourceActionGrid = new TableLayoutPanel();

	private Control _cardSearchField;

	private readonly Panel _resourceContextBar = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.SurfaceAlt,
		Visible = false,
		Margin = Padding.Empty
	};

	private readonly FlowLayoutPanel _modContextActions = new FlowLayoutPanel
	{
		Dock = DockStyle.Fill,
		FlowDirection = FlowDirection.LeftToRight,
		WrapContents = false,
		Padding = new Padding(14, 5, 14, 5),
		BackColor = UiTheme.SurfaceAlt,
		Visible = false
	};

	private readonly FlowLayoutPanel _overFrameContextActions = new FlowLayoutPanel
	{
		Dock = DockStyle.Fill,
		FlowDirection = FlowDirection.LeftToRight,
		WrapContents = false,
		Padding = new Padding(14, 5, 14, 5),
		BackColor = UiTheme.SurfaceAlt,
		Visible = false
	};

	private readonly Panel _resourcePage = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _animationPage = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _framesPage = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _modsPage = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _settingsPage = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _animationHost = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private readonly Panel _framesHost = new Panel
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.Window
	};

	private TableLayoutPanel? _rootLayout;

	private readonly List<NavigationButton> _navigationButtons = new List<NavigationButton>();

	private bool _animationWorkspaceQueued;

	private bool _framesWorkspaceQueued;

	private readonly List<VisualShortcut> _visualShortcuts = new List<VisualShortcut>();

	private readonly Dictionary<Control, string> _localizedControls = new Dictionary<Control, string>();

	private readonly AppSettings _settings = AppSettingsStore.Load();

	private CardCatalogService _cardCatalog = CardCatalogService.LoadBestAvailable();

	private readonly Label _modPageSummary = new Label
	{
		Dock = DockStyle.Top,
		Height = 80,
		Padding = new Padding(20),
		ForeColor = UiTheme.Text,
		BackColor = UiTheme.SurfaceAlt
	};

	private SplitContainer? _resourceSplit;

	private SplitContainer? _workspaceSplit;

	private MonsterAnimationForm? _embeddedAnimation;

	private OverFrameForm? _embeddedFrames;

	private OverFrameForm? _overFrameManager;

	private bool _overFrameManagerQueued;

	private int _overFrameManagerGeneration;

	private WorkspacePage _currentPage;

	private bool _changingProfile;

	private bool _changingLanguage;

	private CancellationTokenSource? _previewCancellation;

	private CancellationTokenSource? _backgroundRefreshCancellation;

	private Task? _backgroundRefreshTask;

	private readonly System.Windows.Forms.Timer _searchDebounce = new System.Windows.Forms.Timer
	{
		Interval = 180
	};

	private readonly System.Windows.Forms.Timer _layoutSurfaceReset = new System.Windows.Forms.Timer
	{
		Interval = 90
	};

	private bool _resettingLayoutSurface;

	private string? _gameRoot;

	private string? _assetRoot;

	private string? _streamingRoot;

	private bool _modsOnly;

	private GameIndex? _index;

	private int _changedModBundleCount;

	private int _changedAnimationBundleCount;

	private int _previewGeneration;

	private bool _suppressSearchSuggestions;

	private int _searchSuggestionHeight;

	private bool _changingCategory;

	private string? _activeVisualShortcut;

	private RowStyle? _visualShortcutRow;

	private RowStyle? _resourceContextRow;

	private Image? _brandImage;

	private Icon? _windowIcon;

	private readonly bool _allowBackgroundRefresh;

	public MainForm()
		: this(null)
	{
	}

	internal MainForm(Action<MainForm>? beforeDpiInitialization, string? resourceRoot = null, bool backgroundRefresh = true)
	{
		_allowBackgroundRefresh = backgroundRefresh;
		SuspendLayout();
		base.AutoScaleMode = AutoScaleMode.None;
		Localizer.SetLanguage(_settings.Language);
		MotionPreferences.UserReducesMotion = _settings.ReduceMotion;
		UiTheme.ApplyDarkTitleBar(this);
		Text = Localizer.T("app.title");
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(1240, 780);
		base.Size = new Size(1520, 920);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		base.KeyPreview = true;
		DoubleBuffered = true;
		_brandImage = LoadBrandImage();
		_windowIcon = LoadWindowIcon();
		if (_windowIcon != null)
		{
			base.Icon = _windowIcon;
			base.ShowIcon = true;
		}
		UiTheme.StyleTextBox(_gameFolder);
		UiTheme.StyleTextBox(_search);
		UiTheme.StyleComboBox(_category);
		UiTheme.StyleComboBox(_profileSelector);
		UiTheme.StyleComboBox(_languageSelector);
		UiTheme.StyleTree(_groups);
		UiTheme.StyleList(_list);
		_list.RetrieveVirtualItem += delegate(object? _, RetrieveVirtualItemEventArgs e)
		{
			e.Item = ((e.ItemIndex >= 0 && e.ItemIndex < _visibleTextures.Count) ? CreateListItem(_visibleTextures[e.ItemIndex]) : new ListViewItem(""));
		};
		_list.SearchForVirtualItem += delegate(object? _, SearchForVirtualItemEventArgs e)
		{
			if (_visibleTextures.Count != 0 && !string.IsNullOrWhiteSpace(e.Text))
			{
				int num = Math.Clamp(e.StartIndex, 0, _visibleTextures.Count - 1);
				for (int i = 0; i < _visibleTextures.Count; i++)
				{
					int index = (num + i) % _visibleTextures.Count;
					if (DisplayResourceName(_visibleTextures[index]).StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase))
					{
						e.Index = index;
						break;
					}
				}
			}
		};
		ConfigureSearchSuggestions();
		_list.Columns.Add("资源名称", 205);
		_list.Columns.Add("来源 / 分类", 190);
		_list.Columns.Add("尺寸", 100);
		_list.Columns.Add("Bundle 路径", 360);
		_list.Resize += delegate
		{
			ResizeResourceColumns();
		};
		_list.SelectedIndexChanged += async delegate
		{
			await ShowSelectionAsync();
		};
		_list.DoubleClick += async delegate
		{
			if (!IsAnimationFilterSelected() || !(Selected()?.HasMonsterAnimation ?? false))
			{
				await ReplaceSelectedAsync();
			}
			else
			{
				OpenRawAnimationAssets();
			}
		};
		_list.ItemDrag += async delegate(object? _, ItemDragEventArgs e)
		{
			await DragOutAsync(e.Item as ListViewItem);
		};
		_list.DragEnter += OnDragEnter;
		_list.DragDrop += async delegate(object? _, DragEventArgs e)
		{
			await OnDragDropAsync(e);
		};
		_searchDebounce.Tick += delegate
		{
			_searchDebounce.Stop();
			RefreshSearchResults();
		};
		_search.TextChanged += delegate
		{
			if (!_search.IsImeComposing)
			{
				ScheduleSearchRefresh();
			}
		};
		_search.ImeCompositionStarted += delegate
		{
			_searchDebounce.Stop();
			HideSearchSuggestions();
		};
		_search.ImeCompositionEnded += delegate
		{
			ScheduleSearchRefresh();
		};
		_search.KeyDown += async delegate(object? _, KeyEventArgs e)
		{
			Keys keyCode = e.KeyCode;
			bool flag = ((keyCode == Keys.Up || keyCode == Keys.Down) ? true : false);
			if (flag && _searchSuggestionPopup.Visible && _searchSuggestions.Items.Count > 0)
			{
				int num = ((e.KeyCode == Keys.Down) ? 1 : (-1));
				_searchSuggestions.SelectedIndex = Math.Clamp(_searchSuggestions.SelectedIndex + num, 0, _searchSuggestions.Items.Count - 1);
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			else if (e.KeyCode == Keys.Escape && _searchSuggestionPopup.Visible)
			{
				HideSearchSuggestions();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			else if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				CardCatalogEntry preferred = (_searchSuggestions.SelectedItem as SearchSuggestion)?.Entry;
				await ScanMissingCardAsync(preferred);
			}
		};
		_category.Items.Add(new CategoryFilter(CategoryFilterKind.All, "", Localizer.T("filter.all")));
		_category.SelectedIndex = 0;
		_category.SelectedIndexChanged += delegate
		{
			if (!_changingCategory)
			{
				_activeVisualShortcut = null;
				UpdateVisualShortcutStyles();
				RenderList();
			}
		};
		_groups.AfterSelect += delegate(object? _, TreeViewEventArgs e)
		{
			SelectGroup(e.Node?.Tag as string);
		};
		BuildInterface();
		_layoutSurfaceReset.Tick += delegate
		{
			ResetLayoutSurface();
		};
		base.DpiChanged += delegate
		{
			UiTheme.QueueStableRepaint(this);
			QueueLayoutSurfaceReset();
		};
		_profileSelector.SelectedIndexChanged += async delegate
		{
			if (!_changingProfile && _profileSelector.SelectedItem is LocalDataProfile localDataProfile && _gameRoot != null)
			{
				_backgroundRefreshCancellation?.Cancel();
				IndexService.SetPreferredLocalRoot(_gameRoot, localDataProfile.RootPath);
				_settings.LastProfileByGame[Path.GetFullPath(_gameRoot)] = localDataProfile.AccountId;
				AppSettingsStore.Save(_settings);
				SetGameRoot();
				ResetEmbeddedWorkspaces();
				await ScanAsync();
			}
		};
		_languageSelector.SelectedIndexChanged += delegate
		{
			if (!_changingLanguage && _languageSelector.SelectedItem is LanguageChoice languageChoice)
			{
				_settings.Language = languageChoice.Language;
				AppSettingsStore.Save(_settings);
				Localizer.SetLanguage(languageChoice.Language);
				ApplyLanguage();
				RenderList();
			}
		};
		Localizer.LanguageChanged += OnLanguageChanged;
		base.FormClosed += delegate
		{
			Localizer.LanguageChanged -= OnLanguageChanged;
			_overFrameManager?.Close();
			_overFrameManager?.Dispose();
			_overFrameManager = null;
			_previewCancellation?.Cancel();
			_previewCancellation?.Dispose();
			_backgroundRefreshCancellation?.Cancel();
			_backgroundRefreshCancellation?.Dispose();
			_searchDebounce.Stop();
			_searchDebounce.Dispose();
			_layoutSurfaceReset.Stop();
			_layoutSurfaceReset.Dispose();
			_brandImage?.Dispose();
			_windowIcon?.Dispose();
		};
		ApplyLanguage();
		GameInstallation gameInstallation = ((resourceRoot != null) ? SteamGameDiscovery.FromPath(resourceRoot) : (ResourceSource.IsMobile(_settings.LastGameRoot) ? ResourceSource.Mobile(_settings.LastGameRoot) : SteamGameDiscovery.Discover(_settings.LastGameRoot).FirstOrDefault()));
		if (gameInstallation != null)
		{
			ApplyInstallation(gameInstallation);
		}
		base.Shown += async delegate
		{
			SplitContainer resourceSplit = _resourceSplit;
			if (resourceSplit != null && resourceSplit.Width > 650)
			{
				_resourceSplit.SplitterDistance = 235;
			}
			resourceSplit = _workspaceSplit;
			if (resourceSplit != null && resourceSplit.Width > 1150)
			{
				_workspaceSplit.SplitterDistance = Math.Min((int)((double)_workspaceSplit.Width * 0.66), _workspaceSplit.Width - 410);
			}
			UiTheme.QueueStableRepaint(this);
			QueueLayoutSurfaceReset();
			if (_assetRoot != null)
			{
				await ScanAsync();
			}
		};
		beforeDpiInitialization?.Invoke(this);
		DpiLayout.Initialize(this);
		ResumeLayout(performLayout: true);
	}

	private void BuildInterface()
	{
		Button control = Bind(Button("", async delegate
		{
			await ChooseGameAsync();
		}), "top.choose");
		Button control2 = Bind(Button("", async delegate
		{
			await RebuildIndexAsync();
		}), "action.rebuild");
		Button button = Bind(Button("", async delegate
		{
			await ReplaceSelectedAsync();
		}, ButtonTone.Primary), "action.replace");
		Button button2 = Bind(Button("", async delegate
		{
			await ExportSelectedAsync();
		}), "action.export");
		Button button3 = Bind(Button("", delegate
		{
			OpenBackup();
		}), "action.backup");
		Button button4 = Bind(Button("", async delegate
		{
			await RestoreSelectedAsync();
		}, ButtonTone.Danger), "action.restore");
		Button button5 = Bind(Button("", async delegate
		{
			await InspectSelectedAsync();
		}), "action.inspect");
		Button button6 = Bind(Button("", async delegate
		{
			await OpenCardFrameStudioAsync();
		}, ButtonTone.Gold), "action.frame.studio");
		Button button7 = Bind(Button("", async delegate
		{
			await PreviewSelectedAnimationAsync();
		}, ButtonTone.Primary), "action.animation.preview");
		_visualAssetsButton = Bind(Button("", async delegate
		{
			await LoadVisualAssetsAsync();
		}, ButtonTone.Gold), "action.visuals");
		_modsOnlyButton = Bind(Button("", delegate
		{
			ToggleModsOnly();
		}, ButtonTone.Gold), "action.mods.only");
		_scanMissingButton = Bind(Button("", async delegate
		{
			await ScanMissingCardAsync();
		}), "action.locate");
		Button exportMods = Bind(Button("", async delegate
		{
			await ExportAllModsAsync();
		}, ButtonTone.Primary), "action.mods.export");
		Button importMods = Bind(Button("", async delegate
		{
			await ImportModsAsync();
		}), "action.mods.import");
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 8,
			RowCount = 1,
			Margin = Padding.Empty
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(Bind(Caption(""), "top.game"), 0, 0);
		tableLayoutPanel.Controls.Add(UiTheme.Field(_gameFolder), 1, 0);
		tableLayoutPanel.Controls.Add(control, 2, 0);
		tableLayoutPanel.Controls.Add(Bind(Caption(""), "top.account"), 3, 0);
		tableLayoutPanel.Controls.Add(UiTheme.Field(_profileSelector), 4, 0);
		tableLayoutPanel.Controls.Add(Bind(Caption(""), "top.language"), 5, 0);
		tableLayoutPanel.Controls.Add(UiTheme.Field(_languageSelector), 6, 0);
		tableLayoutPanel.Controls.Add(control2, 7, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Padding = new Padding(16, 10, 16, 10),
			BackColor = UiTheme.Surface,
			ColumnCount = 1,
			RowCount = 1
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(tableLayoutPanel, 0, 0);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 7, 14, 7),
			BackColor = UiTheme.SurfaceAlt,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236f));
		_cardSearchField = UiTheme.Field(_search);
		tableLayoutPanel3.Controls.Add(_cardSearchField, 0, 0);
		tableLayoutPanel3.Controls.Add(UiTheme.Field(_category), 1, 0);
		BuildVisualShortcutBar();
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Margin = new Padding(0, 0, 8, 0)
		};
		borderPanel.Controls.Add(_groups);
		borderPanel.Controls.Add(SectionHeading("资源分类", "RESOURCE GROUPS"));
		BorderPanel borderPanel2 = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Margin = new Padding(8, 0, 0, 0)
		};
		Panel panel = SectionHeading("图片资源", "拖入替换 · 拖出导出");
		panel.Controls.Add(_resultCount);
		_resultCount.BringToFront();
		borderPanel2.Controls.Add(_list);
		borderPanel2.Controls.Add(panel);
		_resourceSplit = new SplitContainer
		{
			Dock = DockStyle.Fill,
			FixedPanel = FixedPanel.Panel1,
			SplitterDistance = 235,
			SplitterWidth = 8,
			IsSplitterFixed = false,
			BackColor = UiTheme.Window
		};
		_resourceSplit.Panel1.Controls.Add(borderPanel);
		_resourceSplit.Panel2.Controls.Add(borderPanel2);
		BorderPanel borderPanel3 = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		borderPanel3.Controls.Add(_preview);
		borderPanel3.Controls.Add(_previewHint);
		_previewHint.BringToFront();
		_resourceActionGrid.Dock = DockStyle.Fill;
		_resourceActionGrid.ColumnCount = 2;
		_resourceActionGrid.RowCount = 4;
		_resourceActionGrid.AutoScroll = false;
		_resourceActionGrid.BackColor = UiTheme.Surface;
		_resourceActionGrid.Padding = new Padding(5, 4, 5, 4);
		_resourceActionGrid.Margin = Padding.Empty;
		_resourceActionGrid.ColumnStyles.Clear();
		_resourceActionGrid.RowStyles.Clear();
		_resourceActionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		_resourceActionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		for (int num = 0; num < 4; num++)
		{
			_resourceActionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		}
		Button[] array = new Button[8] { button, button2, button4, button3, _scanMissingButton, button5, button6, button7 };
		for (int num2 = 0; num2 < array.Length; num2++)
		{
			Button button8 = array[num2];
			button8.AutoSize = false;
			button8.Dock = DockStyle.Fill;
			button8.MinimumSize = Size.Empty;
			button8.Margin = new Padding(4, 3, 4, 3);
			_resourceActionGrid.Controls.Add(button8, num2 % 2, num2 / 2);
		}
		TableLayoutPanel previewLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Window,
			RowCount = 4,
			ColumnCount = 1
		};
		previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
		previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));
		previewLayout.Controls.Add(SectionHeading("实时预览", "LIVE PREVIEW"), 0, 0);
		previewLayout.Controls.Add(borderPanel3, 0, 1);
		previewLayout.Controls.Add(_info, 0, 2);
		previewLayout.Controls.Add(_resourceActionGrid, 0, 3);
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 14, 7, 14),
			BackColor = UiTheme.Window
		};
		panel2.Controls.Add(_resourceSplit);
		Panel panel3 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(7, 14, 14, 14),
			BackColor = UiTheme.Window
		};
		DarkScrollPanel previewScroll = new DarkScrollPanel
		{
			Name = "ResourcePreviewScroll",
			UseExplicitContentHeight = true,
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Window
		};
		previewScroll.ContentPanel.Controls.Add(previewLayout);
		previewScroll.SizeChanged += delegate
		{
			ResizePreviewContent();
		};
		previewScroll.DpiChangedAfterParent += delegate
		{
			ResizePreviewContent();
		};
		previewLayout.Layout += delegate
		{
			ResizePreviewContent();
		};
		panel3.Controls.Add(previewScroll);
		ResizePreviewContent();
		_workspaceSplit = new SplitContainer
		{
			Dock = DockStyle.Fill,
			SplitterDistance = 900,
			SplitterWidth = 6,
			BackColor = UiTheme.Border
		};
		_workspaceSplit.Panel1.Controls.Add(panel2);
		_workspaceSplit.Panel2.Controls.Add(panel3);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = UiTheme.Window
		};
		_visualShortcutRow = new RowStyle(SizeType.Absolute, 0f);
		_resourceContextRow = new RowStyle(SizeType.Absolute, 0f);
		tableLayoutPanel4.RowStyles.Add(_visualShortcutRow);
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		tableLayoutPanel4.RowStyles.Add(_resourceContextRow);
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.Controls.Add(_visualShortcutBar, 0, 0);
		tableLayoutPanel4.Controls.Add(tableLayoutPanel3, 0, 1);
		tableLayoutPanel4.Controls.Add(_resourceContextBar, 0, 2);
		tableLayoutPanel4.Controls.Add(_workspaceSplit, 0, 3);
		_resourcePage.Controls.Add(tableLayoutPanel4);
		BuildAnimationPage();
		BuildResourceContextBar(exportMods, importMods);
		BuildSettingsPage();
		_pageHost.Controls.AddRange(new Control[3] { _settingsPage, _animationPage, _resourcePage });
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 5,
			Padding = new Padding(8, 10, 8, 10),
			BackColor = UiTheme.Surface,
			AutoScroll = true
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int num3 = 0; num3 < 4; num3++)
		{
			tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		}
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		AddNavigation(tableLayoutPanel5, WorkspacePage.Cards, "nav.cards");
		AddNavigation(tableLayoutPanel5, WorkspacePage.Visuals, "nav.visuals");
		AddNavigation(tableLayoutPanel5, WorkspacePage.Animation, "nav.animation");
		AddNavigation(tableLayoutPanel5, WorkspacePage.Settings, "nav.settings");
		PictureBox control3 = new PictureBox
		{
			Dock = DockStyle.Fill,
			Image = _brandImage,
			SizeMode = PictureBoxSizeMode.Zoom,
			Margin = new Padding(0, 4, 12, 4),
			BackColor = Color.Transparent
		};
		Label control4 = new Label
		{
			Text = "MD STUDIO",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI Semibold", 11f),
			AutoEllipsis = true,
			ForeColor = UiTheme.Text,
			TextAlign = ContentAlignment.BottomLeft
		};
		Label control5 = new Label
		{
			Text = "MASTER DUEL\nMOD WORKSPACE",
			AutoEllipsis = true,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 7.5f),
			ForeColor = UiTheme.Primary,
			TextAlign = ContentAlignment.TopLeft
		};
		TableLayoutPanel tableLayoutPanel6 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1,
			Margin = Padding.Empty
		};
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));
		tableLayoutPanel6.Controls.Add(control4, 0, 0);
		tableLayoutPanel6.Controls.Add(control5, 0, 1);
		TableLayoutPanel tableLayoutPanel7 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(14, 13, 12, 10),
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel7.Controls.Add(control3, 0, 0);
		tableLayoutPanel7.Controls.Add(tableLayoutPanel6, 1, 0);
		Label control6 = new Label
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(18, 10, 14, 10),
			ForeColor = UiTheme.Muted,
			BackColor = UiTheme.Surface,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = $"v2.0.16  ·  ASTELLAR CATALOG\n{_cardCatalog.Count:N0} MULTILINGUAL CARDS"
		};
		TableLayoutPanel tableLayoutPanel8 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel8.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
		tableLayoutPanel8.Controls.Add(tableLayoutPanel7, 0, 0);
		tableLayoutPanel8.Controls.Add(tableLayoutPanel5, 0, 1);
		tableLayoutPanel8.Controls.Add(control6, 0, 2);
		TableLayoutPanel tableLayoutPanel9 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Padding = new Padding(22, 8, 20, 6),
			BackColor = UiTheme.Window
		};
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel9.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel9.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel9.Controls.Add(_pageTitle, 0, 0);
		tableLayoutPanel9.Controls.Add(_pageDescription, 0, 1);
		tableLayoutPanel9.Controls.Add(_catalogBadge, 1, 0);
		tableLayoutPanel9.SetRowSpan(_catalogBadge, 2);
		TableLayoutPanel tableLayoutPanel10 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = UiTheme.Window
		};
		tableLayoutPanel10.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		tableLayoutPanel10.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
		tableLayoutPanel10.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel10.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel10.Controls.Add(tableLayoutPanel9, 0, 1);
		tableLayoutPanel10.Controls.Add(_pageHost, 0, 2);
		TableLayoutPanel tableLayoutPanel11 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = UiTheme.Window,
			Padding = new Padding(0, 1, 0, 0)
		};
		tableLayoutPanel11.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228f));
		tableLayoutPanel11.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel11.Controls.Add(tableLayoutPanel8, 0, 0);
		tableLayoutPanel11.Controls.Add(tableLayoutPanel10, 1, 0);
		_status.Spring = true;
		_status.TextAlign = ContentAlignment.MiddleLeft;
		_status.ForeColor = UiTheme.Muted;
		_status.Font = new Font("Microsoft YaHei UI", 8.5f);
		StatusStrip statusStrip = new StatusStrip
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			ForeColor = UiTheme.Muted,
			SizingGrip = false,
			Padding = new Padding(12, 0, 12, 0)
		};
		statusStrip.Items.Add(_status);
		TableLayoutPanel tableLayoutPanel12 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Window,
			RowCount = 2,
			ColumnCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		tableLayoutPanel12.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel12.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel12.Controls.Add(tableLayoutPanel11, 0, 0);
		tableLayoutPanel12.Controls.Add(statusStrip, 0, 1);
		_rootLayout = tableLayoutPanel12;
		base.Controls.Add(tableLayoutPanel12);
		base.Controls.Add(_searchSuggestionPopup);
		_searchSuggestionPopup.BringToFront();
		base.SizeChanged += delegate
		{
			if (_searchSuggestionPopup.Visible)
			{
				PositionSearchSuggestions();
			}
			QueueLayoutSurfaceReset();
		};
		ShowPage(WorkspacePage.Cards);
		void ResizePreviewContent()
		{
			int num4 = (int)Math.Ceiling(previewLayout.RowStyles[0].Height + previewLayout.RowStyles[2].Height + previewLayout.RowStyles[3].Height);
			previewScroll.ContentHeight = Math.Max(previewScroll.ClientSize.Height, num4 + (int)Math.Ceiling(320f * previewLayout.RowStyles[0].Height / 46f) + previewLayout.Margin.Vertical);
		}
	}

	private void ConfigureSearchSuggestions()
	{
		_searchSuggestions.Dock = DockStyle.Fill;
		_searchSuggestions.BackColor = UiTheme.Elevated;
		_searchSuggestions.ForeColor = UiTheme.Text;
		_searchSuggestions.DrawItem += delegate(object? _, DrawItemEventArgs e)
		{
			if (e.Index < 0 || e.Index >= _searchSuggestions.Items.Count)
			{
				return;
			}
			SearchSuggestion searchSuggestion = (SearchSuggestion)_searchSuggestions.Items[e.Index];
			bool flag = (e.State & DrawItemState.Selected) != 0;
			using SolidBrush brush = new SolidBrush(flag ? UiTheme.Selection : UiTheme.Elevated);
			e.Graphics.FillRectangle(brush, e.Bounds);
			using Font font = new Font(Font, FontStyle.Bold);
			TextRenderer.DrawText(e.Graphics, searchSuggestion.Primary, font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 6, e.Bounds.Width - 28, 22), flag ? Color.White : UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			TextRenderer.DrawText(e.Graphics, searchSuggestion.Secondary, Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 28, e.Bounds.Width - 28, 18), flag ? Color.FromArgb(220, Color.White) : UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
		};
		_searchSuggestions.MouseUp += async delegate(object? _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && _searchSuggestions.SelectedItem is SearchSuggestion { Entry: not null } searchSuggestion)
			{
				HideSearchSuggestions();
				await ScanMissingCardAsync(searchSuggestion.Entry);
			}
		};
		_searchSuggestionPopup.Controls.Add(_searchSuggestions);
		_search.Leave += delegate
		{
			BeginInvoke(delegate
			{
				if (!_search.ContainsFocus && !_searchSuggestions.ContainsFocus)
				{
					HideSearchSuggestions();
				}
			});
		};
		_searchSuggestions.Leave += delegate
		{
			BeginInvoke(delegate
			{
				if (!_search.ContainsFocus && !_searchSuggestions.ContainsFocus)
				{
					HideSearchSuggestions();
				}
			});
		};
		base.Deactivate += delegate
		{
			HideSearchSuggestions();
		};
	}

	private void ShowSearchSuggestions()
	{
		if (!_search.IsHandleCreated || !_search.Focused)
		{
			return;
		}
		string text = _search.Text.Trim();
		if (text.Length == 0)
		{
			HideSearchSuggestions();
			return;
		}
		IReadOnlyList<CardCatalogEntry> readOnlyList = _cardCatalog.Search(text);
		_searchSuggestions.BeginUpdate();
		_searchSuggestions.Items.Clear();
		foreach (CardCatalogEntry item in readOnlyList)
		{
			string value = item.Name(Localizer.Language);
			string text2 = string.Join(" · ", new string[2] { item.Type, item.SubType }.Where((string value2) => !string.IsNullOrWhiteSpace(value2)));
			_searchSuggestions.Items.Add(new SearchSuggestion(item, $"{item.CardId}  ·  {value}", (text2.Length == 0) ? Localizer.T("search.catalog_match") : text2));
		}
		if (_searchSuggestions.Items.Count == 0)
		{
			_searchSuggestions.Items.Add(new SearchSuggestion(null, Localizer.T("search.no_results"), Localizer.T("search.no_results_hint")));
		}
		_searchSuggestions.SelectedIndex = 0;
		_searchSuggestions.EndUpdate();
		int requestedWidth = Math.Max(420, _search.Parent?.Width ?? _search.Width);
		int searchSuggestionHeight = Math.Min(8, _searchSuggestions.Items.Count) * _searchSuggestions.ItemHeight + 2;
		_searchSuggestionHeight = searchSuggestionHeight;
		PositionSearchSuggestions(requestedWidth);
		_searchSuggestionPopup.Visible = true;
		_searchSuggestionPopup.BringToFront();
	}

	private void PositionSearchSuggestions(int requestedWidth = 0)
	{
		if (_search.IsHandleCreated && !base.IsDisposed)
		{
			Control control = _search.Parent ?? _search;
			Point p = control.PointToScreen(new Point(0, control.Height + 2));
			Point point = PointToClient(p);
			int val = ((requestedWidth > 0) ? requestedWidth : control.Width);
			val = Math.Min(val, Math.Max(1, base.ClientSize.Width - point.X));
			_searchSuggestionPopup.Bounds = new Rectangle(point.X, point.Y, val, Math.Max(1, _searchSuggestionHeight + 2));
		}
	}

	private void HideSearchSuggestions()
	{
		_searchSuggestionPopup.Visible = false;
	}

	private void ScheduleSearchRefresh()
	{
		_searchDebounce.Stop();
		_searchDebounce.Start();
	}

	private void RefreshSearchResults()
	{
		if (!_search.IsImeComposing && !base.IsDisposed)
		{
			RenderList();
			if (!_suppressSearchSuggestions)
			{
				ShowSearchSuggestions();
			}
		}
	}

	private T Bind<T>(T control, string resourceId) where T : Control
	{
		_localizedControls[control] = resourceId;
		control.Text = Localizer.T(resourceId);
		return control;
	}

	private void AddNavigation(TableLayoutPanel navigation, WorkspacePage page, string resourceId)
	{
		NavigationButton navigationButton = Bind(new NavigationButton
		{
			Page = page,
			AutoSize = false,
			Anchor = (AnchorStyles.Left | AnchorStyles.Right),
			Margin = new Padding(4, 3, 4, 3)
		}, resourceId);
		navigationButton.Click += delegate
		{
			ShowPage(page);
		};
		int count = _navigationButtons.Count;
		_navigationButtons.Add(navigationButton);
		navigation.Controls.Add(navigationButton, 0, count);
	}

	private void BuildVisualShortcutBar()
	{
		_visualShortcutBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		float[] array = new float[8] { 1f, 1.05f, 1.18f, 1.12f, 1f, 0.82f, 1.12f, 1.24f };
		foreach (float width in array)
		{
			_visualShortcutBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
		}
		AddShortcut("all", "visual.all", Array.Empty<string>());
		AddShortcut("fields", "visual.fields", new string[1] { "决斗场地" });
		AddShortcut("wallpaper", "visual.wallpaper", new string[2] { "大厅壁纸", "大厅背景" });
		AddShortcut("sleeves", "visual.sleeves", new string[1] { "卡套" });
		AddShortcut("deckcase", "visual.deckcase", new string[1] { "卡盒" });
		AddShortcut("coin", "visual.coin", new string[1] { "硬币" });
		AddShortcut("profile", "visual.profile", new string[2] { "头像", "头像框" });
		_visualAssetsButton.AutoSize = false;
		_visualAssetsButton.Dock = DockStyle.Fill;
		_visualAssetsButton.MinimumSize = Size.Empty;
		_visualAssetsButton.Margin = new Padding(3, 4, 3, 4);
		_visualShortcutBar.Controls.Add(_visualAssetsButton, 7, 0);
	}

	private void AddShortcut(string key, string resourceId, string[] categories)
	{
		RoundedButton roundedButton = (RoundedButton)Button(Localizer.T(resourceId), delegate
		{
			SelectVisualShortcut(key);
		});
		roundedButton.AutoSize = false;
		roundedButton.Dock = DockStyle.Fill;
		roundedButton.MinimumSize = Size.Empty;
		roundedButton.Margin = new Padding(3, 4, 3, 4);
		roundedButton.CornerRadius = 10;
		int count = _visualShortcuts.Count;
		_visualShortcuts.Add(new VisualShortcut(key, resourceId, categories, roundedButton));
		_visualShortcutBar.Controls.Add(roundedButton, count, 0);
	}

	private void SelectVisualShortcut(string key)
	{
		_activeVisualShortcut = key;
		_changingCategory = true;
		try
		{
			SelectAllCategory();
		}
		finally
		{
			_changingCategory = false;
		}
		UpdateVisualShortcutStyles();
		RenderList();
	}

	private bool VisualShortcutMatches(TexRef texture)
	{
		if (_currentPage != WorkspacePage.Visuals || string.IsNullOrWhiteSpace(_activeVisualShortcut))
		{
			return true;
		}
		VisualShortcut visualShortcut = _visualShortcuts.FirstOrDefault((VisualShortcut x) => x.Key == _activeVisualShortcut);
		if (!(visualShortcut == null) && visualShortcut.Categories.Length != 0)
		{
			return visualShortcut.Categories.Contains<string>(texture.Category, StringComparer.Ordinal);
		}
		return true;
	}

	private void UpdateVisualShortcutStyles()
	{
		foreach (VisualShortcut shortcut in _visualShortcuts)
		{
			bool flag = shortcut.Key == _activeVisualShortcut;
			shortcut.Button.NormalColor = (flag ? Color.FromArgb(29, 74, 103) : UiTheme.Elevated);
			shortcut.Button.HoverColor = (flag ? Color.FromArgb(36, 91, 123) : UiTheme.Surface);
			shortcut.Button.BorderColor = (flag ? UiTheme.Primary : UiTheme.Border);
			shortcut.Button.ForeColor = (flag ? Color.White : UiTheme.Muted);
			shortcut.Button.BackColor = shortcut.Button.NormalColor;
			int value = ((shortcut.Categories.Length == 0) ? _textures.Count(IsVisualAsset) : _textures.Count((TexRef x) => IsVisualAsset(x) && shortcut.Categories.Contains<string>(x.Category, StringComparer.Ordinal)));
			shortcut.Button.Text = $"{Localizer.T(shortcut.ResourceId)}  {value:N0}";
		}
	}

	private static Image? LoadBrandImage()
	{
		foreach (string item in AppPaths.CandidatePaths("app-icon-rounded.png"))
		{
			try
			{
				if (!File.Exists(item))
				{
					continue;
				}
				using FileStream stream = File.OpenRead(item);
				using Image original = Image.FromStream(stream);
				return new Bitmap(original);
			}
			catch
			{
			}
		}
		return null;
	}

	private static Icon? LoadWindowIcon()
	{
		foreach (string item in AppPaths.CandidatePaths("app-icon.ico"))
		{
			try
			{
				if (File.Exists(item))
				{
					return new Icon(item);
				}
			}
			catch
			{
			}
		}
		try
		{
			using Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			return (icon == null) ? null : ((Icon)icon.Clone());
		}
		catch
		{
			return null;
		}
	}

	private void BuildAnimationPage()
	{
		_animationPage.Controls.Add(_animationHost);
	}

	private void BuildResourceContextBar(Button exportMods, Button importMods)
	{
		Button value = Bind(Button("", delegate
		{
			OpenBackup();
		}), "action.backup");
		_modContextActions.Controls.Add(ContextLabel("context.mods.title"));
		_modContextActions.Controls.Add(_modsOnlyButton);
		_modContextActions.Controls.Add(exportMods);
		_modContextActions.Controls.Add(importMods);
		_modContextActions.Controls.Add(value);
		Button value2 = Bind(Button("", delegate
		{
			OpenOverFrameTable();
		}, ButtonTone.Primary), "context.overframe.manage");
		_overFrameContextActions.Controls.Add(ContextLabel("context.overframe.title"));
		_overFrameContextActions.Controls.Add(value2);
		_overFrameContextActions.Controls.Add(Bind(new Label
		{
			AutoSize = true,
			Margin = new Padding(12, 10, 0, 0),
			ForeColor = UiTheme.Muted
		}, "context.overframe.hint"));
		_resourceContextBar.Controls.Add(_overFrameContextActions);
		_resourceContextBar.Controls.Add(_modContextActions);
	}

	private Label ContextLabel(string resourceId)
	{
		return Bind(new Label
		{
			AutoSize = true,
			MinimumSize = new Size(104, 38),
			Height = 38,
			Margin = new Padding(0, 0, 8, 0),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiTheme.Gold,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
		}, resourceId);
	}

	private void BuildFramesPage()
	{
		Button button = Button("制作超框", async delegate
		{
			await OpenCardFrameStudioAsync();
		}, ButtonTone.Gold);
		Button button2 = Button("继续编辑超框", async delegate
		{
			await OpenFrameEditorAsync();
		});
		Button button3 = Button("预览所选卡框", delegate
		{
			OpenFramePreview();
		});
		Button button4 = Button("刷新超框表", delegate
		{
			EnsureEmbeddedFrames(force: true);
		});
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 55,
			Padding = new Padding(14, 6, 14, 6),
			BackColor = UiTheme.SurfaceAlt,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel.Controls.AddRange(new Control[4] { button, button2, button3, button4 });
		_framesPage.Controls.Add(_framesHost);
		_framesPage.Controls.Add(flowLayoutPanel);
	}

	private void BuildModsPage(Button exportMods, Button importMods)
	{
		Button button = Button("打开备份目录", delegate
		{
			OpenBackup();
		});
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 58,
			Padding = new Padding(14, 7, 14, 7),
			BackColor = UiTheme.Surface,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel.Controls.AddRange(new Control[4] { _modsOnlyButton, exportMods, importMods, button });
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Top,
			Height = 155,
			Margin = new Padding(18),
			Padding = new Padding(16),
			BackColor = UiTheme.Surface
		};
		_modPageSummary.Dock = DockStyle.Fill;
		borderPanel.Controls.Add(_modPageSummary);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(22),
			BackColor = UiTheme.Window
		};
		panel.Controls.Add(borderPanel);
		_modsPage.Controls.Add(panel);
		_modsPage.Controls.Add(flowLayoutPanel);
	}

	private void BuildSettingsPage()
	{
		Label value = Bind(new Label
		{
			Dock = DockStyle.Top,
			Height = 54,
			ForeColor = UiTheme.Text,
			Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleLeft
		}, "settings.title");
		CheckBox reduceMotion = Bind(new CheckBox
		{
			Dock = DockStyle.Top,
			Height = 42,
			ForeColor = UiTheme.Text,
			FlatStyle = FlatStyle.Flat,
			Checked = (_settings.ReduceMotion || MotionPreferences.WindowsReducesMotion)
		}, "settings.motion");
		reduceMotion.CheckedChanged += delegate
		{
			_settings.ReduceMotion = reduceMotion.Checked;
			MotionPreferences.UserReducesMotion = reduceMotion.Checked;
			AppSettingsStore.Save(_settings);
		};
		Label label = new Label
		{
			Dock = DockStyle.Top,
			Height = 68,
			ForeColor = UiTheme.Muted,
			Text = Localizer.T("settings.path") + Environment.NewLine + AppSettingsStore.SettingsPath
		};
		_localizedControls[label] = "settings.path";
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Top,
			Height = 210,
			Padding = new Padding(22),
			BackColor = UiTheme.Surface
		};
		borderPanel.Controls.Add(label);
		borderPanel.Controls.Add(reduceMotion);
		borderPanel.Controls.Add(value);
		borderPanel.Margin = Padding.Empty;
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(28),
			BackColor = UiTheme.Window
		};
		panel.Controls.Add(borderPanel);
		_settingsPage.Controls.Add(panel);
	}

	private void ShowPage(WorkspacePage page)
	{
		bool flag = PausesBackgroundRefresh(_currentPage);
		bool flag2 = PausesBackgroundRefresh(page);
		if (flag2 && !flag)
		{
			_backgroundRefreshCancellation?.Cancel();
		}
		_currentPage = page;
		bool flag3 = page == WorkspacePage.Cards;
		if (_cardSearchField != null)
		{
			_cardSearchField.Visible = flag3;
		}
		if (!flag3)
		{
			HideSearchSuggestions();
		}
		if (_visualShortcutRow != null)
		{
			_visualShortcutRow.Height = ((page == WorkspacePage.Visuals) ? 62f : 0f);
		}
		_visualShortcutBar.Visible = page == WorkspacePage.Visuals;
		if (page == WorkspacePage.Visuals && string.IsNullOrWhiteSpace(_activeVisualShortcut))
		{
			_activeVisualShortcut = "all";
		}
		else if (page == WorkspacePage.Cards)
		{
			_activeVisualShortcut = null;
		}
		bool flag4 = page == WorkspacePage.Animation && (_embeddedAnimation == null || _embeddedAnimation.IsDisposed);
		if (flag4)
		{
			_animationHost.Controls.Clear();
			_animationHost.Controls.Add(EmptyState(Localizer.T("page.animation.loading")));
		}
		Panel resourcePage = _resourcePage;
		bool visible = (uint)page <= 1u;
		resourcePage.Visible = visible;
		_animationPage.Visible = page == WorkspacePage.Animation;
		_settingsPage.Visible = page == WorkspacePage.Settings;
		foreach (NavigationButton navigationButton in _navigationButtons)
		{
			navigationButton.Selected = navigationButton.Page == page;
			navigationButton.Invalidate();
		}
		UpdatePageHeader();
		UpdateVisualShortcutStyles();
		UpdateResourceContextBar();
		if (page == WorkspacePage.Visuals)
		{
			EnsureVisualAssetsAsync();
		}
		if ((uint)page <= 1u)
		{
			if (_textures.Count > 0)
			{
				RefreshCategories();
			}
			RenderList();
		}
		(page switch
		{
			WorkspacePage.Animation => _animationPage,
			WorkspacePage.Settings => _settingsPage,
			_ => _resourcePage,
		}).BringToFront();
		_pageHost.PerformLayout();
		_pageTitle.Refresh();
		_pageDescription.Refresh();
		_catalogBadge.Refresh();
		_pageHost.Invalidate(invalidateChildren: true);
		UiTheme.QueueStableRepaint(this);
		QueueLayoutSurfaceReset();
		if (flag4)
		{
			QueueEmbeddedAnimationWorkspace();
		}
		if (!flag2 && flag && _gameRoot != null && _index != null && _allowBackgroundRefresh)
		{
			StartGameBuildRefresh();
		}
	}

	private static bool PausesBackgroundRefresh(WorkspacePage page)
	{
		return page == WorkspacePage.Animation;
	}

	private void QueueEmbeddedAnimationWorkspace()
	{
		if (_animationWorkspaceQueued || base.IsDisposed)
		{
			return;
		}
		_animationWorkspaceQueued = true;
		BeginInvoke(async delegate
		{
			try
			{
				Task backgroundRefreshTask = _backgroundRefreshTask;
				if (backgroundRefreshTask != null && !backgroundRefreshTask.IsCompleted)
				{
					await backgroundRefreshTask;
				}
				if (!base.IsDisposed && _currentPage == WorkspacePage.Animation)
				{
					EnsureEmbeddedAnimation();
					_animationHost.PerformLayout();
					if (_currentPage == WorkspacePage.Animation)
					{
						_animationPage.Invalidate(invalidateChildren: true);
						_animationPage.Update();
					}
				}
			}
			catch (Exception ex)
			{
				_embeddedAnimation?.Dispose();
				_embeddedAnimation = null;
				_animationHost.Controls.Clear();
				_animationHost.Controls.Add(EmptyState(Localizer.T("page.animation.error") + Environment.NewLine + ex.Message));
				_animationHost.PerformLayout();
				_animationHost.Invalidate(invalidateChildren: true);
			}
			finally
			{
				_animationWorkspaceQueued = false;
			}
		});
	}

	private void QueueEmbeddedFramesWorkspace()
	{
		if (_framesWorkspaceQueued || base.IsDisposed)
		{
			return;
		}
		_framesWorkspaceQueued = true;
		BeginInvoke(async delegate
		{
			try
			{
				Task backgroundRefreshTask = _backgroundRefreshTask;
				if (backgroundRefreshTask != null && !backgroundRefreshTask.IsCompleted)
				{
					await backgroundRefreshTask;
				}
				if (!base.IsDisposed && _currentPage == WorkspacePage.Frames)
				{
					EnsureEmbeddedFrames();
					_framesHost.PerformLayout();
					_framesPage.Invalidate(invalidateChildren: true);
				}
			}
			catch (Exception ex)
			{
				_embeddedFrames?.Dispose();
				_embeddedFrames = null;
				_framesHost.Controls.Clear();
				_framesHost.Controls.Add(EmptyState(Localizer.T("page.frames.error") + Environment.NewLine + ex.Message));
				_framesHost.PerformLayout();
				_framesHost.Invalidate(invalidateChildren: true);
			}
			finally
			{
				_framesWorkspaceQueued = false;
			}
		});
	}

	private static void StabilizeEmbeddedLayout(Control root)
	{
		root.PerformLayout();
		foreach (Control control in root.Controls)
		{
			StabilizeEmbeddedLayout(control);
		}
		root.PerformLayout();
	}

	private void QueueLayoutSurfaceReset()
	{
		if (!base.IsDisposed && !_resettingLayoutSurface)
		{
			TableLayoutPanel rootLayout = _rootLayout;
			if (rootLayout != null && rootLayout.Visible)
			{
				_layoutSurfaceReset.Stop();
				_layoutSurfaceReset.Start();
			}
		}
	}

	private void ResetLayoutSurface()
	{
		_layoutSurfaceReset.Stop();
		TableLayoutPanel rootLayout = _rootLayout;
		if (base.IsDisposed || !base.Visible || base.WindowState == FormWindowState.Minimized || _resettingLayoutSurface || rootLayout == null || rootLayout.IsDisposed || !rootLayout.Visible)
		{
			return;
		}
		_resettingLayoutSurface = true;
		try
		{
			StabilizeEmbeddedLayout(rootLayout);
			rootLayout.Invalidate(invalidateChildren: true);
			_searchSuggestionPopup.BringToFront();
		}
		finally
		{
			_resettingLayoutSurface = false;
		}
	}

	private void UpdatePageHeader()
	{
		string text = _currentPage switch
		{
			WorkspacePage.Visuals => "visuals",
			WorkspacePage.Animation => "animation",
			WorkspacePage.Settings => "settings",
			_ => "cards",
		};
		_pageTitle.Text = Localizer.T("page." + text + ".title");
		_pageDescription.Text = Localizer.T("page." + text + ".description");
		_catalogBadge.Text = Localizer.F("catalog.badge", _cardCatalog.Count);
	}

	private async Task EnsureVisualAssetsAsync()
	{
		if (_index != null && !_textures.Any(IsVisualAsset))
		{
			await LoadVisualAssetsAsync();
		}
		RenderList();
	}

	private void EnsureEmbeddedAnimation()
	{
		if (_embeddedAnimation != null && !_embeddedAnimation.IsDisposed)
		{
			return;
		}
		if (_gameRoot == null)
		{
			_animationHost.Controls.Clear();
			_animationHost.Controls.Add(EmptyState("请先选择 Master Duel 游戏目录。"));
			return;
		}
		string initialCardId = Selected()?.CardKey;
		Control[] array = _animationHost.Controls.Cast<Control>().ToArray();
		MonsterAnimationForm monsterAnimationForm = new MonsterAnimationForm(_gameRoot, initialCardId)
		{
			TopLevel = false,
			FormBorderStyle = FormBorderStyle.None,
			MinimumSize = Size.Empty,
			Dock = DockStyle.Fill
		};
		_animationHost.SuspendLayout();
		Control[] array2;
		try
		{
			_animationHost.Controls.Add(monsterAnimationForm);
			array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				array2[i].BringToFront();
			}
		}
		catch
		{
			monsterAnimationForm.Dispose();
			throw;
		}
		finally
		{
			_animationHost.ResumeLayout(performLayout: true);
		}
		monsterAnimationForm.Bounds = _animationHost.ClientRectangle;
		StabilizeEmbeddedLayout(monsterAnimationForm);
		try
		{
			monsterAnimationForm.Show();
			array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				array2[i].BringToFront();
			}
			StabilizeEmbeddedLayout(monsterAnimationForm);
			_embeddedAnimation = monsterAnimationForm;
		}
		catch
		{
			monsterAnimationForm.Dispose();
			throw;
		}
		array2 = array;
		foreach (Control control in array2)
		{
			_animationHost.Controls.Remove(control);
			control.Dispose();
		}
		monsterAnimationForm.BringToFront();
		UiTheme.QueueStableRepaint(monsterAnimationForm);
		UiTheme.QueueStableRepaint(this);
		QueueLayoutSurfaceReset();
	}

	private void EnsureEmbeddedFrames(bool force = false)
	{
		if (force || _embeddedFrames == null || _embeddedFrames.IsDisposed)
		{
			_embeddedFrames?.Dispose();
			_framesHost.Controls.Clear();
			if (_gameRoot == null)
			{
				_framesHost.Controls.Add(EmptyState("请先选择 Master Duel 游戏目录。"));
				return;
			}
			_embeddedFrames = new OverFrameForm(_gameRoot, Selected()?.CardKey)
			{
				TopLevel = false,
				FormBorderStyle = FormBorderStyle.None,
				Dock = DockStyle.Fill
			};
			_framesHost.Controls.Add(_embeddedFrames);
			_embeddedFrames.Show();
		}
	}

	private static Label EmptyState(string text)
	{
		return new Label
		{
			Dock = DockStyle.Fill,
			Text = text,
			TextAlign = ContentAlignment.MiddleCenter,
			ForeColor = UiTheme.Muted,
			BackColor = UiTheme.Window,
			Font = new Font("Microsoft YaHei UI", 11f)
		};
	}

	private void ResetEmbeddedWorkspaces()
	{
		_overFrameManagerQueued = false;
		Interlocked.Increment(ref _overFrameManagerGeneration);
		OverFrameForm? overFrameManager = _overFrameManager;
		_overFrameManager = null;
		overFrameManager?.Close();
		overFrameManager?.Dispose();
		_embeddedAnimation?.Dispose();
		_embeddedFrames?.Dispose();
		_embeddedAnimation = null;
		_embeddedFrames = null;
		_animationHost.Controls.Clear();
		_framesHost.Controls.Clear();
		if (_currentPage == WorkspacePage.Animation)
		{
			EnsureEmbeddedAnimation();
		}
		if (_currentPage == WorkspacePage.Frames)
		{
			EnsureEmbeddedFrames();
		}
	}

	private void ApplyInstallation(GameInstallation installation)
	{
		_backgroundRefreshCancellation?.Cancel();
		_gameRoot = installation.GameRoot;
		_gameFolder.Text = installation.GameRoot;
		_settings.LastGameRoot = installation.GameRoot;
		_changingProfile = true;
		try
		{
			_profileSelector.Items.Clear();
			foreach (LocalDataProfile profile in installation.Profiles)
			{
				_profileSelector.Items.Add(profile);
			}
			string preferredId = _settings.LastProfileByGame.GetValueOrDefault(Path.GetFullPath(installation.GameRoot), "");
			LocalDataProfile localDataProfile = installation.Profiles.FirstOrDefault((LocalDataProfile x) => x.AccountId == preferredId) ?? installation.Profiles.FirstOrDefault();
			if (localDataProfile != null)
			{
				_profileSelector.SelectedItem = localDataProfile;
				IndexService.SetPreferredLocalRoot(installation.GameRoot, localDataProfile.RootPath);
				_settings.LastProfileByGame[Path.GetFullPath(installation.GameRoot)] = localDataProfile.AccountId;
			}
		}
		finally
		{
			_changingProfile = false;
		}
		AppSettingsStore.Save(_settings);
		SetGameRoot();
		ResetEmbeddedWorkspaces();
	}

	private void ApplyLanguage()
	{
		Text = Localizer.T("app.title");
		foreach (KeyValuePair<Control, string> localizedControl in _localizedControls)
		{
			localizedControl.Deconstruct(out var key, out var value);
			Control control = key;
			string text = value;
			control.Text = ((text == "settings.path") ? (Localizer.T(text) + Environment.NewLine + AppSettingsStore.SettingsPath) : Localizer.T(text));
		}
		_search.PlaceholderText = Localizer.T("top.search");
		_gameFolder.AccessibleName = Localizer.T("top.game");
		_search.AccessibleName = Localizer.T("top.search");
		_profileSelector.AccessibleName = Localizer.T("top.account");
		_languageSelector.AccessibleName = Localizer.T("top.language");
		UpdateModsOnlyButtonText();
		UpdatePageHeader();
		UpdateVisualShortcutStyles();
		_changingLanguage = true;
		try
		{
			_languageSelector.Items.Clear();
			_languageSelector.Items.AddRange(new object[4]
			{
				new LanguageChoice(AppLanguage.SimplifiedChinese, Localizer.T("language.zhcn")),
				new LanguageChoice(AppLanguage.TraditionalChinese, Localizer.T("language.zhtw")),
				new LanguageChoice(AppLanguage.Japanese, Localizer.T("language.ja")),
				new LanguageChoice(AppLanguage.English, Localizer.T("language.en"))
			});
			_languageSelector.SelectedItem = _languageSelector.Items.Cast<LanguageChoice>().First((LanguageChoice x) => x.Language == Localizer.Language);
		}
		finally
		{
			_changingLanguage = false;
		}
		if (_textures.Count > 0)
		{
			RefreshCategories();
			RenderList();
		}
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		ApplyLanguage();
	}

	private static bool IsVisualAsset(TexRef texture)
	{
		if (!(texture.SourceKind == "视觉资源"))
		{
			return texture.SourceKind == "基础视觉资源";
		}
		return true;
	}

	private bool IsAnimationFilterSelected()
	{
		if (_category.SelectedItem is CategoryFilter categoryFilter)
		{
			return categoryFilter.Kind == CategoryFilterKind.Animation;
		}
		return false;
	}

	private static Button Button(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral)
	{
		return UiTheme.Button(text, click, tone);
	}

	private static Label Caption(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiTheme.Gold,
			Font = new Font("Segoe UI Semibold", 8.5f),
			Padding = new Padding(0, 8, 10, 0)
		};
	}

	private static Panel SectionHeading(string title, string subtitle)
	{
		return new Panel
		{
			Dock = DockStyle.Top,
			Height = 44,
			BackColor = UiTheme.SurfaceAlt,
			Padding = new Padding(12, 5, 12, 4),
			Controls = 
			{
				(Control?)new Label
				{
					Text = subtitle,
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.MiddleLeft,
					ForeColor = UiTheme.Muted,
					Font = new Font("Segoe UI", 8f),
					Padding = new Padding(4, 2, 0, 0)
				},
				(Control?)new Label
				{
					Text = title,
					Dock = DockStyle.Left,
					Width = 100,
					TextAlign = ContentAlignment.MiddleLeft,
					ForeColor = UiTheme.Text,
					Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
				}
			}
		};
	}

	private async Task ChooseGameAsync()
	{
		FolderBrowserDialog d = new FolderBrowserDialog
		{
			Description = "选择 Master Duel 游戏根目录，或手机端已解压的 0000 文件夹",
			InitialDirectory = (Directory.Exists(_gameRoot) ? _gameRoot : _settings.LastGameRoot)
		};
		try
		{
			if (d.ShowDialog(this) == DialogResult.OK)
			{
				ApplyInstallation(SteamGameDiscovery.FromPath(d.SelectedPath));
				await ScanAsync();
			}
		}
		finally
		{
			((IDisposable)d)?.Dispose();
		}
	}

	private async Task RebuildIndexAsync()
	{
		if (ResourceSource.IsMobile(_gameRoot))
		{
			await ScanAsync(forceRebuild: true);
		}
		else
		{
			if (_gameRoot == null || MessageBox.Show(this, "将用随程序提供的完整预绑定修复本地索引，并保留本机额外发现的新卡。\n\n这个操作不会重新扫描整个 LocalData，也不会再因游戏按需缓存而漏掉部分卡图。继续？", "确认修复索引", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk) != DialogResult.OK)
			{
				return;
			}
			try
			{
				base.UseWaitCursor = true;
				_status.Text = "正在用随包预绑定修复本地索引…";
				(bool Found, string BuildId, int RetainedExtras) repair = await Task.Run(delegate
				{
					GameIndex repaired;
					string buildId;
					int retainedExtras;
					bool num = PortableIndexService.TryRepairFromBundled(_gameRoot, _index, out repaired, out buildId, out retainedExtras);
					if (num)
					{
						IndexService.Save(_gameRoot, repaired);
					}
					return (Found: num, BuildId: buildId, RetainedExtras: retainedExtras);
				});
				if (!repair.Found)
				{
					_status.Text = "分享包缺少预绑定索引，改为扫描当前游戏资源…";
					await ScanAsync(forceRebuild: true);
					return;
				}
				await ScanAsync();
				_status.Text = $"索引修复完成：已恢复随包完整映射，并保留 {repair.RetainedExtras:N0} 个本机额外资源。";
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "索引修复失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				base.UseWaitCursor = false;
			}
		}
	}

	private void SetGameRoot()
	{
		if (_gameRoot != null)
		{
			_assetRoot = IndexService.FindLocalRoot(_gameRoot);
			_streamingRoot = Path.Combine(_gameRoot, "masterduel_Data", "StreamingAssets", "AssetBundle");
			if (_assetRoot != null)
			{
				_status.Text = "资源目录：" + _assetRoot + "（替换直接写入游戏本体，自动备份）";
			}
		}
	}

	private string CachePath()
	{
		return IndexService.CachePath(_assetRoot ?? throw new InvalidOperationException("尚未选择 LocalData 账号。"), _streamingRoot ?? throw new InvalidOperationException("尚未定位 StreamingAssets。"));
	}

	private int _scanGeneration;
	private async Task ScanAsync(bool forceRebuild = false)
	{
		int generation = ++_scanGeneration;
		string? _gameRoot = this._gameRoot;
		string? localSnapshot = _assetRoot;
		bool Current() => !IsDisposed && generation == _scanGeneration && this._gameRoot == _gameRoot && _assetRoot == localSnapshot;
		if (_assetRoot == null || !Directory.Exists(_assetRoot))
		{
			MessageBox.Show(this, "未找到 LocalData\\<用户哈希>\\0000。请选择 Master Duel 游戏根目录。", Text);
			return;
		}
		base.UseWaitCursor = true;
		_textures.Clear();
		_preview.Image = null;
		_previewHint.Visible = true;
		try
		{
			string cache = CachePath();
			GameIndex cached = null;
			bool loadedFromPrebuilt = false;
			string prebuiltBuildId = "";
			if (!forceRebuild && File.Exists(cache))
			{
				try
				{
						cached = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache));
						if (cached != null) cached = await Task.Run(() => IndexWorkspaceGuard.Repair(_gameRoot!, cached, cache));
				}
				catch
				{
					cached = null; // Keep the original cache available for recovery.
				}
			}
			if (!Current()) return;
			if (cached == null && !forceRebuild)
			{
				try
				{
					_status.Text = "正在载入随程序提供的卡号预绑定索引…";
					GameIndex index;
					string buildId;
					(bool, GameIndex, string) tuple = await Task.Run(() => (Found: PortableIndexService.TryLoadBundled(_gameRoot, out index, out buildId), Index: index, BuildId: buildId));
					(loadedFromPrebuilt, _, _) = tuple;
					cached = (loadedFromPrebuilt ? tuple.Item2 : null);
					prebuiltBuildId = tuple.Item3;
					if (loadedFromPrebuilt && cached != null)
					{
						await Task.Run(delegate
						{
							IndexService.Save(_gameRoot, cached);
						});
					}
				}
				catch (Exception ex)
				{
					_status.Text = "随包预绑定索引不可用，将自动重建：" + ex.Message;
					cached = null;
				}
			}
			if (cached == null)
			{
				_status.Text = (forceRebuild ? "正在按要求重新扫描全部资源…" : "未找到预绑定索引，正在建立本地索引（仅此一次）…");
				await Task.Run(delegate
				{
					IndexService.BuildAndSave(_gameRoot, delegate(int done, int total, int found)
					{
						BeginInvoke(delegate
						{
							_status.Text = $"正在重建索引：{done:N0}/{total:N0} Bundle，已索引 {found:N0} 张图片…";
						});
					});
				});
				cached = JsonSerializer.Deserialize<GameIndex>(await File.ReadAllTextAsync(cache)) ?? new GameIndex();
			}
			if (!Current()) return;
			int num = cached.Textures.RemoveAll((TexRef x) => x.SourceKind == "卡框资源");
			IReadOnlyList<TexRef> readOnlyList = BuiltInCardFrameCatalog.Load();
			cached.Textures.AddRange(readOnlyList);
			int num2 = IndexService.RemoveSpineAtlasParts(cached);
			int num3 = IndexService.RemoveNonCardLocalTextures(cached);
			if (num > 0 || readOnlyList.Count > 0 || num2 + num3 > 0)
			{
				await Task.Run(delegate
				{
					IndexService.Save(_gameRoot, cached);
				});
			}
			if (!Current()) return;
			_index = cached;
			_textures.AddRange(cached.Textures);
			string text = (loadedFromPrebuilt ? PortableIndexService.GetGameBuildId(_gameRoot) : "");
			string value = ((prebuiltBuildId.Length > 0 && text.Length > 0 && prebuiltBuildId != text) ? $"；预绑定 Build {prebuiltBuildId}，本机 Build {text}，新版卡可用“定位卡图”补充" : "");
			_status.Text = (loadedFromPrebuilt ? $"已用随包预绑定瞬时建立本机索引：{_textures.Count:N0} 张图片，无需首次扫描{value}。" : (forceRebuild ? $"索引已重建：{_textures.Count:N0} 张图片。" : $"已载入本地索引：{_textures.Count:N0} 张图片。"));
			if (cached != null && cached.AlternateArtIndexVersion < 4)
			{
				_status.Text = "正在建立本地异画卡名单（仅首次，需要下载一次百鸽卡片库）…";
				try
				{
					await YgoCdbCardCatalog.ClassifyAlternateArtsAsync(cached);
					if (!Current()) return;
					_textures.Clear();
					_textures.AddRange(cached.Textures);
					await Task.Run(delegate
					{
						IndexService.Save(_gameRoot, cached);
					});
					_status.Text = $"异画卡与 Token／杂图已分类并保存到本地：异画 {_textures.Count((TexRef x) => x.IsAlternateArt):N0} 张，Token／杂图 {_textures.Count((TexRef x) => x.IsTokenOrMisc):N0} 张。";
				}
				catch (Exception ex2)
				{
					_status.Text = "异画卡名单下载失败：" + ex2.Message + "；本次仍可正常使用，下次会自动重试。";
				}
			}
			await ApplyOverFrameTagsAsync();
			if (!Current()) return;
			ApplyMonsterAnimationTags();
			await RefreshModFlagsAsync();
			if (!Current()) return;
			RefreshCategories();
			RenderList();
			StartGameBuildRefresh();
		}
		catch (Exception ex3)
		{
			if (Current()) MessageBox.Show(this, ex3.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			if (Current()) base.UseWaitCursor = false;
		}
	}

	private void RefreshCategories()
	{
		bool changingCategory = _changingCategory;
		_changingCategory = true;
		try
		{
			string old = (_category.SelectedItem as CategoryFilter)?.Key ?? "";
			_category.Items.Clear();
			CategoryFilter categoryFilter = new CategoryFilter(CategoryFilterKind.All, "", Localizer.T("filter.all"));
			_category.Items.Add(categoryFilter);
			_groups.BeginUpdate();
			_groups.Nodes.Clear();
			int num = _textures.Count((TexRef x) => x.IsModded);
			TreeNode treeNode = _groups.Nodes.Add($"{Localizer.T("nav.mods")}（{num + _changedAnimationBundleCount}）");
			treeNode.Tag = "__mods__";
			treeNode.ForeColor = UiTheme.Gold;
			foreach (IGrouping<string, TexRef> item in from x in _textures.Where(PageMatches)
				group x by x.SourceKind into x
				orderby x.Key
				select x)
			{
				TreeNode treeNode2 = _groups.Nodes.Add($"{ResourceLabel(item.Key)}（{item.Count()}）");
				if (item.Key == "本地卡图")
				{
					int num2 = item.Count((TexRef x) => x.HasMonsterAnimation);
					if (num2 > 0)
					{
						_category.Items.Add(new CategoryFilter(CategoryFilterKind.Animation, "local-card|animation", ResourceLabel("有怪兽动画")));
						TreeNode treeNode3 = treeNode2.Nodes.Add($"{ResourceLabel("有怪兽动画")}（{num2}）");
						treeNode3.Tag = "local-card|animation";
						treeNode3.ForeColor = UiTheme.Gold;
					}
				}
				foreach (IGrouping<string, TexRef> item2 in from x in item
					group x by x.Category into x
					orderby ResourceCategoryOrder(x.Key), x.Key
					select x)
				{
					string text = item.Key + "|" + item2.Key;
					_category.Items.Add(new CategoryFilter(CategoryFilterKind.Category, text, ResourceLabel(item2.Key)));
					treeNode2.Nodes.Add($"{ResourceLabel(item2.Key)}（{item2.Count()}）").Tag = text;
				}
				treeNode2.Expand();
			}
			_category.SelectedItem = _category.Items.Cast<CategoryFilter>().FirstOrDefault((CategoryFilter x) => x.Key == old) ?? categoryFilter;
			UpdateModSummary();
			UpdateVisualShortcutStyles();
		}
		finally
		{
			_groups.EndUpdate();
			_changingCategory = changingCategory;
		}
	}

	internal async Task OpenResourceSourceAsync(string path)
	{
		ApplyInstallation(SteamGameDiscovery.FromPath(path));
		await ScanAsync();
	}

	private static int ResourceCategoryOrder(string category)
	{
		return category switch
		{
			"透明卡框" => 10,
			"透明炫彩卡框" => 11,
			"炫彩卡框" => 12,
			"普通卡框" => 13,
			_ => 0,
		};
	}

	private void RenderList()
	{
		string q = ((_currentPage == WorkspacePage.Cards) ? _search.Text.Trim() : "");
		CategoryFilter filter = (_category.SelectedItem as CategoryFilter) ?? new CategoryFilter(CategoryFilterKind.All, "", Localizer.T("filter.all"));
		_resultCount.Text = Localizer.T("list.updating");
		_resultCount.Update();
		_list.BeginUpdate();
		try
		{
			_list.VirtualListSize = 0;
			_visibleTextures.Clear();
			bool globalSearch = q.Length > 0;
			HashSet<int> matchedCardIds = (globalSearch ? (from x in _cardCatalog.Search(q, 250)
				select x.CardId).ToHashSet() : null);
			IEnumerable<TexRef> enumerable = _textures.Where((TexRef texRef) => (globalSearch || (PageMatches(texRef) && VisualShortcutMatches(texRef) && (!_modsOnly || texRef.IsModded) && CategoryMatches(texRef, filter))) && MatchesSearch(texRef, q, matchedCardIds));
			if (globalSearch)
			{
				Dictionary<int, int> cardOrder = _cardCatalog.Search(q, 250).Select((CardCatalogEntry entry, int index) => (CardId: entry.CardId, index: index)).ToDictionary(((int CardId, int index) item) => item.CardId, ((int CardId, int index) item) => item.index);
				enumerable = enumerable.OrderBy((TexRef texture) => (!int.TryParse(texture.CardKey, out var result) || !cardOrder.TryGetValue(result, out var value)) ? int.MaxValue : value).ThenBy(CardTextureRank).ThenByDescending((TexRef texture) => (long)texture.Width * (long)texture.Height);
			}
			_visibleTextures.AddRange(enumerable);
			_list.VirtualListSize = _visibleTextures.Count;
			int num = (globalSearch ? _textures.Count : _textures.Count((TexRef x) => PageMatches(x) && VisualShortcutMatches(x) && (!_modsOnly || x.IsModded)));
			_resultCount.Text = Localizer.F(globalSearch ? "list.global_count" : "list.count", _visibleTextures.Count, num);
		}
		finally
		{
			_list.EndUpdate();
		}
		UpdateResourceContextBar();
	}

	private ListViewItem CreateListItem(TexRef texture)
	{
		return new ListViewItem(new string[4]
		{
			DisplayResourceName(texture),
			$"{ResourceLabel(texture.SourceKind)} / {ResourceLabel(texture.Category)}{(texture.HasMonsterAnimation ? ("  · " + ResourceLabel("动画")) : "")}{(texture.IsModded ? "  · MOD" : "")}",
			$"{texture.Width}×{texture.Height}",
			texture.RelativeBundlePath
		})
		{
			Tag = texture
		};
	}

	private void ResizeResourceColumns()
	{
		if (_list.Columns.Count != 4 || _list.ClientSize.Width <= 0)
		{
			return;
		}
		int num = Math.Max(UiTheme.Scale(_list, 360), _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - UiTheme.Scale(_list, 3));
		int num2 = Math.Clamp((int)Math.Round((double)num * 0.35), UiTheme.Scale(_list, 130), UiTheme.Scale(_list, 225));
		int num3 = Math.Clamp((int)Math.Round((double)num * 0.3), UiTheme.Scale(_list, 120), UiTheme.Scale(_list, 205));
		int num4 = Math.Clamp((int)Math.Round((double)num * 0.14), UiTheme.Scale(_list, 68), UiTheme.Scale(_list, 100));
		int num5 = Math.Max(UiTheme.Scale(_list, 72), num - num2 - num3 - num4);
		int num6 = num2 + num3 + num4 + num5 - num;
		if (num6 > 0)
		{
			num2 = Math.Max(UiTheme.Scale(_list, 110), num2 - (num6 + 1) / 2);
			num3 = Math.Max(UiTheme.Scale(_list, 105), num3 - num6 / 2);
			num5 = Math.Max(UiTheme.Scale(_list, 56), num - num2 - num3 - num4);
		}
		int[] array = new int[4] { num2, num3, num4, num5 };
		for (int i = 0; i < array.Length; i++)
		{
			if (_list.Columns[i].Width != array[i])
			{
				_list.Columns[i].Width = array[i];
			}
		}
	}

	private static bool CategoryMatches(TexRef texture, CategoryFilter filter)
	{
		if (filter.Kind == CategoryFilterKind.Animation)
		{
			if (texture.SourceKind == "本地卡图")
			{
				return texture.HasMonsterAnimation;
			}
			return false;
		}
		if (filter.Kind != CategoryFilterKind.All)
		{
			return filter.Key == texture.SourceKind + "|" + texture.Category;
		}
		return true;
	}

	private static bool MatchesSearch(TexRef texture, string query, HashSet<int>? matchedCardIds)
	{
		if (query.Length == 0)
		{
			return true;
		}
		if (query.All(char.IsAsciiDigit))
		{
			if (!(texture.CardKey == query))
			{
				return texture.Name == query;
			}
			return true;
		}
		if (int.TryParse(texture.CardKey, out var result) && matchedCardIds != null && matchedCardIds.Contains(result))
		{
			return true;
		}
		string value = (texture.HasMonsterAnimation ? "有怪兽动画 召唤动画" : "");
		return $"{texture.Name} {texture.CardKey} {texture.SourceKind} {texture.Category} {value} {texture.RelativeBundlePath}".Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private async Task ScanMissingCardAsync(CardCatalogEntry? preferred = null)
	{
		string cardKey = _search.Text.Trim();
		if (!Regex.IsMatch(cardKey, "^\\d+$"))
		{
			CardCatalogEntry cardCatalogEntry = preferred ?? _cardCatalog.Search(cardKey, 1).FirstOrDefault();
			if (cardCatalogEntry == null)
			{
				HideSearchSuggestions();
				_status.Text = Localizer.T("search.no_results_hint");
				return;
			}
			_suppressSearchSuggestions = true;
			try
			{
				_search.Text = cardCatalogEntry.CardId.ToString();
			}
			finally
			{
				_suppressSearchSuggestions = false;
			}
			HideSearchSuggestions();
			_status.Text = $"{cardCatalogEntry.Name(Localizer.Language)} · {cardCatalogEntry.CardId} · {Localizer.T("search.locating")}";
			await ScanMissingCardAsync(cardCatalogEntry);
		}
		else
		{
			if (_gameRoot == null || _index == null)
			{
				return;
			}
			TexRef texRef = BestCardTexture(cardKey);
			if (texRef == null || CardTextureRank(texRef) > 1)
			{
				try
				{
					_scanMissingButton.Enabled = false;
					_status.Text = "正在按游戏资源路径直接定位卡号 " + cardKey + "…";
					MissingCardScanResult result = await Task.Run(() => IndexService.ScanMissingLocalCard(_gameRoot, _index, cardKey, delegate(int done, int total, int added)
					{
						if (!base.IsDisposed && base.IsHandleCreated)
						{
							BeginInvoke(delegate
							{
								_status.Text = $"正在定位 {cardKey}：{done:N0}/{total:N0} 个候选 Bundle…";
							});
						}
					}));
					HashSet<string> known = _textures.Select((TexRef x) => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
					List<TexRef> additions = result.Textures.Where((TexRef x) => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")).ToList();
					if (additions.Count > 0)
					{
						await YgoCdbCardCatalog.ClassifyTexturesAsync(additions);
						HashSet<string> animationIds = MonsterAnimationIndexService.LoadBundledCardIds();
						foreach (TexRef addition in additions)
						{
							TexRef texRef2 = addition;
							bool flag = HasAnimationIdOrEquivalent(addition.CardKey, animationIds);
							if (!flag)
							{
								flag = await Task.Run(() => MonsterAnimationIndexService.HasInstalledAnimation(_gameRoot, addition.CardKey));
							}
							texRef2.HasMonsterAnimation = flag;
						}
						_index.Textures.AddRange(additions);
						_textures.AddRange(additions);
					}
					await Task.Run(delegate
					{
						IndexService.Save(_gameRoot, _index);
					});
					RefreshCategories();
					RenderList();
					TexRef texRef3 = BestCardTexture(cardKey);
					if (texRef3 != null)
					{
						SetModsOnly(enabled: false);
						SelectAllCategory();
						RenderList();
						SelectTexture(texRef3);
						_status.Text = ((texRef3.Width != 512) ? $"卡号 {cardKey} 的完整插图尚未下载；已显示现有 {texRef3.Width}×{texRef3.Height} 缩略图。进入游戏查看该卡后可再次定位完整卡图。" : ((texRef3.Height == 1024) ? ("已定位灵摆卡 " + cardKey + " 的 512×1024 原生卡图；可直接预览、裁剪和替换，映射已保存。") : ("已定位卡号 " + cardKey + " 的 512×512 卡图；映射已保存。")));
					}
					else
					{
						_status.Text = $"LocalData 与 StreamingAssets 中都没有卡号 {cardKey} 的卡图 Bundle；本次只检查了游戏计算出的 {result.TotalBundles:N0} 个候选路径。";
					}
					return;
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, ex.Message, "定位卡图失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
					return;
				}
				finally
				{
					_scanMissingButton.Enabled = true;
				}
			}
			SetModsOnly(enabled: false);
			SelectAllCategory();
			RenderList();
			SelectTexture(texRef);
			string text = preferred?.Name(Localizer.Language) ?? _cardCatalog.Find(cardKey)?.Name(Localizer.Language) ?? "";
			_status.Text = ((text.Length > 0) ? (text + " · ") : "") + "卡号 " + cardKey + " 已定位；当前以全局检索显示，不受分类筛选影响。";
		}
	}

	private TexRef? BestCardTexture(string cardKey)
	{
		return _textures.Where((TexRef texture) => texture.SourceKind == "本地卡图" && texture.CardKey == cardKey).OrderBy(CardTextureRank).ThenByDescending((TexRef texture) => (long)texture.Width * (long)texture.Height)
			.FirstOrDefault();
	}

	private static int CardTextureRank(TexRef texture)
	{
		if (texture.Width == 704 && texture.Height == 1024)
		{
			return 0;
		}
		if (texture.Width == 512 && (texture.Height == 512 || texture.Height == 1024))
		{
			return 1;
		}
		if (texture.Width >= 256 && texture.Height >= 256)
		{
			return 2;
		}
		return 3;
	}

	private bool PageMatches(TexRef texture)
	{
		return _currentPage switch
		{
			WorkspacePage.Visuals => IsVisualAsset(texture),
			WorkspacePage.Cards => !IsVisualAsset(texture),
			_ => true,
		};
	}

	private string DisplayResourceName(TexRef texture)
	{
		if (BuiltInCardFrameCatalog.IsPackagedFrame(texture))
		{
			return $"{texture.Name} · {CardFrameCatalog.FriendlyName(texture.Name)} · {ResourceLabel(texture.Category)}";
		}
		CardCatalogEntry cardCatalogEntry = _cardCatalog.Find(texture.CardKey);
		if (!(cardCatalogEntry == null))
		{
			return texture.CardKey + " · " + cardCatalogEntry.Name(Localizer.Language);
		}
		return texture.Name;
	}

	private static string ResourceLabel(string value)
	{
		(string, string, string, string) tuple = value switch
		{
			"本地卡图" => ("本地卡图", "本地卡圖", "ローカルカード画像", "Local Card Art"),
			"游戏内图片" => ("游戏内图片", "遊戲內圖片", "ゲーム内画像", "Built-in Images"),
			"视觉资源" => ("已下载视觉资源", "已下載視覺資源", "ダウンロード済みビジュアル", "Downloaded Visual Assets"),
			"基础视觉资源" => ("游戏基础视觉资源", "遊戲基礎視覺資源", "ゲーム内ビジュアル", "Built-in Visual Assets"),
			"本地视觉资源" => ("本地视觉资源", "本地視覺資源", "ローカルビジュアル", "Local Visual Assets"),
			"游戏视觉资源" => ("游戏视觉资源", "遊戲視覺資源", "ゲームビジュアル", "Built-in Visual Assets"),
			"卡框资源" => ("卡框资源", "卡框資源", "カードフレーム", "Card Frames"),
			"普通卡框" => ("普通卡框", "普通卡框", "標準カードフレーム", "Normal Frames"),
			"透明卡框" => ("透明卡框", "透明卡框", "透明カードフレーム", "Transparent Frames"),
			"透明炫彩卡框" => ("透明炫彩卡框", "透明炫彩卡框", "透明グラデーションフレーム", "Transparent Iridescent Frames"),
			"炫彩卡框" => ("炫彩卡框", "炫彩卡框", "グラデーションカードフレーム", "Iridescent Frames"),
			"决斗场地" => ("决斗场地", "決鬥場地", "デュエルフィールド", "Duel Fields"),
			"大厅壁纸" => ("大厅壁纸", "大廳桌布", "ホーム壁紙", "Home Wallpaper"),
			"大厅背景" => ("大厅背景", "大廳背景", "ホーム背景", "Home Background"),
			"卡套" => ("卡套", "卡套", "プロテクター", "Card Sleeves"),
			"头像" => ("头像", "頭像", "アイコン", "Icons"),
			"头像框" => ("头像框", "頭像框", "アイコンフレーム", "Icon Frames"),
			"卡盒" => ("卡盒", "卡盒", "デッキケース", "Deck Cases"),
			"硬币" => ("硬币", "硬幣", "コイン", "Coins"),
			"有怪兽动画" => ("有怪兽动画", "有怪獸動畫", "演出あり", "Has Animation"),
			"动画" => ("动画", "動畫", "演出", "Animation"),
			_ => (value, value, value, value),
		};
		return Localizer.Language switch
		{
			AppLanguage.TraditionalChinese => tuple.Item2,
			AppLanguage.Japanese => tuple.Item3,
			AppLanguage.English => tuple.Item4,
			_ => tuple.Item1,
		};
	}

	private async Task LoadVisualAssetsAsync()
	{
		if (_gameRoot == null || _index == null)
		{
			MessageBox.Show(this, "请先选择游戏目录并载入索引。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			_visualAssetsButton.Enabled = false;
			base.UseWaitCursor = true;
			_status.Text = "正在按 Astellar 资源目录定位场地、壁纸与外观资源…";
			VisualAssetScanResult result = await Task.Run(() => VisualAssetIndexService.Scan(_gameRoot, delegate(int done, int total, int found)
			{
				if (!base.IsDisposed && base.IsHandleCreated)
				{
					BeginInvoke(delegate
					{
						_status.Text = $"正在载入视觉资源：{done:N0}/{total:N0} 个候选 Bundle，已找到 {found:N0} 张贴图…";
					});
				}
			}));
			if (result.Textures.Count == 0)
			{
				throw new InvalidDataException("本机没有找到可读取的场地或壁纸资源；请先让游戏下载相关资源后重试。");
			}
			_index.Textures.RemoveAll(IsVisual);
			_textures.RemoveAll(IsVisual);
			_index.Textures.AddRange(result.Textures);
			_textures.AddRange(result.Textures);
			await Task.Run(delegate
			{
				IndexService.Save(_gameRoot, _index);
			});
			await RefreshModFlagsAsync();
			RefreshCategories();
			RenderList();
			int value = result.Textures.Count((TexRef x) => x.Category == "决斗场地");
			int value2 = result.Textures.Count((TexRef x) => x.Category == "大厅壁纸" || x.Category == "大厅背景");
			_status.Text = $"视觉资源已载入：场地 {value:N0} 张、壁纸／大厅背景 {value2:N0} 张，另含卡套、头像、卡盒与硬币；替换时同样自动备份。";
			UpdateVisualShortcutStyles();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "载入视觉资源失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
			_visualAssetsButton.Enabled = true;
		}
		static bool IsVisual(TexRef x)
		{
			if (!(x.SourceKind == "视觉资源"))
			{
				return x.SourceKind == "基础视觉资源";
			}
			return true;
		}
	}

	private void SelectGroup(string? key)
	{
		if (key == null)
		{
			return;
		}
		if (key == "__mods__")
		{
			SetModsOnly(enabled: true);
			SelectAllCategory();
			RenderList();
			return;
		}
		_activeVisualShortcut = null;
		UpdateVisualShortcutStyles();
		SetModsOnly(enabled: false);
		CategoryFilter categoryFilter = _category.Items.Cast<CategoryFilter>().FirstOrDefault((CategoryFilter x) => x.Key == key);
		if (categoryFilter != null)
		{
			_category.SelectedItem = categoryFilter;
		}
	}

	private void ToggleModsOnly()
	{
		SetModsOnly(!_modsOnly);
		RenderList();
	}

	private void SetModsOnly(bool enabled)
	{
		_modsOnly = enabled;
		UpdateModsOnlyButtonText();
		UpdateModSummary();
		UpdateResourceContextBar();
	}

	private void UpdateModsOnlyButtonText()
	{
		if (_modsOnlyButton != null)
		{
			_modsOnlyButton.Text = Localizer.T(_modsOnly ? "action.mods.all" : "action.mods.only");
		}
	}

	private void UpdateResourceContextBar()
	{
		if (_resourceContextRow != null)
		{
			bool num = _currentPage == WorkspacePage.Cards;
			bool flag = num && _modsOnly;
			bool flag2 = num && !flag && _category.SelectedItem is CategoryFilter { Kind: CategoryFilterKind.Category } categoryFilter && categoryFilter.Key.EndsWith("|超框卡图", StringComparison.Ordinal);
			bool flag3 = flag || flag2;
			_modContextActions.Visible = flag;
			_overFrameContextActions.Visible = flag2;
			if (flag)
			{
				_modContextActions.BringToFront();
			}
			else if (flag2)
			{
				_overFrameContextActions.BringToFront();
			}
			_resourceContextBar.Visible = flag3;
			_resourceContextRow.Height = (flag3 ? 52f : 0f);
		}
	}

	private void UpdateModSummary()
	{
		TexRef[] array = _textures.Where((TexRef x) => x.IsModded).ToArray();
		_modSummary.Text = ((_changedModBundleCount == 0) ? "暂无改动；替换后的卡图与动画会自动纳入 Mod 管理" : $"已管理 {array.Length:N0} 个图片资源 · {_changedModBundleCount:N0} 个已修改 Bundle{((_changedAnimationBundleCount > 0) ? $" · 动画 {_changedAnimationBundleCount:N0}" : "")}");
		_modPageSummary.Text = _modSummary.Text;
	}

	private async Task RefreshModFlagsAsync()
	{
		string? root = _gameRoot;
		string? local = _assetRoot;
		int generation = _scanGeneration;
		var textures = _textures.ToList();
		if (root != null)
		{
			ModChangeSummary modChangeSummary = await Task.Run(delegate
			{
				_mods.RefreshFlags(root, textures);
				return _mods.GetChangeSummary(root, textures);
			});
			if (IsDisposed || generation != _scanGeneration || root != _gameRoot || local != _assetRoot) return;
			_changedModBundleCount = modChangeSummary.BundleCount;
			_changedAnimationBundleCount = modChangeSummary.AnimationBundleCount;
			UpdateModSummary();
		}
	}

	private TexRef? Selected()
	{
		if (_list.SelectedIndices.Count != 1)
		{
			return null;
		}
		int num = _list.SelectedIndices[0];
		if (num < 0 || num >= _visibleTextures.Count)
		{
			return null;
		}
		return _visibleTextures[num];
	}

	private async Task ShowSelectionAsync()
	{
		int generation = Interlocked.Increment(ref _previewGeneration);
		_previewCancellation?.Cancel();
		_previewCancellation?.Dispose();
		_previewCancellation = new CancellationTokenSource();
		CancellationToken cancellationToken = _previewCancellation.Token;
		TexRef x = Selected();
		_preview.Image?.Dispose();
		_preview.Image = null;
		_previewHint.Text = ((x == null) ? "SELECT A RESOURCE\n\n选择一张卡图查看预览" : "LOADING\n\n正在读取当前 Bundle…");
		_previewHint.Visible = true;
		if (x == null)
		{
			return;
		}
		try
		{
			byte[] data = await DecodeWithReferenceRepairAsync(x, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			GameTextureDisplayMapping displayMapping = DisplayMappingFor(x);
			byte[] texturePng = ((!displayMapping.RequiresMapping) ? data : (await Task.Run(() => displayMapping.DecodeForDisplay(data), cancellationToken)));
			cancellationToken.ThrowIfCancellationRequested();
			Bitmap bitmap = CardPreviewRenderer.RenderRaw(texturePng);
			if (cancellationToken.IsCancellationRequested || generation != _previewGeneration || Selected() != x)
			{
				bitmap.Dispose();
				return;
			}
			_preview.Image?.Dispose();
			_preview.Image = bitmap;
			_previewHint.Visible = false;
			string text = (displayMapping.RequiresMapping ? ("\n" + displayMapping.EditorSummary + " · 写入时自动反向映射") : "");
			if (_gameRoot != null && x.SourceKind == "本地卡图" && x.Width == 704 && x.Height == 1024 && ushort.TryParse(x.CardKey, out var result))
			{
				text = "\n预览：真实 Alpha 显示（透明像素中的 RGB 数据仍原样保留）";
				if (OverFrameArtStore.HasSettings(_gameRoot, result))
				{
					OverFrameFrameSettings overFrameFrameSettings = OverFrameArtStore.ReadSettings(_gameRoot, result);
					text = text + "\n卡框：" + (overFrameFrameSettings.UsesCustomFrame ? "自定义卡框" : overFrameFrameSettings.FrameKey) + "  ·  可用下方“制作超框”更换";
				}
				else
				{
					text += "\n卡框：尚未单独合成  ·  点击下方“制作超框”，可直接使用当前原卡图";
				}
			}
			else if (x.SourceKind == "本地卡图" && x.Width == 512 && (x.Height == 512 || x.Height == 1024))
			{
				string text2 = PreferredFrameKeyFor(x);
				text += ((displayMapping.Kind == TextureDisplayMappingKind.PendulumCardArt) ? ("\n当前为灵摆卡图正常比例预览（未叠加卡框） · 推荐卡框 " + text2 + " · " + CardFrameCatalog.FriendlyName(text2)) : ("\n当前显示原始 Texture2D（未叠加卡框） · 推荐卡框 " + text2 + " · " + CardFrameCatalog.FriendlyName(text2)));
			}
			if (x.HasMonsterAnimation)
			{
				text += "\n怪兽动画：双击动画分类中的卡图，或点击“原始动画资源”，查看 PNG / Atlas / JSON";
			}
			string value = (displayMapping.RequiresMapping ? $"正常预览 {displayMapping.DisplayWidth} × {displayMapping.DisplayHeight}   ·   Texture2D {x.Width} × {x.Height}" : $"{x.Width} × {x.Height}");
			_info.Text = $"{x.Name}  ·  {x.Category}\n{value}   PathID {x.PathId}\n{x.RelativeBundlePath}{text}";
		}
		catch (Exception ex)
		{
			if (generation == _previewGeneration)
			{
				_preview.Image?.Dispose();
				_preview.Image = null;
				string text3 = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
				if (text3.Length > 96)
				{
					text3 = text3.Substring(0, 93) + "…";
				}
				_previewHint.Text = "PREVIEW UNAVAILABLE\n\n" + text3 + "\n\n已检查 LocalData 与 StreamingAssets";
				_previewHint.Visible = true;
				_info.Text = $"{x.Name}  ·  {x.Category}\n映射 PathID {x.PathId}\n{x.RelativeBundlePath}";
				_status.Text = "预览失败：" + ex.Message;
			}
		}
	}

	private async Task<byte[]> DecodeWithReferenceRepairAsync(TexRef texture, CancellationToken cancellationToken = default(CancellationToken))
	{
		string oldBundlePath = texture.BundlePath;
		string oldAssetFileName = texture.AssetFileName;
		long oldPathId = texture.PathId;
		try
		{
			byte[] data = await Task.Run(() => _engine.DecodePng(texture), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			bool repaired = oldPathId != texture.PathId || !string.Equals(oldAssetFileName, texture.AssetFileName, StringComparison.Ordinal) || !string.Equals(oldBundlePath, texture.BundlePath, StringComparison.OrdinalIgnoreCase);
			if (repaired && _gameRoot != null && _index != null)
			{
				await Task.Run(delegate
				{
					IndexService.Save(_gameRoot, _index);
				});
			}
			if (repaired && Selected() == texture)
			{
				int num = _visibleTextures.IndexOf(texture);
				if (_list.IsHandleCreated && num >= 0 && num < _list.VirtualListSize)
				{
					_list.RedrawItems(num, num, invalidateOnly: false);
				}
			}
			if (repaired)
			{
				_status.Text = $"已自动修复 Texture2D 映射：PathID {texture.PathId}";
			}
			return data;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			string text = ((texture.CardKey.Length > 0) ? ("卡号 " + texture.CardKey) : texture.Name);
			throw new InvalidDataException("无法读取 " + text + " 的 Texture2D：" + ex2.Message, ex2);
		}
	}

	private GameTextureDisplayMapping DisplayMappingFor(TexRef texture)
	{
		string preferredFrameKey = ((texture.SourceKind == "本地卡图" && texture.CardKey.Length > 0) ? PreferredFrameKeyFor(texture) : null);
		return GameTextureDisplayMapping.Resolve(texture, preferredFrameKey);
	}

	private async Task<byte[]> DecodeForDisplayAsync(TexRef texture, CancellationToken cancellationToken = default(CancellationToken))
	{
		byte[] stored = await DecodeWithReferenceRepairAsync(texture, cancellationToken);
		GameTextureDisplayMapping mapping = DisplayMappingFor(texture);
		return (!mapping.RequiresMapping) ? stored : (await Task.Run(() => mapping.DecodeForDisplay(stored), cancellationToken));
	}

	private void SelectAllCategory()
	{
		CategoryFilter categoryFilter = _category.Items.Cast<CategoryFilter>().FirstOrDefault((CategoryFilter x) => x.Kind == CategoryFilterKind.All);
		if (categoryFilter != null)
		{
			_category.SelectedItem = categoryFilter;
		}
	}

	private async Task ReplaceSelectedAsync(string? image = null)
	{
		TexRef x = Selected();
		if (x == null || _gameRoot == null)
		{
			MessageBox.Show(this, "先选择一张图片。", Text);
			return;
		}
		if (BuiltInCardFrameCatalog.IsPackagedFrame(x))
		{
			MessageBox.Show(this, "这是随工具提供的只读卡框模板。请在“卡片资源”中选择目标卡图，再使用“制作超框／卡框”把它应用到单张卡。", Text);
			return;
		}
		if (image == null)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
			};
			try
			{
				if (openFileDialog.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				image = openFileDialog.FileName;
			}
			finally
			{
				((IDisposable)openFileDialog)?.Dispose();
			}
		}
		GameTextureDisplayMapping displayMapping = DisplayMappingFor(x);
		byte[] cropped;
		try
		{
			TexRef[] array = ((x.SourceKind == "本地卡图" && x.CardKey.Length > 0 && x.Width == 512 && (x.Height == 512 || x.Height == 1024)) ? OverFrameFrames() : null);
			using ImageCropForm crop = new ImageCropForm(image, displayMapping.DisplayWidth, displayMapping.DisplayHeight, "替换 " + x.Name, array, (array == null) ? null : PreferredFrameKeyFor(x), fullCardOverlay: false, null, displayMapping);
			if (crop.ShowDialog(this) != DialogResult.OK || crop.OutputPng == null)
			{
				return;
			}
			byte[] normalPreview = crop.OutputPng;
			byte[] array2 = await Task.Run(() => displayMapping.EncodeForStorage(normalPreview));
			cropped = array2;
			if (crop.SelectedFrameKey.Length > 0)
			{
				x.PreviewFrameKey = crop.SelectedFrameKey;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "无法打开裁剪器", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return;
		}
		string text = (displayMapping.RequiresMapping ? $"裁剪器按正常比例 {displayMapping.DisplayWidth}×{displayMapping.DisplayHeight} 预览；写入时已自动转换为游戏存储 {x.Width}×{x.Height}。" : $"裁剪结果将以 {x.Width}×{x.Height} 写入游戏 Bundle。");
		if (MessageBox.Show(this, text + "\n原始文件会备份到游戏目录的 _MD卡图备份 中。继续？", "确认替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			x.OverrideBundlePath = null;
			await Task.Run(delegate
			{
				_engine.Replace(x, cropped, Path.Combine(_gameRoot, "_MD卡图备份", x.SourceKind));
			});
			await RefreshModFlagsAsync();
			await Task.Run(delegate
			{
				IndexService.Save(_gameRoot, _textures);
			});
			RefreshCategories();
			RenderList();
			SelectTexture(x);
			_status.Text = "替换完成：已写入游戏本体，并加入“我的 Mod”。";
			await ShowSelectionAsync();
		}
		catch (Exception ex2)
		{
			MessageBox.Show(this, ex2.Message, "替换失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private async Task RestoreSelectedAsync()
	{
		TexRef x = Selected();
		if (x == null || _gameRoot == null)
		{
			return;
		}
		string backup = Path.Combine(_gameRoot, "_MD卡图备份", x.SourceKind, x.RelativeBundlePath);
		if (!File.Exists(backup))
		{
			MessageBox.Show(this, "该 Bundle 尚未被本工具备份，无法还原。", Text);
			return;
		}
		ushort cardId = 0;
		bool isOverFrame = x.Category == "超框卡图" && ushort.TryParse(x.CardKey, out cardId);
		string text = (isOverFrame ? "确认还原该超框卡？会还原原始卡图、关闭本卡超框登记，并重新归类到卡图列表。" : "确认把该 Bundle 还原为备份版本？");
		if (MessageBox.Show(this, text, "确认还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			await Task.Run(delegate
			{
				File.Copy(backup, x.BundlePath, overwrite: true);
				if (isOverFrame)
				{
					_overFrames.Disable(_gameRoot, cardId);
				}
				TexRef texRef = _engine.ScanBundle(x.BundlePath, _assetRoot, x.SourceKind, includeDependencies: false).Textures.FirstOrDefault((TexRef t) => t.PathId == x.PathId && t.AssetFileName == x.AssetFileName);
				if (texRef != null)
				{
					foreach (TexRef item in _textures.Where((TexRef t) => t.BundlePath.Equals(x.BundlePath, StringComparison.OrdinalIgnoreCase) && t.PathId == x.PathId && t.AssetFileName == x.AssetFileName))
					{
						item.Width = texRef.Width;
						item.Height = texRef.Height;
						item.Category = texRef.Category;
						IndexService.NormalizeLocalCardCategory(item);
					}
				}
			});
			await ApplyOverFrameTagsAsync();
			await RefreshModFlagsAsync();
			await Task.Run(delegate
			{
				IndexService.Save(_gameRoot, _textures);
			});
			RefreshCategories();
			string targetCategory = x.SourceKind + "|" + x.Category;
			if (isOverFrame)
			{
				SetModsOnly(enabled: false);
			}
			CategoryFilter categoryFilter = _category.Items.Cast<CategoryFilter>().FirstOrDefault((CategoryFilter filter) => filter.Key == targetCategory);
			if (categoryFilter != null)
			{
				_category.SelectedItem = categoryFilter;
			}
			RenderList();
			SelectTexture(x);
			_status.Text = (isOverFrame ? "已还原原始卡图并关闭本卡超框登记，已回到卡图列表。" : "已还原游戏本体中的该 Bundle。");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private async Task ExportSelectedAsync()
	{
		TexRef texRef = Selected();
		if (texRef == null)
		{
			return;
		}
		SaveFileDialog d = new SaveFileDialog
		{
			Filter = "PNG 图片|*.png",
			FileName = Safe(texRef.Name) + ".png"
		};
		try
		{
			if (d.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			try
			{
				string fileName = d.FileName;
				GameTextureDisplayMapping mapping = DisplayMappingFor(texRef);
				string path = fileName;
				await File.WriteAllBytesAsync(path, await DecodeForDisplayAsync(texRef));
				_status.Text = (mapping.RequiresMapping ? $"已按正常比例 {mapping.DisplayWidth}×{mapping.DisplayHeight} 导出：{d.FileName}" : ("已导出当前游戏内卡图：" + d.FileName));
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "导出 PNG 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
		finally
		{
			((IDisposable)d)?.Dispose();
		}
	}

	private async Task DragOutAsync(ListViewItem? item)
	{
		if (!(item?.Tag is TexRef texRef))
		{
			return;
		}
		try
		{
			string path = Path.Combine(Path.GetTempPath(), "MDCardModTool", Safe(texRef.Name) + "_" + texRef.PathId + ".png");
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string path2 = path;
			await File.WriteAllBytesAsync(path2, await DecodeForDisplayAsync(texRef));
			_list.DoDragDrop(new DataObject(DataFormats.FileDrop, new string[1] { path }), DragDropEffects.Copy);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "拖出 PNG 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void OnDragEnter(object? sender, DragEventArgs e)
	{
		IDataObject data = e.Data;
		e.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
	}

	private async Task OnDragDropAsync(DragEventArgs e)
	{
		if (e.Data?.GetData(DataFormats.FileDrop) is string[] array && array.Length != 0 && new string[5] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(array[0]).ToLowerInvariant()))
		{
			await ReplaceSelectedAsync(array[0]);
		}
	}

	private async Task InspectSelectedAsync()
	{
		TexRef x = Selected();
		if (x == null || _assetRoot == null)
		{
			return;
		}
		if (BuiltInCardFrameCatalog.IsPackagedFrame(x))
		{
			string value = (BuiltInCardFrameCatalog.IsTransparentGradientFrame(x) ? "ASTELLAR-NOTICE.txt 与 FLOOWAN-NOTICE.txt" : (BuiltInCardFrameCatalog.IsTransparentFrame(x) ? "ASTELLAR-NOTICE.txt" : "FLOOWAN-NOTICE.txt"));
			MessageBox.Show(this, $"内置{x.Category}：{x.Name} · {CardFrameCatalog.FriendlyName(x.Name)}\n704×1024 PNG\n\n该资源为只读模板；来源与许可详见 {value}。", "卡框资源");
			return;
		}
		try
		{
			BundleSummary bundleSummary = await Task.Run(() => _engine.InspectBundle(x.BundlePath, _assetRoot));
			MessageBox.Show(this, $"Bundle: {bundleSummary.RelativePath}\nSerialized 文件: {bundleSummary.SerializedFiles}\n\nAsset 类型：\n{bundleSummary.Describe()}", "Bundle 检查");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "检查失败");
		}
	}

	private async Task ApplyOverFrameTagsAsync()
	{
		string? root = _gameRoot;
		string? local = _assetRoot;
		int generation = _scanGeneration;
		if (root == null)
		{
			return;
		}
		try
		{
			HashSet<string> hashSet = (await Task.Run(() => _overFrames.ReadCached(root))).Select((OverFrameMapping overFrameMapping) => overFrameMapping.CardId.ToString()).ToHashSet<string>(StringComparer.Ordinal);
			if (IsDisposed || generation != _scanGeneration || root != _gameRoot || local != _assetRoot) return;
			foreach (TexRef item in _textures.Where((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey.Length > 0))
			{
				if (hashSet.Contains(item.CardKey))
				{
					item.Category = "超框卡图";
				}
				else if (item.Category == "超框卡图")
				{
					IndexService.NormalizeLocalCardCategory(item);
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyMonsterAnimationTags()
	{
		HashSet<string> hashSet = MonsterAnimationIndexService.LoadBundledCardIds();
		if (_gameRoot != null)
		{
			try
			{
				string buildId;
				List<MonsterAnimationAssetRef> assets = MonsterAnimationIndexService.LoadBestAvailable(_gameRoot, out buildId);
				hashSet.UnionWith(MonsterAnimationIndexService.CompleteCardIds(assets));
			}
			catch
			{
			}
		}
		hashSet = ExpandEquivalentAnimationIds(hashSet);
		foreach (TexRef texture in _textures)
		{
			texture.HasMonsterAnimation = texture.SourceKind == "本地卡图" && hashSet.Contains(texture.CardKey);
		}
		int num = _textures.Count((TexRef x) => x.HasMonsterAnimation);
		if (num > 0)
		{
			_status.Text += $" 已标记 {num:N0} 张有怪兽召唤动画的卡图。";
		}
	}

	private HashSet<string> ExpandEquivalentAnimationIds(IEnumerable<string> directIds)
	{
		HashSet<string> hashSet = directIds.ToHashSet<string>(StringComparer.Ordinal);
		string[] array = hashSet.ToArray();
		for (int i = 0; i < array.Length; i++)
		{
			if (!int.TryParse(array[i], out var result))
			{
				continue;
			}
			CardCatalogEntry cardCatalogEntry = _cardCatalog.Find(result);
			if (cardCatalogEntry == null)
			{
				continue;
			}
			foreach (CardCatalogEntry item in _cardCatalog.FindEquivalentCards(cardCatalogEntry))
			{
				hashSet.Add(item.CardId.ToString());
			}
		}
		return hashSet;
	}

	private bool HasAnimationIdOrEquivalent(string cardId, ISet<string> directIds)
	{
		if (directIds.Contains(cardId))
		{
			return true;
		}
		CardCatalogEntry cardCatalogEntry = _cardCatalog.Find(cardId);
		if (cardCatalogEntry != null)
		{
			return _cardCatalog.FindEquivalentCards(cardCatalogEntry).Any((CardCatalogEntry equivalent) => directIds.Contains(equivalent.CardId.ToString()));
		}
		return false;
	}

	private void StartGameBuildRefresh()
	{
		if (!_allowBackgroundRefresh)
		{
			return;
		}
		CancellationTokenSource previousCancellation = _backgroundRefreshCancellation;
		Task backgroundRefreshTask = _backgroundRefreshTask;
		previousCancellation?.Cancel();
		if (previousCancellation != null)
		{
			if (backgroundRefreshTask == null || backgroundRefreshTask.IsCompleted)
			{
				previousCancellation.Dispose();
			}
			else
			{
				backgroundRefreshTask.ContinueWith(delegate
				{
					previousCancellation.Dispose();
				}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			}
		}
		if (_gameRoot == null || _index == null)
		{
			_backgroundRefreshCancellation = null;
			_backgroundRefreshTask = null;
		}
		else
		{
			_backgroundRefreshCancellation = new CancellationTokenSource();
			_backgroundRefreshTask = RefreshGameBuildDataAsync(_gameRoot, _index, _backgroundRefreshCancellation.Token);
		}
	}

	private async Task RefreshGameBuildDataAsync(string gameRoot, GameIndex index, CancellationToken cancellationToken)
	{
		try
		{
			string localRoot = IndexService.FindLocalRoot(gameRoot) ?? "";
			int newCards = 0;
			if (await Task.Run(() => GameCardCatalogUpdater.NeedsUpdate(gameRoot), cancellationToken))
			{
				PostBackgroundStatus("检测到游戏资源、Build 或账号变化，正在后台读取 card_name / card_indx / card_prop…");
				Action<int, int, int> catalogProgress = CreateThrottledBackgroundProgress(250, (int done, int total, int found) => $"正在更新四语卡片目录：{done:N0}/{total:N0} Bundle · 已定位 {found}/9 项数据…");
				await Task.Run(() => GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, catalogProgress, cancellationToken), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsSameWorkspace(gameRoot, localRoot, index))
				{
					return;
				}
				_cardCatalog = CardCatalogService.LoadBestAvailable();
				UpdatePageHeader();
				RenderList();
				Action<int, int, int> missingCardProgress = CreateThrottledBackgroundProgress(250, (int done, int total, int added) => $"正在合并新卡图：{done:N0}/{total:N0} · 新增 {added:N0}…");
				MissingCardScanResult missingCardScanResult = await Task.Run(() => IndexService.ScanMissingLocalCard(gameRoot, index, "0", missingCardProgress, cancellationToken), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsSameWorkspace(gameRoot, localRoot, index))
				{
					return;
				}
				HashSet<string> known = index.Textures.Select((TexRef x) => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}").ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (TexRef item in missingCardScanResult.Textures.Where((TexRef x) => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")))
				{
					IndexService.NormalizeLocalCardCategory(item);
					index.Textures.Add(item);
					_textures.Add(item);
					newCards++;
				}
				await Task.Run(delegate
				{
					IndexService.Save(gameRoot, index);
				}, cancellationToken);
			}
			PostBackgroundStatus("正在校验当前 Build 的官方怪兽动画索引；首次更新可能需要数分钟…");
			Action<int, int, int> animationProgress = CreateThrottledBackgroundProgress(100, (int done, int total, int found) => $"正在更新动画索引：{done:N0}/{total:N0} Bundle · {found:N0} 项资源…");
			PortableMonsterAnimationIndex index2 = await Task.Run(() => MonsterAnimationIndexService.EnsureCurrentIndex(gameRoot, animationProgress, cancellationToken), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSameWorkspace(gameRoot, localRoot, index))
			{
				return;
			}
			HashSet<string> hashSet = MonsterAnimationIndexService.CompleteCardIds(index2);
			HashSet<string> hashSet2 = ExpandEquivalentAnimationIds(hashSet);
			foreach (TexRef texture in _textures)
			{
				if (texture.SourceKind == "本地卡图")
				{
					texture.HasMonsterAnimation = hashSet2.Contains(texture.CardKey);
				}
			}
			RefreshCategories();
			UpdatePageHeader();
			RenderList();
			_status.Text = $"当前 Build 增量更新完成：四语卡片 {_cardCatalog.Count:N0} 张，新卡图 {newCards:N0} 张，官方动画 {hashSet.Count:N0} 套（含同名异画映射 {hashSet2.Count:N0} 张）。";
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!base.IsDisposed)
			{
				_status.Text = "后台 Build 增量更新未完成，现有索引仍可使用：" + ex2.Message;
			}
		}
	}

	private bool IsSameWorkspace(string gameRoot, string localRoot, GameIndex index)
	{
		if (!base.IsDisposed && _index == index && string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(IndexService.FindLocalRoot(gameRoot), localRoot, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private Action<int, int, int> CreateThrottledBackgroundProgress(int minimumItemDelta, Func<int, int, int, string> formatter)
	{
		object progressGate = new object();
		int lastReported = -Math.Max(1, minimumItemDelta);
		long nextReportAt = 0L;
		return delegate(int done, int total, int found)
		{
			long tickCount = Environment.TickCount64;
			lock (progressGate)
			{
				if (done < total && (done - lastReported < minimumItemDelta || tickCount < nextReportAt))
				{
					return;
				}
				lastReported = done;
				nextReportAt = tickCount + 150;
			}
			PostBackgroundStatus(formatter(done, total, found));
		};
	}

	private void PostBackgroundStatus(string text)
	{
		if (base.IsDisposed || !base.IsHandleCreated)
		{
			return;
		}
		try
		{
			BeginInvoke(delegate
			{
				if (!base.IsDisposed)
				{
					_status.Text = text;
				}
			});
		}
		catch
		{
		}
	}

	private void OpenFramePreview()
	{
		TexRef art = Selected();
		if (art == null)
		{
			MessageBox.Show(this, "先选择一张卡图，再打开卡框预览。", Text);
			return;
		}
		if ((art.Width != 704 || art.Height != 1024) && (art.Width != 512 || (art.Height != 512 && art.Height != 1024)))
		{
			MessageBox.Show(this, $"当前图片尺寸 {art.Width}×{art.Height} 不是可预览的卡图。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		TexRef[] array = OverFrameFrames();
		if (array.Length == 0)
		{
			MessageBox.Show(this, "索引中没有 card_frame。请点击“重建索引”后重试。", Text);
			return;
		}
		art.PreviewFrameKey = PreferredFrameKeyFor(art);
		FramePreviewForm framePreviewForm = new FramePreviewForm(_engine, art, array);
		framePreviewForm.FormClosed += async delegate
		{
			if (_gameRoot != null)
			{
				await Task.Run(delegate
				{
					IndexService.Save(_gameRoot, _textures);
				});
			}
			if (Selected() == art)
			{
				await ShowSelectionAsync();
			}
		};
		framePreviewForm.Show(this);
	}

	private void OpenOverFrameTable()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		OverFrameForm overFrameManager = _overFrameManager;
		if (overFrameManager != null && !overFrameManager.IsDisposed)
		{
			_overFrameManager.Activate();
		}
		else
		{
			if (_overFrameManagerQueued)
			{
				return;
			}
			_backgroundRefreshCancellation?.Cancel();
			string gameRoot = _gameRoot;
			string selectedCard = Selected()?.CardKey;
			int generation = Interlocked.Increment(ref _overFrameManagerGeneration);
			_overFrameManagerQueued = true;
			BeginInvoke((MethodInvoker)delegate
			{
				_overFrameManagerQueued = false;
				if (generation == Volatile.Read(in _overFrameManagerGeneration) && !base.IsDisposed && string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
				{
					OverFrameForm overFrameManager2 = _overFrameManager;
					if (overFrameManager2 == null || overFrameManager2.IsDisposed)
					{
						OverFrameForm manager = new OverFrameForm(gameRoot, selectedCard);
						_overFrameManager = manager;
						manager.FormClosed += delegate
						{
							if (_overFrameManager == manager)
							{
								_overFrameManager = null;
								if (!base.IsDisposed && !base.Disposing && _gameRoot != null && _index != null)
								{
									StartGameBuildRefresh();
								}
							}
						};
						manager.Show(this);
					}
				}
			});
		}
	}

	private void OpenMonsterAnimation()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		string text = Selected()?.CardKey;
		if (string.IsNullOrWhiteSpace(text) && _search.Text.Trim().All(char.IsAsciiDigit))
		{
			text = _search.Text.Trim();
		}
		MonsterAnimationForm monsterAnimationForm = new MonsterAnimationForm(_gameRoot, text);
		monsterAnimationForm.FormClosed += async delegate
		{
			await RefreshModFlagsAsync();
			RefreshCategories();
			RenderList();
		};
		monsterAnimationForm.Show(this);
	}

	private void OpenRawAnimationAssets()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		TexRef texRef = Selected();
		if (texRef == null || !texRef.HasMonsterAnimation)
		{
			MessageBox.Show(this, "请先在“有怪兽动画”分类中选择一张卡图。", Text);
			return;
		}
		MonsterAnimationRawAssetsForm monsterAnimationRawAssetsForm = new MonsterAnimationRawAssetsForm(_gameRoot, texRef.CardKey);
		monsterAnimationRawAssetsForm.FormClosed += async delegate
		{
			await RefreshModFlagsAsync();
			RefreshCategories();
			RenderList();
		};
		monsterAnimationRawAssetsForm.Show(this);
	}

	private TexRef[] OverFrameFrames()
	{
		return _textures.Where((TexRef x) => BuiltInCardFrameCatalog.IsPackagedFrame(x) && x.Width == 704 && x.Height == 1024).ToArray();
	}

	private TexRef? PreviewFrameFor(TexRef texture)
	{
		if (texture.SourceKind != "本地卡图" || texture.Width != 512 || (texture.Height != 512 && texture.Height != 1024))
		{
			return null;
		}
		TexRef[] source = CardFrameCatalog.CompatibleFrames(OverFrameFrames(), texture.Width, texture.Height).ToArray();
		string wanted = PreferredFrameKeyFor(texture);
		return source.FirstOrDefault((TexRef x) => x.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? source.FirstOrDefault();
	}

	private string PreferredFrameKeyFor(TexRef texture)
	{
		return CardFrameCatalog.RecommendedKey(_cardCatalog.Find(texture.CardKey), texture.Width, texture.Height);
	}

	private async Task OpenCardFrameStudioAsync()
	{
		TexRef texRef = Selected();
		if (texRef == null || _gameRoot == null || texRef.SourceKind != "本地卡图" || !ushort.TryParse(texRef.CardKey, out var _))
		{
			MessageBox.Show(this, "请先选择一张本地卡图。", Text);
		}
		else
		{
			await OpenFrameEditorAsync(texRef);
		}
	}

	private async Task<bool> OpenFrameEditorAsync(TexRef? texture = null, byte[]? initialArt = null, string? initialFrameKey = null, byte[]? initialBackground = null, bool replaceStoredBackground = false)
	{
		TexRef texRef = texture ?? Selected();
		if (texRef == null || _gameRoot == null)
		{
			MessageBox.Show(this, "先选择一张本地卡图。", Text);
			return false;
		}
		if (texRef.SourceKind != "本地卡图" || !ushort.TryParse(texRef.CardKey, out var cardId))
		{
			MessageBox.Show(this, "制作超框只能用于名称为卡号的“本地卡图”。", Text);
			return false;
		}
		TexRef[] array = OverFrameFrames();
		if (array.Length == 0)
		{
			MessageBox.Show(this, "索引中没有 704×1024 card_frame。请点击“重建索引”后重试。", Text);
			return false;
		}
		if (string.IsNullOrWhiteSpace(initialFrameKey))
		{
			OverFrameFrameSettings overFrameFrameSettings = OverFrameArtStore.ReadSettings(_gameRoot, cardId);
			if (!OverFrameArtStore.HasSettings(_gameRoot, cardId) || (!overFrameFrameSettings.UserSelected && !overFrameFrameSettings.UsesCustomFrame))
			{
				initialFrameKey = PreferredFrameKeyFor(texRef);
			}
		}
		using OverFrameFrameEditorForm editor = new OverFrameFrameEditorForm(_gameRoot, texRef, array, initialArt, initialFrameKey, initialBackground, replaceStoredBackground);
		if (editor.ShowDialog(this) != DialogResult.OK)
		{
			return false;
		}
		await RefreshModFlagsAsync();
		await Task.Run(delegate
		{
			IndexService.Save(_gameRoot, _textures);
		});
		RefreshCategories();
		RenderList();
		_status.Text = $"卡号 {cardId} 已应用单卡卡框：{editor.AppliedFrameName}。请完全退出并重启 Master Duel。";
		return true;
	}

	private async Task ExportAllModsAsync()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = Localizer.T("mods.export.filter"),
			FileName = $"MD_Mods_{DateTime.Now:yyyyMMdd_HHmm}.zip",
			DefaultExt = "zip",
			Title = "导出全部已启用 Mod"
		};
		try
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			try
			{
				base.UseWaitCursor = true;
				_status.Text = "正在打包全部已修改 Bundle…";
				bool directReplacement = dialog.FilterIndex == 1;
				TexRef[] exportTextures = _textures.ToArray();
				ModPackageInfo modPackageInfo = await Task.Run(() => _mods.Export(_gameRoot, exportTextures, dialog.FileName, directReplacement));
				_status.Text = $"已导出 {modPackageInfo.BundleCount:N0} 个 Mod Bundle：{dialog.FileName}";
				MessageBox.Show(this, $"导出完成。\n\nBundle：{modPackageInfo.BundleCount:N0} 个\n原始大小：{FormatSize(modPackageInfo.TotalSize)}\n文件：{dialog.FileName}" + (directReplacement ? ("\n\n" + Localizer.T("mods.export.directHelp")) : ""), "全部 Mod 已导出", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "导出 Mod 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				base.UseWaitCursor = false;
			}
		}
		finally
		{
			if (dialog != null)
			{
				((IDisposable)dialog).Dispose();
			}
		}
	}

	private async Task ImportModsAsync()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = Localizer.T("mods.import.filter"),
			Title = "导入 MD Mod 包"
		};
		try
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			try
			{
				ModPackageInfo modPackageInfo = await Task.Run(() => _mods.Inspect(dialog.FileName));
				string text = $"准备导入“{modPackageInfo.Name}”。\n\nBundle：{modPackageInfo.BundleCount:N0} 个\n原始大小：{FormatSize(modPackageInfo.TotalSize)}\n目标账号：{Path.GetFileName(Path.GetDirectoryName(IndexService.FindLocalRoot(_gameRoot)))}\n\n请先退出游戏。LocalData 将映射到当前选中账号；每个文件首次覆盖前都会保存原版备份。继续？";
				if (MessageBox.Show(this, text, "确认导入 Mod", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
				{
					base.UseWaitCursor = true;
					_status.Text = "正在校验并导入 Mod 包…";
					TexRef[] importTextures = _textures.ToArray();
					ModImportResult result = await Task.Run(() => _mods.Import(_gameRoot, dialog.FileName, importTextures));
					await ReloadChangedBundlesAsync(result.ChangedBundlePaths);
					await ApplyOverFrameTagsAsync();
					await RefreshModFlagsAsync();
					await Task.Run(delegate
					{
						IndexService.Save(_gameRoot, _textures);
					});
					RefreshCategories();
					SetModsOnly(enabled: false);
					SelectAllCategory();
					RenderList();
					_status.Text = $"已导入 {result.BundleCount:N0} 个 Mod Bundle；保留全部资源显示，可在“我的 Mod”筛选。";
					MessageBox.Show(this, $"已导入 {result.BundleCount:N0} 个 Bundle。\n\n现在可在“我的 Mod”中集中查看和还原。请完全退出并重启 Master Duel。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "导入 Mod 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				base.UseWaitCursor = false;
			}
		}
		finally
		{
			if (dialog != null)
			{
				((IDisposable)dialog).Dispose();
			}
		}
	}

	private async Task ReloadChangedBundlesAsync(IReadOnlyList<string> changedPaths)
	{
		if (_gameRoot == null || _assetRoot == null || _streamingRoot == null)
		{
			return;
		}
		foreach (string path in changedPaths.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			TexRef[] existing = _textures.Where((TexRef x) => x.BundlePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
			if (existing.Length != 0)
			{
				string sourceKind = existing[0].SourceKind;
				string root = sourceKind switch
				{
					"本地卡图" => _assetRoot,
					"视觉资源" => _assetRoot,
					"游戏内图片" => _streamingRoot,
					_ => _gameRoot,
				};
				List<TexRef> scanned = await Task.Run(() => _engine.ScanBundle(path, root, sourceKind, includeDependencies: false).Textures);
				TexRef[] array = existing;
				for (int num = 0; num < array.Length; num++)
				{
					CardBundleIdentity.Refresh(array[num], scanned);
				}
			}
		}
	}

	private void OpenBackup()
	{
		if (_gameRoot != null)
		{
			string text = Path.Combine(_gameRoot, "_MD卡图备份");
			Directory.CreateDirectory(text);
			Process.Start("explorer.exe", text);
		}
	}

	private void SelectTexture(TexRef texture)
	{
		int num = _visibleTextures.IndexOf(texture);
		if (num >= 0)
		{
			_list.SelectedIndices.Clear();
			_list.SelectedIndices.Add(num);
			_list.EnsureVisible(num);
			_list.Focus();
		}
	}

	private async Task PreviewSelectedAnimationAsync()
	{
		if (_gameRoot == null)
		{
			MessageBox.Show(this, "先选择 Master Duel 游戏目录。", Text);
			return;
		}
		string text = Selected()?.CardKey;
		if (string.IsNullOrWhiteSpace(text) || !text.All(char.IsAsciiDigit))
		{
			string text2 = _search.Text.Trim();
			text = ((text2.Length > 0 && text2.All(char.IsAsciiDigit)) ? text2 : null);
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			MessageBox.Show(this, "请先在卡片资源中选择一张卡，或在卡片页搜索框输入纯数字卡号。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		ShowPage(WorkspacePage.Animation);
		if (_embeddedAnimation != null && !_embeddedAnimation.IsDisposed)
		{
			await _embeddedAnimation.PreviewCardAsync(text);
		}
	}

	private static string FormatSize(long bytes)
	{
		if (bytes < 1073741824)
		{
			if (bytes < 1048576)
			{
				return $"{(double)bytes / 1024.0:0.##} KB";
			}
			return $"{(double)bytes / 1048576.0:0.##} MB";
		}
		return $"{(double)bytes / 1073741824.0:0.##} GB";
	}

	private static string Safe(string n)
	{
		return string.Concat(n.Select((char c) => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
	}
}
