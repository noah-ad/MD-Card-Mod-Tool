using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

/// <summary>
/// Unified over-frame editor used by both ordinary cards and cards that already
/// have an OF draft. The canvas is the source of truth: artwork is dragged and
/// scaled directly over the final background -> frame -> subject layer stack.
/// </summary>
public sealed class OverFrameFrameEditorForm : Form
{
	private enum FrameCompositionMode
	{
		AstellarTransparent,
		FloowanGradient,
		StandardComplete
	}

	private sealed record FrameModeChoice(FrameCompositionMode Mode, string Label)
	{
		public override string ToString() => Label;
	}

	private sealed class FrameChoice
	{
		public TexRef? Texture { get; init; }

		public string? FilePath { get; init; }

		public required string Key { get; init; }

		public required string BaseKey { get; init; }

		public required string DisplayName { get; init; }

		public bool IsCustom => FilePath != null;

		public override string ToString() => DisplayName;
	}

	private readonly string _gameRoot;

	private readonly TexRef _art;

	private readonly ushort _cardId;

	private readonly byte[]? _initialSource;

	private readonly byte[]? _initialBackground;

	private readonly bool _replaceStoredBackground;

	private readonly string? _initialFrameKey;

	private readonly ModEngine _engine = new();

	private readonly OverFrameService _overFrames = new();

	private readonly TexRef[] _availableFrames;

