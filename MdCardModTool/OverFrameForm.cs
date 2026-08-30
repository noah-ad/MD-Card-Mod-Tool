using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed class OverFrameForm : Form
{
	private readonly string _gameRoot;

	private readonly OverFrameService _service = new OverFrameService();

	private readonly TextBox _cardId = new TextBox
	{
		Dock = DockStyle.Fill,
		PlaceholderText = "例如 20570"
	};

	private readonly TextBox _artId = new TextBox
	{
		Dock = DockStyle.Fill,
		PlaceholderText = "默认与显示卡号相同"
	};

	private readonly OverFrameMappingTable _mappings = new OverFrameMappingTable
	{
		Dock = DockStyle.Fill
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Bottom,
		Height = 52,
		Padding = new Padding(12, 7, 12, 7),
		ForeColor = UiTheme.Muted,
		BackColor = UiTheme.SurfaceAlt
	};

	private CancellationTokenSource? _loadCancellation;

	private int _loadGeneration;

	public OverFrameForm(string gameRoot, string? selectedCardId)
	{
		UiTheme.ApplyDarkTitleBar(this);
		_gameRoot = gameRoot;
		Text = "超框模式 · of_card_asset";
		base.StartPosition = FormStartPosition.CenterParent;
		base.Size = new Size(850, 620);
		MinimumSize = new Size(700, 500);
		BackColor = UiTheme.Window;
		ForeColor = UiTheme.Text;
		Font = new Font("Microsoft YaHei UI", 9f);
		UiTheme.StyleTextBox(_cardId);
		UiTheme.StyleTextBox(_artId);
		_cardId.Text = selectedCardId ?? "";
		_artId.Text = selectedCardId ?? "";
		_mappings.SelectedMappingChanged += delegate
		{
			if (_mappings.SelectedMapping is OverFrameMapping overFrameMapping)
			{
				_cardId.Text = overFrameMapping.CardId.ToString();
				_artId.Text = overFrameMapping.ArtId.ToString();
			}
		};
		Button enable = Button("启用／更新", async delegate
		{
			await EnableAsync();
		}, accent: true);
		Button disable = Button("关闭所选", async delegate
		{
			await DisableAsync();
		});
		Button refresh = Button("刷新列表", async delegate
		{
			await LoadAsync();
		});
		Button restore = Button("还原超框表", async delegate
		{
			await RestoreAsync();
		});
		TableLayoutPanel top = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 154,
			Padding = new Padding(12, 10, 12, 8),
			ColumnCount = 5,
			RowCount = 3,
			BackColor = UiTheme.SurfaceAlt
		};
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
		top.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		top.Controls.Add(Label("显示卡号"), 0, 0);
		top.Controls.Add(UiTheme.Field(_cardId), 1, 0);
		top.Controls.Add(Label("高图卡号"), 2, 0);
		top.Controls.Add(UiTheme.Field(_artId), 3, 0);
		top.Controls.Add(enable, 4, 0);
		FlowLayoutPanel actions = new FlowLayoutPanel
		{
			AutoSize = true,
			Dock = DockStyle.Fill
		};
		actions.Controls.Add(disable);
		actions.Controls.Add(refresh);
		actions.Controls.Add(restore);
		top.Controls.Add(actions, 0, 1);
		top.SetColumnSpan(actions, 5);
		Label help = new Label
		{
			Text = "超框登记是：显示卡号 → 高图卡号。普通替换时两者填同一个卡号。实际高图须为 704×1024；修改会备份 LocalData 中真正的 of_card_asset，完成后请重启游戏。",
			AutoSize = true,
			ForeColor = UiTheme.Primary
		};
		top.Controls.Add(help, 0, 2);
		top.SetColumnSpan(help, 5);
		base.Controls.Add(_mappings);
		base.Controls.Add(_status);
		base.Controls.Add(top);
		base.Shown += async delegate
		{
			await LoadAsync();
		};
		base.FormClosed += delegate
		{
			Interlocked.Exchange(ref _loadCancellation, null)?.Cancel();
		};
	}

	private static Label Label(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = true,
			Anchor = AnchorStyles.Left,
			ForeColor = UiTheme.Text,
			Padding = new Padding(0, 5, 4, 0)
		};
	}

	private static Button Button(string text, EventHandler click, bool accent = false)
	{
		return UiTheme.Button(text, click, accent ? ButtonTone.Primary : ButtonTone.Neutral);
	}

	private async Task LoadAsync()
	{
		CancellationTokenSource source = new();
		Interlocked.Exchange(ref _loadCancellation, source)?.Cancel();
		CancellationToken cancellationToken = source.Token;
		int generation = Interlocked.Increment(ref _loadGeneration);
		base.UseWaitCursor = true;
		_status.Text = "正在读取 of_card_asset…";
		try
		{
			object progressGate = new();
			int lastReported = -250;
			long nextReportAt = 0;
			Action<int, int> progress = delegate(int done, int total)
			{
				if (cancellationToken.IsCancellationRequested || generation != Volatile.Read(ref _loadGeneration))
				{
					return;
				}
				long now = Environment.TickCount64;
				lock (progressGate)
				{
					if (done != total && (done - lastReported < 250 || now < nextReportAt))
					{
						return;
					}
					lastReported = done;
					nextReportAt = now + 120;
				}
				if (IsDisposed || !IsHandleCreated)
				{
					return;
				}
				try
				{
					BeginInvoke((MethodInvoker)delegate
					{
						if (!IsDisposed)
						{
							_status.Text = $"首次定位超框表：{done:N0}/{total:N0} Bundle…";
						}
					});
				}
				catch (InvalidOperationException)
				{
					// The embedded page can be closed while a background scan reports its
					// final batch. The next page owns the status surface at that point.
				}
			};
			var snapshot = await Task.Run(() =>
			{
				TextAssetRef gate = _service.FindGate(_gameRoot, progress, cancellationToken);
				return (Gate: gate, Mappings: _service.Read(gate), HasBackup: _service.HasBackup(_gameRoot, gate));
			}, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			if (generation != Volatile.Read(ref _loadGeneration) || IsDisposed)
			{
				return;
			}
			TextAssetRef gate = snapshot.Gate;
			List<OverFrameMapping> mappings = snapshot.Mappings;
			_mappings.SetMappings(mappings);
			Label status = _status;
			object arg = mappings.Count;
			object relativeBundlePath = gate.RelativeBundlePath;
			status.Text = string.Format("已读取 {0} 条超框映射  ·  {1}  ·  {2}", arg, relativeBundlePath, snapshot.HasBackup ? "已有备份" : "首次写入时自动备份");
		}
		catch (OperationCanceledException)
		{
			if (!IsDisposed && generation == Volatile.Read(ref _loadGeneration))
			{
				_status.Text = "读取已取消。";
			}
		}
		catch (Exception ex)
		{
			if (!IsDisposed && generation == Volatile.Read(ref _loadGeneration))
			{
				_status.Text = "读取失败：" + ex.Message;
			}
		}
		finally
		{
			if (!IsDisposed && generation == Volatile.Read(ref _loadGeneration))
			{
				base.UseWaitCursor = false;
			}
			Interlocked.CompareExchange(ref _loadCancellation, null, source);
			source.Dispose();
		}
	}

	private bool TryIds(out ushort card, out ushort art)
	{
		if (!ushort.TryParse(_cardId.Text.Trim(), out card))
		{
			MessageBox.Show(this, "显示卡号必须是 0–65535 的整数。", Text);
			art = 0;
			return false;
		}
		if (_artId.Text.Trim().Length == 0)
		{
			_artId.Text = card.ToString();
		}
		if (!ushort.TryParse(_artId.Text.Trim(), out art))
		{
			MessageBox.Show(this, "高图卡号必须是 0–65535 的整数。", Text);
			return false;
		}
		return true;
	}

	private async Task EnableAsync()
	{
		if (!TryIds(out var card, out var art) || MessageBox.Show(this, $"启用超框：{card} → {art}\n\n将修改 of_card_asset 并自动创建一次备份。卡图本身必须是 704×1024。继续？", "确认超框", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			await Task.Run(delegate
			{
				_service.EnableOrUpdate(_gameRoot, card, art);
			});
			await LoadAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "超框写入失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private async Task DisableAsync()
	{
		if (!TryIds(out var card, out var _) || MessageBox.Show(this, $"关闭卡号 {card} 的超框登记？不会还原已经替换的高图。", "确认关闭", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			await Task.Run(delegate
			{
				_service.Disable(_gameRoot, card);
			});
			await LoadAsync();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			base.UseWaitCursor = false;
		}
	}

	private async Task RestoreAsync()
	{
		if (MessageBox.Show(this, "将恢复本工具第一次修改前备份的完整超框表，并覆盖当前超框登记。继续？", "确认还原超框表", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
		{
			return;
		}
		try
		{
			base.UseWaitCursor = true;
			await Task.Run(delegate
			{
				_service.RestoreBackup(_gameRoot);
			});
			await LoadAsync();
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
}
