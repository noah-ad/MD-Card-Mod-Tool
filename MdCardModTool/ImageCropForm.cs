using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class ImageCropForm : Form
{
	private enum FrameCategory
	{
		Transparent,
		TransparentGradient,
		Gradient,
		Normal
	}

	private sealed record FrameCategoryChoice(FrameCategory Category, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private sealed class FrameChoice
	{
		public TexRef Texture { get; }

		public FrameChoice(TexRef texture)
		{
			Texture = texture;
		}

		public override string ToString()
		{
			string text = CardFrameCatalog.BaseKey(Texture.Name);
			return text + " · " + CardFrameCatalog.FriendlyName(text);
		}
	}

	private string _sourcePath;

	private readonly int _targetWidth;

	private readonly int _targetHeight;

	private readonly GameTextureDisplayMapping _displayMapping;

	private readonly CropCanvas _canvas;

	private readonly ModEngine _engine = new ModEngine();

	private readonly TexRef[] _availableFrames;

	private readonly ModernComboBox _frameCategory = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Top,
		Height = 34
	};

	private readonly ModernComboBox _frames = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Top,
		Height = 34
	};

	private readonly Label _mapping = new Label
	{
		Dock = DockStyle.Top,
		Height = 52,
		ForeColor = UiTheme.Gold,
		Padding = new Padding(0, 5, 0, 5)
	};

	private readonly TrackBar _zoom = new TrackBar
	{
		Minimum = 1,
		Maximum = 2000,
		Value = 100,
		TickFrequency = 100,
		Dock = DockStyle.Top,
		Height = 48
	};

	private readonly Label _zoomValue = new Label
	{
		Dock = DockStyle.Top,
		Height = 28,
		ForeColor = UiTheme.Primary,
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly bool _fullCardOverlay;

	private readonly Label _layerStatus = new Label
	{
		Dock = DockStyle.Top,
		Height = 42,
		ForeColor = UiTheme.Muted,
		TextAlign = ContentAlignment.MiddleLeft,
		AutoEllipsis = true
	};

	private Button? _clearBackgroundButton;

	private string? _backgroundPath;

	private byte[]? _initialBackgroundPng;

	private bool _refreshingFrameChoices;

	private int _frameGeneration;

	public byte[]? OutputPng { get; private set; }

	public byte[]? BackgroundPng { get; private set; }

	public string SelectedFrameKey { get; private set; } = "";

	private FrameCategory CurrentFrameCategory => (_frameCategory.SelectedItem as FrameCategoryChoice)?.Category ?? FrameCategory.Normal;

	public ImageCropForm(string sourcePath, int targetWidth, int targetHeight, string purpose = "替换图片", IEnumerable<TexRef>? cardFrames = null, string? preferredFrameKey = null, bool fullCardOverlay = false, byte[]? initialBackgroundPng = null, GameTextureDisplayMapping? displayMapping = null)
	{
		if (targetWidth <= 0 || targetHeight <= 0)
		{
			throw new ArgumentOutOfRangeException("targetWidth", "目标尺寸必须大于 0。");
		}
		_sourcePath = sourcePath;
		_targetWidth = targetWidth;
		_targetHeight = targetHeight;
		_displayMapping = displayMapping ?? new GameTextureDisplayMapping(TextureDisplayMappingKind.Native, targetWidth, targetHeight, targetWidth, targetHeight);
		if (_displayMapping.DisplayWidth != targetWidth || _displayMapping.DisplayHeight != targetHeight)
		{
			throw new ArgumentException("裁剪器目标尺寸必须与正常预览映射尺寸一致。", "displayMapping");
		}
		_fullCardOverlay = fullCardOverlay;
		_initialBackgroundPng = initialBackgroundPng?.ToArray();
		Bitmap source = ImageCropService.LoadPreview(sourcePath);
		_canvas = new CropCanvas(source, targetWidth, targetHeight, fullCardOverlay)
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TabStop = true
		};
		TexRef[] availableFrames;
		if (cardFrames != null)
		{
			IEnumerable<TexRef> source2;
			if (!fullCardOverlay)
			{
				IEnumerable<TexRef> enumerable = CardFrameCatalog.CompatibleFrames(cardFrames, targetWidth, targetHeight);
				source2 = enumerable;
			}
			else
			{
				source2 = cardFrames.Where((TexRef frame) => BuiltInCardFrameCatalog.IsPackagedFrame(frame) && frame.Width == 704 && frame.Height == 1024);
			}
			availableFrames = source2.ToArray();
		}
		else
		{
			availableFrames = Array.Empty<TexRef>();
		}
		_availableFrames = availableFrames;
		bool flag = _availableFrames.Length != 0;
		if (fullCardOverlay && flag)
		{
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.Transparent, "透明卡框"));
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.TransparentGradient, "透明炫彩卡框"));
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.Gradient, "炫彩卡框"));
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.Normal, "普通卡框"));
			_frameCategory.SelectedIndex = CategoryIndex(preferredFrameKey);
		}
		RefreshFrameChoices(preferredFrameKey);
		if (_initialBackgroundPng != null)
		{
			_canvas.SetBackground(RgbaBitmap.FromPng(_initialBackgroundPng));
		}
		UiTheme.ApplyDarkTitleBar(this);
		UiTheme.StyleComboBox(_frames);
		UiTheme.StyleComboBox(_frameCategory);
		Text = "卡片实装裁剪 · " + purpose;
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new Size(1180, 850);
		MinimumSize = new Size(900, 660);
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
		Label value = new Label
		{
			Text = (fullCardOverlay ? "超框实装构图" : (flag ? "卡片实装构图" : "固定比例裁剪")),
			Dock = DockStyle.Top,
			Height = 30,
			ForeColor = UiTheme.Text,
			Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold)
		};
		string editorSummary = _displayMapping.EditorSummary;
		Label value2 = new Label
		{
			Text = (flag ? (purpose + " · 按卡框实际效果构图 · " + editorSummary) : (purpose + " · " + editorSummary)),
			Dock = DockStyle.Fill,
			ForeColor = UiTheme.Gold,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		GradientBanner gradientBanner = new GradientBanner
		{
			Dock = DockStyle.Top,
			Height = 72,
			Padding = new Padding(20, 10, 20, 8)
		};
		gradientBanner.Controls.Add(value2);
		gradientBanner.Controls.Add(value);
		Label value3 = new Label
		{
			Text = (fullCardOverlay ? "图层顺序：叠底背景 → 卡框 → 卡图主体。\n\n• 四类卡框可独立切换\n• 更换卡图不会移除叠底背景\n• 左键拖动卡图，滚轮／滑杆缩放\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n确认后进入超框编辑器完成应用。" : (flag ? "操作\n\n• 卡框下拉只控制实装预览与构图比例\n• 左键拖动图片，滚轮／滑杆缩放\n• 可缩到画框以内，透明处按游戏白色底板显示\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n灵摆卡使用正常比例构图；确认后才自动压缩到游戏的高画布。" : (_displayMapping.RequiresMapping ? "操作\n\n• 当前画布使用游戏中的正常显示比例\n• 左键拖动图片，滚轮／滑杆缩放\n• 方向键微调位置\n• 双击恢复铺满\n\n确认后自动转换为 Texture2D 存储比例；游戏映射后会恢复为当前预览效果。" : "操作\n\n• 左键拖动图片\n• 滚轮／滑杆可自由放大和缩小\n• 方向键微调位置\n• 双击恢复铺满\n\n确认后自动转换为目标尺寸，透明 PNG 的 Alpha 会保留。"))),
			Dock = DockStyle.Top,
			Height = (fullCardOverlay ? 190 : (flag ? 260 : 210)),
			ForeColor = UiTheme.Text,
			Padding = new Padding(2, 8, 2, 8),
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		Label value4 = new Label
		{
			Text = "卡框分类",
			Dock = DockStyle.Top,
			Height = 28,
			ForeColor = UiTheme.Gold,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			Visible = (fullCardOverlay && flag)
		};
		_frameCategory.Visible = fullCardOverlay && flag;
		Label value5 = new Label
		{
			Text = (fullCardOverlay ? "卡框样式" : "预览卡框"),
			Dock = DockStyle.Top,
			Height = 28,
			ForeColor = UiTheme.Muted,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			Visible = flag
		};
		_frames.Visible = flag;
		_mapping.Visible = flag || _displayMapping.RequiresMapping;
		_mapping.Text = (_displayMapping.RequiresMapping ? (_displayMapping.KindLabel + "\n" + _displayMapping.EditorSummary) : _displayMapping.EditorSummary);
		Label value6 = new Label
		{
			Text = "缩放（1%–2000%）",
			Dock = DockStyle.Top,
			Height = 30,
			ForeColor = UiTheme.Muted,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
		};
		Button value7 = UiTheme.Button("铺满画框", delegate
		{
			_canvas.ResetView();
		});
		Button value8 = UiTheme.Button("显示整图", delegate
		{
			_canvas.ShowWholeImage();
		});
		Button button = UiTheme.Button("更换卡图", async delegate
		{
			await ChangeSourceAsync();
		}, ButtonTone.Primary);
		Button button2 = UiTheme.Button("添加叠底背景", async delegate
		{
			await ChooseBackgroundAsync();
		}, ButtonTone.Gold);
		_clearBackgroundButton = UiTheme.Button("清除背景", delegate
		{
			ClearBackground();
		});
		button.Visible = fullCardOverlay;
		button2.Visible = fullCardOverlay;
		_clearBackgroundButton.Visible = fullCardOverlay;
		Button button3 = UiTheme.Button("取消", delegate
		{
			DialogResult = DialogResult.Cancel;
			Close();
		});
		Button button4 = UiTheme.Button(fullCardOverlay ? "保存构图并继续" : "按预览效果替换", delegate
		{
			ConfirmCrop();
		}, ButtonTone.Primary);
		button3.DialogResult = DialogResult.Cancel;
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 54,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			Padding = new Padding(0, 7, 0, 5),
			BackColor = UiTheme.Surface
		};
		flowLayoutPanel.Controls.Add(button4);
		flowLayoutPanel.Controls.Add(button3);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 48,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(0, 4, 0, 4),
			BackColor = UiTheme.Surface
		};
		flowLayoutPanel2.Controls.Add(value7);
		flowLayoutPanel2.Controls.Add(value8);
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 82,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			Padding = new Padding(0, 3, 0, 3),
			BackColor = UiTheme.Surface,
			Visible = fullCardOverlay
		};
		flowLayoutPanel3.Controls.Add(button);
		flowLayoutPanel3.Controls.Add(button2);
		flowLayoutPanel3.Controls.Add(_clearBackgroundButton);
		_layerStatus.Visible = fullCardOverlay;
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		panel.Controls.Add(flowLayoutPanel);
		panel.Controls.Add(value3);
		panel.Controls.Add(_layerStatus);
		panel.Controls.Add(flowLayoutPanel3);
		panel.Controls.Add(flowLayoutPanel2);
		panel.Controls.Add(_zoomValue);
		panel.Controls.Add(_zoom);
		panel.Controls.Add(value6);
		panel.Controls.Add(_mapping);
		panel.Controls.Add(_frames);
		panel.Controls.Add(value5);
		panel.Controls.Add(_frameCategory);
		panel.Controls.Add(value4);
		BorderPanel borderPanel = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.SurfaceAlt,
			Padding = new Padding(1),
			Margin = new Padding(14, 14, 7, 14)
		};
		borderPanel.Controls.Add(_canvas);
		BorderPanel borderPanel2 = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(1),
			Margin = new Padding(7, 14, 14, 14)
		};
		borderPanel2.Controls.Add(panel);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = UiTheme.Window
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fullCardOverlay ? 360f : 315f));
		tableLayoutPanel.Controls.Add(borderPanel, 0, 0);
		tableLayoutPanel.Controls.Add(borderPanel2, 1, 0);
		base.Controls.Add(tableLayoutPanel);
		base.Controls.Add(gradientBanner);
		base.AcceptButton = button4;
		base.CancelButton = button3;
		_zoom.Scroll += delegate
		{
			_canvas.SetZoom((float)_zoom.Value / 100f, null);
		};
		_canvas.ZoomChanged += delegate(float num2)
		{
			int num = Math.Clamp((int)Math.Round(num2 * 100f), _zoom.Minimum, _zoom.Maximum);
			if (_zoom.Value != num)
			{
				_zoom.Value = num;
			}
			_zoomValue.Text = $"{num2 * 100f:0}%";
		};
		_frames.SelectedIndexChanged += async delegate
		{
			if (!_refreshingFrameChoices)
			{
				await LoadSelectedFrameAsync();
			}
		};
		_frameCategory.SelectedIndexChanged += async delegate
		{
			if (!_refreshingFrameChoices)
			{
				string wantedKey = (_frames.SelectedItem as FrameChoice)?.Texture.Name;
				RefreshFrameChoices(wantedKey);
				await LoadSelectedFrameAsync();
			}
		};
		_zoomValue.Text = "100%";
		UpdateLayerStatus();
		base.Shown += async delegate
		{
			_canvas.Focus();
			if (_frames.SelectedItem is FrameChoice)
			{
				await LoadSelectedFrameAsync();
			}
			else
			{
				_canvas.ResetView();
			}
		};
		base.FormClosed += delegate
		{
			_canvas.DisposeFrame();
		};
	}

	private static int CategoryIndex(string? frameKey)
	{
		if (frameKey != null && frameKey.StartsWith("transparent_gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (frameKey != null && frameKey.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (frameKey != null && frameKey.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		return 3;
	}

	private void RefreshFrameChoices(string? wantedKey)
	{
		_refreshingFrameChoices = true;
		try
		{
			string wantedBase = CardFrameCatalog.BaseKey(string.IsNullOrWhiteSpace(wantedKey) ? CardFrameCatalog.DefaultKey(_targetWidth, _targetHeight) : wantedKey);
			IEnumerable<TexRef> source = _availableFrames;
			if (_fullCardOverlay && _frameCategory.Items.Count > 0)
			{
				source = source.Where((TexRef frame) => CurrentFrameCategory switch
				{
					FrameCategory.Transparent => BuiltInCardFrameCatalog.IsTransparentFrame(frame),
					FrameCategory.TransparentGradient => BuiltInCardFrameCatalog.IsTransparentGradientFrame(frame),
					FrameCategory.Gradient => BuiltInCardFrameCatalog.IsGradientFrame(frame),
					_ => BuiltInCardFrameCatalog.IsNormalFrame(frame),
				});
			}
			FrameChoice[] source2 = (from frame in source.OrderBy<TexRef, string>((TexRef frame) => CardFrameCatalog.BaseKey(frame.Name), StringComparer.OrdinalIgnoreCase)
				select new FrameChoice(frame)).ToArray();
			_frames.BeginUpdate();
			_frames.Items.Clear();
			_frames.Items.AddRange(source2.Cast<object>().ToArray());
			FrameChoice selectedItem = source2.FirstOrDefault((FrameChoice choice) => choice.Texture.Name.Equals(wantedKey, StringComparison.OrdinalIgnoreCase)) ?? source2.FirstOrDefault((FrameChoice choice) => CardFrameCatalog.BaseKey(choice.Texture.Name).Equals(wantedBase, StringComparison.OrdinalIgnoreCase)) ?? source2.FirstOrDefault();
			_frames.SelectedItem = selectedItem;
		}
		finally
		{
			_frames.EndUpdate();
			_refreshingFrameChoices = false;
		}
	}

	private async Task ChangeSourceAsync()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "更换用于制作超框的卡图",
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
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
				Bitmap source = await Task.Run(() => ImageCropService.LoadPreview(dialog.FileName));
				_sourcePath = dialog.FileName;
				_canvas.SetSource(source);
				UpdateLayerStatus();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "更换卡图失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
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

	private async Task ChooseBackgroundAsync()
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "选择叠底背景图",
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
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
				Bitmap background = await Task.Run(() => ImageCropService.LoadPreview(dialog.FileName));
				_backgroundPath = dialog.FileName;
				_initialBackgroundPng = null;
				_canvas.SetBackground(background);
				UpdateLayerStatus();
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, ex.Message, "叠底背景载入失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
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

	private void ClearBackground()
	{
		_backgroundPath = null;
		_initialBackgroundPng = null;
		_canvas.SetBackground(null);
		UpdateLayerStatus();
	}

	private void UpdateLayerStatus()
	{
		string text = ((_backgroundPath != null) ? Path.GetFileName(_backgroundPath) : ((_initialBackgroundPng != null) ? "已保存的叠底背景" : "无叠底背景"));
		_layerStatus.Text = "卡图：" + Path.GetFileName(_sourcePath) + "\n背景：" + text;
		if (_clearBackgroundButton != null)
		{
			_clearBackgroundButton.Enabled = _backgroundPath != null || _initialBackgroundPng != null;
		}
	}

	private async Task LoadSelectedFrameAsync()
	{
		object selectedItem = _frames.SelectedItem;
		FrameChoice choice = selectedItem as FrameChoice;
		if (choice == null)
		{
			return;
		}
		int generation = ++_frameGeneration;
		_mapping.Text = "正在读取卡框与实际插图区…";
		try
		{
			Bitmap bitmap = FrameComposer.BitmapFrom(await Task.Run(() => _engine.DecodePng(choice.Texture)));
			if (generation != _frameGeneration)
			{
				bitmap.Dispose();
				return;
			}
			_canvas.SetFrame(bitmap);
			SelectedFrameKey = choice.Texture.Name;
			SizeF visualArtSize = _canvas.VisualArtSize;
			_mapping.Text = (_fullCardOverlay ? $"超框画布 {visualArtSize.Width:0}×{visualArtSize.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}" : (_displayMapping.RequiresMapping ? $"实际插图区 {visualArtSize.Width:0}×{visualArtSize.Height:0}\n{_displayMapping.EditorSummary}" : $"实际插图区 {visualArtSize.Width:0}×{visualArtSize.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}"));
		}
		catch (Exception ex)
		{
			_mapping.Text = "卡框预览载入失败：" + ex.Message;
		}
	}

	private void ConfirmCrop()
	{
		if (_frames.Items.Count > 0 && !_canvas.HasFrame)
		{
			MessageBox.Show(this, "卡框预览还没有载入完成，请稍候。", Text, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			OutputPng = ImageCropService.RenderToTarget(_sourcePath, _canvas.RenderSpec, _targetWidth, _targetHeight);
			BackgroundPng = ((_backgroundPath != null) ? ImageCropService.RenderCoverToTarget(_backgroundPath, _targetWidth, _targetHeight) : _initialBackgroundPng?.ToArray());
			base.DialogResult = DialogResult.OK;
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "裁剪失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}
}