	private readonly ModernComboBox _mode = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly ModernComboBox _frames = new()
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Fill
	};

	private readonly CropCanvas _canvas;

	private readonly TrackBar _zoom = new()
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

	private readonly Label _zoomValue = new()
	{
		AutoSize = false,
		Width = 58,
		Height = 32,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Primary,
		Text = "100%"
	};

	private readonly Label _transformStatus = new()
	{
		AutoSize = false,
		Width = 210,
		Height = 32,
		TextAlign = ContentAlignment.MiddleLeft,
		ForeColor = UiTheme.Muted,
		AutoEllipsis = true
	};

	private readonly Label _status = new()
	{
		Dock = DockStyle.Bottom,
		Height = 46,
		Padding = new Padding(12, 7, 12, 7),
		ForeColor = Color.Gainsboro,
		AutoEllipsis = true
	};

	private readonly Label _layerStatus = new()
	{
		Dock = DockStyle.Fill,
		ForeColor = UiTheme.Gold,
		TextAlign = ContentAlignment.MiddleLeft,
		AutoEllipsis = true
	};

	private readonly Button _clearBackgroundButton;

	private readonly System.Windows.Forms.Timer _renderTimer = new()
	{
		Interval = 160
	};

	private readonly Dictionary<string, byte[]> _frameCache = new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, AstellarOverFrameTemplate> _overFrameTemplateCache = new(StringComparer.OrdinalIgnoreCase);

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

	private bool _syncingZoom;

	private bool _suppressCanvasChanges;

	private bool _restoredTransform;

	private bool _sourceHasTransparency;

	private string? _customFramePath;

	private OverFrameFrameSettings _savedSettings = new();

	public string AppliedFrameName { get; private set; } = "";

	public OverFrameFrameEditorForm(string gameRoot, TexRef art, IEnumerable<TexRef> frames,
		byte[]? initialArt = null, string? initialFrameKey = null,
		byte[]? initialBackground = null, bool replaceStoredBackground = false)
	{
		UiTheme.ApplyDarkTitleBar(this);
		if (!ushort.TryParse(art.CardKey, out _cardId))
		{
			throw new ArgumentException("所选资源没有有效卡号。", nameof(art));
		}

		_gameRoot = gameRoot;
		_art = art;
		_availableFrames = frames.Where(frame => frame.Width == FrameComposer.Width
			&& frame.Height == FrameComposer.Height).ToArray();
		_initialSource = initialArt?.ToArray();
		_initialFrameKey = initialFrameKey;
		_initialBackground = initialBackground?.ToArray();
		_replaceStoredBackground = replaceStoredBackground;

		Text = $"制作超框 · {_cardId}";
		StartPosition = FormStartPosition.CenterParent;
		Size = new Size(1120, 900);
		MinimumSize = new Size(860, 700);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		AutoScaleMode = AutoScaleMode.Dpi;
		KeyPreview = true;

		UiTheme.StyleComboBox(_mode);
		UiTheme.StyleComboBox(_frames);
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.AstellarTransparent, "透明卡框"));
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.FloowanGradient, "炫酷卡框"));
		_mode.Items.Add(new FrameModeChoice(FrameCompositionMode.StandardComplete, "普通卡框"));
		_mode.SelectedItem = _mode.Items.Cast<object>().OfType<FrameModeChoice>()
			.First(choice => choice.Mode == ModeForFrameKey(_initialFrameKey,
				FrameCompositionMode.AstellarTransparent));
		RefreshFrameChoices(_initialFrameKey);

		using Bitmap placeholder = new(2, 2);
		_canvas = new CropCanvas(new Bitmap(placeholder), FrameComposer.Width, FrameComposer.Height,
			fullCardOverlay: false, overFrameEditing: true)
		{
			Dock = DockStyle.Fill,
			TabStop = true,
			AccessibleName = "超框卡图直接编辑画布"
		};

		Button exportFrame = Button("导出卡框 PNG", async delegate { await ExportFrameAsync(); });
		Button importFrame = Button("导入自定义卡框", async delegate { await ImportFrameAsync(); });
		Button changeArt = UiTheme.Button("更换卡图", async delegate { await ChangeArtAsync(); }, ButtonTone.Primary);
		Button addBackground = UiTheme.Button("添加叠底背景", async delegate { await AddBackgroundAsync(); }, ButtonTone.Gold);
		_clearBackgroundButton = Button("清除背景", async delegate { await ClearBackgroundAsync(); });
		Button exportPreview = Button("导出合成预览", delegate { ExportPreview(); });
		Button reset = Button("铺满插图区", delegate { _canvas.ResetView(); });
		Button whole = Button("显示整张图", delegate { _canvas.ShowWholeImage(); });
		Button apply = Button("应用到游戏", async delegate { await ApplyAsync(); }, accent: true);

		TableLayoutPanel top = BuildToolbar(apply, changeArt, addBackground, importFrame,
			exportFrame, exportPreview, reset, whole);
		Controls.Add(_canvas);
		Controls.Add(_status);
		Controls.Add(top);

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
			if (!_loading) await RenderAsync();
		};
		_mode.SelectedIndexChanged += async delegate
		{
			if (_loading) return;
			string? baseKey = (_frames.SelectedItem as FrameChoice)?.BaseKey;
			RefreshFrameChoices(baseKey);
			await RenderAsync();
		};
		Shown += async delegate { await LoadAsync(); };
		FormClosing += delegate
		{
			_renderTimer.Stop();
			_generation++;
			SaveLatestDraftOnClose();
		};
		FormClosed += delegate { _renderTimer.Stop(); };

		UpdateLayerStatus();
		UpdateTransformStatus();
	}

	private TableLayoutPanel BuildToolbar(Button apply, Button changeArt, Button addBackground,
		Button importFrame, Button exportFrame, Button exportPreview, Button reset, Button whole)
	{
		TableLayoutPanel top = new()
		{
			Dock = DockStyle.Top,
			Height = 268,
			Padding = new Padding(14, 10, 14, 8),
			ColumnCount = 3,
			RowCount = 7,
			BackColor = UiTheme.SurfaceAlt
		};
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

		top.Controls.Add(ToolbarLabel("卡框分类", UiTheme.Gold), 0, 0);
		top.Controls.Add(_mode, 1, 0);
		top.SetColumnSpan(_mode, 2);
		top.Controls.Add(ToolbarLabel("卡框", Color.FromArgb(160, 195, 255)), 0, 1);
		top.Controls.Add(_frames, 1, 1);
		top.Controls.Add(apply, 2, 1);

		FlowLayoutPanel layerActions = ActionRow();
		layerActions.Controls.Add(changeArt);
		layerActions.Controls.Add(addBackground);
		layerActions.Controls.Add(_clearBackgroundButton);
		top.Controls.Add(layerActions, 0, 2);
		top.SetColumnSpan(layerActions, 3);

		FlowLayoutPanel fileActions = ActionRow();
		fileActions.Controls.Add(importFrame);
		fileActions.Controls.Add(exportFrame);
		fileActions.Controls.Add(exportPreview);
		top.Controls.Add(fileActions, 0, 3);
		top.SetColumnSpan(fileActions, 3);

		FlowLayoutPanel canvasActions = ActionRow();
		canvasActions.Controls.Add(ToolbarLabel("卡图缩放", UiTheme.Text));
		canvasActions.Controls.Add(_zoom);
		canvasActions.Controls.Add(_zoomValue);
		canvasActions.Controls.Add(reset);
		canvasActions.Controls.Add(whole);
		canvasActions.Controls.Add(_transformStatus);
		top.Controls.Add(canvasActions, 0, 4);
		top.SetColumnSpan(canvasActions, 3);

		top.Controls.Add(_layerStatus, 0, 5);
		top.SetColumnSpan(_layerStatus, 3);
		Label help = new()
		{
			Text = "直接在下方卡面拖动主体；滚轮或滑杆缩放，方向键微调，Shift + 方向键快速移动，双击恢复铺满。真正超框的图层顺序为：叠底背景 → 卡框 → 透明主体。",
			Dock = DockStyle.Fill,
			ForeColor = UiTheme.Muted,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		top.Controls.Add(help, 0, 6);
		top.SetColumnSpan(help, 3);
		return top;
	}

	private static Label ToolbarLabel(string text, Color color)
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
			Padding = new Padding(0, 2, 0, 0)
		};
	}

	private static Button Button(string text, EventHandler click, bool accent = false)
	{
		return UiTheme.Button(text, click, accent ? ButtonTone.Primary : ButtonTone.Neutral);
	}

	private FrameCompositionMode CurrentMode =>
		(_mode.SelectedItem as FrameModeChoice)?.Mode ?? FrameCompositionMode.AstellarTransparent;

	private static FrameCompositionMode ModeForFrameKey(string? frameKey, FrameCompositionMode fallback)
	{
		if (frameKey?.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase) == true)
		{
			return FrameCompositionMode.AstellarTransparent;
		}
		if (frameKey?.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase) == true)
		{
			return FrameCompositionMode.FloowanGradient;
		}
		if (frameKey?.Equals("__custom__", StringComparison.OrdinalIgnoreCase) == true)
		{
			return FrameCompositionMode.StandardComplete;
		}
		// A bare card_frameXX key is a card-type recommendation, not a request for
		// the ordinary/flat composition mode. New cards therefore keep the caller's
		// transparent-overframe fallback while still selecting the correct base frame.
		return fallback;
	}

	private void SelectMode(FrameCompositionMode mode)
	{
		_mode.SelectedItem = _mode.Items.Cast<object>().OfType<FrameModeChoice>()
			.First(choice => choice.Mode == mode);
	}

	private void RefreshFrameChoices(string? wantedKey)
	{
		bool previousLoading = _loading;
		_loading = true;
		try
		{
			_frames.BeginUpdate();
			_frames.Items.Clear();
			IEnumerable<TexRef> source = CurrentMode switch
			{
				FrameCompositionMode.AstellarTransparent => _availableFrames.Where(BuiltInCardFrameCatalog.IsTransparentFrame),
				FrameCompositionMode.FloowanGradient => _availableFrames.Where(BuiltInCardFrameCatalog.IsGradientFrame),
				_ => _availableFrames.Where(BuiltInCardFrameCatalog.IsNormalFrame)
			};
			foreach (TexRef texture in source.OrderBy(frame => CardFrameCatalog.BaseKey(frame.Name),
				StringComparer.OrdinalIgnoreCase))
			{
				string baseKey = CardFrameCatalog.BaseKey(texture.Name);
				_frames.Items.Add(new FrameChoice
				{
					Texture = texture,
					Key = texture.Name,
					BaseKey = baseKey,
					DisplayName = $"{baseKey} · {CardFrameCatalog.FriendlyName(baseKey)}"
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
			string wantedBase = CardFrameCatalog.BaseKey(string.IsNullOrWhiteSpace(wantedKey)
				? "card_frame01"
				: wantedKey);
			FrameChoice? selected = _frames.Items.Cast<object>().OfType<FrameChoice>()
				.FirstOrDefault(choice => choice.Key.Equals(wantedKey, StringComparison.OrdinalIgnoreCase))
				?? _frames.Items.Cast<object>().OfType<FrameChoice>()
					.FirstOrDefault(choice => choice.BaseKey.Equals(wantedBase, StringComparison.OrdinalIgnoreCase))
				?? _frames.Items.Cast<object>().OfType<FrameChoice>().FirstOrDefault();
			_frames.SelectedItem = selected;
		}
		finally
		{
			_frames.EndUpdate();
			_loading = previousLoading;
		}
	}

	private async Task LoadAsync()
	{
		UseWaitCursor = true;
		_loading = true;
		_status.Text = "正在读取当前卡图、构图与卡框…";
		try
		{
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
			if (_initialSource != null)
			{
				await Task.Run(() => OverFrameArtStore.SaveSource(_gameRoot, _cardId, _initialSource));
			}
			else if (!File.Exists(sourcePath))
			{
				string legacyArt = OverFrameArtStore.ArtPath(_gameRoot, _cardId);
				if (File.Exists(legacyArt))
				{
					await Task.Run(() => OverFrameArtStore.SaveSource(_gameRoot, _cardId, legacyArt));
				}
				else
				{
					byte[] current = await Task.Run(() => _engine.DecodePng(_art));
					await Task.Run(() => OverFrameArtStore.SaveSource(_gameRoot, _cardId, current));
				}
			}

			_sourceBytes = await File.ReadAllBytesAsync(sourcePath);
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

			string backgroundPath = OverFrameArtStore.BackgroundPath(_gameRoot, _cardId);
			_backgroundBytes = File.Exists(backgroundPath)
				? await File.ReadAllBytesAsync(backgroundPath)
				: null;
			SetCanvasBackground();

			_savedSettings = OverFrameArtStore.ReadSettings(_gameRoot, _cardId);
			FrameCompositionMode savedMode = _savedSettings.CompositionMode.Equals("StandardComplete",
				StringComparison.OrdinalIgnoreCase)
				? FrameCompositionMode.StandardComplete
				: _savedSettings.CompositionMode.Equals("FloowanGradient", StringComparison.OrdinalIgnoreCase)
					? FrameCompositionMode.FloowanGradient
					: FrameCompositionMode.AstellarTransparent;
			if (!string.IsNullOrWhiteSpace(_initialFrameKey))
			{
				savedMode = ModeForFrameKey(_initialFrameKey, savedMode);
			}
			SelectMode(savedMode);

			string custom = OverFrameArtStore.CustomFramePath(_gameRoot, _cardId);
			if (File.Exists(custom)) _customFramePath = custom;
			string wanted = !string.IsNullOrWhiteSpace(_initialFrameKey)
				? _initialFrameKey
				: (_savedSettings.UsesCustomFrame ? "__custom__" : _savedSettings.FrameKey);
			if (_savedSettings.UsesCustomFrame && string.IsNullOrWhiteSpace(_initialFrameKey))
			{
				SelectMode(FrameCompositionMode.StandardComplete);
			}
			RefreshFrameChoices(wanted);
			if (_frames.Items.Count == 0)
			{
				throw new InvalidOperationException("没有可用的 704×1024 卡框，请回主界面重建索引。\n自定义卡框也可通过“导入自定义卡框”加入。");
			}
			UpdateLayerStatus();
			_status.Text = "卡图已载入。可直接在卡面拖动，使用滚轮或滑杆缩放。";
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "超框编辑器载入失败", MessageBoxButtons.OK,
				MessageBoxIcon.Hand);
			_status.Text = "载入失败：" + ex.Message;
		}
		finally
		{
			_loading = false;
			UseWaitCursor = false;
		}

		if (_sourceBytes != null && _frames.SelectedItem is FrameChoice)
		{
			await RenderAsync();
		}
	}

	private async Task RenderAsync()
	{
		if (IsDisposed || _sourceBytes == null || _frames.SelectedItem is not FrameChoice choice)
		{
			return;
		}
		int generation = ++_generation;
		_renderTimer.Stop();
		_rendering = true;
		_outputBytes = null;
		_previewBytes = null;
		_previewFrameKey = null;
		_transparentEdgePixels = 0;
		UseWaitCursor = true;
		_status.Text = "正在更新超框合成…";
		try
		{
			FrameCompositionMode mode = CurrentMode;
			byte[] frameBytes = await GetFrameBytesAsync(choice);
			string canvasFrameKey = mode + ":" + choice.Key;
			if (!canvasFrameKey.Equals(_canvasFrameKey, StringComparison.OrdinalIgnoreCase))
			{
				// Transparent frames deliberately carry RGB below zero alpha for the game
				// shader. Keep those pixels transparent on the interactive canvas; forcing
				// them opaque hides the optional background and makes the editor disagree
				// with its documented layer stack. Compose() still preserves this RGB in
				// the PNG written to the game.
				byte[] visibleFrame = frameBytes;
				_suppressCanvasChanges = true;
				try
				{
					bool preserve = _canvas.HasFrame;
					_canvas.SetFrame(FrameComposer.PreviewBitmap(visibleFrame), preserve,
						ResolveArtWindow(choice.BaseKey));
					_canvasFrameKey = canvasFrameKey;
					if (!_restoredTransform)
					{
						if (_savedSettings.ArtImageScale > 0f)
						{
							_canvas.SetRenderSpec(new ImageRenderSpec(FrameComposer.Width, FrameComposer.Height,
								_savedSettings.ArtImageScale, _savedSettings.ArtOffsetX,
								_savedSettings.ArtOffsetY));
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

			byte[] artBytes = _canvas.RenderSourceToTarget();
			byte[]? backgroundBytes = _backgroundBytes?.ToArray();
			byte[] output;
			byte[] preview;
			int transparentPixels = 0;
			if (mode == FrameCompositionMode.AstellarTransparent)
			{
				if (choice.IsCustom)
				{
					throw new InvalidOperationException("自定义扁平 PNG 没有 Astellar 六层数据，只能用于“普通卡框”模式。");
				}
				AstellarOverFrameTemplate template = await GetOverFrameTemplateAsync(choice.BaseKey);
				AstellarOverFrameComposition composed = await Task.Run(() =>
					AstellarOverFrameComposer.Compose(artBytes, template, backgroundBytes));
				output = composed.GamePng;
				preview = composed.PreviewPng;
				transparentPixels = composed.TransparentEdgePixels;
			}
			else
			{
				output = await Task.Run(() =>
					AstellarOverFrameComposer.ComposeFlatFrame(artBytes, frameBytes, backgroundBytes));
				preview = output;
			}

			if (generation != _generation || IsDisposed) return;
			_currentFrameBytes = frameBytes;
			_currentFrameKey = choice.Key;
			_artBytes = artBytes;
			_previewBytes = preview;
			_outputBytes = output;
			_previewFrameKey = choice.Key;
			_previewMode = mode;
			_transparentEdgePixels = transparentPixels;
			await PersistDraftAsync(choice, artBytes);

			string background = backgroundBytes == null ? "无叠底背景" : "含叠底背景";
			string transparency = _sourceHasTransparency ? "透明主体置于卡框上层" : "源图无透明区域";
			_status.Text = mode switch
			{
				FrameCompositionMode.AstellarTransparent =>
					$"透明卡框 · {choice.DisplayName} · {background} · {transparency} · 保留 {transparentPixels:N0} 个透明 RGB 像素",
				FrameCompositionMode.FloowanGradient =>
					$"炫酷卡框 · {choice.DisplayName} · {background} · {transparency} · 704×1024",
				_ => $"普通卡框 · {choice.DisplayName} · {background} · {transparency} · 704×1024"
			};
		}
		catch (Exception ex)
		{
			if (generation == _generation) _status.Text = "预览失败：" + ex.Message;
		}
		finally
		{
			_rendering = false;
			UseWaitCursor = false;
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
		if (_loading || _suppressCanvasChanges) return;
		_generation++;
		_outputBytes = null;
		_status.Text = "构图已调整；正在生成最终超框预览…";
		_renderTimer.Stop();
		_renderTimer.Start();
	}

	private void ZoomValueChanged(object? sender, EventArgs e)
	{
		if (_syncingZoom || _loading) return;
		_canvas.SetZoom(_zoom.Value / 100f, null);
	}

	private void UpdateTransformStatus(ImageRenderSpec? provided = null)
	{
		if (_canvas.IsDisposed) return;
		ImageRenderSpec spec = provided ?? _canvas.RenderSpec;
		_transformStatus.Text = $"位置 X {spec.OffsetX:0}  Y {spec.OffsetY:0}";
	}

	private static RectangleF ResolveArtWindow(string baseKey)
	{
		string normalized = CardFrameCatalog.BaseKey(baseKey);
		bool pendulum = normalized is "card_frame13" or "card_frame14" or "card_frame15"
			or "card_frame16" or "card_frame17" or "card_frame19";
		return pendulum
			? new RectangleF(50, 186, 604, 451)
			: new RectangleF(89, 191, 527, 528);
	}

	private Task<AstellarOverFrameTemplate> GetOverFrameTemplateAsync(string key)
	{
		if (_overFrameTemplateCache.TryGetValue(key, out AstellarOverFrameTemplate? cached))
		{
			return Task.FromResult(cached);
		}
		return Task.Run(() =>
		{
			AstellarOverFrameTemplate loaded = AstellarOverFrameTemplateCatalog.Load(key);
			_overFrameTemplateCache[key] = loaded;
			return loaded;
		});
	}

	private async Task<byte[]> GetFrameBytesAsync(FrameChoice choice)
	{
		if (_frameCache.TryGetValue(choice.Key, out byte[]? cached)) return cached;
		byte[] bytes = choice.IsCustom
			? await File.ReadAllBytesAsync(choice.FilePath!)
			: await Task.Run(() => _engine.DecodePng(choice.Texture));
		_frameCache[choice.Key] = bytes;
		return bytes;
	}

	private async Task PersistDraftAsync(FrameChoice choice, byte[] artBytes)
	{
		ImageRenderSpec spec = _canvas.RenderSpec;
		OverFrameFrameSettings settings = new(choice.Key, choice.IsCustom, UserSelected: true,
			CompositionMode: CurrentMode.ToString(), ArtImageScale: spec.ImageScale,
			ArtOffsetX: spec.OffsetX, ArtOffsetY: spec.OffsetY);
		await Task.Run(delegate
		{
			OverFrameArtStore.SaveArt(_gameRoot, _cardId, artBytes);
			OverFrameArtStore.SaveSettings(_gameRoot, _cardId, settings);
		});
		_savedSettings = settings;
	}

	private void SaveLatestDraftOnClose()
	{
		if (_sourceBytes == null || _frames.SelectedItem is not FrameChoice choice) return;
		try
		{
			byte[] artBytes = _canvas.RenderSourceToTarget();
			ImageRenderSpec spec = _canvas.RenderSpec;
			OverFrameArtStore.SaveArt(_gameRoot, _cardId, artBytes);
			OverFrameArtStore.SaveSettings(_gameRoot, _cardId,
				new OverFrameFrameSettings(choice.Key, choice.IsCustom, UserSelected: true,
					CompositionMode: CurrentMode.ToString(), ArtImageScale: spec.ImageScale,
					ArtOffsetX: spec.OffsetX, ArtOffsetY: spec.OffsetY));
		}
		catch
		{
			// Closing the editor must not be blocked by a draft-only persistence failure.
		}
	}

	private async Task ImportFrameAsync()
	{
		using OpenFileDialog dialog = new()
		{
			Filter = "PNG 图片|*.png|图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "导入 704×1024 自定义卡框"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		try
		{
			string path = await Task.Run(() =>
				OverFrameArtStore.SaveCustomFrame(_gameRoot, _cardId, dialog.FileName));
			_frameCache.Remove("__custom__");
			_customFramePath = path;
			SelectMode(FrameCompositionMode.StandardComplete);
			RefreshFrameChoices("__custom__");
			await RenderAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "自定义卡框无效", MessageBoxButtons.OK,
				MessageBoxIcon.Exclamation);
		}
	}

	private async Task ChangeArtAsync()
	{
		using OpenFileDialog dialog = new()
		{
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "更换卡图（带透明背景的 PNG 才能形成主体越框效果）"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		try
		{
			UseWaitCursor = true;
			_status.Text = "正在载入新卡图…";
			await Task.Run(() => OverFrameArtStore.SaveSource(_gameRoot, _cardId, dialog.FileName));
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
			MessageBox.Show(this, ex.Message, "卡图无效", MessageBoxButtons.OK,
				MessageBoxIcon.Exclamation);
			_status.Text = "更换卡图失败：" + ex.Message;
		}
		finally
		{
			UseWaitCursor = false;
		}
	}

	private async Task AddBackgroundAsync()
	{
		using OpenFileDialog dialog = new()
		{
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
			Title = "添加叠底背景图（自动居中铺满 704×1024）"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
		try
		{
			UseWaitCursor = true;
			_status.Text = "正在处理叠底背景…";
			_backgroundBytes = await Task.Run(() =>
			{
				byte[] rendered = ImageCropService.RenderCoverToTarget(dialog.FileName,
					FrameComposer.Width, FrameComposer.Height);
				OverFrameArtStore.SaveBackground(_gameRoot, _cardId, rendered);
				return rendered;
			});
			SetCanvasBackground();
			UpdateLayerStatus();
			await RenderAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "叠底背景无效", MessageBoxButtons.OK,
				MessageBoxIcon.Exclamation);
			_status.Text = "叠底背景添加失败：" + ex.Message;
		}
		finally
		{
			UseWaitCursor = false;
		}
	}

	private async Task ClearBackgroundAsync()
	{
		if (_backgroundBytes == null) return;
		try
		{
			await Task.Run(() => OverFrameArtStore.DeleteBackground(_gameRoot, _cardId));
			_backgroundBytes = null;
			SetCanvasBackground();
			UpdateLayerStatus();
			await RenderAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "清除叠底背景失败", MessageBoxButtons.OK,
				MessageBoxIcon.Hand);
		}
	}

	private void SetCanvasBackground()
	{
		_canvas.SetBackground(_backgroundBytes == null
			? null
			: FrameComposer.PreviewBitmap(_backgroundBytes));
	}

	private void UpdateLayerStatus()
	{
		bool hasBackground = _backgroundBytes != null;
		string layers = hasBackground
			? "图层：叠底背景 → 卡框 → 透明主体 · 已添加背景"
			: "图层：卡框 → 透明主体 · 叠底背景：未添加";
		_layerStatus.Text = _sourceHasTransparency
			? layers + " · 主体可越过卡框"
			: layers + " · 当前卡图没有透明区域；越框会呈矩形，请更换透明 PNG";
		_clearBackgroundButton.Enabled = hasBackground;
	}

	private async Task ExportFrameAsync()
	{
		if (_frames.SelectedItem is not FrameChoice choice) return;
		try
		{
			byte[] bytes = _currentFrameKey == choice.Key && _currentFrameBytes != null
				? _currentFrameBytes
				: await GetFrameBytesAsync(choice);
			using SaveFileDialog dialog = new()
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
			MessageBox.Show(this, ex.Message, "导出卡框失败", MessageBoxButtons.OK,
				MessageBoxIcon.Hand);
		}
	}

	private void ExportPreview()
	{
		if (_previewBytes == null)
		{
			MessageBox.Show(this, "最终预览仍在生成，请稍候。", Text);
			return;
		}
		using SaveFileDialog dialog = new()
		{
			Filter = "PNG 图片|*.png",
			FileName = $"{_cardId}_超框合成预览.png"
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			File.WriteAllBytes(dialog.FileName, _previewBytes);
		}
	}

	private async Task ApplyAsync()
	{
		_renderTimer.Stop();
		if (_rendering || _outputBytes == null)
		{
			await RenderAsync();
		}
		if (_outputBytes == null || _frames.SelectedItem is not FrameChoice choice
			|| _previewFrameKey != choice.Key || _previewMode != CurrentMode)
		{
			MessageBox.Show(this, "当前超框预览尚未生成完成，请稍候再应用。", Text);
			return;
		}

		string modeLabel = CurrentMode switch
		{
			FrameCompositionMode.AstellarTransparent =>
				$"透明卡框（保留 {_transparentEdgePixels:N0} 个透明 RGB 像素）",
			FrameCompositionMode.FloowanGradient => "炫酷卡框",
			_ => "普通卡框"
		};
		string background = _backgroundBytes == null ? "未使用叠底背景" : "已包含叠底背景";
		string warning = _sourceHasTransparency
			? "主体位于卡框上方，可形成真正的越框效果。"
			: "当前源图没有透明区域，越框部分会保持矩形边缘。";
		if (MessageBox.Show(this,
			$"把“{choice.DisplayName}”以“{modeLabel}”模式写入卡号 {_cardId}？\n\n{background}。{warning}\n只修改这张卡的 Bundle，不会改全局 card_frame；编辑源图、背景与构图参数都会保留。",
			"确认应用超框", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}

		try
		{
			UseWaitCursor = true;
			_status.Text = "正在定位 LocalData 超框表…";
			await Task.Run(() => _overFrames.FindGate(_gameRoot, delegate(int done, int total)
			{
				if (!IsDisposed && IsHandleCreated)
				{
					BeginInvoke(delegate { _status.Text = $"首次定位超框表：{done:N0}/{total:N0} Bundle…"; });
				}
			}));
			_status.Text = "正在写入单卡超框合成图…";
			byte[] output = _outputBytes.ToArray();
			await Task.Run(() => _engine.Replace(_art, output,
				Path.Combine(_gameRoot, "_MD卡图备份", _art.SourceKind)));
			try
			{
				await Task.Run(() => _overFrames.EnableOrUpdate(_gameRoot, _cardId, _cardId));
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException(
					"超框合成图已经写入，但超框登记失败。请在主界面的“超框表”中为该卡重试启用。\n\n" + ex.Message,
					ex);
			}

			ImageRenderSpec spec = _canvas.RenderSpec;
			OverFrameArtStore.SaveSettings(_gameRoot, _cardId,
				new OverFrameFrameSettings(choice.Key, choice.IsCustom, UserSelected: true,
					CompositionMode: CurrentMode.ToString(), ArtImageScale: spec.ImageScale,
					ArtOffsetX: spec.OffsetX, ArtOffsetY: spec.OffsetY));
			_art.Category = "超框卡图";
			AppliedFrameName = modeLabel + " · " + choice.DisplayName;
			DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "应用超框失败", MessageBoxButtons.OK,
				MessageBoxIcon.Hand);
			_status.Text = "应用失败：" + ex.Message;
		}
		finally
		{
			UseWaitCursor = false;
		}
	}

	private static bool HasMeaningfulTransparency(byte[] png)
	{
		using SixLabors.ImageSharp.Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		for (int y = 0; y < image.Height; y++)
		for (int x = 0; x < image.Width; x++)
		{
			if (image[x, y].A < 245) return true;
		}
		return false;
	}
}
