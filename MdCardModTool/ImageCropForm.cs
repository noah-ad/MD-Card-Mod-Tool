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
		Cool,
		Normal
	}

	private sealed record FrameCategoryChoice(FrameCategory Category, string Label)
	{
		public override string ToString() => Label;
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
			string key = CardFrameCatalog.BaseKey(Texture.Name);
			return key + " · " + CardFrameCatalog.FriendlyName(key);
		}
	}

	private string _sourcePath;

	private readonly int _targetWidth;

	private readonly int _targetHeight;

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

	public ImageCropForm(string sourcePath, int targetWidth, int targetHeight, string purpose = "替换图片", IEnumerable<TexRef>? cardFrames = null, string? preferredFrameKey = null, bool fullCardOverlay = false, byte[]? initialBackgroundPng = null)
	{
		ImageCropForm imageCropForm = this;
		if (targetWidth <= 0 || targetHeight <= 0)
		{
			throw new ArgumentOutOfRangeException("targetWidth", "目标尺寸必须大于 0。");
		}
		_sourcePath = sourcePath;
		_targetWidth = targetWidth;
		_targetHeight = targetHeight;
		_fullCardOverlay = fullCardOverlay;
		_initialBackgroundPng = initialBackgroundPng?.ToArray();
		Bitmap preview = ImageCropService.LoadPreview(sourcePath);
		_canvas = new CropCanvas(preview, targetWidth, targetHeight, fullCardOverlay)
		{
			Dock = DockStyle.Fill,
			Margin = Padding.Empty,
			TabStop = true
		};
		_availableFrames = cardFrames == null
			? []
			: (fullCardOverlay
				? cardFrames.Where(frame => BuiltInCardFrameCatalog.IsPackagedFrame(frame)
					&& frame.Width == 704 && frame.Height == 1024)
				: CardFrameCatalog.CompatibleFrames(cardFrames, targetWidth, targetHeight)).ToArray();
		bool hasFrames = _availableFrames.Length > 0;
		if (fullCardOverlay && hasFrames)
		{
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.Transparent, "透明卡框"));
			_frameCategory.Items.Add(new FrameCategoryChoice(FrameCategory.Cool, "炫酷卡框"));
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
		base.KeyPreview = true;
		Label title = new Label
		{
			Text = (fullCardOverlay ? "超框实装构图" : (hasFrames ? "卡片实装构图" : "固定比例裁剪")),
			Dock = DockStyle.Top,
			Height = 30,
			ForeColor = UiTheme.Text,
			Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold)
		};
		Label subtitle = new Label
		{
			Text = (hasFrames ? $"{purpose} · 先按卡框实际效果构图，确认后自动转换为 {_targetWidth}×{_targetHeight} 存储纹理" : $"{purpose} · 输出 {_targetWidth}×{_targetHeight}"),
			Dock = DockStyle.Fill,
			ForeColor = UiTheme.Gold,
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		GradientBanner header = new GradientBanner
		{
			Dock = DockStyle.Top,
			Height = 72,
			Padding = new Padding(20, 10, 20, 8)
		};
		header.Controls.Add(subtitle);
		header.Controls.Add(title);
		Label help = new Label
		{
			Text = (fullCardOverlay ? "图层顺序：叠底背景 → 卡框 → 卡图主体。\n\n• 三类卡框可独立切换\n• 更换卡图不会移除叠底背景\n• 左键拖动卡图，滚轮／滑杆缩放\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n确认后进入超框编辑器完成应用。" : (hasFrames ? "操作\n\n• 卡框下拉只控制实装预览与构图比例\n• 左键拖动图片，滚轮／滑杆缩放\n• 可缩到画框以内，透明处按游戏白色底板显示\n• 方向键微调，Shift + 方向键快速移动\n• 双击画面恢复铺满\n\n灵摆卡会按正常宽画面预览，保存时再自动压回 512×1024。" : "操作\n\n• 左键拖动图片\n• 滚轮／滑杆可自由放大和缩小\n• 方向键微调位置\n• 双击恢复铺满\n\n确认后自动转换为目标尺寸，透明 PNG 的 Alpha 会保留。")),
			Dock = DockStyle.Top,
			Height = (fullCardOverlay ? 190 : (hasFrames ? 260 : 210)),
			ForeColor = UiTheme.Text,
			Padding = new Padding(2, 8, 2, 8),
			Font = new Font("Microsoft YaHei UI", 9f)
		};
		Label categoryTitle = new Label
		{
			Text = "卡框分类",
			Dock = DockStyle.Top,
			Height = 28,
			ForeColor = UiTheme.Gold,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			Visible = fullCardOverlay && hasFrames
		};
		_frameCategory.Visible = fullCardOverlay && hasFrames;
		Label frameTitle = new Label
		{
			Text = fullCardOverlay ? "卡框样式" : "预览卡框",
			Dock = DockStyle.Top,
			Height = 28,
			ForeColor = UiTheme.Muted,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
			Visible = hasFrames
		};
		_frames.Visible = hasFrames;
		_mapping.Visible = hasFrames;
		Label zoomTitle = new Label
		{
			Text = "缩放（1%–2000%）",
			Dock = DockStyle.Top,
			Height = 30,
			ForeColor = UiTheme.Muted,
			Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
		};
		Button fill = UiTheme.Button("铺满画框", delegate
		{
			imageCropForm._canvas.ResetView();
		});
		Button whole = UiTheme.Button("显示整图", delegate
		{
			imageCropForm._canvas.ShowWholeImage();
		});
		Button changeCard = UiTheme.Button("更换卡图", async delegate
		{
			await imageCropForm.ChangeSourceAsync();
		}, ButtonTone.Primary);
		Button addBackground = UiTheme.Button("添加叠底背景", async delegate
		{
			await imageCropForm.ChooseBackgroundAsync();
		}, ButtonTone.Gold);
		_clearBackgroundButton = UiTheme.Button("清除背景", delegate
		{
			imageCropForm.ClearBackground();
		});
		changeCard.Visible = fullCardOverlay;
		addBackground.Visible = fullCardOverlay;
		_clearBackgroundButton.Visible = fullCardOverlay;
		Button cancel = UiTheme.Button("取消", delegate
		{
			imageCropForm.DialogResult = DialogResult.Cancel;
			imageCropForm.Close();
		});
		Button apply = UiTheme.Button(fullCardOverlay ? "保存构图并继续" : "按预览效果替换", delegate
		{
			imageCropForm.ConfirmCrop();
		}, ButtonTone.Primary);
		cancel.DialogResult = DialogResult.Cancel;
		FlowLayoutPanel actionRow = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 54,
			FlowDirection = FlowDirection.RightToLeft,
			WrapContents = false,
			Padding = new Padding(0, 7, 0, 5),
			BackColor = UiTheme.Surface
		};
		actionRow.Controls.Add(apply);
		actionRow.Controls.Add(cancel);
		FlowLayoutPanel resetRow = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 48,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Padding = new Padding(0, 4, 0, 4),
			BackColor = UiTheme.Surface
		};
		resetRow.Controls.Add(fill);
		resetRow.Controls.Add(whole);
		FlowLayoutPanel layerActions = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 82,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			Padding = new Padding(0, 3, 0, 3),
			BackColor = UiTheme.Surface,
			Visible = fullCardOverlay
		};
		layerActions.Controls.Add(changeCard);
		layerActions.Controls.Add(addBackground);
		layerActions.Controls.Add(_clearBackgroundButton);
		_layerStatus.Visible = fullCardOverlay;
		Panel side = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(18)
		};
		side.Controls.Add(actionRow);
		side.Controls.Add(help);
		side.Controls.Add(_layerStatus);
		side.Controls.Add(layerActions);
		side.Controls.Add(resetRow);
		side.Controls.Add(_zoomValue);
		side.Controls.Add(_zoom);
		side.Controls.Add(zoomTitle);
		side.Controls.Add(_mapping);
		side.Controls.Add(_frames);
		side.Controls.Add(frameTitle);
		side.Controls.Add(_frameCategory);
		side.Controls.Add(categoryTitle);
		BorderPanel canvasFrame = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.SurfaceAlt,
			Padding = new Padding(1),
			Margin = new Padding(14, 14, 7, 14)
		};
		canvasFrame.Controls.Add(_canvas);
		BorderPanel sideFrame = new BorderPanel
		{
			Dock = DockStyle.Fill,
			BackColor = UiTheme.Surface,
			Padding = new Padding(1),
			Margin = new Padding(7, 14, 14, 14)
		};
		sideFrame.Controls.Add(side);
		TableLayoutPanel body = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = UiTheme.Window
		};
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fullCardOverlay ? 360f : 315f));
		body.Controls.Add(canvasFrame, 0, 0);
		body.Controls.Add(sideFrame, 1, 0);
		base.Controls.Add(body);
		base.Controls.Add(header);
		base.AcceptButton = apply;
		base.CancelButton = cancel;
		_zoom.Scroll += delegate
		{
			imageCropForm._canvas.SetZoom((float)imageCropForm._zoom.Value / 100f, null);
		};
		_canvas.ZoomChanged += delegate(float value)
		{
			int num = Math.Clamp((int)Math.Round(value * 100f), imageCropForm._zoom.Minimum, imageCropForm._zoom.Maximum);
			if (imageCropForm._zoom.Value != num)
			{
				imageCropForm._zoom.Value = num;
			}
			imageCropForm._zoomValue.Text = $"{value * 100f:0}%";
		};
		_frames.SelectedIndexChanged += async delegate
		{
			if (!imageCropForm._refreshingFrameChoices)
			{
				await imageCropForm.LoadSelectedFrameAsync();
			}
		};
		_frameCategory.SelectedIndexChanged += async delegate
		{
			if (!imageCropForm._refreshingFrameChoices)
			{
				string? wanted = (imageCropForm._frames.SelectedItem as FrameChoice)?.Texture.Name;
				imageCropForm.RefreshFrameChoices(wanted);
				await imageCropForm.LoadSelectedFrameAsync();
			}
		};
		_zoomValue.Text = "100%";
		UpdateLayerStatus();
		base.Shown += async delegate
		{
			imageCropForm._canvas.Focus();
			if (imageCropForm._frames.SelectedItem is FrameChoice)
			{
				await imageCropForm.LoadSelectedFrameAsync();
			}
			else
			{
				imageCropForm._canvas.ResetView();
			}
		};
		base.FormClosed += delegate
		{
			imageCropForm._canvas.DisposeFrame();
		};
	}

	private static int CategoryIndex(string? frameKey)
	{
		if (frameKey?.StartsWith("gradient_", StringComparison.OrdinalIgnoreCase) == true) return 1;
		if (frameKey?.StartsWith("transparent_", StringComparison.OrdinalIgnoreCase) == true) return 0;
		return 2;
	}

	private FrameCategory CurrentFrameCategory =>
		(_frameCategory.SelectedItem as FrameCategoryChoice)?.Category ?? FrameCategory.Normal;

	private void RefreshFrameChoices(string? wantedKey)
	{
		_refreshingFrameChoices = true;
		try
		{
			string wantedBase = CardFrameCatalog.BaseKey(string.IsNullOrWhiteSpace(wantedKey)
				? CardFrameCatalog.DefaultKey(_targetWidth, _targetHeight) : wantedKey);
			IEnumerable<TexRef> source = _availableFrames;
			if (_fullCardOverlay && _frameCategory.Items.Count > 0)
			{
				source = source.Where(frame => CurrentFrameCategory switch
				{
					FrameCategory.Transparent => BuiltInCardFrameCatalog.IsTransparentFrame(frame),
					FrameCategory.Cool => BuiltInCardFrameCatalog.IsGradientFrame(frame),
					_ => BuiltInCardFrameCatalog.IsNormalFrame(frame)
				});
			}
			FrameChoice[] choices = source.OrderBy(frame => CardFrameCatalog.BaseKey(frame.Name), StringComparer.OrdinalIgnoreCase)
				.Select(frame => new FrameChoice(frame)).ToArray();
			_frames.BeginUpdate();
			_frames.Items.Clear();
			_frames.Items.AddRange(choices.Cast<object>().ToArray());
			FrameChoice? selected = choices.FirstOrDefault(choice =>
				choice.Texture.Name.Equals(wantedKey, StringComparison.OrdinalIgnoreCase))
				?? choices.FirstOrDefault(choice => CardFrameCatalog.BaseKey(choice.Texture.Name)
					.Equals(wantedBase, StringComparison.OrdinalIgnoreCase))
				?? choices.FirstOrDefault();
			_frames.SelectedItem = selected;
		}
		finally
		{
			_frames.EndUpdate();
			_refreshingFrameChoices = false;
		}
	}

	private async Task ChangeSourceAsync()
	{
		using OpenFileDialog dialog = new()
		{
			Title = "更换用于制作超框的卡图",
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
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

	private async Task ChooseBackgroundAsync()
	{
		using OpenFileDialog dialog = new()
		{
			Title = "选择叠底背景图",
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK) return;
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

	private void ClearBackground()
	{
		_backgroundPath = null;
		_initialBackgroundPng = null;
		_canvas.SetBackground(null);
		UpdateLayerStatus();
	}

	private void UpdateLayerStatus()
	{
		string background = _backgroundPath != null
			? Path.GetFileName(_backgroundPath)
			: _initialBackgroundPng != null ? "已保存的叠底背景" : "无叠底背景";
		_layerStatus.Text = $"卡图：{Path.GetFileName(_sourcePath)}\n背景：{background}";
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
			Bitmap frame = FrameComposer.BitmapFrom(await Task.Run(() => _engine.DecodePng(choice.Texture)));
			if (generation != _frameGeneration)
			{
				frame.Dispose();
				return;
			}
			_canvas.SetFrame(frame);
			SelectedFrameKey = choice.Texture.Name;
			SizeF window = _canvas.VisualArtSize;
			_mapping.Text = (_fullCardOverlay ? $"超框画布 {window.Width:0}×{window.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}" : ((_targetWidth == 512 && _targetHeight == 1024) ? $"实际插图区 {window.Width:0}×{window.Height:0}\n显示区 → 512×{596} · 完整纹理 512×1024" : $"实际插图区 {window.Width:0}×{window.Height:0}\n保存映射 → {_targetWidth}×{_targetHeight}"));
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
			int visibleTargetHeight = ((!_fullCardOverlay && _targetWidth == 512 && _targetHeight == 1024) ? 596 : _targetHeight);
			OutputPng = ImageCropService.RenderToTarget(_sourcePath, _canvas.RenderSpec, _targetWidth, _targetHeight, visibleTargetHeight);
			BackgroundPng = _backgroundPath != null
				? ImageCropService.RenderCoverToTarget(_backgroundPath, _targetWidth, _targetHeight)
				: _initialBackgroundPng?.ToArray();
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
