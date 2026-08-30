using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class FramePreviewForm : Form
{
	private sealed class FrameChoice(TexRef texture)
	{
		public TexRef Texture { get; } = texture;

		public override string ToString()
		{
			return Texture.Name + " · " + CardFrameCatalog.FriendlyName(Texture.Name);
		}
	}

	private readonly ModEngine _engine;

	private readonly TexRef _art;

	private readonly ModernComboBox _frames = new ModernComboBox
	{
		DropDownStyle = ComboBoxStyle.DropDownList,
		Dock = DockStyle.Top
	};

	private readonly PictureBox _preview = new PictureBox
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = Color.FromArgb(16, 22, 34)
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Bottom,
		Height = 42,
		Padding = new Padding(8),
		ForeColor = Color.Gainsboro
	};

	private int _generation;

	public FramePreviewForm(ModEngine engine, TexRef art, IEnumerable<TexRef> frames)
	{
		UiTheme.ApplyDarkTitleBar(this);
		_engine = engine;
		_art = art;
		Text = "卡框预览模式";
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new Size(920, 820);
		MinimumSize = new Size(660, 600);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		UiTheme.StyleComboBox(_frames);
		Button export = Button("导出预览 PNG", delegate
		{
			Export();
		});
		TableLayoutPanel top = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 70,
			Padding = new Padding(12, 10, 12, 8),
			ColumnCount = 3,
			BackColor = UiTheme.SurfaceAlt
		};
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.Controls.Add(new Label
		{
			Text = "卡框",
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = Color.FromArgb(160, 195, 255)
		}, 0, 0);
		top.Controls.Add(_frames, 1, 0);
		top.Controls.Add(export, 2, 0);
		bool directCard = art.Width == 512 && (art.Height == 512 || art.Height == 1024);
		top.Controls.Add(new Label
		{
			Text = (directCard ? "按游戏实际插图区还原显示比例；卡框只用于预览，不会修改全局卡框。" : "按游戏超框层级预览：卡框在下、透明高图在上；只合成显示，不写入游戏。"),
			AutoSize = true,
			ForeColor = Color.Gainsboro
		}, 0, 1);
		top.SetColumnSpan(top.GetControlFromPosition(0, 1), 3);
		IEnumerable<TexRef> source;
		if (!directCard)
		{
			IEnumerable<TexRef> enumerable = from x in frames
				where x.Name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase) && x.Width == 704 && x.Height == 1024
				orderby x.Name
				select x;
			source = enumerable;
		}
		else
		{
			source = CardFrameCatalog.CompatibleFrames(frames, art.Width, art.Height);
		}
		FrameChoice[] choices = source.Select((TexRef x) => new FrameChoice(x)).ToArray();
		ComboBox.ObjectCollection items = _frames.Items;
		object[] items2 = choices;
		items.AddRange(items2);
		string wanted = ((art.PreviewFrameKey.Length > 0) ? art.PreviewFrameKey : CardFrameCatalog.DefaultKey(art.Width, art.Height));
		int defaultIndex = Array.FindIndex(choices, (FrameChoice x) => x.Texture.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
		_frames.SelectedIndex = ((defaultIndex >= 0) ? defaultIndex : 0);
		_frames.SelectedIndexChanged += async delegate
		{
			await RenderAsync();
		};
		base.Controls.Add(_preview);
		base.Controls.Add(_status);
		base.Controls.Add(top);
		base.Shown += async delegate
		{
			await RenderAsync();
		};
		base.FormClosed += delegate
		{
			_preview.Image?.Dispose();
		};
	}

	private static Button Button(string text, EventHandler click)
	{
		return UiTheme.Button(text, click);
	}

	private async Task RenderAsync()
	{
		object selectedItem = _frames.SelectedItem;
		FrameChoice choice = selectedItem as FrameChoice;
		if (choice == null)
		{
			return;
		}
		int generation = ++_generation;
		base.UseWaitCursor = true;
		_status.Text = "正在合成卡框预览…";
		try
		{
			byte[][] sources = await Task.WhenAll<byte[]>(Task.Run(() => _engine.DecodePng(_art)), Task.Run(() => _engine.DecodePng(choice.Texture)));
			if (generation == _generation)
			{
				byte[] composed = await Task.Run(() => (_art.Width != 704 || _art.Height != 1024) ? CardFrameRenderer.ComposeStoredArtPreview(sources[0], sources[1]) : FrameComposer.Compose(sources[0], sources[1]));
				Bitmap output = ((_art.Width == 704 && _art.Height == 1024) ? FrameComposer.PreviewBitmap(composed) : FrameComposer.BitmapFrom(composed));
				_art.PreviewFrameKey = choice.Texture.Name;
				Image image = _preview.Image;
				_preview.Image = output;
				image?.Dispose();
				_status.Text = $"{_art.Name}  +  {choice.Texture.Name}（{output.Width}×{output.Height}）";
			}
		}
		catch (Exception ex)
		{
			_status.Text = "卡框预览失败：" + ex.Message;
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private void Export()
	{
		if (_preview.Image == null)
		{
			return;
		}
		SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = "PNG 图片|*.png",
			FileName = Safe(_art.Name) + "_卡框预览.png"
		};
		try
		{
			if (dialog.ShowDialog(this) == DialogResult.OK)
			{
				_preview.Image.Save(dialog.FileName, ImageFormat.Png);
			}
		}
		finally
		{
			((IDisposable)(object)dialog)?.Dispose();
		}
	}

	private static string Safe(string n)
	{
		return string.Concat(n.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
	}
}
