using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MdCardModTool;

// The workspace owns geometry. Business actions and resource selection stay in
// MainForm; there are no fixed-height wrappers around a minimum-height field.
public sealed partial class MainForm
{
	private WorkspaceToolbar? _workspaceToolbar;
	private TableLayoutPanel? _resourceSearchBar;
	private TableLayoutPanel? _previewLayout;
	private DarkScrollPanel? _previewScroll;

	private void BuildWorkspaceLayout(Button choose, Button rebuild, Button exportMods, Button importMods, Button[] actions)
	{
		_workspaceToolbar = new WorkspaceToolbar(UiTheme.Field(_gameFolder), choose, UiTheme.Field(_profileSelector), UiTheme.Field(_languageSelector), rebuild)
		{
			Name = "WorkspaceToolbar", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16, 10, 16, 10), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
		};
		var sidebar = BuildWorkspaceNavigation();
		_gameUiBanner = new GameUiHeaderPanel
		{
			Name = "GameUiBanner",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			Height = UiTheme.Scale(this, 86),
			MinimumSize = new Size(0, UiTheme.Scale(this, 72))
		};
		var header = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16, 6, 16, 6), Margin = Padding.Empty, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
		header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		_pageTitle.Dock = DockStyle.Fill;
		_pageTitle.AutoSize = true;
		_pageTitle.Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold);
		_pageDescription.AutoSize = true;
		_catalogBadge.AutoSize = true;
		_catalogBadge.Dock = DockStyle.Fill;
		header.Controls.Add(_pageTitle, 0, 0);
		header.Controls.Add(_pageDescription, 0, 1);
		header.Controls.Add(_catalogBadge, 1, 0);
		header.SetRowSpan(_catalogBadge, 2);

		_cardSearchField = UiTheme.Field(_search);
		_cardSearchField.Name = "CardSearchField";
		_resourceSearchBar = new WorkspaceGrid
		{
			Name = "ResourceSearchBar", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
			AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 10)
		};
		_resourceSearchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		_resourceSearchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
		_resourceSearchBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		_cardSearchField.Margin = new Padding(0, 0, 10, 0);
		_cardSearchField.AutoSize = true;
		((RoundedField)_cardSearchField).AutoSizeMode = AutoSizeMode.GrowAndShrink;
		Control category = UiTheme.Field(_category);
		category.Margin = Padding.Empty;
		category.AutoSize = true;
		((RoundedField)category).AutoSizeMode = AutoSizeMode.GrowAndShrink;
		_resourceSearchBar.Controls.Add(_cardSearchField, 0, 0);
		_resourceSearchBar.Controls.Add(category, 1, 0);

		BuildVisualShortcutBar();
		var groups = WorkspaceSection("资源分类", _groups);
		groups.Name = "ResourceGroupsSurface";
		var results = new WorkspaceSurface { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = Padding.Empty, BackColor = UiTheme.Surface };
		results.Name = "ResourceListSurface";
		Panel listHeader = SectionHeading("图片资源", "");
		listHeader.Controls.Add(_resultCount);
		_resultCount.BringToFront();
		_resourceGallery = new CardResourceGallery
		{
			Name = "CardResourceGallery",
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			ImageLoader = LoadGalleryImageAsync,
			NameFormatter = DisplayResourceName
		};
		_resourceGallery.SelectionChanged += (_, _) =>
		{
			if (_resourceGallery.SelectedIndex < 0 || _resourceGallery.SelectedIndex >= _visibleTextures.Count) return;
			_renderingResourceList = true;
			try
			{
				_list.SelectedIndices.Clear();
				_list.SelectedIndices.Add(_resourceGallery.SelectedIndex);
			}
			finally { _renderingResourceList = false; }
			QueueResourceSelectionUpdate();
		};
		results.Controls.Add(_resourceGallery);
		results.Controls.Add(_list);
		_list.Visible = false;
		_resourceGallery.BringToFront();
		results.Controls.Add(listHeader);
		_resourceSplit = new SplitContainer
		{
			Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 8,
			BackColor = UiTheme.Window, Margin = Padding.Empty, Size = new Size(700, 500), SplitterDistance = 202
		};
		_resourceSplit.Panel1.Controls.Add(groups);
		_resourceSplit.Panel2.Controls.Add(results);

		_resourceActionGrid.Dock = DockStyle.Fill;
		_resourceActionGrid.ColumnCount = 2;
		_resourceActionGrid.RowCount = 4;
		_resourceActionGrid.AutoScroll = false;
		_resourceActionGrid.BackColor = UiTheme.Surface;
		_resourceActionGrid.Padding = Padding.Empty;
		_resourceActionGrid.Margin = Padding.Empty;
		_resourceActionGrid.ColumnStyles.Clear();
		_resourceActionGrid.RowStyles.Clear();
		_resourceActionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		_resourceActionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
		for (int row = 0; row < 4; row++) _resourceActionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
		int[] order = { 0, 6, 1, 7, 2, 3, 4, 5 };
		for (int i = 0; i < actions.Length; i++)
		{
			Button action = actions[order[i]];
			action.AutoSize = false;
			action.Dock = DockStyle.Fill;
			action.MinimumSize = Size.Empty;
			action.Margin = new Padding(3, 3, 3, 3);
			_resourceActionGrid.Controls.Add(action, i % 2, i / 2);
		}
		var imageSurface = new WorkspaceSurface { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = UiTheme.Surface, Margin = Padding.Empty };
		imageSurface.Controls.Add(_preview);
		imageSurface.Controls.Add(_previewHint);
		_previewHint.BringToFront();
		_info.Margin = Padding.Empty;
		_info.Padding = new Padding(12, 10, 12, 10);
		_info.TextChanged += (_, _) => ResizeWorkspacePreview();
		_previewLayout = new WorkspaceGrid { Name = "ResourceInspectorLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = UiTheme.Surface };
		_previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		_previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
		_previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		_previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
		_previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
		_previewLayout.Controls.Add(SectionHeading("实时预览", ""), 0, 0);
		_previewLayout.Controls.Add(imageSurface, 0, 1);
		_previewLayout.Controls.Add(_info, 0, 2);
		_previewLayout.Controls.Add(_resourceActionGrid, 0, 3);
		_previewScroll = new DarkScrollPanel
		{
			Name = "ResourcePreviewScroll", Dock = DockStyle.Fill, UseExplicitContentHeight = true, BackColor = UiTheme.Surface
		};
		_previewScroll.ContentPanel.Controls.Add(_previewLayout);
		_previewScroll.SizeChanged += (_, _) => ResizeWorkspacePreview();
		_previewScroll.DpiChangedAfterParent += (_, _) => ResizeWorkspacePreview();
		_workspaceSplit = new SplitContainer
		{
			Name = "ResourceWorkspace", Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 10,
			BackColor = UiTheme.Window, Margin = Padding.Empty, Size = new Size(1140, 580), SplitterDistance = 780
		};
		_workspaceSplit.Panel1.Controls.Add(_resourceSplit);
		_workspaceSplit.Panel2.Controls.Add(_previewScroll);
		_workspaceSplit.SizeChanged += (_, _) => ReflowWorkspaceColumns();
		var resourceLayout = new WorkspaceGrid
		{
			Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 6, 16, 12), Margin = Padding.Empty
		};
		resourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		_visualShortcutRow = new RowStyle(SizeType.Absolute, 0);
		_resourceContextRow = new RowStyle(SizeType.Absolute, 0);
		resourceLayout.RowStyles.Add(_visualShortcutRow);
		resourceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		resourceLayout.RowStyles.Add(_resourceContextRow);
		resourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		resourceLayout.Controls.Add(_visualShortcutBar, 0, 0);
		resourceLayout.Controls.Add(_resourceSearchBar, 0, 1);
		resourceLayout.Controls.Add(_resourceContextBar, 0, 2);
		resourceLayout.Controls.Add(_workspaceSplit, 0, 3);
		_resourcePage.Controls.Add(resourceLayout);
		BuildAnimationPage();
		BuildResourceContextBar(exportMods, importMods);
		BuildSettingsPage();
		_pageHost.Controls.AddRange(new Control[] { _settingsPage, _animationPage, _resourcePage });

		var content = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
		content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		content.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Scale(this, 86)));
		content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		content.Controls.Add(_workspaceToolbar, 0, 0);
		content.Controls.Add(_gameUiBanner, 0, 1);
		content.Controls.Add(header, 0, 2);
		content.Controls.Add(_pageHost, 0, 3);
		// MD's shell uses a broad, compact navigation strip.  Keeping the
		// navigation above the workspace also prevents the old sidebar from
		// squeezing the preview at 125/150% DPI.
		var body = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		body.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Scale(this, 70)));
		body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		body.Controls.Add(sidebar, 0, 0);
		body.Controls.Add(content, 0, 1);
		_status.Spring = true;
		_status.TextAlign = ContentAlignment.MiddleLeft;
		_status.ForeColor = UiTheme.Muted;
		var status = new StatusStrip { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, SizingGrip = false, Margin = Padding.Empty, Padding = new Padding(12, 0, 12, 0) };
		status.Items.Add(_status);
		_rootLayout = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
		_rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		_rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		_rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
		_rootLayout.Controls.Add(body, 0, 0);
		_rootLayout.Controls.Add(status, 0, 1);
		Controls.Add(_rootLayout);
		Controls.Add(_searchSuggestionPopup);
		SizeChanged += (_, _) => { if (_searchSuggestionPopup.Visible) PositionSearchSuggestions(); };
		Shown += (_, _) => { ReflowWorkspaceColumns(); ResizeWorkspacePreview(); };
		ShowPage(WorkspacePage.Cards);
	}

	private void ReflowWorkspaceColumns()
	{
		if (_workspaceSplit == null || _resourceSplit == null || !IsHandleCreated || _workspaceSplit.Width < UiTheme.Scale(this, 680)) return;
		int inspector = Math.Min(UiTheme.Scale(this, 344), _workspaceSplit.Width * 39 / 100);
		int desired = _workspaceSplit.Width - inspector - _workspaceSplit.SplitterWidth;
		if (_workspaceSplit.SplitterDistance != desired) _workspaceSplit.SplitterDistance = desired;
		int groups = Math.Min(UiTheme.Scale(this, 202), _resourceSplit.Width * 35 / 100);
		if (_resourceSplit.SplitterDistance != groups) _resourceSplit.SplitterDistance = groups;
		ResizeWorkspacePreview();
	}

	private Control BuildWorkspaceNavigation()
	{
		var navigation = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = UiTheme.Surface, Padding = new Padding(12, 8, 12, 8), Margin = Padding.Empty };
		navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Scale(this, 248)));
		for (int i = 0; i < 4; i++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
		navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
		AddNavigation(navigation, WorkspacePage.Cards, "nav.cards");
		AddNavigation(navigation, WorkspacePage.Visuals, "nav.visuals");
		AddNavigation(navigation, WorkspacePage.Animation, "nav.animation");
		AddNavigation(navigation, WorkspacePage.Settings, "nav.settings");
		var brand = new WorkspaceGrid { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = UiTheme.Surface, Padding = new Padding(6, 5, 14, 5), Margin = Padding.Empty };
		brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
		brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		brand.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = _brandImage, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) }, 0, 0);
		brand.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "MD STUDIO\nMASTER DUEL", Font = new Font("Segoe UI Semibold", 10), ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
		navigation.Controls.Add(brand, 0, 0);
		var version = new Label
		{
			Name = "AppVersionLabel", Dock = DockStyle.Fill, Padding = new Padding(18, 10, 14, 10), ForeColor = UiTheme.Muted,
			Font = new Font("Segoe UI", 8), TextAlign = ContentAlignment.MiddleLeft,
			Text = $"v{Application.ProductVersion.Split('+')[0]}  ·  ASTELLAR CATALOG\n{_cardCatalog.Count:N0} MULTILINGUAL CARDS"
		};
		version.Visible = false;
		return navigation;
	}

	private static Panel WorkspaceSection(string title, Control body)
	{
		var section = new WorkspaceSurface { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = Padding.Empty, BackColor = UiTheme.Surface };
		section.Controls.Add(body);
		section.Controls.Add(SectionHeading(title, ""));
		return section;
	}

	private void ResizeWorkspacePreview()
	{
		if (_previewScroll == null || _previewLayout == null || _previewScroll.IsDisposed) return;
		float scale = _previewLayout.RowStyles[0].Height / 40f;
		int width = Math.Max(100, _previewScroll.ClientSize.Width - _info.Padding.Horizontal - (int)(20 * scale));
		int information = string.IsNullOrEmpty(_info.Text) ? (int)(44 * scale)
			: TextRenderer.MeasureText(_info.Text, _info.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + _info.Padding.Vertical;
		if (Math.Abs(_previewLayout.RowStyles[2].Height - information) > 1) _previewLayout.RowStyles[2].Height = information;
		int fixedHeight = (int)Math.Ceiling(_previewLayout.RowStyles[0].Height + information + _previewLayout.RowStyles[3].Height);
		_previewScroll.ContentHeight = Math.Max(_previewScroll.ClientSize.Height, fixedHeight + (int)Math.Ceiling(300 * scale));
	}
}

