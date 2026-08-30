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

	private readonly ListBox _searchSuggestions = new()
	{
		BorderStyle = BorderStyle.None,
		DrawMode = DrawMode.OwnerDrawFixed,
		ItemHeight = 52,
		IntegralHeight = false,
		TabStop = false
	};

	private readonly Panel _searchSuggestionPopup = new()
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

	private readonly PictureBox _preview = new PictureBox
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

	private Button _modsOnlyButton = null!;

	private Button _scanMissingButton = null!;

	private Button _visualAssetsButton = null!;

	private readonly ModernComboBox _profileSelector = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly ModernComboBox _languageSelector = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Label _pageTitle = new()
	{
		Dock = DockStyle.Top,
		Height = 31,
		ForeColor = UiTheme.Text,
		Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly Label _pageDescription = new()
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Muted,
		Font = new Font("Microsoft YaHei UI", 9f),
		TextAlign = ContentAlignment.TopLeft
	};

	private readonly Label _catalogBadge = new()
	{
		Dock = DockStyle.Right,
		Width = 220,
		ForeColor = UiTheme.Primary,
		Font = new Font("Segoe UI Semibold", 9f),
		TextAlign = ContentAlignment.MiddleRight
	};

	private readonly TableLayoutPanel _visualShortcutBar = new()
	{
		Dock = DockStyle.Fill,
		ColumnCount = 8,
		RowCount = 1,
		BackColor = UiTheme.SurfaceAlt,
		Padding = new Padding(12, 7, 12, 7),
		Margin = Padding.Empty
	};

	private readonly TableLayoutPanel _resourceActionGrid = new();

	private Control _cardSearchField = null!;

	private readonly Panel _resourceContextBar = new()
	{
		Dock = DockStyle.Fill,
		BackColor = UiTheme.SurfaceAlt,
		Visible = false,
		Margin = Padding.Empty
	};

	private readonly FlowLayoutPanel _modContextActions = new()
	{
		Dock = DockStyle.Fill,
		FlowDirection = FlowDirection.LeftToRight,
		WrapContents = false,
		Padding = new Padding(14, 5, 14, 5),
		BackColor = UiTheme.SurfaceAlt,
		Visible = false
	};

	private readonly FlowLayoutPanel _overFrameContextActions = new()
	{
		Dock = DockStyle.Fill,
		FlowDirection = FlowDirection.LeftToRight,
		WrapContents = false,
		Padding = new Padding(14, 5, 14, 5),
		BackColor = UiTheme.SurfaceAlt,
		Visible = false
	};

	private readonly Panel _resourcePage = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _animationPage = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _framesPage = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _modsPage = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _settingsPage = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _animationHost = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly Panel _framesHost = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Window };

	private readonly List<NavigationButton> _navigationButtons = [];

	private bool _animationWorkspaceQueued;

	private bool _framesWorkspaceQueued;

	private readonly List<VisualShortcut> _visualShortcuts = [];

	private readonly Dictionary<Control, string> _localizedControls = [];

	private readonly AppSettings _settings = AppSettingsStore.Load();

	private CardCatalogService _cardCatalog = CardCatalogService.LoadBestAvailable();

	private readonly Label _modPageSummary = new()
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

	private WorkspacePage _currentPage = WorkspacePage.Cards;

	private bool _changingProfile;

	private bool _changingLanguage;

	private CancellationTokenSource? _previewCancellation;

	private CancellationTokenSource? _backgroundRefreshCancellation;

	private Task? _backgroundRefreshTask;

	private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 180 };

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

	public MainForm()
	{
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
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.KeyPreview = true;
		DoubleBuffered = true;
		_brandImage = LoadBrandImage();
		_windowIcon = LoadWindowIcon();
		if (_windowIcon != null)
		{
			Icon = _windowIcon;
			ShowIcon = true;
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
			e.Item = e.ItemIndex >= 0 && e.ItemIndex < _visibleTextures.Count
				? CreateListItem(_visibleTextures[e.ItemIndex])
				: new ListViewItem("");
		};
		_list.SearchForVirtualItem += delegate(object? _, SearchForVirtualItemEventArgs e)
		{
			if (_visibleTextures.Count == 0 || string.IsNullOrWhiteSpace(e.Text))
			{
				return;
			}
			int start = Math.Clamp(e.StartIndex, 0, _visibleTextures.Count - 1);
			for (int offset = 0; offset < _visibleTextures.Count; offset++)
			{
				int index = (start + offset) % _visibleTextures.Count;
				if (DisplayResourceName(_visibleTextures[index]).StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase))
				{
					e.Index = index;
					break;
				}
			}
		};
		ConfigureSearchSuggestions();
		_list.Columns.Add("资源名称", 205);
		_list.Columns.Add("来源 / 分类", 190);
		_list.Columns.Add("尺寸", 100);
		_list.Columns.Add("Bundle 路径", 360);
		_list.Resize += delegate { ResizeResourceColumns(); };
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
		_search.ImeCompositionEnded += delegate { ScheduleSearchRefresh(); };
		_search.KeyDown += async delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode is Keys.Down or Keys.Up && _searchSuggestionPopup.Visible && _searchSuggestions.Items.Count > 0)
			{
				int delta = e.KeyCode == Keys.Down ? 1 : -1;
				_searchSuggestions.SelectedIndex = Math.Clamp(_searchSuggestions.SelectedIndex + delta, 0, _searchSuggestions.Items.Count - 1);
				e.Handled = true;
				e.SuppressKeyPress = true;
				return;
			}
			if (e.KeyCode == Keys.Escape && _searchSuggestionPopup.Visible)
			{
				HideSearchSuggestions();
				e.Handled = true;
				e.SuppressKeyPress = true;
				return;
			}
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				CardCatalogEntry? preferred = (_searchSuggestions.SelectedItem as SearchSuggestion)?.Entry;
				await ScanMissingCardAsync(preferred);
			}
		};
		_category.Items.Add(new CategoryFilter(CategoryFilterKind.All, "", Localizer.T("filter.all")));
		_category.SelectedIndex = 0;
		_category.SelectedIndexChanged += delegate
		{
			if (_changingCategory)
			{
				return;
			}
			_activeVisualShortcut = null;
			UpdateVisualShortcutStyles();
			RenderList();
		};
		_groups.AfterSelect += delegate(object? _, TreeViewEventArgs e)
		{
			SelectGroup(e.Node?.Tag as string);
		};
		BuildInterface();
		_profileSelector.SelectedIndexChanged += async delegate
		{
			if (!_changingProfile && _profileSelector.SelectedItem is LocalDataProfile profile && _gameRoot != null)
			{
				_backgroundRefreshCancellation?.Cancel();
				IndexService.SetPreferredLocalRoot(_gameRoot, profile.RootPath);
				_settings.LastProfileByGame[Path.GetFullPath(_gameRoot)] = profile.AccountId;
				AppSettingsStore.Save(_settings);
				SetGameRoot();
				ResetEmbeddedWorkspaces();
				await ScanAsync();
			}
		};
		_languageSelector.SelectedIndexChanged += delegate
		{
			if (!_changingLanguage && _languageSelector.SelectedItem is LanguageChoice choice)
			{
				_settings.Language = choice.Language;
				AppSettingsStore.Save(_settings);
				Localizer.SetLanguage(choice.Language);
				ApplyLanguage();
				RenderList();
			}
		};
		Localizer.LanguageChanged += OnLanguageChanged;
		FormClosed += delegate
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
			_brandImage?.Dispose();
			_windowIcon?.Dispose();
		};
		ApplyLanguage();
		GameInstallation? discovered = SteamGameDiscovery.Discover(_settings.LastGameRoot).FirstOrDefault();
		if (discovered != null)
		{
			ApplyInstallation(discovered);
		}
		base.Shown += async delegate
		{
			if (_resourceSplit is { Width: > 650 })
			{
				_resourceSplit.SplitterDistance = 235;
			}
			if (_workspaceSplit is { Width: > 1150 })
			{
				_workspaceSplit.SplitterDistance = Math.Min((int)(_workspaceSplit.Width * 0.66), _workspaceSplit.Width - 410);
			}
			if (_assetRoot != null)
			{
				await ScanAsync();
			}
		};
	}

	private void BuildInterface()
	{
		Button choose = Bind(Button("", async delegate { await ChooseGameAsync(); }), "top.choose");
		Button scan = Bind(Button("", async delegate { await RebuildIndexAsync(); }), "action.rebuild");
		Button replace = Bind(Button("", async delegate { await ReplaceSelectedAsync(); }, ButtonTone.Primary), "action.replace");
		Button export = Bind(Button("", async delegate { await ExportSelectedAsync(); }), "action.export");
		Button backup = Bind(Button("", delegate { OpenBackup(); }), "action.backup");
		Button restore = Bind(Button("", async delegate { await RestoreSelectedAsync(); }, ButtonTone.Danger), "action.restore");
		Button inspect = Bind(Button("", async delegate { await InspectSelectedAsync(); }), "action.inspect");
		Button cardFrameStudio = Bind(Button("", async delegate { await OpenCardFrameStudioAsync(); }, ButtonTone.Gold), "action.frame.studio");
		Button animationPreview = Bind(Button("", async delegate { await PreviewSelectedAnimationAsync(); }, ButtonTone.Primary), "action.animation.preview");
		_visualAssetsButton = Bind(Button("", async delegate { await LoadVisualAssetsAsync(); }, ButtonTone.Gold), "action.visuals");
		_modsOnlyButton = Bind(Button("", delegate { ToggleModsOnly(); }, ButtonTone.Gold), "action.mods.only");
		_scanMissingButton = Bind(Button("", async delegate { await ScanMissingCardAsync(); }), "action.locate");
		Button exportMods = Bind(Button("", async delegate { await ExportAllModsAsync(); }, ButtonTone.Primary), "action.mods.export");
		Button importMods = Bind(Button("", async delegate { await ImportModsAsync(); }), "action.mods.import");

		TableLayoutPanel gameRow = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 8,
			RowCount = 1,
			Margin = Padding.Empty
		};
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194f));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
		gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		gameRow.Controls.Add(Bind(Caption(""), "top.game"), 0, 0);
		gameRow.Controls.Add(UiTheme.Field(_gameFolder), 1, 0);
		gameRow.Controls.Add(choose, 2, 0);
		gameRow.Controls.Add(Bind(Caption(""), "top.account"), 3, 0);
		gameRow.Controls.Add(UiTheme.Field(_profileSelector), 4, 0);
		gameRow.Controls.Add(Bind(Caption(""), "top.language"), 5, 0);
		gameRow.Controls.Add(UiTheme.Field(_languageSelector), 6, 0);
		gameRow.Controls.Add(scan, 7, 0);

		TableLayoutPanel topBar = new()
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(16, 10, 16, 10),
			BackColor = UiTheme.Surface,
			ColumnCount = 1,
			RowCount = 1
		};
		topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		topBar.Controls.Add(gameRow, 0, 0);

		TableLayoutPanel cardSearchRow = new()
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 7, 14, 7),
			BackColor = UiTheme.SurfaceAlt,
			ColumnCount = 2,
			RowCount = 1
		};
		cardSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		cardSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236f));
		_cardSearchField = UiTheme.Field(_search);
		cardSearchRow.Controls.Add(_cardSearchField, 0, 0);
		cardSearchRow.Controls.Add(UiTheme.Field(_category), 1, 0);
		BuildVisualShortcutBar();

		BorderPanel categoryPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Margin = new Padding(0, 0, 8, 0)
		};
		categoryPanel.Controls.Add(_groups);
		categoryPanel.Controls.Add(SectionHeading("资源分类", "RESOURCE GROUPS"));
		BorderPanel listPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Margin = new Padding(8, 0, 0, 0)
		};
		Panel listHeader = SectionHeading("图片资源", "拖入替换 · 拖出导出");
		listHeader.Controls.Add(_resultCount);
		_resultCount.BringToFront();
		listPanel.Controls.Add(_list);
		listPanel.Controls.Add(listHeader);
		_resourceSplit = new SplitContainer
		{
			Dock = DockStyle.Fill,
			FixedPanel = FixedPanel.Panel1,
			SplitterDistance = 235,
			SplitterWidth = 8,
			IsSplitterFixed = false,
			BackColor = UiTheme.Window
		};
		_resourceSplit.Panel1.Controls.Add(categoryPanel);
		_resourceSplit.Panel2.Controls.Add(listPanel);
		BorderPanel previewFrame = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		previewFrame.Controls.Add(_preview);
		previewFrame.Controls.Add(_previewHint);
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
		for (int row = 0; row < 4; row++)
		{
			_resourceActionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		}
		Button[] resourceActions = [replace, export, restore, backup, _scanMissingButton, inspect, cardFrameStudio, animationPreview];
		for (int i = 0; i < resourceActions.Length; i++)
		{
			Button action = resourceActions[i];
			action.AutoSize = false;
			action.Dock = DockStyle.Fill;
			action.MinimumSize = Size.Empty;
			action.Margin = new Padding(4, 3, 4, 3);
			_resourceActionGrid.Controls.Add(action, i % 2, i / 2);
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
		previewLayout.Controls.Add(previewFrame, 0, 1);
		previewLayout.Controls.Add(_info, 0, 2);
		previewLayout.Controls.Add(_resourceActionGrid, 0, 3);
		Panel left = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 14, 7, 14),
			BackColor = UiTheme.Window
		};
		left.Controls.Add(_resourceSplit);
		Panel right = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(7, 14, 14, 14),
			BackColor = UiTheme.Window
		};
		right.Controls.Add(previewLayout);
		_workspaceSplit = new SplitContainer
		{
			Dock = DockStyle.Fill,
			SplitterDistance = 900,
			SplitterWidth = 6,
			BackColor = UiTheme.Border
		};
		_workspaceSplit.Panel1.Controls.Add(left);
		_workspaceSplit.Panel2.Controls.Add(right);
		TableLayoutPanel resourceLayout = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = UiTheme.Window
		};
		_visualShortcutRow = new RowStyle(SizeType.Absolute, 0f);
		_resourceContextRow = new RowStyle(SizeType.Absolute, 0f);
		resourceLayout.RowStyles.Add(_visualShortcutRow);
		resourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		resourceLayout.RowStyles.Add(_resourceContextRow);
		resourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		resourceLayout.Controls.Add(_visualShortcutBar, 0, 0);
		resourceLayout.Controls.Add(cardSearchRow, 0, 1);
		resourceLayout.Controls.Add(_resourceContextBar, 0, 2);
		resourceLayout.Controls.Add(_workspaceSplit, 0, 3);
		_resourcePage.Controls.Add(resourceLayout);

		BuildAnimationPage();
		BuildResourceContextBar(exportMods, importMods);
		BuildSettingsPage();
		_pageHost.Controls.AddRange([_settingsPage, _animationPage, _resourcePage]);

		TableLayoutPanel navigation = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 5,
			Padding = new Padding(8, 10, 8, 10),
			BackColor = UiTheme.Surface,
			AutoScroll = true
		};
		navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < 4; i++) navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
		navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		AddNavigation(navigation, WorkspacePage.Cards, "nav.cards");
		AddNavigation(navigation, WorkspacePage.Visuals, "nav.visuals");
		AddNavigation(navigation, WorkspacePage.Animation, "nav.animation");
		AddNavigation(navigation, WorkspacePage.Settings, "nav.settings");
		PictureBox brandIcon = new()
		{
			Dock = DockStyle.Fill,
			Image = _brandImage,
			SizeMode = PictureBoxSizeMode.Zoom,
			Margin = new Padding(0, 4, 12, 4),
			BackColor = Color.Transparent
		};
		Label brandTitle = new()
		{
			Text = "MD CARD STUDIO",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI Semibold", 13f),
			ForeColor = UiTheme.Text,
			TextAlign = ContentAlignment.BottomLeft
		};
		Label brandSubtitle = new()
		{
			Text = "MASTER DUEL MOD WORKSPACE",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 7.5f),
			ForeColor = UiTheme.Primary,
			TextAlign = ContentAlignment.TopLeft
		};
		TableLayoutPanel brandText = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
		brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 56f));
		brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));
		brandText.Controls.Add(brandTitle, 0, 0);
		brandText.Controls.Add(brandSubtitle, 0, 1);
		TableLayoutPanel brandCard = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(14, 13, 12, 10),
			BackColor = UiTheme.Surface
		};
		brandCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
		brandCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		brandCard.Controls.Add(brandIcon, 0, 0);
		brandCard.Controls.Add(brandText, 1, 0);
		Label sidebarFooter = new()
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(18, 10, 14, 10),
			ForeColor = UiTheme.Muted,
			BackColor = UiTheme.Surface,
			Font = new Font("Segoe UI", 8f),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = $"v2.0.1  ·  ASTELLAR CATALOG\n{_cardCatalog.Count:N0} MULTILINGUAL CARDS"
		};
		TableLayoutPanel sidebar = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = UiTheme.Surface
		};
		sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
		sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
		sidebar.Controls.Add(brandCard, 0, 0);
		sidebar.Controls.Add(navigation, 0, 1);
		sidebar.Controls.Add(sidebarFooter, 0, 2);

		TableLayoutPanel pageHeader = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Padding = new Padding(22, 8, 20, 6),
			BackColor = UiTheme.Window
		};
		pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230f));
		pageHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		pageHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		pageHeader.Controls.Add(_pageTitle, 0, 0);
		pageHeader.Controls.Add(_pageDescription, 0, 1);
		pageHeader.Controls.Add(_catalogBadge, 1, 0);
		pageHeader.SetRowSpan(_catalogBadge, 2);

		TableLayoutPanel mainArea = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			BackColor = UiTheme.Window
		};
		mainArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		mainArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
		mainArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		mainArea.Controls.Add(topBar, 0, 0);
		mainArea.Controls.Add(pageHeader, 0, 1);
		mainArea.Controls.Add(_pageHost, 0, 2);

		TableLayoutPanel body = new()
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = UiTheme.Window,
			Padding = new Padding(0, 1, 0, 0)
		};
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228f));
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		body.Controls.Add(sidebar, 0, 0);
		body.Controls.Add(mainArea, 1, 0);

		_status.Spring = true;
		_status.TextAlign = ContentAlignment.MiddleLeft;
		_status.ForeColor = UiTheme.Muted;
		_status.Font = new Font("Microsoft YaHei UI", 8.5f);
		StatusStrip status = new StatusStrip
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			ForeColor = UiTheme.Muted,
			SizingGrip = false,
			Padding = new Padding(12, 0, 12, 0)
		};
		status.Items.Add(_status);
		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Window,
			RowCount = 2,
			ColumnCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
		root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		root.Controls.Add(body, 0, 0);
		root.Controls.Add(status, 0, 1);
		base.Controls.Add(root);
		base.Controls.Add(_searchSuggestionPopup);
		_searchSuggestionPopup.BringToFront();
		SizeChanged += delegate
		{
			if (_searchSuggestionPopup.Visible)
			{
				PositionSearchSuggestions();
			}
		};
		ShowPage(WorkspacePage.Cards);
	}

	private sealed record LanguageChoice(AppLanguage Language, string Label)
	{
		public override string ToString() => Label;
	}

	private sealed record SearchSuggestion(CardCatalogEntry? Entry, string Primary, string Secondary)
	{
		public override string ToString() => Primary;
	}

	private sealed record VisualShortcut(string Key, string ResourceId, string[] Categories, RoundedButton Button);

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
			SearchSuggestion suggestion = (SearchSuggestion)_searchSuggestions.Items[e.Index];
			bool selected = (e.State & DrawItemState.Selected) != 0;
			using SolidBrush background = new(selected ? UiTheme.Selection : UiTheme.Elevated);
			e.Graphics.FillRectangle(background, e.Bounds);
			using Font primary = new(Font, FontStyle.Bold);
			TextRenderer.DrawText(e.Graphics, suggestion.Primary, primary,
				new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 6, e.Bounds.Width - 28, 22),
				selected ? Color.White : UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			TextRenderer.DrawText(e.Graphics, suggestion.Secondary, Font,
				new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 28, e.Bounds.Width - 28, 18),
				selected ? Color.FromArgb(220, Color.White) : UiTheme.Muted,
				TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
		};
		_searchSuggestions.MouseUp += async delegate(object? _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && _searchSuggestions.SelectedItem is SearchSuggestion { Entry: not null } selected)
			{
				HideSearchSuggestions();
				await ScanMissingCardAsync(selected.Entry);
			}
		};
		_searchSuggestionPopup.Controls.Add(_searchSuggestions);
		_search.Leave += delegate
		{
			BeginInvoke((Action)delegate
			{
				if (!_search.ContainsFocus && !_searchSuggestions.ContainsFocus)
				{
					HideSearchSuggestions();
				}
			});
		};
		_searchSuggestions.Leave += delegate
		{
			BeginInvoke((Action)delegate
			{
				if (!_search.ContainsFocus && !_searchSuggestions.ContainsFocus)
				{
					HideSearchSuggestions();
				}
			});
		};
		Deactivate += delegate { HideSearchSuggestions(); };
	}

	private void ShowSearchSuggestions()
	{
		if (!_search.IsHandleCreated || !_search.Focused)
		{
			return;
		}
		string query = _search.Text.Trim();
		if (query.Length == 0)
		{
			HideSearchSuggestions();
			return;
		}
		IReadOnlyList<CardCatalogEntry> matches = _cardCatalog.Search(query, 50);
		_searchSuggestions.BeginUpdate();
		_searchSuggestions.Items.Clear();
		foreach (CardCatalogEntry entry in matches)
		{
			string name = entry.Name(Localizer.Language);
			string kind = string.Join(" · ", new[] { entry.Type, entry.SubType }.Where(value => !string.IsNullOrWhiteSpace(value)));
			_searchSuggestions.Items.Add(new SearchSuggestion(entry, $"{entry.CardId}  ·  {name}",
				kind.Length == 0 ? Localizer.T("search.catalog_match") : kind));
		}
		if (_searchSuggestions.Items.Count == 0)
		{
			_searchSuggestions.Items.Add(new SearchSuggestion(null, Localizer.T("search.no_results"), Localizer.T("search.no_results_hint")));
		}
		_searchSuggestions.SelectedIndex = 0;
		_searchSuggestions.EndUpdate();
		int width = Math.Max(420, _search.Parent?.Width ?? _search.Width);
		int height = Math.Min(8, _searchSuggestions.Items.Count) * _searchSuggestions.ItemHeight + 2;
		_searchSuggestionHeight = height;
		PositionSearchSuggestions(width);
		_searchSuggestionPopup.Visible = true;
		_searchSuggestionPopup.BringToFront();
	}

	private void PositionSearchSuggestions(int requestedWidth = 0)
	{
		if (!_search.IsHandleCreated || IsDisposed)
		{
			return;
		}
		Control anchor = _search.Parent ?? _search;
		Point screen = anchor.PointToScreen(new Point(0, anchor.Height + 2));
		Point client = PointToClient(screen);
		int width = requestedWidth > 0 ? requestedWidth : anchor.Width;
		width = Math.Min(width, Math.Max(1, ClientSize.Width - client.X));
		_searchSuggestionPopup.Bounds = new Rectangle(client.X, client.Y, width,
			Math.Max(1, _searchSuggestionHeight + 2));
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
		if (_search.IsImeComposing || IsDisposed)
		{
			return;
		}
		RenderList();
		if (!_suppressSearchSuggestions)
		{
			ShowSearchSuggestions();
		}
	}

	private enum CategoryFilterKind
	{
		All,
		Animation,
		Category
	}

	private sealed record CategoryFilter(CategoryFilterKind Kind, string Key, string Label)
	{
		public override string ToString() => Label;
	}

	private T Bind<T>(T control, string resourceId) where T : Control
	{
		_localizedControls[control] = resourceId;
		control.Text = Localizer.T(resourceId);
		return control;
	}

	private void AddNavigation(TableLayoutPanel navigation, WorkspacePage page, string resourceId)
	{
		NavigationButton button = Bind(new NavigationButton
		{
			Page = page,
			Dock = DockStyle.Fill,
			Margin = new Padding(4, 3, 4, 3)
		}, resourceId);
		button.Click += delegate { ShowPage(page); };
		int row = _navigationButtons.Count;
		_navigationButtons.Add(button);
		navigation.Controls.Add(button, 0, row);
	}

	private void BuildVisualShortcutBar()
	{
		_visualShortcutBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		foreach (float weight in new[] { 1f, 1.05f, 1.18f, 1.12f, 1f, 0.82f, 1.12f, 1.24f })
		{
			_visualShortcutBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight));
		}
		AddShortcut("all", "visual.all", []);
		AddShortcut("fields", "visual.fields", ["决斗场地"]);
		AddShortcut("wallpaper", "visual.wallpaper", ["大厅壁纸", "大厅背景"]);
		AddShortcut("sleeves", "visual.sleeves", ["卡套"]);
		AddShortcut("deckcase", "visual.deckcase", ["卡盒"]);
		AddShortcut("coin", "visual.coin", ["硬币"]);
		AddShortcut("profile", "visual.profile", ["头像", "头像框"]);
		_visualAssetsButton.AutoSize = false;
		_visualAssetsButton.Dock = DockStyle.Fill;
		_visualAssetsButton.MinimumSize = Size.Empty;
		_visualAssetsButton.Margin = new Padding(3, 4, 3, 4);
		_visualShortcutBar.Controls.Add(_visualAssetsButton, 7, 0);
	}

	private void AddShortcut(string key, string resourceId, string[] categories)
	{
		RoundedButton button = (RoundedButton)Button(Localizer.T(resourceId), delegate { SelectVisualShortcut(key); });
		button.AutoSize = false;
		button.Dock = DockStyle.Fill;
		button.MinimumSize = Size.Empty;
		button.Margin = new Padding(3, 4, 3, 4);
		button.CornerRadius = 10;
		int column = _visualShortcuts.Count;
		_visualShortcuts.Add(new VisualShortcut(key, resourceId, categories, button));
		_visualShortcutBar.Controls.Add(button, column, 0);
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
		VisualShortcut? shortcut = _visualShortcuts.FirstOrDefault(x => x.Key == _activeVisualShortcut);
		return shortcut == null || shortcut.Categories.Length == 0 || shortcut.Categories.Contains(texture.Category, StringComparer.Ordinal);
	}

	private void UpdateVisualShortcutStyles()
	{
		foreach (VisualShortcut shortcut in _visualShortcuts)
		{
			bool selected = shortcut.Key == _activeVisualShortcut;
			shortcut.Button.NormalColor = selected ? Color.FromArgb(29, 74, 103) : UiTheme.Elevated;
			shortcut.Button.HoverColor = selected ? Color.FromArgb(36, 91, 123) : UiTheme.Surface;
			shortcut.Button.BorderColor = selected ? UiTheme.Primary : UiTheme.Border;
			shortcut.Button.ForeColor = selected ? Color.White : UiTheme.Muted;
			shortcut.Button.BackColor = shortcut.Button.NormalColor;
			int count = shortcut.Categories.Length == 0
				? _textures.Count(IsVisualAsset)
				: _textures.Count(x => IsVisualAsset(x) && shortcut.Categories.Contains(x.Category, StringComparer.Ordinal));
			shortcut.Button.Text = $"{Localizer.T(shortcut.ResourceId)}  {count:N0}";
		}
	}

	private static Image? LoadBrandImage()
	{
		foreach (string path in AppPaths.CandidatePaths("app-icon-rounded.png"))
		{
			try
			{
				if (!File.Exists(path)) continue;
				using FileStream stream = File.OpenRead(path);
				using Image source = Image.FromStream(stream);
				return new Bitmap(source);
			}
			catch
			{
			}
		}
		return null;
	}

	private static Icon? LoadWindowIcon()
	{
		foreach (string path in AppPaths.CandidatePaths("app-icon.ico"))
		{
			try
			{
				if (File.Exists(path)) return new Icon(path);
			}
			catch
			{
			}
		}
		try
		{
			using Icon? associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			return associated == null ? null : (Icon)associated.Clone();
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
		Button openBackup = Bind(Button("", delegate { OpenBackup(); }), "action.backup");
		_modContextActions.Controls.Add(ContextLabel("context.mods.title"));
		_modContextActions.Controls.Add(_modsOnlyButton);
		_modContextActions.Controls.Add(exportMods);
		_modContextActions.Controls.Add(importMods);
		_modContextActions.Controls.Add(openBackup);

		Button openTable = Bind(Button("", delegate { OpenOverFrameTable(); }, ButtonTone.Primary), "context.overframe.manage");
		_overFrameContextActions.Controls.Add(ContextLabel("context.overframe.title"));
		_overFrameContextActions.Controls.Add(openTable);
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
		Button overFrameReplace = Button("制作超框", async delegate { await OpenCardFrameStudioAsync(); }, ButtonTone.Gold);
		Button frameEditor = Button("继续编辑超框", async delegate { await OpenFrameEditorAsync(); });
		Button framePreview = Button("预览所选卡框", delegate { OpenFramePreview(); });
		Button refresh = Button("刷新超框表", delegate { EnsureEmbeddedFrames(force: true); });
		FlowLayoutPanel actions = new()
		{
			Dock = DockStyle.Top,
			Height = 55,
			Padding = new Padding(14, 6, 14, 6),
			BackColor = UiTheme.SurfaceAlt,
			FlowDirection = FlowDirection.LeftToRight
		};
		actions.Controls.AddRange([overFrameReplace, frameEditor, framePreview, refresh]);
		_framesPage.Controls.Add(_framesHost);
		_framesPage.Controls.Add(actions);
	}

	private void BuildModsPage(Button exportMods, Button importMods)
	{
		Button openBackup = Button("打开备份目录", delegate { OpenBackup(); });
		FlowLayoutPanel actions = new()
		{
			Dock = DockStyle.Top,
			Height = 58,
			Padding = new Padding(14, 7, 14, 7),
			BackColor = UiTheme.Surface,
			FlowDirection = FlowDirection.LeftToRight
		};
		actions.Controls.AddRange([_modsOnlyButton, exportMods, importMods, openBackup]);
		BorderPanel card = new()
		{
			Dock = DockStyle.Top,
			Height = 155,
			Margin = new Padding(18),
			Padding = new Padding(16),
			BackColor = UiTheme.Surface
		};
		_modPageSummary.Dock = DockStyle.Fill;
		card.Controls.Add(_modPageSummary);
		Panel content = new()
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(22),
			BackColor = UiTheme.Window
		};
		content.Controls.Add(card);
		_modsPage.Controls.Add(content);
		_modsPage.Controls.Add(actions);
	}

	private void BuildSettingsPage()
	{
		Label title = Bind(new Label
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
			Checked = _settings.ReduceMotion || MotionPreferences.WindowsReducesMotion
		}, "settings.motion");
		reduceMotion.CheckedChanged += delegate
		{
			_settings.ReduceMotion = reduceMotion.Checked;
			MotionPreferences.UserReducesMotion = reduceMotion.Checked;
			AppSettingsStore.Save(_settings);
		};
		Label path = new()
		{
			Dock = DockStyle.Top,
			Height = 68,
			ForeColor = UiTheme.Muted,
			Text = Localizer.T("settings.path") + Environment.NewLine + AppSettingsStore.SettingsPath
		};
		_localizedControls[path] = "settings.path";
		BorderPanel card = new()
		{
			Dock = DockStyle.Top,
			Height = 210,
			Padding = new Padding(22),
			BackColor = UiTheme.Surface
		};
		card.Controls.Add(path);
		card.Controls.Add(reduceMotion);
		card.Controls.Add(title);
		card.Margin = Padding.Empty;
		Panel content = new()
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(28),
			BackColor = UiTheme.Window
		};
		content.Controls.Add(card);
		_settingsPage.Controls.Add(content);
	}

	private void ShowPage(WorkspacePage page)
	{
		WorkspacePage previousPage = _currentPage;
		bool previousWasInteractive = PausesBackgroundRefresh(previousPage);
		bool pageIsInteractive = PausesBackgroundRefresh(page);
		if (pageIsInteractive && !previousWasInteractive)
		{
			_backgroundRefreshCancellation?.Cancel();
		}
		_currentPage = page;
		bool cardWorkspace = page == WorkspacePage.Cards;
		if (_cardSearchField != null)
		{
			_cardSearchField.Visible = cardWorkspace;
		}
		if (!cardWorkspace)
		{
			HideSearchSuggestions();
		}
		if (_visualShortcutRow != null)
		{
			_visualShortcutRow.Height = page == WorkspacePage.Visuals ? 62f : 0f;
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
		bool queueAnimationWorkspace = page == WorkspacePage.Animation
			&& (_embeddedAnimation == null || _embeddedAnimation.IsDisposed);
		if (queueAnimationWorkspace)
		{
			// Switch pages first so a fully painted, localized loading state replaces
			// the card workspace before the relatively expensive animation controls
			// are constructed on the UI thread.
			_animationHost.Controls.Clear();
			_animationHost.Controls.Add(EmptyState(Localizer.T("page.animation.loading")));
		}
		_resourcePage.Visible = page is WorkspacePage.Cards or WorkspacePage.Visuals;
		_animationPage.Visible = page == WorkspacePage.Animation;
		_settingsPage.Visible = page == WorkspacePage.Settings;
		foreach (NavigationButton button in _navigationButtons)
		{
			button.Selected = button.Page == page;
			button.Invalidate();
			button.Update();
		}
		UpdatePageHeader();
		UpdateVisualShortcutStyles();
		UpdateResourceContextBar();
		if (page == WorkspacePage.Visuals)
		{
			_ = EnsureVisualAssetsAsync();
		}
		if (page is WorkspacePage.Cards or WorkspacePage.Visuals)
		{
			if (_textures.Count > 0)
			{
				RefreshCategories();
			}
			RenderList();
		}
		Control active = page switch
		{
			WorkspacePage.Animation => _animationPage,
			WorkspacePage.Settings => _settingsPage,
			_ => _resourcePage
		};
		active.BringToFront();
		_pageHost.PerformLayout();
		_pageTitle.Refresh();
		_pageDescription.Refresh();
		_catalogBadge.Refresh();
		_pageHost.Invalidate(true);
		_pageHost.Update();
		if (queueAnimationWorkspace)
		{
			QueueEmbeddedAnimationWorkspace();
		}
		if (!pageIsInteractive && previousWasInteractive && _gameRoot != null && _index != null)
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
		if (_animationWorkspaceQueued || IsDisposed)
		{
			return;
		}
		_animationWorkspaceQueued = true;
		BeginInvoke((Action)(async () =>
		{
			try
			{
				Task? refresh = _backgroundRefreshTask;
				if (refresh != null && !refresh.IsCompleted)
				{
					await refresh;
				}
				if (IsDisposed || _currentPage != WorkspacePage.Animation)
				{
					return;
				}
				EnsureEmbeddedAnimation();
				_animationHost.PerformLayout();
				if (_currentPage == WorkspacePage.Animation)
				{
					_animationPage.Invalidate(true);
					_animationPage.Update();
				}
			}
			catch (Exception ex)
			{
				_embeddedAnimation?.Dispose();
				_embeddedAnimation = null;
				_animationHost.Controls.Clear();
				_animationHost.Controls.Add(EmptyState(Localizer.T("page.animation.error") + Environment.NewLine + ex.Message));
				_animationHost.PerformLayout();
				_animationHost.Invalidate(true);
			}
			finally
			{
				_animationWorkspaceQueued = false;
			}
		}));
	}

	private void QueueEmbeddedFramesWorkspace()
	{
		if (_framesWorkspaceQueued || IsDisposed)
		{
			return;
		}
		_framesWorkspaceQueued = true;
		BeginInvoke((Action)(async () =>
		{
			try
			{
				Task? refresh = _backgroundRefreshTask;
				if (refresh != null && !refresh.IsCompleted)
				{
					await refresh;
				}
				if (IsDisposed || _currentPage != WorkspacePage.Frames)
				{
					return;
				}
				EnsureEmbeddedFrames();
				_framesHost.PerformLayout();
				_framesPage.Invalidate(true);
			}
			catch (Exception ex)
			{
				_embeddedFrames?.Dispose();
				_embeddedFrames = null;
				_framesHost.Controls.Clear();
				_framesHost.Controls.Add(EmptyState(Localizer.T("page.frames.error") + Environment.NewLine + ex.Message));
				_framesHost.PerformLayout();
				_framesHost.Invalidate(true);
			}
			finally
			{
				_framesWorkspaceQueued = false;
			}
		}));
	}

	private static void StabilizeEmbeddedLayout(Control root)
	{
		root.PerformLayout();
		foreach (Control child in root.Controls)
		{
			StabilizeEmbeddedLayout(child);
		}
		// Containers such as TableLayoutPanel depend on their children's preferred
		// sizes, so finish with a bottom-up pass as well.
		root.PerformLayout();
	}

	private void UpdatePageHeader()
	{
		string key = _currentPage switch
		{
			WorkspacePage.Visuals => "visuals",
			WorkspacePage.Animation => "animation",
			WorkspacePage.Settings => "settings",
			_ => "cards"
		};
		_pageTitle.Text = Localizer.T($"page.{key}.title");
		_pageDescription.Text = Localizer.T($"page.{key}.description");
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
		string? initial = Selected()?.CardKey;
		Control[] loadingOverlay = _animationHost.Controls.Cast<Control>().ToArray();
		MonsterAnimationForm candidate = new MonsterAnimationForm(_gameRoot, initial)
		{
			TopLevel = false,
			FormBorderStyle = FormBorderStyle.None,
			MinimumSize = Size.Empty,
			Dock = DockStyle.Fill
		};
		_animationHost.SuspendLayout();
		try
		{
			_animationHost.Controls.Add(candidate);
			foreach (Control overlay in loadingOverlay)
			{
				overlay.BringToFront();
			}
			candidate.Show();
			_embeddedAnimation = candidate;
		}
		catch
		{
			candidate.Dispose();
			throw;
		}
		finally
		{
			_animationHost.ResumeLayout(performLayout: true);
		}
		// A TopLevel=false Form receives two layout waves when its previously hidden
		// parent becomes visible. Finish both waves once, while the opaque loading
		// overlay still covers native child handles, then reveal the finished page.
		StabilizeEmbeddedLayout(candidate);
		foreach (Control overlay in loadingOverlay)
		{
			_animationHost.Controls.Remove(overlay);
			overlay.Dispose();
		}
		candidate.BringToFront();
	}

	private void EnsureEmbeddedFrames(bool force = false)
	{
		if (!force && _embeddedFrames != null && !_embeddedFrames.IsDisposed)
		{
			return;
		}
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

	private static Label EmptyState(string text) => new()
	{
		Dock = DockStyle.Fill,
		Text = text,
		TextAlign = ContentAlignment.MiddleCenter,
		ForeColor = UiTheme.Muted,
		BackColor = UiTheme.Window,
		Font = new Font("Microsoft YaHei UI", 11f)
	};

	private void ResetEmbeddedWorkspaces()
	{
		_overFrameManagerQueued = false;
		Interlocked.Increment(ref _overFrameManagerGeneration);
		OverFrameForm? manager = _overFrameManager;
		_overFrameManager = null;
		manager?.Close();
		manager?.Dispose();
		_embeddedAnimation?.Dispose();
		_embeddedFrames?.Dispose();
		_embeddedAnimation = null;
		_embeddedFrames = null;
		_animationHost.Controls.Clear();
		_framesHost.Controls.Clear();
		if (_currentPage == WorkspacePage.Animation) EnsureEmbeddedAnimation();
		if (_currentPage == WorkspacePage.Frames) EnsureEmbeddedFrames();
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
			LocalDataProfile? preferred = installation.Profiles.FirstOrDefault(x => x.AccountId == preferredId)
				?? installation.Profiles.FirstOrDefault();
			if (preferred != null)
			{
				_profileSelector.SelectedItem = preferred;
				IndexService.SetPreferredLocalRoot(installation.GameRoot, preferred.RootPath);
				_settings.LastProfileByGame[Path.GetFullPath(installation.GameRoot)] = preferred.AccountId;
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
		foreach ((Control control, string id) in _localizedControls)
		{
			control.Text = id == "settings.path"
				? Localizer.T(id) + Environment.NewLine + AppSettingsStore.SettingsPath
				: Localizer.T(id);
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
			_languageSelector.Items.AddRange([
				new LanguageChoice(AppLanguage.SimplifiedChinese, Localizer.T("language.zhcn")),
				new LanguageChoice(AppLanguage.TraditionalChinese, Localizer.T("language.zhtw")),
				new LanguageChoice(AppLanguage.Japanese, Localizer.T("language.ja")),
				new LanguageChoice(AppLanguage.English, Localizer.T("language.en"))
			]);
			_languageSelector.SelectedItem = _languageSelector.Items.Cast<LanguageChoice>().First(x => x.Language == Localizer.Language);
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

	private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

	private static bool IsVisualAsset(TexRef texture) =>
		texture.SourceKind == VisualAssetIndexService.LocalSourceKind
		|| texture.SourceKind == VisualAssetIndexService.BuiltInSourceKind;

	private bool IsAnimationFilterSelected() => _category.SelectedItem is CategoryFilter option && option.Kind == CategoryFilterKind.Animation;

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
			Description = "选择 Yu-Gi-Oh! Master Duel 游戏根目录",
			InitialDirectory = Directory.Exists(_gameRoot) ? _gameRoot : _settings.LastGameRoot
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
			((IDisposable)(object)d)?.Dispose();
		}
	}

	private async Task RebuildIndexAsync()
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
		return IndexService.CachePath(_assetRoot ?? throw new InvalidOperationException("尚未选择 LocalData 账号。"),
			_streamingRoot ?? throw new InvalidOperationException("尚未定位 StreamingAssets。"));
	}

	private async Task ScanAsync(bool forceRebuild = false)
	{
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
				}
				catch
				{
					File.Delete(cache);
				}
			}
			if (cached == null && !forceRebuild)
			{
				try
				{
					_status.Text = "正在载入随程序提供的卡号预绑定索引…";
					GameIndex index;
					string buildId;
					(bool, GameIndex, string) prebuilt = await Task.Run(() => (Found: PortableIndexService.TryLoadBundled(_gameRoot, out index, out buildId), Index: index, BuildId: buildId));
					loadedFromPrebuilt = prebuilt.Item1;
					cached = (loadedFromPrebuilt ? prebuilt.Item2 : null);
					prebuiltBuildId = prebuilt.Item3;
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
			// Current data.unity3d contains unrelated CP/rarity badges named
			// card_frame*.  Always replace stale scanned entries with the curated,
			// transparent Astellar frame templates shipped with the tool.
			int replacedFrames = cached.Textures.RemoveAll(x => x.SourceKind == BuiltInCardFrameCatalog.SourceKind);
			IReadOnlyList<TexRef> packagedFrames = BuiltInCardFrameCatalog.Load();
			cached.Textures.AddRange(packagedFrames);
			int num = IndexService.RemoveSpineAtlasParts(cached);
			int removedNonCards = IndexService.RemoveNonCardLocalTextures(cached);
			if (replacedFrames > 0 || packagedFrames.Count > 0 || num + removedNonCards > 0)
			{
				await Task.Run(delegate
				{
					IndexService.Save(_gameRoot, cached);
				});
			}
			_index = cached;
			_textures.AddRange(cached.Textures);
			string currentBuildId = (loadedFromPrebuilt ? PortableIndexService.GetGameBuildId(_gameRoot) : "");
			string buildNote = ((prebuiltBuildId.Length > 0 && currentBuildId.Length > 0 && prebuiltBuildId != currentBuildId) ? $"；预绑定 Build {prebuiltBuildId}，本机 Build {currentBuildId}，新版卡可用“定位卡图”补充" : "");
			_status.Text = (loadedFromPrebuilt ? $"已用随包预绑定瞬时建立本机索引：{_textures.Count:N0} 张图片，无需首次扫描{buildNote}。" : (forceRebuild ? $"索引已重建：{_textures.Count:N0} 张图片。" : $"已载入本地索引：{_textures.Count:N0} 张图片。"));
			if (cached != null && cached.AlternateArtIndexVersion < 4)
			{
				_status.Text = "正在建立本地异画卡名单（仅首次，需要下载一次百鸽卡片库）…";
				try
				{
					await YgoCdbCardCatalog.ClassifyAlternateArtsAsync(cached);
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
			ApplyMonsterAnimationTags();
			await RefreshModFlagsAsync();
			RefreshCategories();
			RenderList();
			StartGameBuildRefresh();
		}
		catch (Exception ex3)
		{
			MessageBox.Show(this, ex3.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private void RefreshCategories()
	{
		bool wasChangingCategory = _changingCategory;
		_changingCategory = true;
		try
		{
			string old = (_category.SelectedItem as CategoryFilter)?.Key ?? "";
			_category.Items.Clear();
			CategoryFilter all = new(CategoryFilterKind.All, "", Localizer.T("filter.all"));
			_category.Items.Add(all);
			_groups.BeginUpdate();
			_groups.Nodes.Clear();
			int modCount = _textures.Count((TexRef x) => x.IsModded);
			TreeNode treeNode = _groups.Nodes.Add($"{Localizer.T("nav.mods")}（{modCount + _changedAnimationBundleCount}）");
			treeNode.Tag = "__mods__";
			treeNode.ForeColor = UiTheme.Gold;
			IEnumerable<TexRef> visibleSources = _textures.Where(PageMatches);
			foreach (IGrouping<string, TexRef> source in from x in visibleSources
				group x by x.SourceKind into x
				orderby x.Key
				select x)
			{
				TreeNode sourceNode = _groups.Nodes.Add($"{ResourceLabel(source.Key)}（{source.Count()}）");
				if (source.Key == "本地卡图")
				{
					int animationCount = source.Count((TexRef x) => x.HasMonsterAnimation);
					if (animationCount > 0)
					{
						const string animationKey = "local-card|animation";
						_category.Items.Add(new CategoryFilter(CategoryFilterKind.Animation, animationKey, ResourceLabel("有怪兽动画")));
						TreeNode treeNode2 = sourceNode.Nodes.Add($"{ResourceLabel("有怪兽动画")}（{animationCount}）");
						treeNode2.Tag = animationKey;
						treeNode2.ForeColor = UiTheme.Gold;
					}
				}
				foreach (IGrouping<string, TexRef> group in from x in source
					group x by x.Category into x
					orderby x.Key
					select x)
				{
					string key = source.Key + "|" + group.Key;
					_category.Items.Add(new CategoryFilter(CategoryFilterKind.Category, key, ResourceLabel(group.Key)));
					sourceNode.Nodes.Add($"{ResourceLabel(group.Key)}（{group.Count()}）").Tag = key;
				}
				sourceNode.Expand();
			}
			_category.SelectedItem = _category.Items.Cast<CategoryFilter>().FirstOrDefault(x => x.Key == old) ?? all;
			UpdateModSummary();
			UpdateVisualShortcutStyles();
		}
		finally
		{
			_groups.EndUpdate();
			_changingCategory = wasChangingCategory;
		}
	}

	private void RenderList()
	{
		// The multilingual card search belongs to the card workspace. Keeping a
		// previous query active after switching to visuals used to leak card results
		// into unrelated pages even when the search box was no longer meaningful.
		string q = _currentPage == WorkspacePage.Cards ? _search.Text.Trim() : "";
		CategoryFilter filter = _category.SelectedItem as CategoryFilter ?? new CategoryFilter(CategoryFilterKind.All, "", Localizer.T("filter.all"));
		_resultCount.Text = Localizer.T("list.updating");
		_resultCount.Update();
		_list.BeginUpdate();
		try
		{
			_list.VirtualListSize = 0;
			_visibleTextures.Clear();
			bool globalSearch = q.Length > 0;
			HashSet<int>? matchedCardIds = globalSearch
				? _cardCatalog.Search(q, 250).Select(x => x.CardId).ToHashSet()
				: null;
			IEnumerable<TexRef> visible = _textures.Where(texRef =>
				(globalSearch || (PageMatches(texRef) && VisualShortcutMatches(texRef) && (!_modsOnly || texRef.IsModded) && CategoryMatches(texRef, filter)))
				&& MatchesSearch(texRef, q, matchedCardIds));
			if (globalSearch)
			{
				Dictionary<int, int> cardOrder = _cardCatalog.Search(q, 250)
					.Select((entry, index) => (entry.CardId, index))
					.ToDictionary(item => item.CardId, item => item.index);
				visible = visible.OrderBy(texture => int.TryParse(texture.CardKey, out int cardId)
					&& cardOrder.TryGetValue(cardId, out int order) ? order : int.MaxValue)
					.ThenBy(CardTextureRank)
					.ThenByDescending(texture => (long)texture.Width * texture.Height);
			}
			_visibleTextures.AddRange(visible);
			_list.VirtualListSize = _visibleTextures.Count;
			int total = globalSearch ? _textures.Count : _textures.Count(x => PageMatches(x) && VisualShortcutMatches(x) && (!_modsOnly || x.IsModded));
			_resultCount.Text = Localizer.F(globalSearch ? "list.global_count" : "list.count", _visibleTextures.Count, total);
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
			$"{ResourceLabel(texture.SourceKind)} / {ResourceLabel(texture.Category)}{(texture.HasMonsterAnimation ? "  · " + ResourceLabel("动画") : "")}{(texture.IsModded ? "  · MOD" : "")}",
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
		// Reserve the native vertical indicator before distributing the columns.
		// The old fixed 205+190+100 widths guaranteed a bright horizontal scrollbar
		// in the normal two-pane layout shown by the user.
		int available = Math.Max(UiTheme.Scale(_list, 360), _list.ClientSize.Width
			- SystemInformation.VerticalScrollBarWidth - UiTheme.Scale(_list, 3));
		int first = Math.Clamp((int)Math.Round(available * 0.35), UiTheme.Scale(_list, 130), UiTheme.Scale(_list, 225));
		int second = Math.Clamp((int)Math.Round(available * 0.30), UiTheme.Scale(_list, 120), UiTheme.Scale(_list, 205));
		int size = Math.Clamp((int)Math.Round(available * 0.14), UiTheme.Scale(_list, 68), UiTheme.Scale(_list, 100));
		int bundle = Math.Max(UiTheme.Scale(_list, 72), available - first - second - size);
		int overflow = first + second + size + bundle - available;
		if (overflow > 0)
		{
			first = Math.Max(UiTheme.Scale(_list, 110), first - (overflow + 1) / 2);
			second = Math.Max(UiTheme.Scale(_list, 105), second - overflow / 2);
			bundle = Math.Max(UiTheme.Scale(_list, 56), available - first - second - size);
		}
		int[] widths = [first, second, size, bundle];
		for (int i = 0; i < widths.Length; i++)
		{
			if (_list.Columns[i].Width != widths[i])
			{
				_list.Columns[i].Width = widths[i];
			}
		}
	}

	private static bool CategoryMatches(TexRef texture, CategoryFilter filter)
	{
		if (filter.Kind == CategoryFilterKind.Animation)
		{
			return texture.SourceKind == "本地卡图" && texture.HasMonsterAnimation;
		}
		return filter.Kind == CategoryFilterKind.All || filter.Key == texture.SourceKind + "|" + texture.Category;
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
		if (int.TryParse(texture.CardKey, out int cardId) && matchedCardIds?.Contains(cardId) == true)
		{
			return true;
		}
		string animation = (texture.HasMonsterAnimation ? "有怪兽动画 召唤动画" : "");
		return $"{texture.Name} {texture.CardKey} {texture.SourceKind} {texture.Category} {animation} {texture.RelativeBundlePath}".Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private async Task ScanMissingCardAsync(CardCatalogEntry? preferred = null)
	{
		string cardKey = _search.Text.Trim();
		if (!Regex.IsMatch(cardKey, "^\\d+$"))
		{
			CardCatalogEntry? matched = preferred ?? _cardCatalog.Search(cardKey, 1).FirstOrDefault();
			if (matched == null)
			{
				HideSearchSuggestions();
				_status.Text = Localizer.T("search.no_results_hint");
				return;
			}
			_suppressSearchSuggestions = true;
			try
			{
				_search.Text = matched.CardId.ToString();
			}
			finally
			{
				_suppressSearchSuggestions = false;
			}
			HideSearchSuggestions();
			_status.Text = $"{matched.Name(Localizer.Language)} · {matched.CardId} · {Localizer.T("search.locating")}";
			await ScanMissingCardAsync(matched);
			return;
		}
		else
		{
			if (_gameRoot == null || _index == null)
			{
				return;
			}
			TexRef? existing = BestCardTexture(cardKey);
			bool needsFullCardProbe = existing == null || CardTextureRank(existing) > 1;
			if (needsFullCardProbe)
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
							TexRef texRef = addition;
							bool flag = HasAnimationIdOrEquivalent(addition.CardKey, animationIds);
							if (!flag)
							{
								flag = await Task.Run(() => MonsterAnimationIndexService.HasInstalledAnimation(_gameRoot, addition.CardKey));
							}
							texRef.HasMonsterAnimation = flag;
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
					TexRef? found = BestCardTexture(cardKey);
					if (found != null)
					{
						SetModsOnly(enabled: false);
						SelectAllCategory();
						RenderList();
						SelectTexture(found);
						_status.Text = found.Width == 512
							? ((found.Height == 1024) ? ("已定位灵摆卡 " + cardKey + " 的 512×1024 原生卡图；可直接预览、裁剪和替换，映射已保存。") : ("已定位卡号 " + cardKey + " 的 512×512 卡图；映射已保存。"))
							: $"卡号 {cardKey} 的完整插图尚未下载；已显示现有 {found.Width}×{found.Height} 缩略图。进入游戏查看该卡后可再次定位完整卡图。";
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
			SelectTexture(existing);
			string matchedName = preferred?.Name(Localizer.Language) ?? _cardCatalog.Find(cardKey)?.Name(Localizer.Language) ?? "";
			_status.Text = (matchedName.Length > 0 ? matchedName + " · " : "") + "卡号 " + cardKey + " 已定位；当前以全局检索显示，不受分类筛选影响。";
		}
	}

	private TexRef? BestCardTexture(string cardKey) => _textures
		.Where(texture => texture.SourceKind == "本地卡图" && texture.CardKey == cardKey)
		.OrderBy(CardTextureRank)
		.ThenByDescending(texture => (long)texture.Width * texture.Height)
		.FirstOrDefault();

	private static int CardTextureRank(TexRef texture)
	{
		if (texture.Width == 704 && texture.Height == 1024) return 0;
		if (texture.Width == 512 && (texture.Height == 512 || texture.Height == 1024)) return 1;
		if (texture.Width >= 256 && texture.Height >= 256) return 2;
		return 3;
	}

	private bool PageMatches(TexRef texture)
	{
		return _currentPage switch
		{
			WorkspacePage.Visuals => IsVisualAsset(texture),
			WorkspacePage.Cards => !IsVisualAsset(texture),
			_ => true
		};
	}

	private string DisplayResourceName(TexRef texture)
	{
		if (BuiltInCardFrameCatalog.IsPackagedFrame(texture))
		{
			return $"{texture.Name} · {CardFrameCatalog.FriendlyName(texture.Name)} · {ResourceLabel(texture.Category)}";
		}
		CardCatalogEntry? card = _cardCatalog.Find(texture.CardKey);
		return card == null ? texture.Name : $"{texture.CardKey} · {card.Name(Localizer.Language)}";
	}

	private static string ResourceLabel(string value)
	{
		(string ZhCn, string ZhTw, string Ja, string En) text = value switch
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
			"炫彩超框" => ("炫彩超框", "炫彩超框", "グラデーションOF", "Iridescent Overframes"),
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
			_ => (value, value, value, value)
		};
		return Localizer.Language switch
		{
			AppLanguage.TraditionalChinese => text.ZhTw,
			AppLanguage.Japanese => text.Ja,
			AppLanguage.English => text.En,
			_ => text.ZhCn
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
			bool IsVisual(TexRef x)
			{
				return x.SourceKind == VisualAssetIndexService.LocalSourceKind || x.SourceKind == VisualAssetIndexService.BuiltInSourceKind;
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
			int fields = result.Textures.Count((TexRef x) => x.Category == "决斗场地");
			int wallpapers = result.Textures.Count((TexRef x) => x.Category == "大厅壁纸" || x.Category == "大厅背景");
			_status.Text = $"视觉资源已载入：场地 {fields:N0} 张、壁纸／大厅背景 {wallpapers:N0} 张，另含卡套、头像、卡盒与硬币；替换时同样自动备份。";
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
		CategoryFilter? filter = _category.Items.Cast<CategoryFilter>().FirstOrDefault(x => x.Key == key);
		if (filter != null)
		{
			_category.SelectedItem = filter;
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
		if (_resourceContextRow == null)
		{
			return;
		}
		bool cardWorkspace = _currentPage == WorkspacePage.Cards;
		bool showMods = cardWorkspace && _modsOnly;
		bool showOverFrames = cardWorkspace && !showMods && _category.SelectedItem is CategoryFilter filter
			&& filter.Kind == CategoryFilterKind.Category
			&& filter.Key.EndsWith("|超框卡图", StringComparison.Ordinal);
		bool visible = showMods || showOverFrames;
		_modContextActions.Visible = showMods;
		_overFrameContextActions.Visible = showOverFrames;
		if (showMods)
		{
			_modContextActions.BringToFront();
		}
		else if (showOverFrames)
		{
			_overFrameContextActions.BringToFront();
		}
		_resourceContextBar.Visible = visible;
		_resourceContextRow.Height = visible ? 52f : 0f;
	}

	private void UpdateModSummary()
	{
		TexRef[] textures = _textures.Where((TexRef x) => x.IsModded).ToArray();
		_modSummary.Text = ((_changedModBundleCount == 0) ? "暂无改动；替换后的卡图与动画会自动纳入 Mod 管理" : $"已管理 {textures.Length:N0} 个图片资源 · {_changedModBundleCount:N0} 个已修改 Bundle{((_changedAnimationBundleCount > 0) ? $" · 动画 {_changedAnimationBundleCount:N0}" : "")}");
		_modPageSummary.Text = _modSummary.Text;
	}

	private async Task RefreshModFlagsAsync()
	{
		if (_gameRoot != null)
		{
			ModChangeSummary summary = await Task.Run(delegate
			{
				_mods.RefreshFlags(_gameRoot, _textures);
				return _mods.GetChangeSummary(_gameRoot, _textures);
			});
			_changedModBundleCount = summary.BundleCount;
			_changedAnimationBundleCount = summary.AnimationBundleCount;
			UpdateModSummary();
		}
	}

	private TexRef? Selected()
	{
		if (_list.SelectedIndices.Count != 1)
		{
			return null;
		}
		int index = _list.SelectedIndices[0];
		return index >= 0 && index < _visibleTextures.Count ? _visibleTextures[index] : null;
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
			// The resource workspace previews exactly what is stored in Texture2D.
			// Card-frame composition is an explicit action; doing it automatically made
			// Pendulum cards look as if their frame were part of the selected artwork.
			bool showTransparentRgb = x.SourceKind == "本地卡图"
				&& x.Width == FrameComposer.Width && x.Height == FrameComposer.Height;
			Bitmap display = CardPreviewRenderer.RenderRaw(data, showTransparentRgb);
			if (cancellationToken.IsCancellationRequested || generation != _previewGeneration || Selected() != x)
			{
				display.Dispose();
				return;
			}
			_preview.Image?.Dispose();
			_preview.Image = display;
			_previewHint.Visible = false;
			string frameLine = "";
			if (_gameRoot != null && x.SourceKind == "本地卡图" && x.Width == 704 && x.Height == 1024 && ushort.TryParse(x.CardKey, out var cardId))
			{
				frameLine = "\n预览：游戏 RGB 显示（透明 Alpha 仍原样保存在 Texture2D）";
				if (OverFrameArtStore.HasSettings(_gameRoot, cardId))
				{
					OverFrameFrameSettings settings = OverFrameArtStore.ReadSettings(_gameRoot, cardId);
					frameLine += "\n卡框：" + (settings.UsesCustomFrame ? "自定义卡框" : settings.FrameKey) + "  ·  可用下方“制作超框”更换";
				}
				else
				{
					frameLine += "\n卡框：尚未单独合成  ·  点击下方“制作超框”，可直接使用当前原卡图";
				}
			}
			else if (x.SourceKind == "本地卡图" && x.Width == 512 && (x.Height == 512 || x.Height == 1024))
			{
				string recommended = PreferredFrameKeyFor(x);
				frameLine = $"\n当前显示原始 Texture2D（未叠加卡框）  ·  推荐卡框 {recommended} · {CardFrameCatalog.FriendlyName(recommended)}";
			}
			if (x.HasMonsterAnimation)
			{
				frameLine += "\n怪兽动画：双击动画分类中的卡图，或点击“原始动画资源”，查看 PNG / Atlas / JSON";
			}
			_info.Text = $"{x.Name}  ·  {x.Category}\n{x.Width} × {x.Height}   PathID {x.PathId}\n{x.RelativeBundlePath}{frameLine}";
		}
		catch (Exception ex)
		{
			if (generation == _previewGeneration)
			{
				_preview.Image?.Dispose();
				_preview.Image = null;
				string diagnostic = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
				if (diagnostic.Length > 96) diagnostic = diagnostic[..93] + "…";
				_previewHint.Text = $"PREVIEW UNAVAILABLE\n\n{diagnostic}\n\n已检查 LocalData 与 StreamingAssets";
				_previewHint.Visible = true;
				_info.Text = $"{x.Name}  ·  {x.Category}\n映射 PathID {x.PathId}\n{x.RelativeBundlePath}";
				_status.Text = "预览失败：" + ex.Message;
			}
		}
	}

	private async Task<byte[]> DecodeWithReferenceRepairAsync(TexRef texture, CancellationToken cancellationToken = default)
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
				int index = _visibleTextures.IndexOf(texture);
				if (_list.IsHandleCreated && index >= 0 && index < _list.VirtualListSize)
				{
					_list.RedrawItems(index, index, invalidateOnly: false);
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
		catch (Exception ex)
		{
			string target = (texture.CardKey.Length > 0) ? ("卡号 " + texture.CardKey) : texture.Name;
			throw new InvalidDataException("无法读取 " + target + " 的 Texture2D：" + ex.Message, ex);
		}
	}

	private void SelectAllCategory()
	{
		CategoryFilter? all = _category.Items.Cast<CategoryFilter>().FirstOrDefault(x => x.Kind == CategoryFilterKind.All);
		if (all != null)
		{
			_category.SelectedItem = all;
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
			OpenFileDialog d = new OpenFileDialog
			{
				Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
			};
			try
			{
				if (d.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				image = d.FileName;
			}
			finally
			{
				((IDisposable)(object)d)?.Dispose();
			}
		}
		byte[] cropped;
		try
		{
			TexRef[] frames = ((x.SourceKind == "本地卡图" && x.CardKey.Length > 0 && x.Width == 512 && (x.Height == 512 || x.Height == 1024)) ? OverFrameFrames() : null);
			using ImageCropForm crop = new ImageCropForm(image, x.Width, x.Height, "替换 " + x.Name, frames,
				frames == null ? null : PreferredFrameKeyFor(x));
			if (crop.ShowDialog(this) != DialogResult.OK || crop.OutputPng == null)
			{
				return;
			}
			cropped = crop.OutputPng;
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
		if (MessageBox.Show(this, $"裁剪结果将以 {x.Width}×{x.Height} 写入游戏 Bundle。原始文件会备份到游戏目录的 _MD卡图备份 中。继续？", "确认替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
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
		string message = (isOverFrame ? "确认还原该超框卡？会还原原始卡图、关闭本卡超框登记，并重新归类到卡图列表。" : "确认把该 Bundle 还原为备份版本？");
		if (MessageBox.Show(this, message, "确认还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
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
			CategoryFilter? targetFilter = _category.Items.Cast<CategoryFilter>().FirstOrDefault(filter => filter.Key == targetCategory);
			if (targetFilter != null)
			{
				_category.SelectedItem = targetFilter;
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
		TexRef x = Selected();
		if (x == null)
		{
			return;
		}
		SaveFileDialog d = new SaveFileDialog
		{
			Filter = "PNG 图片|*.png",
			FileName = Safe(x.Name) + ".png"
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
				await File.WriteAllBytesAsync(fileName, await DecodeWithReferenceRepairAsync(x));
				_status.Text = "已导出当前游戏内卡图：" + d.FileName;
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "导出 PNG 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
		}
		finally
		{
			((IDisposable)(object)d)?.Dispose();
		}
	}

	private async Task DragOutAsync(ListViewItem? item)
	{
		if (!(item?.Tag is TexRef x))
		{
			return;
		}
		try
		{
			string path = Path.Combine(Path.GetTempPath(), "MDCardModTool", Safe(x.Name) + "_" + x.PathId + ".png");
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string path2 = path;
			await File.WriteAllBytesAsync(path2, await DecodeWithReferenceRepairAsync(x));
			_list.DoDragDrop(new DataObject(DataFormats.FileDrop, new string[1] { path }), DragDropEffects.Copy);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "拖出 PNG 失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void OnDragEnter(object? sender, DragEventArgs e)
	{
		IDataObject? data = e.Data;
		e.Effect = ((data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
	}

	private async Task OnDragDropAsync(DragEventArgs e)
	{
		if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length != 0 && new string[5] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(files[0]).ToLowerInvariant()))
		{
			await ReplaceSelectedAsync(files[0]);
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
			string notice = BuiltInCardFrameCatalog.IsTransparentFrame(x)
				? "ASTELLAR-NOTICE.txt"
				: "FLOOWAN-NOTICE.txt";
			MessageBox.Show(this,
				$"内置{x.Category}：{x.Name} · {CardFrameCatalog.FriendlyName(x.Name)}\n704×1024 PNG\n\n该资源为只读模板；来源与许可详见 {notice}。",
				"卡框资源");
			return;
		}
		try
		{
			BundleSummary s = await Task.Run(() => _engine.InspectBundle(x.BundlePath, _assetRoot));
			MessageBox.Show(this, $"Bundle: {s.RelativePath}\nSerialized 文件: {s.SerializedFiles}\n\nAsset 类型：\n{s.Describe()}", "Bundle 检查");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "检查失败");
		}
	}

	private async Task ApplyOverFrameTagsAsync()
	{
		if (_gameRoot == null)
		{
			return;
		}
		try
		{
			HashSet<string> ids = (await Task.Run(() => _overFrames.ReadCached(_gameRoot))).Select((OverFrameMapping overFrameMapping) => overFrameMapping.CardId.ToString()).ToHashSet<string>(StringComparer.Ordinal);
			foreach (TexRef x in _textures.Where((TexRef texRef) => texRef.SourceKind == "本地卡图" && texRef.CardKey.Length > 0))
			{
				if (ids.Contains(x.CardKey))
				{
					x.Category = "超框卡图";
				}
				else if (x.Category == "超框卡图")
				{
					IndexService.NormalizeLocalCardCategory(x);
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyMonsterAnimationTags()
	{
		HashSet<string> animationIds = MonsterAnimationIndexService.LoadBundledCardIds();
		if (_gameRoot != null)
		{
			try
			{
				List<MonsterAnimationAssetRef> available = MonsterAnimationIndexService.LoadBestAvailable(_gameRoot, out _);
				animationIds.UnionWith(MonsterAnimationIndexService.CompleteCardIds(available));
			}
			catch
			{
			}
		}
		animationIds = ExpandEquivalentAnimationIds(animationIds);
		foreach (TexRef texture in _textures)
		{
			texture.HasMonsterAnimation = texture.SourceKind == "本地卡图" && animationIds.Contains(texture.CardKey);
		}
		int count = _textures.Count((TexRef x) => x.HasMonsterAnimation);
		if (count > 0)
		{
			_status.Text += $" 已标记 {count:N0} 张有怪兽召唤动画的卡图。";
		}
	}

	private HashSet<string> ExpandEquivalentAnimationIds(IEnumerable<string> directIds)
	{
		HashSet<string> expanded = directIds.ToHashSet(StringComparer.Ordinal);
		foreach (string id in expanded.ToArray())
		{
			if (!int.TryParse(id, out int numeric)) continue;
			CardCatalogEntry? card = _cardCatalog.Find(numeric);
			if (card == null) continue;
			foreach (CardCatalogEntry equivalent in _cardCatalog.FindEquivalentCards(card))
			{
				expanded.Add(equivalent.CardId.ToString());
			}
		}
		return expanded;
	}

	private bool HasAnimationIdOrEquivalent(string cardId, ISet<string> directIds)
	{
		if (directIds.Contains(cardId)) return true;
		CardCatalogEntry? card = _cardCatalog.Find(cardId);
		return card != null && _cardCatalog.FindEquivalentCards(card)
			.Any(equivalent => directIds.Contains(equivalent.CardId.ToString()));
	}

	private void StartGameBuildRefresh()
	{
		CancellationTokenSource? previousCancellation = _backgroundRefreshCancellation;
		Task? previousTask = _backgroundRefreshTask;
		previousCancellation?.Cancel();
		if (previousCancellation != null)
		{
			if (previousTask == null || previousTask.IsCompleted)
			{
				previousCancellation.Dispose();
			}
			else
			{
				_ = previousTask.ContinueWith(_ => previousCancellation.Dispose(), CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			}
		}
		if (_gameRoot == null || _index == null)
		{
			_backgroundRefreshCancellation = null;
			_backgroundRefreshTask = null;
			return;
		}
		_backgroundRefreshCancellation = new CancellationTokenSource();
		_backgroundRefreshTask = RefreshGameBuildDataAsync(_gameRoot, _index, _backgroundRefreshCancellation.Token);
	}

	private async Task RefreshGameBuildDataAsync(string gameRoot, GameIndex index, CancellationToken cancellationToken)
	{
		try
		{
			string localRoot = IndexService.FindLocalRoot(gameRoot) ?? "";
			int newCards = 0;
			if (GameCardCatalogUpdater.NeedsUpdate(gameRoot))
			{
				PostBackgroundStatus("检测到游戏 Build 或账号变化，正在后台读取 card_name / card_indx / card_prop…");
				Action<int, int, int> catalogProgress = CreateThrottledBackgroundProgress(250,
					(done, total, found) => $"正在更新四语卡片目录：{done:N0}/{total:N0} Bundle · 已定位 {found}/9 项数据…");
				await Task.Run(() => GameCardCatalogUpdater.UpdateIfNeeded(gameRoot, catalogProgress, cancellationToken), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsSameWorkspace(gameRoot, localRoot, index)) return;
				_cardCatalog = CardCatalogService.LoadBestAvailable();
				Action<int, int, int> missingCardProgress = CreateThrottledBackgroundProgress(250,
					(done, total, added) => $"正在合并新卡图：{done:N0}/{total:N0} · 新增 {added:N0}…");
				MissingCardScanResult additions = await Task.Run(() => IndexService.ScanMissingLocalCard(gameRoot, index, "0",
					missingCardProgress, cancellationToken), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (!IsSameWorkspace(gameRoot, localRoot, index)) return;
				HashSet<string> known = index.Textures.Select(x => $"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				foreach (TexRef texture in additions.Textures.Where(x => known.Add($"{x.BundlePath}\0{x.AssetFileName}\0{x.PathId}")))
				{
					IndexService.NormalizeLocalCardCategory(texture);
					index.Textures.Add(texture);
					_textures.Add(texture);
					newCards++;
				}
				await Task.Run(() => IndexService.Save(gameRoot, index), cancellationToken);
			}

			PostBackgroundStatus("正在校验当前 Build 的官方怪兽动画索引；首次更新可能需要数分钟…");
			Action<int, int, int> animationProgress = CreateThrottledBackgroundProgress(100,
				(done, total, found) => $"正在更新动画索引：{done:N0}/{total:N0} Bundle · {found:N0} 项资源…");
			PortableMonsterAnimationIndex animationIndex = await Task.Run(() => MonsterAnimationIndexService.EnsureCurrentIndex(gameRoot,
				animationProgress, cancellationToken), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsSameWorkspace(gameRoot, localRoot, index)) return;
			HashSet<string> directAnimationIds = MonsterAnimationIndexService.CompleteCardIds(animationIndex);
			HashSet<string> animationIds = ExpandEquivalentAnimationIds(directAnimationIds);
			foreach (TexRef texture in _textures)
			{
				if (texture.SourceKind == "本地卡图") texture.HasMonsterAnimation = animationIds.Contains(texture.CardKey);
			}
			RefreshCategories();
			UpdatePageHeader();
			RenderList();
			_status.Text = $"当前 Build 增量更新完成：四语卡片 {_cardCatalog.Count:N0} 张，新卡图 {newCards:N0} 张，官方动画 {directAnimationIds.Count:N0} 套（含同名异画映射 {animationIds.Count:N0} 张）。";
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			if (!IsDisposed)
			{
				_status.Text = "后台 Build 增量更新未完成，现有索引仍可使用：" + ex.Message;
			}
		}
	}

	private bool IsSameWorkspace(string gameRoot, string localRoot, GameIndex index)
	{
		return !IsDisposed && ReferenceEquals(_index, index)
			&& string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(IndexService.FindLocalRoot(gameRoot), localRoot, StringComparison.OrdinalIgnoreCase);
	}

	private Action<int, int, int> CreateThrottledBackgroundProgress(int minimumItemDelta,
		Func<int, int, int, string> formatter)
	{
		object progressGate = new();
		int lastReported = -Math.Max(1, minimumItemDelta);
		long nextReportAt = 0;
		return (done, total, found) =>
		{
			long now = Environment.TickCount64;
			lock (progressGate)
			{
				if (done < total && (done - lastReported < minimumItemDelta || now < nextReportAt))
				{
					return;
				}
				lastReported = done;
				nextReportAt = now + 150;
			}
			PostBackgroundStatus(formatter(done, total, found));
		};
	}

	private void PostBackgroundStatus(string text)
	{
		if (IsDisposed || !IsHandleCreated) return;
		try
		{
			BeginInvoke(delegate
			{
				if (!IsDisposed) _status.Text = text;
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
		TexRef[] frames = OverFrameFrames();
		if (frames.Length == 0)
		{
			MessageBox.Show(this, "索引中没有 card_frame。请点击“重建索引”后重试。", Text);
			return;
		}
		art.PreviewFrameKey = PreferredFrameKeyFor(art);
		FramePreviewForm framePreviewForm = new FramePreviewForm(_engine, art, frames);
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
		}
		else if (_overFrameManager is { IsDisposed: false })
		{
			_overFrameManager.Activate();
		}
		else if (!_overFrameManagerQueued)
		{
			// The manager performs its own Bundle lookup. Pause the lower-priority
			// build refresh first so both readers do not compete for AssetsTools. Queue
			// construction after the click returns, keeping the card workspace's message
			// pump responsive while the cancelled reader leaves AssetsTools.
			_backgroundRefreshCancellation?.Cancel();
			string gameRoot = _gameRoot;
			string? selectedCard = Selected()?.CardKey;
			int generation = Interlocked.Increment(ref _overFrameManagerGeneration);
			_overFrameManagerQueued = true;
			BeginInvoke((MethodInvoker)delegate
			{
				_overFrameManagerQueued = false;
				if (generation != Volatile.Read(ref _overFrameManagerGeneration) || IsDisposed
					|| !string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase)
					|| _overFrameManager is { IsDisposed: false })
				{
					return;
				}
				OverFrameForm manager = new(gameRoot, selectedCard);
				_overFrameManager = manager;
				manager.FormClosed += delegate
				{
					if (!ReferenceEquals(_overFrameManager, manager))
					{
						return;
					}
					_overFrameManager = null;
					if (!IsDisposed && !Disposing && _gameRoot != null && _index != null)
					{
						StartGameBuildRefresh();
					}
				};
				manager.Show(this);
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
		string initial = Selected()?.CardKey;
		if (string.IsNullOrWhiteSpace(initial) && _search.Text.Trim().All(char.IsAsciiDigit))
		{
			initial = _search.Text.Trim();
		}
		MonsterAnimationForm monsterAnimationForm = new MonsterAnimationForm(_gameRoot, initial);
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
		TexRef selected = Selected();
		if (selected == null || !selected.HasMonsterAnimation)
		{
			MessageBox.Show(this, "请先在“有怪兽动画”分类中选择一张卡图。", Text);
			return;
		}
		MonsterAnimationRawAssetsForm monsterAnimationRawAssetsForm = new MonsterAnimationRawAssetsForm(_gameRoot, selected.CardKey);
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
		return _textures.Where((TexRef x) => BuiltInCardFrameCatalog.IsPackagedFrame(x)
			&& x.Width == 704 && x.Height == 1024).ToArray();
	}

	private TexRef? PreviewFrameFor(TexRef texture)
	{
		if (texture.SourceKind != "本地卡图" || texture.Width != 512 || (texture.Height != 512 && texture.Height != 1024))
		{
			return null;
		}
		TexRef[] frames = CardFrameCatalog.CompatibleFrames(OverFrameFrames(), texture.Width, texture.Height).ToArray();
		string wanted = PreferredFrameKeyFor(texture);
		return frames.FirstOrDefault((TexRef x) => x.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? frames.FirstOrDefault();
	}

	private string PreferredFrameKeyFor(TexRef texture)
	{
		CardCatalogEntry? card = _cardCatalog.Find(texture.CardKey);
		return CardFrameCatalog.RecommendedKey(card, texture.Width, texture.Height);
	}

	private async Task OpenCardFrameStudioAsync()
	{
		TexRef? selected = Selected();
		if (selected == null || _gameRoot == null || selected.SourceKind != "本地卡图"
			|| !ushort.TryParse(selected.CardKey, out _))
		{
			MessageBox.Show(this, "请先选择一张本地卡图。", Text);
			return;
		}
		// Ordinary cards and existing OF cards deliberately share this exact editor.
		// The editor captures the live Texture2D as its source when no draft exists,
		// so there is no separate crop dialog or mandatory file upload.
		await OpenFrameEditorAsync(selected);
	}

	private async Task<bool> OpenFrameEditorAsync(TexRef? texture = null, byte[]? initialArt = null,
		string? initialFrameKey = null, byte[]? initialBackground = null,
		bool replaceStoredBackground = false)
	{
		TexRef x = texture ?? Selected();
		if (x == null || _gameRoot == null)
		{
			MessageBox.Show(this, "先选择一张本地卡图。", Text);
			return false;
		}
		if (x.SourceKind != "本地卡图" || !ushort.TryParse(x.CardKey, out var cardId))
		{
			MessageBox.Show(this, "制作超框只能用于名称为卡号的“本地卡图”。", Text);
			return false;
		}
		TexRef[] frames = OverFrameFrames();
		if (frames.Length == 0)
		{
			MessageBox.Show(this, "索引中没有 704×1024 card_frame。请点击“重建索引”后重试。", Text);
			return false;
		}
		if (string.IsNullOrWhiteSpace(initialFrameKey))
		{
			OverFrameFrameSettings saved = OverFrameArtStore.ReadSettings(_gameRoot, cardId);
			if (!OverFrameArtStore.HasSettings(_gameRoot, cardId) || (!saved.UserSelected && !saved.UsesCustomFrame))
			{
				initialFrameKey = PreferredFrameKeyFor(x);
			}
		}
		using OverFrameFrameEditorForm editor = new OverFrameFrameEditorForm(_gameRoot, x, frames,
			initialArt, initialFrameKey, initialBackground, replaceStoredBackground);
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
			Filter = "Master Duel Mod 包|*.mdmod.zip",
			FileName = $"MD_Mods_{DateTime.Now:yyyyMMdd_HHmm}.mdmod.zip",
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
				ModPackageInfo info = await Task.Run(() => _mods.Export(_gameRoot, _textures, dialog.FileName));
				_status.Text = $"已导出 {info.BundleCount:N0} 个 Mod Bundle：{dialog.FileName}";
				MessageBox.Show(this, $"导出完成。\n\nBundle：{info.BundleCount:N0} 个\n原始大小：{FormatSize(info.TotalSize)}\n文件：{dialog.FileName}", "全部 Mod 已导出", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
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
				((IDisposable)(object)dialog).Dispose();
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
			Filter = "Master Duel Mod 包|*.mdmod.zip|ZIP 压缩包|*.zip",
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
				ModPackageInfo info = await Task.Run(() => _mods.Inspect(dialog.FileName));
				string confirm = $"准备导入“{info.Name}”。\n\nBundle：{info.BundleCount:N0} 个\n原始大小：{FormatSize(info.TotalSize)}\n\n将直接覆盖对应游戏 Bundle；每个文件首次覆盖前都会自动保存原版备份。继续？";
				if (MessageBox.Show(this, confirm, "确认导入 Mod", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
				{
					base.UseWaitCursor = true;
					_status.Text = "正在校验并导入 Mod 包…";
					ModImportResult result = await Task.Run(() => _mods.Import(_gameRoot, dialog.FileName));
					await ReloadChangedBundlesAsync(result.ChangedBundlePaths);
					await ApplyOverFrameTagsAsync();
					await RefreshModFlagsAsync();
					await Task.Run(delegate
					{
						IndexService.Save(_gameRoot, _textures);
					});
					RefreshCategories();
					SetModsOnly(enabled: true);
					SelectAllCategory();
					RenderList();
					_status.Text = $"已导入 {result.BundleCount:N0} 个 Mod Bundle，并加入“我的 Mod”。";
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
				((IDisposable)(object)dialog).Dispose();
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
			if (existing.Length == 0)
			{
				continue;
			}
			string sourceKind = existing[0].SourceKind;
			string root = sourceKind switch
			{
				"本地卡图" => _assetRoot,
				"视觉资源" => _assetRoot,
				"游戏内图片" => _streamingRoot,
				_ => _gameRoot
			};
			List<TexRef> scanned = await Task.Run(() => _engine.ScanBundle(path, root, sourceKind, includeDependencies: false).Textures);
			TexRef[] array = existing;
			foreach (TexRef texture in array)
			{
				TexRef updated = scanned.FirstOrDefault((TexRef x) => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName);
				if (updated != null)
				{
					texture.Width = updated.Width;
					texture.Height = updated.Height;
					texture.Category = updated.Category;
					texture.OverrideBundlePath = null;
					IndexService.NormalizeLocalCardCategory(texture);
				}
			}
		}
	}

	private void OpenBackup()
	{
		if (_gameRoot != null)
		{
			string path = Path.Combine(_gameRoot, "_MD卡图备份");
			Directory.CreateDirectory(path);
			Process.Start("explorer.exe", path);
		}
	}

	private void SelectTexture(TexRef texture)
	{
		int index = _visibleTextures.IndexOf(texture);
		if (index >= 0)
		{
			_list.SelectedIndices.Clear();
			_list.SelectedIndices.Add(index);
			_list.EnsureVisible(index);
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
		string? initial = Selected()?.CardKey;
		if (string.IsNullOrWhiteSpace(initial) || !initial.All(char.IsAsciiDigit))
		{
			string query = _search.Text.Trim();
			initial = query.Length > 0 && query.All(char.IsAsciiDigit) ? query : null;
		}
		if (string.IsNullOrWhiteSpace(initial))
		{
			MessageBox.Show(this, "请先在卡片资源中选择一张卡，或在卡片页搜索框输入纯数字卡号。", Text,
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		ShowPage(WorkspacePage.Animation);
		if (_embeddedAnimation != null && !_embeddedAnimation.IsDisposed)
		{
			await _embeddedAnimation.PreviewCardAsync(initial);
		}
	}

	private static string FormatSize(long bytes)
	{
		if (bytes < 1073741824)
		{
			if (bytes >= 1048576)
			{
				return $"{(double)bytes / 1048576.0:0.##} MB";
			}
			return $"{(double)bytes / 1024.0:0.##} KB";
		}
		return $"{(double)bytes / 1073741824.0:0.##} GB";
	}

	private static string Safe(string n)
	{
		return string.Concat(n.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
	}
}
