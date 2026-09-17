using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

public sealed class OverFrameFrameEditorForm : Form
{
	private enum FrameCompositionMode
	{
		AstellarTransparent,
		TransparentGradient,
		FloowanGradient,
		StandardComplete
	}

	private sealed record FrameModeChoice(FrameCompositionMode Mode, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private sealed class FrameChoice
	{
		public TexRef? Texture { get; init; }

		public string? FilePath { get; init; }

		public required string Key { get; init; }

		public required string BaseKey { get; init; }

		public required string DisplayName { get; init; }

		public bool IsCustom => FilePath != null;

		public override string ToString()
		{
			return DisplayName;
		}
	}

	private readonly string _gameRoot;

	private readonly TexRef _art;

	private readonly ushort _cardId;

	private readonly byte[]? _initialSource;

	private readonly byte[]? _initialBackground;

	private readonly bool _replaceStoredBackground;

	private readonly string? _initialFrameKey;

	private readonly ModEngine _engine = new ModEngine();

	private readonly OverFrameService _overFrames = new OverFrameService();

	private readonly TexRef[] _availableFrames;

	private readonly ModernComboBox _mode = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly ModernComboBox _frames = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly CropCanvas _canvas;

	private readonly TrackBar _zoom = new TrackBar
	{
		Minimum = 1,
		Maximum = 2000,
		Value = 100,
		TickFrequency = 100,
		SmallChange = 2,
		LargeChange = 10,
		AutoSize = false,
		Width = 220,
		Height = 34,
		BackColor = UiTheme.SurfaceAlt
	};

	private readonly Label _zoomValue = new Label
	{
		AutoSize = false,
		Width = 58,
		Height = 32,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Primary,
		Text = "100%"
	};

	private readonly Label _transformStatus = new Label
	{
		AutoSize = false,
		Width = 210,
		Height = 32,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Muted,
		AutoEllipsis = true
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Bottom,
		Height = 46,
		Padding = new Padding(12, 7, 12, 7),
		ForeColor = System.Drawing.Color.Gainsboro,
		AutoEllipsis = true
	};

	private readonly Label _layerStatus = new Label
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Gold,
		TextAlign = ContentAlignment.MiddleLeft,
		AutoEllipsis = true
	};

	private readonly Button _clearBackgroundButton;

	private readonly RoundedButton _finalPreviewButton;

	private readonly RoundedButton _editCanvasButton;

	private readonly Timer _renderTimer = new Timer
	{
		Interval = 160
	};

	private readonly Dictionary<string, byte[]> _frameCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, AstellarOverFrameTemplate> _overFrameTemplateCache = new Dictionary<string, AstellarOverFrameTemplate>(StringComparer.OrdinalIgnoreCase);

	private byte[]? _sourceBytes;

	private byte[]? _artBytes;

	private byte[]? _backgroundBytes;

	private byte[]? _currentFrameBytes;

	private byte[]? _previewBytes;

	private byte[]? _outputBytes;

	private string? _currentFrameKey;

	private string? _canvasFrameKey;

	private string? _previewFrameKey;

	private FrameCompositionMode _previewMode;

	private int _transparentEdgePixels;

	private int _generation;

	private bool _loading;

	private bool _rendering;

	private bool _renderPending;

	private bool _syncingZoom;

	private bool _suppressCanvasChanges;

	private bool _restoredTransform;

	private bool _sourceHasTransparency;

	private bool _showRenderedPreview = true;

	private string? _customFramePath;

	private OverFrameFrameSettings _savedSettings = new OverFrameFrameSettings();
	private readonly CheckBox _removeFoilInnerFrame = new CheckBox
	{
		Text = "去除闪卡／镜碎内框（实验，仅当前卡；可能影响局部闪度）",
		AutoSize = true, ForeColor = UiTheme.Text, BackColor = UiTheme.SurfaceAlt,
		Margin = new Padding(4, 5, 4, 5)
	};

	public string AppliedFrameName { get; private set; } = "";

	private FrameCompositionMode CurrentMode => (_mode.SelectedItem as FrameModeChoice)?.Mode ?? FrameCompositionMode.AstellarTransparent;