internal sealed class GameUiHeaderPanel : Panel
{
	private Image? _backgroundImage;
	private Image? _logoImage;

	internal Image? BackgroundImageFromGame
	{
		get => _backgroundImage;
		set { _backgroundImage?.Dispose(); _backgroundImage = value; Invalidate(); }
	}

	internal Image? LogoImageFromGame
	{
		get => _logoImage;
		set { _logoImage?.Dispose(); _logoImage = value; Invalidate(); }
	}

	internal GameUiHeaderPanel()
	{
		DoubleBuffered = true;
		BackColor = UiTheme.Window;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		if (_backgroundImage != null)
		{
			using var attributes = new System.Drawing.Imaging.ImageAttributes();
			var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.78f };
			attributes.SetColorMatrix(matrix);
			e.Graphics.DrawImage(_backgroundImage, ClientRectangle, 0, 0, _backgroundImage.Width, _backgroundImage.Height, GraphicsUnit.Pixel, attributes);
		}
		using var scrim = new SolidBrush(Color.FromArgb(92, 3, 10, 24));
		e.Graphics.FillRectangle(scrim, ClientRectangle);
		if (_logoImage != null)
		{
			int maxW = Math.Min(ClientSize.Width / 3, UiTheme.Scale(this, 250));
			int maxH = Math.Max(UiTheme.Scale(this, 42), ClientSize.Height - UiTheme.Scale(this, 16));
			double scale = Math.Min((double)maxW / _logoImage.Width, (double)maxH / _logoImage.Height);
			int w = Math.Max(1, (int)Math.Round(_logoImage.Width * scale));
			int h = Math.Max(1, (int)Math.Round(_logoImage.Height * scale));
			e.Graphics.DrawImage(_logoImage, new Rectangle(UiTheme.Scale(this, 22), (ClientSize.Height - h) / 2, w, h));
		}
		using var pen = new Pen(UiTheme.Gold, UiTheme.Scale(this, 2));
		e.Graphics.DrawLine(pen, UiTheme.Scale(this, 16), ClientSize.Height - UiTheme.Scale(this, 4), ClientSize.Width - UiTheme.Scale(this, 16), ClientSize.Height - UiTheme.Scale(this, 4));
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) { _backgroundImage?.Dispose(); _logoImage?.Dispose(); }
		base.Dispose(disposing);
	}
}

