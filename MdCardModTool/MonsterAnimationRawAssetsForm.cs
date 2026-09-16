using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class MonsterAnimationRawAssetsForm : Form
{
	private readonly string _gameRoot;

	private readonly string _cardId;

	private readonly MonsterAnimationRawAssetService _service = new MonsterAnimationRawAssetService();

	private readonly ModEngine _engine = new ModEngine();

	private readonly BufferedListView _assets = new BufferedListView
	{
		Dock = DockStyle.Fill,
		View = View.Details,
		FullRowSelect = true,
		MultiSelect = false,
		HideSelection = false,
		AllowDrop = true
	};

	private readonly PictureBox _imagePreview = new PictureBox
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = UiTheme.SurfaceAlt
	};

	private readonly TextBox _textPreview = new TextBox
	{
		Dock = DockStyle.Fill,
		Multiline = true,
		ReadOnly = true,
		ScrollBars = ScrollBars.Both,
		WordWrap = false,
		Font = new Font("Consolas", 9.5f),
		BackColor = UiTheme.SurfaceAlt,
		ForeColor = UiTheme.Text,
		BorderStyle = BorderStyle.None
	};

	private readonly Label _details = new Label
	{
		Dock = DockStyle.Bottom,
		Height = 88,
		Padding = new Padding(12, 9, 12, 6),
		ForeColor = UiTheme.Text,
		BackColor = UiTheme.Surface
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Fill,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Muted
	};

	private readonly List<Button> _buttons = new List<Button>();

	private readonly HashSet<Button> _mutationButtons = new HashSet<Button>();

	private readonly Dictionary<Control, string> _localizedControls = new Dictionary<Control, string>();

	private readonly Label _bannerSubtitle = new Label
	{
		Dock = DockStyle.Bottom,
		Height = 22,
		ForeColor = UiTheme.Primary,
		BackColor = Color.Transparent
	};

	private MonsterAnimationSet? _set;

	private string? _lastExportDirectory;

	private bool _busy;

	private bool _readOnlyEquivalent;

	private string? _statusResourceId;

	private object?[] _statusValues = Array.Empty<object>();

	public MonsterAnimationRawAssetsForm(string gameRoot, string cardId)
	{
		_gameRoot = gameRoot;
		_cardId = cardId;
		UiTheme.ApplyDarkTitleBar(this);
		Text = Localizer.F("raw.title", _cardId);
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new Size(1240, 800);
		MinimumSize = new Size(960, 640);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		AllowDrop = true;
		UiTheme.StyleList(_assets);
		UiTheme.StyleTextBox(_textPreview);
		_assets.Columns.Add("", 230);
		_assets.Columns.Add("", 110);
		_assets.Columns.Add("", 150);
		_assets.Columns.Add("", 120);
		_assets.Columns.Add("", 350);
		_assets.Resize += delegate
		{
			if (_assets.Columns.Count == 5)
			{
				_assets.Columns[4].Width = Math.Max(220, _assets.ClientSize.Width - 610);
			}
		};
		_assets.SelectedIndexChanged += async delegate
		{
			await ShowSelectedAsync();
		};
		_assets.DoubleClick += async delegate
		{
			await ReplaceSelectedAsync();
		};
		_assets.DragEnter += OnDragEnter;
		_assets.DragDrop += async delegate(object? _, DragEventArgs e)
		{
			await OnDragDropAsync(e);
		};
		base.DragEnter += OnDragEnter;
		base.DragDrop += async delegate(object? _, DragEventArgs e)
		{
			await OnDragDropAsync(e);
		};
		Button button = Bind(ActionButton("", async delegate
		{
			await ExportAllAsync();
		}, ButtonTone.Primary), "raw.action.exportall");
		Button button2 = Bind(ActionButton("", async delegate
		{
			await ImportAllAsync();
		}, ButtonTone.Gold, mutating: true), "raw.action.importall");
		Button button3 = Bind(ActionButton("", async delegate
		{
			await ExportSelectedAsync();
		}), "raw.action.exportselected");
		Button button4 = Bind(ActionButton("", async delegate
		{
			await ReplaceSelectedAsync();
		}, ButtonTone.Neutral, mutating: true), "raw.action.replaceselected");
		Button button5 = Bind(ActionButton("", async delegate
		{
			await RestoreSelectedAsync();
		}, ButtonTone.Danger, mutating: true), "raw.action.restorebundle");
		Button button6 = Bind(ActionButton("", delegate
		{
			OpenExportDirectory();
		}), "raw.action.openfolder");
		GradientBanner gradientBanner = new GradientBanner
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(22, 9, 22, 8)
		};
		gradientBanner.Controls.Add(Bind(new Label
		{
			Dock = DockStyle.Top,
			Height = 28,
			Font = new Font("Segoe UI Semibold", 16f),
			ForeColor = UiTheme.Text,
			BackColor = Color.Transparent
		}, "raw.banner.title"));
		gradientBanner.Controls.Add(_bannerSubtitle);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			Padding = new Padding(18, 7, 18, 7),
			BackColor = UiTheme.Surface
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(_status, 0, 0);
		tableLayoutPanel.Controls.Add(Bind(new Label
		{
			AutoSize = true,
			Anchor = AnchorStyles.Right,
			ForeColor = Color.Orange
		}, "raw.warning"), 1, 0);
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface
		};
		borderPanel.Controls.Add(_assets);
		borderPanel.Controls.Add(SectionHeading("raw.section.assets", "raw.section.assets.subtitle"));
		BorderPanel borderPanel2 = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface
		};
		borderPanel2.Controls.Add(_imagePreview);
		borderPanel2.Controls.Add(_textPreview);
		borderPanel2.Controls.Add(_details);
		borderPanel2.Controls.Add(SectionHeading("raw.section.preview", "raw.section.preview.subtitle"));
		_textPreview.BringToFront();
		_details.BringToFront();
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			SplitterDistance = 720,
			SplitterWidth = 8,
			BackColor = UiTheme.Window
		};
		splitContainer.Panel1.Padding = new Padding(14, 14, 7, 8);
		splitContainer.Panel2.Padding = new Padding(7, 14, 14, 8);
		splitContainer.Panel1.Controls.Add(borderPanel);
		splitContainer.Panel2.Controls.Add(borderPanel2);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			AutoScroll = true,
			BackColor = UiTheme.SurfaceAlt,
			Padding = new Padding(14, 7, 14, 7)
		};
		Button[] array = new Button[6] { button, button2, button3, button4, button5, button6 };
		foreach (Button value in array)
		{
			flowLayoutPanel.Controls.Add(value);
		}
		Label control = new Label
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(18, 4, 18, 4),
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiTheme.Muted,
			BackColor = UiTheme.Surface,
			Text = ""
		};
		Bind(control, "raw.note");
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 5,
			ColumnCount = 1,
			BackColor = UiTheme.Window
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		tableLayoutPanel2.Controls.Add(gradientBanner, 0, 0);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel, 0, 1);
		tableLayoutPanel2.Controls.Add(splitContainer, 0, 2);
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 3);
		tableLayoutPanel2.Controls.Add(control, 0, 4);
		base.Controls.Add(tableLayoutPanel2);
		base.Shown += async delegate
		{
			await LoadAssetsAsync();
		};
		base.FormClosed += delegate
		{
			Localizer.LanguageChanged -= OnLanguageChanged;
			_imagePreview.Image?.Dispose();
		};
		Localizer.LanguageChanged += OnLanguageChanged;
		ApplyLanguage();
	}

	private Button ActionButton(string text, EventHandler click, ButtonTone tone = ButtonTone.Neutral, bool mutating = false)
	{
		Button button = UiTheme.Button(text, click, tone);
		_buttons.Add(button);
		if (mutating)
		{
			_mutationButtons.Add(button);
		}
		return button;
	}

	private T Bind<T>(T control, string resourceId) where T : Control
	{
		_localizedControls[control] = resourceId;
		control.Text = Localizer.T(resourceId);
		return control;
	}

	private void ApplyLanguage()
	{
		Text = Localizer.F("raw.title", _cardId);
		foreach (KeyValuePair<Control, string> localizedControl in _localizedControls)
		{
			localizedControl.Deconstruct(out var key, out var value);
			Control control = key;
			string id = value;
			control.Text = Localizer.T(id);
		}
		_bannerSubtitle.Text = Localizer.F("raw.banner.subtitle", _cardId);
		if (_assets.Columns.Count == 5)
		{
			string[] array = new string[5] { "raw.column.profile", "raw.column.kind", "raw.column.name", "raw.column.pathid", "raw.column.bundle" };
			for (int i = 0; i < array.Length; i++)
			{
				_assets.Columns[i].Text = Localizer.T(array[i]);
			}
		}
		foreach (ListViewItem item in _assets.Items)
		{
			if (item.Tag is MonsterAnimationAssetRef monsterAnimationAssetRef && item.SubItems.Count > 1)
			{
				item.SubItems[1].Text = KindLabel(monsterAnimationAssetRef.Kind);
			}
		}
		if (_statusResourceId == "raw.status.located" && _set != null)
		{
			_statusValues = new object[3]
			{
				_set.CountSummary,
				_set.Assets.Count,
				Localizer.T(_set.IsComplete ? "raw.completeness.complete" : "raw.completeness.incomplete")
			};
		}
		else if (_statusResourceId == "raw.status.located.fallback" && _set != null)
		{
			_statusValues = new object[4]
			{
				_cardId,
				_set.CardId,
				_set.CountSummary,
				_set.Assets.Count
			};
		}
		RefreshSelectedDetails();
		RefreshStatus();
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		ApplyLanguage();
	}

	private void SetStatus(string resourceId, params object?[] values)
	{
		_statusResourceId = resourceId;
		_statusValues = values;
		RefreshStatus();
	}

	private void RefreshStatus()
	{
		if (_statusResourceId != null)
		{
			_status.Text = Localizer.F(_statusResourceId, _statusValues);
		}
		else if (_status.Text.Length == 0)
		{
			_status.Text = Localizer.T("raw.status.ready");
		}
	}

	private static string KindLabel(MonsterAnimationAssetKind kind)
	{
		return Localizer.T(kind switch
		{
			MonsterAnimationAssetKind.Texture => "raw.kind.texture",
			MonsterAnimationAssetKind.Atlas => "raw.kind.atlas",
			_ => "raw.kind.skeleton",
		});
	}

	private void RefreshSelectedDetails()
	{
		MonsterAnimationAssetRef monsterAnimationAssetRef = SelectedAsset();
		if (monsterAnimationAssetRef != null && !string.IsNullOrWhiteSpace(_details.Text))
		{
			MonsterAnimationAssetProfile monsterAnimationAssetProfile = _service.ResolveProfile(monsterAnimationAssetRef);
			_details.Text = Localizer.F("raw.details", monsterAnimationAssetProfile.DisplayName, KindLabel(monsterAnimationAssetRef.Kind), monsterAnimationAssetRef.Name, monsterAnimationAssetRef.PathId, monsterAnimationAssetRef.StorageKind, monsterAnimationAssetRef.RelativeBundlePath);
		}
	}

	private async Task LoadAssetsAsync()
	{
		try
		{
			SetBusy(true, "raw.status.locating");
			_set = await Task.Run(() => MonsterAnimationIndexService.Find(_gameRoot, _cardId));
			_readOnlyEquivalent = false;
			if (!_set.IsComplete && int.TryParse(_cardId, out var result))
			{
				CardCatalogService catalog = CardCatalogService.LoadBestAvailable();
				CardCatalogEntry card = catalog.Find(result);
				MonsterAnimationSet monsterAnimationSet = ((!(card == null)) ? (await Task.Run(() => MonsterAnimationIndexService.FindEquivalentPreview(_gameRoot, card, catalog))) : null);
				MonsterAnimationSet monsterAnimationSet2 = monsterAnimationSet;
				if (monsterAnimationSet2 != null && monsterAnimationSet2.IsComplete)
				{
					_set = monsterAnimationSet2;
					_readOnlyEquivalent = true;
				}
			}
			_assets.BeginUpdate();
			_assets.Items.Clear();
			foreach (MonsterAnimationAssetRef item in from x in _set.Assets
				orderby _service.ResolveProfile(x).Tier, x.Kind
				select x)
			{
				MonsterAnimationAssetProfile monsterAnimationAssetProfile = _service.ResolveProfile(item);
				_assets.Items.Add(new ListViewItem(new string[5]
				{
					monsterAnimationAssetProfile.DisplayName,
					KindLabel(item.Kind),
					item.Name,
					item.PathId.ToString(),
					item.RelativeBundlePath
				})
				{
					Tag = item
				});
			}
			_assets.EndUpdate();
			if (_readOnlyEquivalent)
			{
				SetStatus("raw.status.located.fallback", _cardId, _set.CardId, _set.CountSummary, _set.Assets.Count);
			}
			else
			{
				SetStatus("raw.status.located", _set.CountSummary, _set.Assets.Count, Localizer.T(_set.IsComplete ? "raw.completeness.complete" : "raw.completeness.incomplete"));
			}
			_status.ForeColor = (_set.IsComplete ? UiTheme.Primary : Color.OrangeRed);
			if (_assets.Items.Count > 0)
			{
				_assets.Items[0].Selected = true;
				_assets.Items[0].Focused = true;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("raw.error.read"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(false, null);
		}
		if (_assets.SelectedItems.Count == 1)
		{
			await ShowSelectedAsync();
		}
	}

	private MonsterAnimationAssetRef? SelectedAsset()
	{
		if (_assets.SelectedItems.Count != 1)
		{
			return null;
		}
		return _assets.SelectedItems[0].Tag as MonsterAnimationAssetRef;
	}

	private async Task ShowSelectedAsync(bool force = false)
	{
		MonsterAnimationAssetRef asset = SelectedAsset();
		if (asset == null || (_busy && !force))
		{
			return;
		}
		try
		{
			SetBusy(true, "raw.status.reading");
			byte[] array = await Task.Run(() => (asset.Kind == MonsterAnimationAssetKind.Texture) ? _engine.DecodePng(asset.AsTexture(), 1024) : _service.Read(asset));
			if (asset.Kind == MonsterAnimationAssetKind.Texture)
			{
				using MemoryStream stream = new MemoryStream(array);
				using Image original = Image.FromStream(stream);
				Bitmap image = new Bitmap(original);
				_imagePreview.Image?.Dispose();
				_imagePreview.Image = image;
				_imagePreview.Visible = true;
				_imagePreview.BringToFront();
				_textPreview.Visible = false;
				_details.BringToFront();
			}
			else
			{
				_imagePreview.Visible = false;
				_textPreview.Visible = true;
				string text = Encoding.UTF8.GetString(array).TrimEnd('\0');
				_textPreview.Text = ((text.Length <= 1500000) ? text : (text.Substring(0, 1500000) + "\r\n\r\n" + Localizer.T("raw.preview.truncated")));
				_textPreview.SelectionStart = 0;
				_textPreview.ScrollToCaret();
				_textPreview.BringToFront();
				_details.BringToFront();
			}
			MonsterAnimationAssetProfile monsterAnimationAssetProfile = _service.ResolveProfile(asset);
			_details.Text = Localizer.F("raw.details", monsterAnimationAssetProfile.DisplayName, KindLabel(asset.Kind), asset.Name, asset.PathId, asset.StorageKind, asset.RelativeBundlePath);
			SetStatus("raw.status.read", _service.ExportFileName(asset));
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("raw.error.preview"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(false, null);
		}
	}

	private async Task ExportSelectedAsync()
	{
		MonsterAnimationAssetRef asset = SelectedAsset();
		if (asset == null)
		{
			MessageBox.Show(this, Localizer.T("raw.prompt.selectasset"), Text);
			return;
		}
		string filter = asset.Kind switch
		{
			MonsterAnimationAssetKind.Texture => Localizer.T("raw.filter.png"),
			MonsterAnimationAssetKind.Atlas => Localizer.T("raw.filter.atlas"),
			_ => Localizer.T("raw.filter.json"),
		};
		SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = filter,
			FileName = _service.ExportFileName(asset)
		};
		try
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			try
			{
				SetBusy(true, "raw.status.exporting.selected");
				string fileName = dialog.FileName;
				string path = fileName;
				await File.WriteAllBytesAsync(path, await Task.Run(() => _service.Read(asset)));
				_lastExportDirectory = Path.GetDirectoryName(dialog.FileName);
				SetStatus("raw.status.exported.path", dialog.FileName);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, Localizer.T("raw.error.export"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				SetBusy(false, null);
			}
		}
		finally
		{
			((IDisposable)dialog)?.Dispose();
		}
	}

	private async Task ExportAllAsync()
	{
		if (_set == null || _set.Assets.Count == 0)
		{
			return;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), Localizer.T("raw.folder.export.parent"), "P" + _cardId);
		FolderBrowserDialog dialog = new FolderBrowserDialog
		{
			Description = Localizer.T("raw.folder.export.description"),
			InitialDirectory = (Directory.Exists(text) ? text : Environment.GetFolderPath(Environment.SpecialFolder.Personal))
		};
		try
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			string directory = Path.Combine(dialog.SelectedPath, Localizer.F("raw.folder.export.name", _cardId));
			try
			{
				SetBusy(true, "raw.status.exporting.all");
				RawAnimationManifest rawAnimationManifest = await Task.Run(() => _service.ExportAll(_set, directory));
				_lastExportDirectory = directory;
				SetStatus("raw.status.exported.count", rawAnimationManifest.Files.Count, directory);
				OpenExportDirectory();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, Localizer.T("raw.error.exportall"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				SetBusy(false, null);
			}
		}
		finally
		{
			((IDisposable)dialog)?.Dispose();
		}
	}

	private async Task ReplaceSelectedAsync(string? inputPath = null)
	{
		if (_readOnlyEquivalent)
		{
			MessageBox.Show(this, Localizer.F("raw.prompt.equivalent.readonly", _cardId, _set?.CardId), Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		MonsterAnimationAssetRef asset = SelectedAsset();
		if (asset == null)
		{
			MessageBox.Show(this, Localizer.T("raw.prompt.selectreplace"), Text);
		}
		else
		{
			if (!EnsureGameClosed())
			{
				return;
			}
			if (inputPath == null)
			{
				string filter = asset.Kind switch
				{
					MonsterAnimationAssetKind.Texture => Localizer.T("raw.filter.png"),
					MonsterAnimationAssetKind.Atlas => Localizer.T("raw.filter.atlasinput"),
					_ => Localizer.T("raw.filter.jsoninput"),
				};
				OpenFileDialog openFileDialog = new OpenFileDialog
				{
					Title = Localizer.T("raw.dialog.choosefile"),
					Filter = filter
				};
				try
				{
					if (openFileDialog.ShowDialog(this) != DialogResult.OK)
					{
						return;
					}
					inputPath = openFileDialog.FileName;
				}
				finally
				{
					((IDisposable)openFileDialog)?.Dispose();
				}
			}
			if (MessageBox.Show(this, Localizer.F("raw.confirm.single.message", KindLabel(asset.Kind), _service.ExportFileName(asset)), Localizer.T("raw.confirm.single.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
			{
				return;
			}
			try
			{
				SetBusy(true, "raw.status.replacing");
				await Task.Run(delegate
				{
					_service.ReplaceOne(_gameRoot, asset, inputPath);
				});
				await ShowSelectedAsync(force: true);
				SetStatus("raw.status.replaced", _service.ExportFileName(asset));
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, Localizer.T("raw.error.replace"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
			}
			finally
			{
				SetBusy(false, null);
			}
		}
	}

	private async Task ImportAllAsync()
	{
		if (_readOnlyEquivalent)
		{
			MessageBox.Show(this, Localizer.F("raw.prompt.equivalent.readonly", _cardId, _set?.CardId), Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			if (_set == null || _set.Assets.Count == 0 || !EnsureGameClosed())
			{
				return;
			}
			FolderBrowserDialog dialog = new FolderBrowserDialog
			{
				Description = Localizer.T("raw.folder.import.description"),
				InitialDirectory = (_lastExportDirectory ?? "")
			};
			try
			{
				if (dialog.ShowDialog(this) != DialogResult.OK || MessageBox.Show(this, Localizer.T("raw.confirm.import.message"), Localizer.T("raw.confirm.import.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
				{
					return;
				}
				try
				{
					SetBusy(true, "raw.status.importing");
					int count = await Task.Run(() => _service.ImportAll(_gameRoot, _set, dialog.SelectedPath));
					_lastExportDirectory = dialog.SelectedPath;
					await ShowSelectedAsync(force: true);
					SetStatus("raw.status.imported", count);
					MessageBox.Show(this, Localizer.F("raw.message.imported", count), Localizer.T("raw.message.imported.title"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, ex.Message, Localizer.T("raw.error.import"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
				finally
				{
					SetBusy(false, null);
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
	}

	private async Task RestoreSelectedAsync()
	{
		if (_readOnlyEquivalent)
		{
			MessageBox.Show(this, Localizer.F("raw.prompt.equivalent.readonly", _cardId, _set?.CardId), Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		MonsterAnimationAssetRef asset = SelectedAsset();
		if (asset == null || !EnsureGameClosed() || MessageBox.Show(this, Localizer.T("raw.confirm.restore.message"), Localizer.T("raw.confirm.restore.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			SetBusy(true, "raw.status.restoring");
			bool restored = await Task.Run(() => _service.Restore(_gameRoot, asset));
			if (restored)
			{
				await ShowSelectedAsync(force: true);
			}
			SetStatus(restored ? "raw.status.restored" : "raw.status.nobackup");
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, Localizer.T("raw.error.restore"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			SetBusy(false, null);
		}
	}

	private void OnDragEnter(object? sender, DragEventArgs e)
	{
		IDataObject data = e.Data;
		e.Effect = ((!_readOnlyEquivalent && data != null && data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
	}

	private async Task OnDragDropAsync(DragEventArgs e)
	{
		if (!_readOnlyEquivalent && e.Data?.GetData(DataFormats.FileDrop) is string[] array && array.Length != 0)
		{
			await ReplaceSelectedAsync(array[0]);
		}
	}

	private bool EnsureGameClosed()
	{
		if (Process.GetProcessesByName("masterduel").Length == 0)
		{
			return true;
		}
		MessageBox.Show(this, Localizer.T("raw.error.game.running"), Text, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		return false;
	}

	private void OpenExportDirectory()
	{
		if (string.IsNullOrWhiteSpace(_lastExportDirectory) || !Directory.Exists(_lastExportDirectory))
		{
			MessageBox.Show(this, Localizer.T("raw.error.noexport"), Text);
			return;
		}
		Process.Start(new ProcessStartInfo("explorer.exe")
		{
			UseShellExecute = true,
			ArgumentList = { _lastExportDirectory }
		});
	}

	private void SetBusy(bool busy, string? statusResourceId = null, params object?[] statusValues)
	{
		_busy = busy;
		base.UseWaitCursor = busy;
		_assets.Enabled = !busy;
		foreach (Button button in _buttons)
		{
			button.Enabled = !busy && (!_readOnlyEquivalent || !_mutationButtons.Contains(button));
		}
		if (statusResourceId != null)
		{
			SetStatus(statusResourceId, statusValues);
		}
	}

	private Panel SectionHeading(string titleResourceId, string subtitleResourceId)
	{
		Panel obj = new Panel
		{
			Dock = DockStyle.Top,
			Height = 44,
			BackColor = UiTheme.SurfaceAlt,
			Padding = new Padding(12, 5, 12, 4)
		};
		Label value = Bind(new Label
		{
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiTheme.Muted,
			Font = new Font("Segoe UI", 8f),
			Padding = new Padding(4, 2, 0, 0)
		}, subtitleResourceId);
		Label value2 = Bind(new Label
		{
			Dock = DockStyle.Left,
			Width = 112,
			TextAlign = ContentAlignment.MiddleLeft,
			ForeColor = UiTheme.Text,
			Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
		}, titleResourceId);
		obj.Controls.Add(value);
		obj.Controls.Add(value2);
		return obj;
	}
}
