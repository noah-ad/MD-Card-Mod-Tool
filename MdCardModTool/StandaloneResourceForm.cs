using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

internal sealed class StandaloneResourceForm : Form
{
	private readonly string root;

	private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

	private readonly List<TexRef> all = new List<TexRef>();

	private List<TexRef> visible = new List<TexRef>();

	private readonly ConcurrentQueue<TexRef> pending = new ConcurrentQueue<TexRef>();

	private readonly CardCatalogService catalog = CardCatalogService.LoadBestAvailable();

	private readonly Dictionary<int, CardCatalogEntry> cardNames;

	private readonly SemaphoreSlim decodeGate = new SemaphoreSlim(1, 1);

	private readonly TextBox search = new TextBox
	{
		Dock = DockStyle.Fill,
		PlaceholderText = "输入卡号、卡名、贴图名称或文件名，按 Enter 搜索"
	};

	private readonly ListView list = new ListView
	{
		Dock = DockStyle.Fill,
		View = View.Details,
		VirtualMode = true,
		FullRowSelect = true,
		MultiSelect = false,
		HideSelection = false
	};

	private readonly PictureBox preview = new PictureBox
	{
		Dock = DockStyle.Fill,
		SizeMode = PictureBoxSizeMode.Zoom,
		BackColor = UiTheme.Surface
	};

	private readonly Label status = new Label
	{
		Dock = DockStyle.Fill,
		AutoEllipsis = true,
		TextAlign = ContentAlignment.MiddleLeft
	};

	private readonly Label detail = new Label
	{
		Dock = DockStyle.Fill,
		AutoEllipsis = true
	};

	private readonly Button export = new Button
	{
		Text = "导出原尺寸 PNG",
		Dock = DockStyle.Fill,
		Enabled = false
	};

	private readonly Button stop = new Button
	{
		Text = "停止扫描",
		AutoSize = true
	};

	private readonly Button replace = new Button
	{
		Text = "更换图片",
		Dock = DockStyle.Fill,
		Enabled = false
	};

	private readonly Button restore = new Button
	{
		Text = "还原此 Bundle",
		Dock = DockStyle.Fill,
		Enabled = false
	};

	private bool writing;

	private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
	{
		Interval = 600
	};

	private int done;

	private int total;

	private int errors;

	private int previewVersion;

	private bool scanning;

	private string lastError = "";

	private Task? scanTask;

	private Task? decodeTask;

	private TexRef? selected;

	private string query = "";

	internal bool AutoScan { get; set; } = true;