	public OverFrameFrameEditorForm(string gameRoot, TexRef art, IEnumerable<TexRef> frames, byte[]? initialArt = null, string? initialFrameKey = null, byte[]? initialBackground = null, bool replaceStoredBackground = false)
	{
		UiTheme.ApplyDarkTitleBar(this);
		if (!ushort.TryParse(art.CardKey, out _cardId))
		{
			throw new ArgumentException("所选资源没有有效卡号。", "art");
		}
		_gameRoot = gameRoot;
		_art = art;
		_availableFrames = frames.Where((TexRef frame) => frame.Width == 704 && frame.Height == 1024).ToArray();
		_initialSource = initialArt?.ToArray();
		_initialFrameKey = initialFrameKey;
		_initialBackground = initialBackground?.ToArray();
		_replaceStoredBackground = replaceStoredBackground;
		Text = $"制作超框 · {_cardId}";
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new System.Drawing.Size(1120, 900);
		MinimumSize = new System.Drawing.Size(860, 700);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		base.KeyPreview = true;
		base.DpiChanged += delegate
		{
			UiTheme.QueueStableRepaint(this);
		};
		base.ResizeEnd += delegate
		{
			UiTheme.QueueStableRepaint(this);
		};
		UiTheme.StyleComboBox(_mode);
		UiTheme.StyleComboBox(_frames);
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.AstellarTransparent, "透明卡框"));
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.TransparentGradient, "透明炫彩卡框"));
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.FloowanGradient, "炫彩卡框"));
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.StandardComplete, "普通卡框"));
		_mode.SelectedItem = _mode.Items.Cast<object>().OfType<FrameModeChoice>().First((FrameModeChoice choice) => choice.Mode == ModeForFrameKey(_initialFrameKey, FrameCompositionMode.AstellarTransparent));
		RefreshFrameChoices(_initialFrameKey);
		using Bitmap original = new Bitmap(2, 2);
		_canvas = new CropCanvas(new Bitmap(original), 704, 1024, fullCardOverlay: false, overFrameEditing: true)
		{
			Dock = DockStyle.Fill,
			TabStop = true,
			AccessibleName = "超框卡图直接编辑画布"
		};
		Button exportFrame = Button("导出卡框 PNG", async delegate
		{
			await ExportFrameAsync();
		});
		Button importFrame = Button("导入自定义卡框", async delegate
		{
			await ImportFrameAsync();
		});
		Button changeArt = UiTheme.Button("更换卡图", async delegate
		{
			await ChangeArtAsync();
		}, ButtonTone.Primary);
		Button addBackground = UiTheme.Button("添加叠底背景", async delegate
		{
			await AddBackgroundAsync();
		}, ButtonTone.Gold);
		_clearBackgroundButton = Button("清除背景", async delegate
		{
			await ClearBackgroundAsync();
		});
		Button exportPreview = Button("导出最终 PNG", delegate
		{
			ExportPreview();
		});
		_finalPreviewButton = (RoundedButton)UiTheme.Button("真实 Alpha 预览", delegate
		{
			SetCanvasView(showRenderedPreview: true);
		}, ButtonTone.Primary);
		_editCanvasButton = (RoundedButton)UiTheme.Button("构图编辑", delegate
		{
			SetCanvasView(showRenderedPreview: false);
		});
		Button reset = Button("铺满插图区", delegate
		{
			SetCanvasView(showRenderedPreview: false);
			_canvas.ResetView();
		});
		Button whole = Button("显示整张图", delegate
		{
			SetCanvasView(showRenderedPreview: false);
			_canvas.ShowWholeImage();
		});
		Button apply = Button("应用到游戏", async delegate
		{
			await ApplyAsync();
		}, accent: true);
		TableLayoutPanel value = BuildToolbar(apply, changeArt, addBackground, importFrame, exportFrame, exportPreview, _finalPreviewButton, _editCanvasButton, reset, whole);
		base.Controls.Add(_canvas);
		base.Controls.Add(_status);
		base.Controls.Add(value);
		_canvas.ZoomChanged += CanvasZoomChanged;
		_canvas.ViewChanged += CanvasViewChanged;
		_zoom.ValueChanged += ZoomValueChanged;
		_renderTimer.Tick += async delegate
		{
			_renderTimer.Stop();
			await RenderAsync();
		};
		_frames.SelectedIndexChanged += async delegate
		{
			if (!_loading)
			{
				await RenderAsync();
			}
		};
		_mode.SelectedIndexChanged += async delegate
		{
			if (!_loading)
			{
				string wantedKey = (_frames.SelectedItem as FrameChoice)?.BaseKey;
				RefreshFrameChoices(wantedKey);
				await RenderAsync();
			}
		};
		base.Shown += async delegate
		{
			await LoadAsync();
		};
		base.FormClosing += delegate
		{
			_renderTimer.Stop();
			_generation++;
			SaveLatestDraftOnClose();
		};
		base.FormClosed += delegate
		{
			_renderTimer.Stop();
		};
		UpdateLayerStatus();
		UpdateTransformStatus();
		SetCanvasView(showRenderedPreview: true);
	}

	private TableLayoutPanel BuildToolbar(Button apply, Button changeArt, Button addBackground, Button importFrame, Button exportFrame, Button exportPreview, Button finalPreview, Button editCanvas, Button reset, Button whole)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel();
		tableLayoutPanel.Dock = DockStyle.Top;
		tableLayoutPanel.Height = 350;
		tableLayoutPanel.Padding = new Padding(14, 10, 14, 8);
		tableLayoutPanel.ColumnCount = 3;
		tableLayoutPanel.RowCount = 9;
		tableLayoutPanel.BackColor = UiTheme.SurfaceAlt;
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(ToolbarLabel("卡框分类", UiTheme.Gold), 0, 0);
		tableLayoutPanel.Controls.Add(_mode, 1, 0);
		tableLayoutPanel.SetColumnSpan(_mode, 2);
		tableLayoutPanel.Controls.Add(ToolbarLabel("卡框", System.Drawing.Color.FromArgb(160, 195, 255)), 0, 1);
		tableLayoutPanel.Controls.Add(_frames, 1, 1);
		tableLayoutPanel.Controls.Add(apply, 2, 1);
		FlowLayoutPanel flowLayoutPanel = ActionRow();
		flowLayoutPanel.Controls.Add(changeArt);
		flowLayoutPanel.Controls.Add(addBackground);
		flowLayoutPanel.Controls.Add(_clearBackgroundButton);
		Button button = UiTheme.Button("移动卡图", delegate
		{
		});
		Button button2 = UiTheme.Button("移动背景", delegate
		{
		});
		button.Click += delegate
		{
			SetCanvasView(showRenderedPreview: false);
			_canvas.EditBackground(enabled: false);
		};
		button2.Click += delegate
		{
			SetCanvasView(showRenderedPreview: false);
			_canvas.EditBackground(enabled: true);
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(button2);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 2);
		tableLayoutPanel.SetColumnSpan(flowLayoutPanel, 3);
		FlowLayoutPanel flowLayoutPanel2 = ActionRow();
		flowLayoutPanel2.Controls.Add(importFrame);
		flowLayoutPanel2.Controls.Add(exportFrame);
		flowLayoutPanel2.Controls.Add(exportPreview);
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, 3);
		tableLayoutPanel.SetColumnSpan(flowLayoutPanel2, 3);
		FlowLayoutPanel flowLayoutPanel3 = ActionRow();
		flowLayoutPanel3.Controls.Add(ToolbarLabel("画布模式", UiTheme.Text));
		flowLayoutPanel3.Controls.Add(finalPreview);
		flowLayoutPanel3.Controls.Add(editCanvas);
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = "棋盘格表示真实透明区域；Alpha=0 下的 RGB 数据仍原样保留",
			AutoSize = true,
			ForeColor = UiTheme.Muted,
			Margin = new Padding(12, 9, 0, 0)
		});
		tableLayoutPanel.Controls.Add(flowLayoutPanel3, 0, 4);
		tableLayoutPanel.SetColumnSpan(flowLayoutPanel3, 3);
		FlowLayoutPanel flowLayoutPanel4 = ActionRow();
		flowLayoutPanel4.Controls.Add(ToolbarLabel("当前图层缩放", UiTheme.Text));
		flowLayoutPanel4.Controls.Add(_zoom);
		flowLayoutPanel4.Controls.Add(_zoomValue);
		flowLayoutPanel4.Controls.Add(reset);
		flowLayoutPanel4.Controls.Add(whole);
		flowLayoutPanel4.Controls.Add(_transformStatus);
		tableLayoutPanel.Controls.Add(flowLayoutPanel4, 0, 5);
		tableLayoutPanel.SetColumnSpan(flowLayoutPanel4, 3);
		tableLayoutPanel.Controls.Add(_layerStatus, 0, 6);
		tableLayoutPanel.SetColumnSpan(_layerStatus, 3);
		Label control = new Label
		{
			Text = "直接在下方卡面拖动主体；滚轮或滑杆缩放，方向键微调，Shift + 方向键快速移动，双击恢复铺满。真正超框的图层顺序为：叠底背景 → 卡框 → 透明主体。",
			Dock = DockStyle.Fill,
			ForeColor = UiTheme.Muted,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		tableLayoutPanel.Controls.Add(_removeFoilInnerFrame, 0, 7);
		tableLayoutPanel.SetColumnSpan(_removeFoilInnerFrame, 3);
		_removeFoilInnerFrame.CheckedChanged += async delegate
		{
			if (!_loading) await RenderAsync();
		};
		tableLayoutPanel.Controls.Add(control, 0, 8);
		tableLayoutPanel.SetColumnSpan(control, 3);
		return tableLayoutPanel;
	}

	private static Label ToolbarLabel(string text, System.Drawing.Color color)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = color,
			Padding = new Padding(0, 5, 8, 0),
			Margin = new Padding(0, 3, 2, 0)
		};
	}

	private static FlowLayoutPanel ActionRow()
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			WrapContents = false,
			FlowDirection = FlowDirection.LeftToRight,
			Margin = Padding.Empty,
			Padding = Padding.Empty
		};
	}

	private static Button Button(string text, EventHandler click, bool accent = false)
	{
		return UiTheme.Button(text, click, accent ? ButtonTone.Primary : ButtonTone.Neutral);
	}

	private static FrameCompositionMode ModeForFrameKey(string? frameKey, FrameCompositionMode fallback)
	{
		if (frameKey != null && frameKey.StartsWith("transparent_gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return FrameCompositionMode.TransparentGradient;
		}
		if (frameKey != null && frameKey.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase))
		{
			return FrameCompositionMode.AstellarTransparent;
		}
		if (frameKey != null && frameKey.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return FrameCompositionMode.FloowanGradient;
		}
		if (frameKey != null && frameKey.Equals("__custom__", StringComparison.OrdinalIgnoreCase))
		{
			return FrameCompositionMode.StandardComplete;
		}
		return fallback;
	}

	private void SelectMode(FrameCompositionMode mode)
	{
		_mode.SelectedItem = _mode.Items.Cast<object>().OfType<FrameModeChoice>().First((FrameModeChoice choice) => choice.Mode == mode);
	}

	private void RefreshFrameChoices(string? wantedKey)
	{
		bool loading = _loading;
		_loading = true;
		try
		{
			_frames.BeginUpdate();
			_frames.Items.Clear();
			foreach (TexRef item in (CurrentMode switch
			{
				FrameCompositionMode.AstellarTransparent => _availableFrames.Where(BuiltInCardFrameCatalog.IsTransparentFrame),
				FrameCompositionMode.TransparentGradient => _availableFrames.Where(BuiltInCardFrameCatalog.IsTransparentGradientFrame),
				FrameCompositionMode.FloowanGradient => _availableFrames.Where(BuiltInCardFrameCatalog.IsGradientFrame),
				_ => _availableFrames.Where(BuiltInCardFrameCatalog.IsNormalFrame),
			}).OrderBy<TexRef, string>((TexRef frame) => CardFrameCatalog.BaseKey(frame.Name), StringComparer.OrdinalIgnoreCase))
			{
				string text = CardFrameCatalog.BaseKey(item.Name);
				_frames.Items.Add(new FrameChoice
				{
					Texture = item,
					Key = item.Name,
					BaseKey = text,
					DisplayName = text + " · " + CardFrameCatalog.FriendlyName(text)
				});
			}
			if (CurrentMode == FrameCompositionMode.StandardComplete && File.Exists(_customFramePath))
			{
				_frames.Items.Add(new FrameChoice
				{
					Key = "__custom__",
					BaseKey = "__custom__",
					FilePath = _customFramePath,
					DisplayName = "自定义卡框 · " + Path.GetFileName(_customFramePath)
				});
			}
			string wantedBase = CardFrameCatalog.BaseKey(string.IsNullOrWhiteSpace(wantedKey) ? "card_frame01" : wantedKey);
			FrameChoice selectedItem = _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault((FrameChoice choice) => choice.Key.Equals(wantedKey, StringComparison.OrdinalIgnoreCase)) ?? _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault((FrameChoice choice) => choice.BaseKey.Equals(wantedBase, StringComparison.OrdinalIgnoreCase)) ?? _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault();
			_frames.SelectedItem = selectedItem;
		}
		finally
		{
			_frames.EndUpdate();
			_loading = loading;
		}
	}

	private async Task LoadAsync()
	{
		base.UseWaitCursor = true;
		_loading = true;
		_status.Text = "正在读取当前卡图、构图与卡框…";
		try
		{
			_savedSettings = OverFrameArtStore.ReadSettings(_gameRoot, _cardId);
			_removeFoilInnerFrame.Checked = _savedSettings.RemoveFoilInnerFrame;
			string mappingFrameKey = ((!string.IsNullOrWhiteSpace(_initialFrameKey)) ? _initialFrameKey : _savedSettings.FrameKey);
			if (_replaceStoredBackground)
			{
				await Task.Run(delegate
				{
					if (_initialBackground != null)
					{
						OverFrameArtStore.SaveBackground(_gameRoot, _cardId, _initialBackground);
					}
					else
					{
						OverFrameArtStore.DeleteBackground(_gameRoot, _cardId);
					}
				});
			}
			string sourcePath = OverFrameArtStore.SourcePath(_gameRoot, _cardId);
			GameTextureDisplayMapping liveSourceMapping = GameTextureDisplayMapping.Resolve(_art, mappingFrameKey);
			if (_initialSource != null)
			{
				await Task.Run(delegate
				{
					OverFrameArtStore.SaveSource(_gameRoot, _cardId, _initialSource);
				});
			}
			else if (!File.Exists(sourcePath))
			{
				string legacyArt = OverFrameArtStore.ArtPath(_gameRoot, _cardId);
				if (File.Exists(legacyArt))
				{
					await Task.Run(delegate
					{
						OverFrameArtStore.SaveSource(_gameRoot, _cardId, legacyArt);
					});
				}
				else
				{
					byte[] current = await Task.Run(() => _engine.DecodePng(_art));
					if (liveSourceMapping.RequiresMapping)
					{
						current = await Task.Run(() => liveSourceMapping.DecodeForDisplay(current));
					}
					await Task.Run(delegate
					{
						OverFrameArtStore.SaveSource(_gameRoot, _cardId, current);
					});
				}
			}
			_sourceBytes = await File.ReadAllBytesAsync(sourcePath);
			if (_initialSource == null)
			{
				ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(_sourceBytes) ?? throw new InvalidDataException("超框源图无法识别。");
				GameTextureDisplayMapping draftMapping = GameTextureDisplayMapping.ResolveCanvas(_art, imageInfo.Width, imageInfo.Height, mappingFrameKey);
				if (draftMapping.RequiresMapping)
				{
					_sourceBytes = await Task.Run(() => draftMapping.DecodeForDisplay(_sourceBytes));
					await Task.Run(delegate
					{
						OverFrameArtStore.SaveSource(_gameRoot, _cardId, _sourceBytes);
					});
				}
			}
			_sourceHasTransparency = await Task.Run(() => HasMeaningfulTransparency(_sourceBytes));
			_suppressCanvasChanges = true;
			try
			{
				_canvas.SetSource(FrameComposer.PreviewBitmap(_sourceBytes));
			}
			finally
			{
				_suppressCanvasChanges = false;
			}
			string path = OverFrameArtStore.BackgroundPath(_gameRoot, _cardId);
			byte[] backgroundBytes = ((!File.Exists(path)) ? null : (await File.ReadAllBytesAsync(path)));
			_backgroundBytes = backgroundBytes;
			SetCanvasBackground();
			if (!_replaceStoredBackground && _savedSettings.BackgroundImageScale > 0f)
			{
				_canvas.SetBackgroundRenderSpec(new ImageRenderSpec(704f, 1024f, _savedSettings.BackgroundImageScale, _savedSettings.BackgroundOffsetX, _savedSettings.BackgroundOffsetY));
			}
			FrameCompositionMode frameCompositionMode = (_savedSettings.CompositionMode.Equals("StandardComplete", StringComparison.OrdinalIgnoreCase) ? FrameCompositionMode.StandardComplete : (_savedSettings.CompositionMode.Equals("TransparentGradient", StringComparison.OrdinalIgnoreCase) ? FrameCompositionMode.TransparentGradient : (_savedSettings.CompositionMode.Equals("FloowanGradient", StringComparison.OrdinalIgnoreCase) ? FrameCompositionMode.FloowanGradient : FrameCompositionMode.AstellarTransparent)));
			if (!string.IsNullOrWhiteSpace(_initialFrameKey))
			{
				frameCompositionMode = ModeForFrameKey(_initialFrameKey, frameCompositionMode);
			}
			SelectMode(frameCompositionMode);
			string text = OverFrameArtStore.CustomFramePath(_gameRoot, _cardId);
			if (File.Exists(text))
			{
				_customFramePath = text;
			}
			string wantedKey = ((!string.IsNullOrWhiteSpace(_initialFrameKey)) ? _initialFrameKey : (_savedSettings.UsesCustomFrame ? "__custom__" : _savedSettings.FrameKey));
			if (_savedSettings.UsesCustomFrame && string.IsNullOrWhiteSpace(_initialFrameKey))
			{
				SelectMode(FrameCompositionMode.StandardComplete);
			}
			RefreshFrameChoices(wantedKey);
			if (_frames.Items.Count == 0)
			{
				throw new InvalidOperationException("没有可用的 704×1024 卡框，请回主界面重建索引。\n自定义卡框也可通过“导入自定义卡框”加入。");
			}
			UpdateLayerStatus();
			_status.Text = "卡图已载入。可直接在卡面拖动，使用滚轮或滑杆缩放。";
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "超框编辑器载入失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			_status.Text = "载入失败：" + ex.Message;
		}
		finally
		{
			_loading = false;
			base.UseWaitCursor = false;
		}
		if (_sourceBytes != null && _frames.SelectedItem is FrameChoice)
		{
			await RenderAsync();
		}
	}

	private TaskCompletionSource<bool>? _renderCompletion;
	private string? _renderError;
	private bool _applying;

	private async Task EnsurePreviewReadyAsync()
	{
		_renderTimer.Stop();
		while (_rendering && _renderCompletion != null)
			await _renderCompletion.Task;
		if (IsDisposed || Disposing) throw new OperationCanceledException("编辑器已关闭。");
		_renderTimer.Stop();
		_renderPending = false;
		if (_outputBytes == null || _frames.SelectedItem is not FrameChoice selected ||
			_previewFrameKey != selected.Key || _previewMode != CurrentMode)
			await RenderAsync();
		if (_outputBytes == null)
			throw new InvalidOperationException(_renderError ?? "没有可应用的合成图，请确认卡图和卡框均已载入。");
	}

	private async Task RenderAsync()
	{
		if (base.IsDisposed || _sourceBytes == null)
		{
			return;
		}
		object selectedItem = _frames.SelectedItem;
		if (!(selectedItem is FrameChoice choice))
		{
			return;
		}
		if (_rendering)
		{
			_generation++;
			_outputBytes = null;
			_previewBytes = null;
			_canvas.SetRenderedPreview(null);
			_renderTimer.Stop();
			_renderPending = true;
			return;
		}
		int generation = ++_generation;
		bool removeFoilInnerFrame = _removeFoilInnerFrame.Checked;
		_renderTimer.Stop();
		_rendering = true;
		var completion = _renderCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_renderError = null;
		_outputBytes = null;
		_previewBytes = null;
		_canvas.SetRenderedPreview(null);
		_previewFrameKey = null;
		_transparentEdgePixels = 0;
		base.UseWaitCursor = true;
		_status.Text = "正在更新超框合成…";
		try
		{
			FrameCompositionMode mode = CurrentMode;
			byte[] frameBytes = await GetFrameBytesAsync(choice);
			if (generation != _generation || base.IsDisposed)
			{
				return;
			}
			string text = mode.ToString() + ":" + choice.Key;
			if (!text.Equals(_canvasFrameKey, StringComparison.OrdinalIgnoreCase))
			{
				byte[] data = frameBytes;
				_suppressCanvasChanges = true;
				try
				{
					bool hasFrame = _canvas.HasFrame;
					_canvas.SetFrame(FrameComposer.PreviewBitmap(data), hasFrame, ResolveArtWindow(choice.BaseKey));
					_canvasFrameKey = text;
					if (!_restoredTransform)
					{
						if (_savedSettings.ArtImageScale > 0f)
						{
							_canvas.SetRenderSpec(new ImageRenderSpec(704f, 1024f, _savedSettings.ArtImageScale, _savedSettings.ArtOffsetX, _savedSettings.ArtOffsetY));
						}
						_restoredTransform = true;
					}
				}
				finally
				{
					_suppressCanvasChanges = false;
				}
				UpdateTransformStatus();
			}
			var (artBytes, backgroundBytes) = await _canvas.RenderLayersAsync();
			if (generation != _generation || base.IsDisposed)
			{
				return;
			}
			int transparentPixels = 0;
			byte[] array;
			byte[] array2;
			switch (mode)
			{
			case FrameCompositionMode.AstellarTransparent:
			{
				if (choice.IsCustom)
				{
					throw new InvalidOperationException("自定义扁平 PNG 没有 Astellar 六层数据，只能用于“普通卡框”模式。");
				}
				AstellarOverFrameTemplate template2 = await GetOverFrameTemplateAsync(choice.BaseKey);
				AstellarOverFrameComposition astellarOverFrameComposition2 = await Task.Run(() => AstellarOverFrameComposer.Compose(artBytes, template2, backgroundBytes));
				array = astellarOverFrameComposition2.GamePng;
				array2 = astellarOverFrameComposition2.PreviewPng;
				transparentPixels = astellarOverFrameComposition2.TransparentEdgePixels;
				break;
			}
			case FrameCompositionMode.TransparentGradient:
			{
				AstellarOverFrameTemplate template = await GetOverFrameTemplateAsync(choice.BaseKey);
				AstellarOverFrameComposition astellarOverFrameComposition = await Task.Run(() => AstellarOverFrameComposer.ComposeTransparentFlatFrame(artBytes, frameBytes, template, backgroundBytes));
				array = astellarOverFrameComposition.GamePng;
				array2 = astellarOverFrameComposition.PreviewPng;
				transparentPixels = astellarOverFrameComposition.TransparentEdgePixels;
				break;
			}
			default:
				array = await Task.Run(() => AstellarOverFrameComposer.ComposeFlatFrame(artBytes, frameBytes, backgroundBytes));
				array2 = array;
				break;
			}
			if (removeFoilInnerFrame)
			{
				var foilTemplate = await GetOverFrameTemplateAsync(choice.BaseKey);
				array = await Task.Run(() => AstellarOverFrameComposer.RemoveFoilInnerFrame(array, foilTemplate));
				array2 = array; // Preview/export share the exact encoded texture, not a foil simulation.
			}
			if (generation == _generation && !base.IsDisposed)
			{
				_currentFrameBytes = frameBytes;
				_currentFrameKey = choice.Key;
				_artBytes = artBytes;
				_previewBytes = array2;
				_outputBytes = array;
				_canvas.SetRenderedPreview(FrameComposer.PreviewBitmap(array2));
				_canvas.SetRenderedPreviewVisible(_showRenderedPreview);
				_previewFrameKey = choice.Key;
				_previewMode = mode;
				_transparentEdgePixels = transparentPixels;
				await PersistDraftAsync(choice, artBytes);
				if (generation == _generation && !base.IsDisposed)
				{
					string value = ((backgroundBytes == null) ? "无叠底背景" : "含叠底背景");
					string value2 = (_sourceHasTransparency ? "透明主体置于卡框上层" : "源图无透明区域");
					Label status = _status;
					status.Text = mode switch
					{
						FrameCompositionMode.AstellarTransparent => $"透明卡框 · {choice.DisplayName} · {value} · {value2} · 保留 {transparentPixels:N0} 个透明 RGB 像素",
						FrameCompositionMode.TransparentGradient => $"透明炫彩卡框 · {choice.DisplayName} · {value} · {value2} · 保留 {transparentPixels:N0} 个透明 RGB 像素",
						FrameCompositionMode.FloowanGradient => $"炫彩卡框 · {choice.DisplayName} · {value} · {value2} · 704×1024",
						_ => $"普通卡框 · {choice.DisplayName} · {value} · {value2} · 704×1024",
					};
				}
			}
		}
		catch (Exception ex)
		{
			if (generation == _generation)
			{
				_status.Text = "预览失败：" + ex.Message;
				_renderError = ex.Message;
			}
		}
		finally
		{
			_rendering = false;
			base.UseWaitCursor = false;
			if (_renderPending && !base.IsDisposed)
			{
				_renderPending = false;
				_renderTimer.Start();
			}
			completion.TrySetResult(true);
		}
	}

	private void CanvasZoomChanged(float zoom)
	{
		_syncingZoom = true;
		try
		{
			_zoom.Value = Math.Clamp((int)Math.Round(zoom * 100f), _zoom.Minimum, _zoom.Maximum);
			_zoomValue.Text = $"{zoom * 100f:0}%";
		}
		finally
		{
			_syncingZoom = false;
		}
		UpdateTransformStatus();
	}

	private void CanvasViewChanged(ImageRenderSpec spec)
	{
		UpdateTransformStatus(spec);
		if (!_loading && !_suppressCanvasChanges)
		{
			_generation++;
			_outputBytes = null;
			_status.Text = "构图已调整；正在生成最终超框预览…";
			_renderTimer.Stop();
			_renderTimer.Start();
		}
	}

	private void ZoomValueChanged(object? sender, EventArgs e)
	{
		if (!_syncingZoom && !_loading)
		{
			SetCanvasView(showRenderedPreview: false);
			_canvas.SetZoom((float)_zoom.Value / 100f, null);
		}
	}

	private void SetCanvasView(bool showRenderedPreview)
	{
		_showRenderedPreview = showRenderedPreview;
		_canvas.SetRenderedPreviewVisible(showRenderedPreview);
		UpdateCanvasModeButtons();
		_zoom.Enabled = !showRenderedPreview;
		if (showRenderedPreview && _previewBytes == null)
		{
			_status.Text = "真实 Alpha 预览正在生成；完成前不会显示旧结果。";
		}
	}

	private void UpdateCanvasModeButtons()
	{
		SetSegmentState(_finalPreviewButton, _showRenderedPreview);
		SetSegmentState(_editCanvasButton, !_showRenderedPreview);
	}

	private static void SetSegmentState(RoundedButton button, bool active)
	{
		button.NormalColor = (active ? UiTheme.PrimaryDark : UiTheme.Elevated);
		button.HoverColor = (active ? System.Drawing.Color.FromArgb(38, 148, 203) : System.Drawing.Color.FromArgb(34, 53, 81));
		button.BorderColor = (active ? UiTheme.Primary : UiTheme.Border);
		button.Invalidate();
	}

	private void UpdateTransformStatus(ImageRenderSpec? provided = null)
	{
		if (!_canvas.IsDisposed)
		{
			ImageRenderSpec imageRenderSpec = provided ?? _canvas.ActiveRenderSpec;
			_transformStatus.Text = $"{(_canvas.EditingBackground ? "背景" : "卡图")} X {imageRenderSpec.OffsetX:0}  Y {imageRenderSpec.OffsetY:0}";
		}
	}

	private static System.Drawing.RectangleF ResolveArtWindow(string baseKey)
	{
		bool flag;
		switch (CardFrameCatalog.BaseKey(baseKey))
		{
		case "card_frame13":
		case "card_frame14":
		case "card_frame15":
		case "card_frame16":
		case "card_frame17":
		case "card_frame19":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return new System.Drawing.RectangleF(89f, 191f, 527f, 528f);
		}
		return new System.Drawing.RectangleF(50f, 186f, 604f, 451f);
	}

	private Task<AstellarOverFrameTemplate> GetOverFrameTemplateAsync(string key)
	{
		if (_overFrameTemplateCache.TryGetValue(key, out AstellarOverFrameTemplate value))
		{
			return Task.FromResult(value);
		}
		return Task.Run(delegate
		{
			AstellarOverFrameTemplate astellarOverFrameTemplate = AstellarOverFrameTemplateCatalog.Load(key);
			_overFrameTemplateCache[key] = astellarOverFrameTemplate;
			return astellarOverFrameTemplate;
		});
	}

	private async Task<byte[]> GetFrameBytesAsync(FrameChoice choice)
	{
		if (_frameCache.TryGetValue(choice.Key, out byte[] value))
		{
			return value;
		}
		byte[] array = ((!choice.IsCustom) ? (await Task.Run(() => _engine.DecodePng(choice.Texture))) : (await File.ReadAllBytesAsync(choice.FilePath)));
		byte[] array2 = array;
		_frameCache[choice.Key] = array2;
		return array2;
	}

	private async Task PersistDraftAsync(FrameChoice choice, byte[] artBytes)
	{
		ImageRenderSpec renderSpec = _canvas.RenderSpec;
		OverFrameFrameSettings settings = new OverFrameFrameSettings(choice.Key, choice.IsCustom, UserSelected: true, CurrentMode.ToString(), renderSpec.ImageScale, renderSpec.OffsetX, renderSpec.OffsetY, _canvas.BackgroundRenderSpec.ImageScale, _canvas.BackgroundRenderSpec.OffsetX, _canvas.BackgroundRenderSpec.OffsetY, _removeFoilInnerFrame.Checked);
		await Task.Run(delegate
		{
			OverFrameArtStore.SaveArt(_gameRoot, _cardId, artBytes);
			OverFrameArtStore.SaveSettings(_gameRoot, _cardId, settings);
		});
		_savedSettings = settings;
	}

	private void SaveLatestDraftOnClose()
	{
		if (_sourceBytes == null || !(_frames.SelectedItem is FrameChoice frameChoice))
		{
			return;
		}
		try
		{
			byte[] png = _canvas.RenderSourceToTarget();
			ImageRenderSpec renderSpec = _canvas.RenderSpec;
			OverFrameArtStore.SaveArt(_gameRoot, _cardId, png);
			OverFrameArtStore.SaveSettings(_gameRoot, _cardId, new OverFrameFrameSettings(frameChoice.Key, frameChoice.IsCustom, UserSelected: true, CurrentMode.ToString(), renderSpec.ImageScale, renderSpec.OffsetX, renderSpec.OffsetY, _canvas.BackgroundRenderSpec.ImageScale, _canvas.BackgroundRenderSpec.OffsetX, _canvas.BackgroundRenderSpec.OffsetY, _removeFoilInnerFrame.Checked));
		}
		catch
		{
		}
	}

	private async Task ImportFrameAsync()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "PNG 图片|*.png|图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "导入 704×1024 自定义卡框"
		};
		try
		{
			if (dialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			try
			{
				string customFramePath = await Task.Run(() => OverFrameArtStore.SaveCustomFrame(_gameRoot, _cardId, dialog.FileName));
				_frameCache.Remove("__custom__");
				_customFramePath = customFramePath;
				SelectMode(FrameCompositionMode.StandardComplete);
				RefreshFrameChoices("__custom__");
				await RenderAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "自定义卡框无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
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

	private async Task ChangeArtAsync()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "更换卡图（带透明背景的 PNG 才能形成主体越框效果）"
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
				_status.Text = "正在载入新卡图…";
				await Task.Run(delegate
				{
					OverFrameArtStore.SaveSource(_gameRoot, _cardId, dialog.FileName);
				});
				_sourceBytes = await File.ReadAllBytesAsync(OverFrameArtStore.SourcePath(_gameRoot, _cardId));
				_sourceHasTransparency = await Task.Run(() => HasMeaningfulTransparency(_sourceBytes));
				_suppressCanvasChanges = true;
				try
				{
					_canvas.SetSource(FrameComposer.PreviewBitmap(_sourceBytes));
					_restoredTransform = true;
				}
				finally
				{
					_suppressCanvasChanges = false;
				}
				UpdateLayerStatus();
				await RenderAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "卡图无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				_status.Text = "更换卡图失败：" + ex.Message;
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

	private async Task AddBackgroundAsync()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "添加叠底背景图（保留原图，可拖动和缩放）"
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
				_status.Text = "正在处理叠底背景…";
				_backgroundBytes = await Task.Run(delegate
				{
					OverFrameArtStore.SaveBackground(_gameRoot, _cardId, dialog.FileName);
					return File.ReadAllBytes(OverFrameArtStore.BackgroundPath(_gameRoot, _cardId));
				});
				SetCanvasBackground();
				UpdateLayerStatus();
				SetCanvasView(showRenderedPreview: false);
				_canvas.EditBackground(enabled: true);
				await RenderAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "叠底背景无效", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				_status.Text = "叠底背景添加失败：" + ex.Message;
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

	private async Task ClearBackgroundAsync()
	{
		if (_backgroundBytes == null)
		{
			return;
		}
		try
		{
			await Task.Run(delegate
			{
				OverFrameArtStore.DeleteBackground(_gameRoot, _cardId);
			});
			_backgroundBytes = null;
			SetCanvasBackground();
			UpdateLayerStatus();
			await RenderAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "清除叠底背景失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void SetCanvasBackground()
	{
		_canvas.SetBackground((_backgroundBytes == null) ? null : FrameComposer.PreviewBitmap(_backgroundBytes));
	}

	private void UpdateLayerStatus()
	{
		bool flag = _backgroundBytes != null;
		string text = (flag ? "图层：叠底背景 → 卡框 → 透明主体 · 已添加背景" : "图层：卡框 → 透明主体 · 叠底背景：未添加");
		_layerStatus.Text = (_sourceHasTransparency ? (text + " · 主体可越过卡框") : (text + " · 当前卡图没有透明区域；越框会呈矩形，请更换透明 PNG"));
		_clearBackgroundButton.Enabled = flag;
	}

	private async Task ExportFrameAsync()
	{
		object selectedItem = _frames.SelectedItem;
		if (!(selectedItem is FrameChoice choice))
		{
			return;
		}
		try
		{
			byte[] array = ((!(_currentFrameKey == choice.Key) || _currentFrameBytes == null) ? (await GetFrameBytesAsync(choice)) : _currentFrameBytes);
			byte[] bytes = array;
			using SaveFileDialog dialog = new SaveFileDialog
			{
				Filter = "PNG 图片|*.png",
				FileName = choice.Key + "_可编辑.png"
			};
			if (dialog.ShowDialog(this) == DialogResult.OK)
			{
				await File.WriteAllBytesAsync(dialog.FileName, bytes);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "导出卡框失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private void ExportPreview()
	{
		if (_previewBytes == null)
		{
			MessageBox.Show(this, "最终预览仍在生成，请稍候。", Text);
			return;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "PNG 图片|*.png",
			FileName = $"{_cardId}_透明最终预览.png"
		};
		if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
		{
			File.WriteAllBytes(saveFileDialog.FileName, _previewBytes);
		}
	}

	private async Task ApplyAsync()
	{
		if (_applying) return;
		_applying = true;
		Enabled = false;
		try
		{
			await EnsurePreviewReadyAsync();
			await ApplyReadyPreviewAsync();
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			if (!IsDisposed) MessageBox.Show(this, "无法生成应用所需的超框图：\n" + ex.Message, "应用超框失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			_applying = false;
			if (!IsDisposed) Enabled = true;
		}
	}

	private async Task ApplyReadyPreviewAsync()
	{
		if (_outputBytes != null)
		{
			object selectedItem = _frames.SelectedItem;
			if (selectedItem is FrameChoice choice && !(_previewFrameKey != choice.Key) && _previewMode == CurrentMode)
			{
				string modeLabel = CurrentMode switch
				{
					FrameCompositionMode.AstellarTransparent => $"透明卡框（保留 {_transparentEdgePixels:N0} 个透明 RGB 像素）",
					FrameCompositionMode.TransparentGradient => $"透明炫彩卡框（保留 {_transparentEdgePixels:N0} 个透明 RGB 像素）",
					FrameCompositionMode.FloowanGradient => "炫彩卡框",
					_ => "普通卡框",
				};
				string value = ((_backgroundBytes == null) ? "未使用叠底背景" : "已包含叠底背景");
				string value2 = (_sourceHasTransparency ? "主体位于卡框上方，可形成真正的越框效果。" : "当前源图没有透明区域，越框部分会保持矩形边缘。");
				if (_removeFoilInnerFrame.Checked) value2 += "\n已开启实验性闪卡去内框：仅当前卡，可能影响局部闪度；预览不模拟游戏闪面，请进游戏核验。关闭选项并重新应用可恢复默认遮罩。";
				if (MessageBox.Show(this, $"把“{choice.DisplayName}”以“{modeLabel}”模式写入卡号 {_cardId}？\n\n{value}。{value2}\n只修改这张卡的 Bundle，不会改全局 card_frame；编辑源图、背景与构图参数都会保留。", "确认应用超框", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
				{
					return;
				}
				try
				{
					byte[] output = _outputBytes.ToArray();
					base.UseWaitCursor = true;
					_status.Text = "正在定位 LocalData 超框表…";
					await Task.Run(() => _overFrames.FindGate(_gameRoot, delegate(int done, int total)
					{
						if (!base.IsDisposed && base.IsHandleCreated)
						{
							BeginInvoke(delegate
							{
								_status.Text = $"首次定位超框表：{done:N0}/{total:N0} Bundle…";
							});
						}
					}));
					_status.Text = "正在写入单卡超框合成图…";
					await Task.Run(delegate
					{
						_engine.Replace(_art, output, Path.Combine(_gameRoot, "_MD卡图备份", _art.SourceKind));
					});
					try
					{
						await Task.Run(delegate
						{
							_overFrames.EnableOrUpdate(_gameRoot, _cardId, _cardId);
						});
					}
					catch (Exception ex)
					{
						throw new InvalidOperationException("超框合成图已经写入，但超框登记失败。请在主界面的“超框表”中为该卡重试启用。\n\n" + ex.Message, ex);
					}
					ImageRenderSpec renderSpec = _canvas.RenderSpec;
					OverFrameArtStore.SaveSettings(_gameRoot, _cardId, new OverFrameFrameSettings(choice.Key, choice.IsCustom, UserSelected: true, CurrentMode.ToString(), renderSpec.ImageScale, renderSpec.OffsetX, renderSpec.OffsetY, _canvas.BackgroundRenderSpec.ImageScale, _canvas.BackgroundRenderSpec.OffsetX, _canvas.BackgroundRenderSpec.OffsetY, _removeFoilInnerFrame.Checked));
					_art.Category = "超框卡图";
					AppliedFrameName = modeLabel + " · " + choice.DisplayName;
					base.DialogResult = DialogResult.OK;
					Close();
					return;
				}
				catch (Exception ex2)
				{
					MessageBox.Show(this, ex2.Message, "应用超框失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
					_status.Text = "应用失败：" + ex2.Message;
					return;
				}
				finally
				{
					base.UseWaitCursor = false;
				}
			}
		}
		MessageBox.Show(this, "当前超框预览尚未生成完成，请稍候再应用。", Text);
	}

	private static bool HasMeaningfulTransparency(byte[] png)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		for (int i = 0; i < image.Height; i++)
		{
			for (int j = 0; j < image.Width; j++)
			{
				if (image[j, i].A < 245)
				{
					return true;
				}
			}
		}
		return false;
	}
}