internal sealed class WorkspaceGrid : TableLayoutPanel
{
	internal WorkspaceGrid()
	{
		DoubleBuffered = true;
		BackColor = UiTheme.Window;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
	}
}

internal sealed class WorkspaceSurface : Panel
{
	internal WorkspaceSurface() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
}

// At narrow widths/150% DPI, move account/language into a second row instead
// of squeezing or clipping the fields. Preferred heights include inner padding.
internal sealed class WorkspaceToolbar : Panel
{
	private readonly Control _path, _choose, _account, _language, _rebuild;
	internal WorkspaceToolbar(Control path, Control choose, Control account, Control language, Control rebuild)
	{
		_path = path; _choose = choose; _account = account; _language = language; _rebuild = rebuild;
		DoubleBuffered = true;
		BackColor = UiTheme.Surface;
		Controls.AddRange(new[] { path, choose, account, language, rebuild });
		foreach (Control item in Controls)
		{
			item.Dock = DockStyle.None; item.Margin = Padding.Empty; item.AutoSize = false;
			item.TextChanged += (_, _) => { PerformLayout(); Parent?.PerformLayout(); };
			item.FontChanged += (_, _) => { PerformLayout(); Parent?.PerformLayout(); };
		}
	}
	private int FieldHeight => Math.Max(UiTheme.Scale(this, 38), Controls.Cast<Control>().Max(c => c.GetPreferredSize(new Size(180, 0)).Height));
	private bool Wrap(int width) => width < UiTheme.Scale(this, 1080);
	public override Size GetPreferredSize(Size proposedSize)
	{
		int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
		return new Size(width, Padding.Vertical + FieldHeight * (Wrap(width) ? 2 : 1) + (Wrap(width) ? UiTheme.Scale(this, 8) : 0));
	}
	protected override void OnLayout(LayoutEventArgs levent)
	{
		base.OnLayout(levent);
		int gap = UiTheme.Scale(this, 10), height = FieldHeight;
		int choose = Math.Max(UiTheme.Scale(this, 92), TextRenderer.MeasureText(_choose.Text, _choose.Font).Width + UiTheme.Scale(this, 28));
		int rebuild = Math.Max(UiTheme.Scale(this, 100), TextRenderer.MeasureText(_rebuild.Text, _rebuild.Font).Width + UiTheme.Scale(this, 28));
		int account = UiTheme.Scale(this, 212), language = UiTheme.Scale(this, 148);
		int left = Padding.Left, right = ClientSize.Width - Padding.Right, top = Padding.Top;
		if (Wrap(ClientSize.Width))
		{
			_rebuild.SetBounds(right - rebuild, top, rebuild, height);
			_choose.SetBounds(_rebuild.Left - gap - choose, top, choose, height);
			_path.SetBounds(left, top, Math.Max(1, _choose.Left - gap - left), height);
			top += height + UiTheme.Scale(this, 8);
			_account.SetBounds(left, top, account, height);
			_language.SetBounds(right - language, top, language, height);
		}
		else
		{
			_rebuild.SetBounds(right - rebuild, top, rebuild, height);
			_language.SetBounds(_rebuild.Left - gap - language, top, language, height);
			_account.SetBounds(_language.Left - gap - account, top, account, height);
			_choose.SetBounds(_account.Left - gap - choose, top, choose, height);
			_path.SetBounds(left, top, Math.Max(1, _choose.Left - gap - left), height);
		}
	}
}