	internal StandaloneResourceForm(string path)
	{
		root = StandaloneResourceService.ResolveRoot(path) ?? throw new DirectoryNotFoundException("请选择包含两位哈希子目录的独立 0000 文件夹。");
		cardNames = catalog.Entries.DistinctBy((CardCatalogEntry c) => c.CardId).ToDictionary((CardCatalogEntry c) => c.CardId);
		base.AutoScaleDimensions = new SizeF(96f, 96f);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		Text = "手机 / 独立 0000 资源修改";
		base.Size = new Size(1200, 800);
		MinimumSize = new Size(800, 580);
		base.StartPosition = FormStartPosition.CenterParent;
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		UiTheme.StyleTextBox(search);
		UiTheme.StyleList(list);
		Button[] array = new Button[4] { export, stop, replace, restore };
		foreach (Button obj in array)
		{
			obj.FlatStyle = FlatStyle.Flat;
			obj.BackColor = UiTheme.Surface;
			obj.ForeColor = UiTheme.Text;
			obj.Padding = new Padding(10, 4, 10, 4);
		}
		list.Columns.Add("卡号 / 名称", 260);
		list.Columns.Add("尺寸", 110);
		list.Columns.Add("文件", 150);
		list.SizeChanged += delegate
		{
			list.Columns[2].Width = Math.Max(150 * list.DeviceDpi / 96, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width);
		};
		status.Click += delegate
		{
			if (errors > 0)
			{
				MessageBox.Show(this, lastError, "最近一次扫描错误");
			}
		};
		list.RetrieveVirtualItem += delegate(object? _, RetrieveVirtualItemEventArgs e)
		{
			if (e.ItemIndex >= visible.Count)
			{
				e.Item = new ListViewItem("");
			}
			else
			{
				TexRef texRef = visible[e.ItemIndex];
				e.Item = new ListViewItem(new string[3]
				{
					DisplayName(texRef),
					$"{texRef.Width}×{texRef.Height}",
					texRef.RelativeBundlePath
				});
			}
		};
		list.SelectedIndexChanged += delegate
		{
			decodeTask = LoadPreviewAsync();
		};
		search.KeyDown += delegate(object? _, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				ApplyFilter();
			}
		};
		Button button = new Button
		{
			Text = "搜索",
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			BackColor = UiTheme.Surface
		};
		button.Click += delegate
		{
			if (!writing)
			{
				ApplyFilter();
			}
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
		tableLayoutPanel.Controls.Add(search, 0, 0);
		tableLayoutPanel.Controls.Add(button, 1, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			ColumnCount = 1
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			Margin = Padding.Empty
		};
		for (int num2 = 0; num2 < 3; num2++)
		{
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333332f));
		}
		tableLayoutPanel3.Controls.Add(export, 0, 0);
		tableLayoutPanel3.Controls.Add(replace, 1, 0);
		tableLayoutPanel3.Controls.Add(restore, 2, 0);
		tableLayoutPanel2.Controls.Add(preview, 0, 0);
		tableLayoutPanel2.Controls.Add(detail, 0, 1);
		tableLayoutPanel2.Controls.Add(tableLayoutPanel3, 0, 2);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
		tableLayoutPanel4.Controls.Add(list, 0, 0);
		tableLayoutPanel4.Controls.Add(tableLayoutPanel2, 1, 0);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125f));
		tableLayoutPanel5.Controls.Add(status, 0, 0);
		tableLayoutPanel5.Controls.Add(stop, 1, 0);
		TableLayoutPanel tableLayoutPanel6 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(14),
			RowCount = 4,
			ColumnCount = 1
		};
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
		tableLayoutPanel6.Controls.Add(new Label
		{
			Dock = DockStyle.Fill,
			AutoEllipsis = true,
			Text = root + "\n手机资源：搜索、预览、更换图片与备份还原。修改本机这份 0000，完成后需自行复制回手机。"
		}, 0, 0);
		tableLayoutPanel6.Controls.Add(tableLayoutPanel, 0, 1);
		tableLayoutPanel6.Controls.Add(tableLayoutPanel4, 0, 2);
		tableLayoutPanel6.Controls.Add(tableLayoutPanel5, 0, 3);
		base.Controls.Add(tableLayoutPanel6);
		stop.Click += delegate
		{
			cancellation.Cancel();
			stop.Enabled = false;
			status.Text = "正在停止；等待当前 Bundle 读取结束…";
		};
		export.Click += async delegate
		{
			await ExportAsync();
		};
		replace.Click += async delegate
		{
			await ReplaceAsync();
		};
		restore.Click += async delegate
		{
			await RestoreAsync();
		};
		timer.Tick += delegate
		{
			Drain();
		};
		base.Shown += delegate
		{
			if (AutoScan)
			{
				timer.Start();
				scanTask = ScanAsync();
			}
		};
		base.FormClosed += delegate
		{
			cancellation.Cancel();
			timer.Stop();
			previewVersion++;
			preview.Image?.Dispose();
			preview.Image = null;
		};
		base.FormClosing += delegate(object? _, FormClosingEventArgs e)
		{
			if (writing)
			{
				e.Cancel = true;
				MessageBox.Show(this, "正在验证或写入，请等待完成后关闭。");
			}
		};
	}

	private string DisplayName(TexRef item)
	{
		int result;
		CardCatalogEntry cardCatalogEntry = (int.TryParse(item.CardKey, out result) ? cardNames.GetValueOrDefault(result) : null);
		if (!(cardCatalogEntry == null))
		{
			return item.CardKey + " · " + cardCatalogEntry.Name(Localizer.Language);
		}
		return item.Name;
	}

	private async Task ScanAsync()
	{
		scanning = true;
		try
		{
			await Task.Run(delegate
			{
				StandaloneResourceService.Scan(root, delegate(IReadOnlyList<TexRef> items, int count, int maximum, string? error)
				{
					foreach (TexRef item in items)
					{
						pending.Enqueue(item);
					}
					Volatile.Write(ref done, count);
					Volatile.Write(ref total, maximum);
					if (error != null)
					{
						Interlocked.Increment(ref errors);
						lastError = error;
					}
				}, cancellation.Token);
			});
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			lastError = ex2.Message;
			Interlocked.Increment(ref errors);
		}
		finally
		{
			scanning = false;
			if (!base.IsDisposed)
			{
				Drain();
				stop.Enabled = false;
			}
		}
	}

	private void Drain()
	{
		bool flag = false;
		TexRef result;
		while (pending.TryDequeue(out result))
		{
			all.Add(result);
			flag = true;
		}
		if (flag)
		{
			ApplyFilter(preserveSelection: true);
		}
		status.Text = $"{(scanning ? "扫描中" : (cancellation.IsCancellationRequested ? "已停止" : "扫描完成"))} {Volatile.Read(in done):N0}/{Volatile.Read(in total):N0} · 图片 {all.Count:N0} · 当前 {visible.Count:N0} · 错误 {errors}";
		if (errors > 0)
		{
			status.AccessibleDescription = lastError;
		}
	}

	private void ApplyFilter(bool preserveSelection = false)
	{
		if (!preserveSelection)
		{
			query = search.Text.Trim();
		}
		HashSet<string> matches = ((query.Length == 0) ? new HashSet<string>() : (from c in catalog.Search(query, 10000)
			select c.CardId.ToString()).ToHashSet());
		visible = all.Where((TexRef t) => query.Length == 0 || t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || t.RelativeBundlePath.Contains(query, StringComparison.OrdinalIgnoreCase) || matches.Contains(t.CardKey)).ToList();
		list.VirtualListSize = visible.Count;
		list.Invalidate();
		if (!preserveSelection)
		{
			list.SelectedIndices.Clear();
			ClearPreview();
		}
	}

	private void ClearPreview()
	{
		previewVersion++;
		selected = null;
		Button button = export;
		Button button2 = replace;
		bool flag = (restore.Enabled = false);
		bool enabled = (button2.Enabled = flag);
		button.Enabled = enabled;
		Image image = preview.Image;
		preview.Image = null;
		image?.Dispose();
		detail.Text = "";
	}

	private async Task LoadPreviewAsync()
	{
		ClearPreview();
		if (list.SelectedIndices.Count == 0 || list.SelectedIndices[0] >= visible.Count)
		{
			return;
		}
		TexRef texture = visible[list.SelectedIndices[0]];
		int version = previewVersion;
		detail.Text = "正在解码…";
		try
		{
			await decodeGate.WaitAsync();
			byte[] buffer;
			try
			{
				if (base.IsDisposed || version != previewVersion)
				{
					return;
				}
				buffer = await Task.Run(() => StandaloneResourceService.Decode(texture, 1400));
			}
			finally
			{
				decodeGate.Release();
			}
			if (base.IsDisposed || version != previewVersion)
			{
				return;
			}
			using MemoryStream stream = new MemoryStream(buffer);
			using Image original = Image.FromStream(stream);
			preview.Image = new Bitmap(original);
			selected = texture;
			Button button = export;
			bool enabled = (replace.Enabled = !writing);
			button.Enabled = enabled;
			restore.Enabled = !writing && StandaloneModService.HasBackup(root, texture);
			detail.Text = DisplayName(texture) + $"\n{texture.Width}×{texture.Height} · {texture.RelativeBundlePath}\n{(restore.Enabled ? "已有首次备份，可还原整个 Bundle" : "首次替换自动备份")} · 原始纹理尺寸";
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed && version == previewVersion)
			{
				detail.Text = "解码失败：" + ex.Message;
			}
		}
	}

	private async Task ExportAsync()
	{
		if (selected == null)
		{
			return;
		}
		TexRef texture = selected;
		using SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = "PNG 图片|*.png",
			FileName = Path.GetFileNameWithoutExtension(texture.BundlePath) + ".png"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		if (StandaloneResourceService.IsInside(dialog.FileName, root))
		{
			MessageBox.Show(this, "PNG 请导出到 0000 以外的目录，避免覆盖资源。");
			return;
		}
		export.Enabled = false;
		try
		{
			DirectModArchive.EnsureNoLinks(dialog.FileName);
			byte[] bytes = await Task.Run(() => StandaloneResourceService.Decode(texture));
			await File.WriteAllBytesAsync(dialog.FileName, bytes);
		}
		catch (Exception ex)
		{
			if (!base.IsDisposed)
			{
				MessageBox.Show(this, ex.Message, "导出失败");
			}
		}
		finally
		{
			if (!base.IsDisposed)
			{
				export.Enabled = selected != null;
			}
		}
	}

	private async Task ReplaceAsync()
	{
		if (selected == null || writing)
		{
			return;
		}
		TexRef texture = selected;
		using OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
		};
		if (dialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			ImageCropForm crop = new ImageCropForm(dialog.FileName, texture.Width, texture.Height, "更换手机纹理：" + texture.Name);
			try
			{
				if (crop.ShowDialog(this) == DialogResult.OK && crop.OutputPng != null && MessageBox.Show(this, $"将修改本机文件：\n{texture.ActiveBundlePath}\n\n首次备份：\n{StandaloneModService.BackupRoot(root)}\n\n请先退出手机游戏、停止同步。写入使用 RGBA32（可能大于手机原压缩纹理），游戏内效果尚需手机验证。继续？", "确认手机资源替换", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
				{
					await WriteAsync(texture, delegate
					{
						StandaloneModService.Replace(root, texture, crop.OutputPng);
					}, "替换完成；请将修改后的同名文件复制回手机对应 0000。首次备份已保留。");
				}
			}
			finally
			{
				if (crop != null)
				{
					((IDisposable)crop).Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "无法更换图片");
		}
	}

	private async Task RestoreAsync()
	{
		if (selected == null || writing)
		{
			return;
		}
		TexRef texture = selected;
		if (MessageBox.Show(this, "还原此 Bundle 内所有纹理到第一次修改之前（不只当前图片）？请先退出游戏、停止同步。", "确认还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
		{
			await WriteAsync(texture, delegate
			{
				StandaloneModService.Restore(root, texture);
			}, "已还原首次备份；如已复制到手机，请再将还原文件复制回去。");
		}
	}

	private async Task WriteAsync(TexRef texture, Action action, string message)
	{
		writing = true;
		cancellation.Cancel();
		timer.Stop();
		ListView listView = list;
		TextBox textBox = search;
		Button button = export;
		Button button2 = replace;
		bool flag = (restore.Enabled = false);
		bool flag3 = (button2.Enabled = flag);
		bool flag5 = (button.Enabled = flag3);
		bool enabled = (textBox.Enabled = flag5);
		listView.Enabled = enabled;
		previewVersion++;
		try
		{
			_ = 3;
			try
			{
				status.Text = "等待扫描结束，准备临时副本验证…";
				if (scanTask != null)
				{
					await scanTask;
				}
				await decodeGate.WaitAsync();
				try
				{
					status.Text = "正在构建、回读验证并提交手机 Bundle…";
					await Task.Run(action);
					List<TexRef> collection = await Task.Run(() => StandaloneResourceService.ReadBundle(texture.ActiveBundlePath, root));
					all.RemoveAll((TexRef t) => t.BundlePath.Equals(texture.BundlePath, StringComparison.OrdinalIgnoreCase));
					all.AddRange(collection);
					ApplyFilter(preserveSelection: true);
					list.SelectedIndices.Clear();
					int num = visible.FindIndex((TexRef t) => t.BundlePath == texture.BundlePath && t.PathId == texture.PathId);
					if (num >= 0)
					{
						list.SelectedIndices.Add(num);
					}
					status.Text = message;
				}
				finally
				{
					decodeGate.Release();
				}
			}
			catch (Exception ex)
			{
				status.Text = "操作失败：" + ex.Message;
				MessageBox.Show(this, ex.Message, "手机资源修改失败");
			}
		}
		finally
		{
			writing = false;
			ListView listView2 = list;
			enabled = (search.Enabled = true);
			listView2.Enabled = enabled;
			await LoadPreviewAsync();
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			cancellation.Cancel();
			timer.Dispose();
		}
		base.Dispose(disposing);
	}
}
