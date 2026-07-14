namespace MdCardModTool;

/// <summary>可视化编辑 of_card_asset：显示卡号 → 应使用的高图卡号。</summary>
public sealed class OverFrameForm : Form
{
    readonly string _gameRoot;
    readonly OverFrameService _service = new();
    readonly TextBox _cardId = new() { Dock = DockStyle.Fill, PlaceholderText = "例如 20570" };
    readonly TextBox _artId = new() { Dock = DockStyle.Fill, PlaceholderText = "默认与显示卡号相同" };
    readonly ListView _mappings = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 7, 12, 7), ForeColor = Color.Gainsboro };

    public OverFrameForm(string gameRoot, string? selectedCardId)
    {
        UiTheme.ApplyDarkTitleBar(this);
        _gameRoot = gameRoot;
        Text = "超框模式 · of_card_asset"; StartPosition = FormStartPosition.CenterParent; Size = new Size(850, 620); MinimumSize = new Size(700, 500);
        BackColor = Color.FromArgb(15, 22, 35); ForeColor = Color.White; Font = new Font("Microsoft YaHei UI", 9F);
        _cardId.Text = selectedCardId ?? ""; _artId.Text = selectedCardId ?? "";
        _mappings.Columns.Add("显示卡号", 135); _mappings.Columns.Add("高图卡号", 135); _mappings.Columns.Add("模式", 130);
        _mappings.SelectedIndexChanged += (_, _) => { if (_mappings.SelectedItems.Count != 1 || _mappings.SelectedItems[0].Tag is not OverFrameMapping x) return; _cardId.Text = x.CardId.ToString(); _artId.Text = x.ArtId.ToString(); };
        var enable = Button("启用／更新", async (_, _) => await EnableAsync(), true);
        var disable = Button("关闭所选", async (_, _) => await DisableAsync());
        var refresh = Button("刷新列表", async (_, _) => await LoadAsync());
        var restore = Button("还原超框表", async (_, _) => await RestoreAsync());
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 105, Padding = new Padding(12, 10, 12, 8), ColumnCount = 5, RowCount = 3, BackColor = Color.FromArgb(20, 28, 45) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(Label("显示卡号"), 0, 0); top.Controls.Add(_cardId, 1, 0); top.Controls.Add(Label("高图卡号"), 2, 0); top.Controls.Add(_artId, 3, 0); top.Controls.Add(enable, 4, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; actions.Controls.Add(disable); actions.Controls.Add(refresh); actions.Controls.Add(restore); top.Controls.Add(actions, 0, 1); top.SetColumnSpan(actions, 5);
        var help = new Label { Text = "超框登记是：显示卡号 → 高图卡号。普通替换时两者填同一个卡号。实际高图须为 704×1024；修改会备份 LocalData 中真正的 of_card_asset，完成后请重启游戏。", AutoSize = true, ForeColor = Color.FromArgb(160, 195, 255) };
        top.Controls.Add(help, 0, 2); top.SetColumnSpan(help, 5);
        Controls.Add(_mappings); Controls.Add(_status); Controls.Add(top);
        Shown += async (_, _) => await LoadAsync();
    }

    static Label Label(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.Gainsboro, Padding = new Padding(0, 5, 4, 0) };
    static Button Button(string text, EventHandler click, bool accent = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(4, 1, 0, 1), FlatStyle = FlatStyle.Flat, BackColor = accent ? Color.FromArgb(46, 102, 204) : Color.FromArgb(36, 50, 76), ForeColor = Color.White, Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = accent ? Color.FromArgb(93, 148, 255) : Color.FromArgb(71, 91, 128); b.Click += click; return b;
    }

    async Task LoadAsync()
    {
        UseWaitCursor = true; _status.Text = "正在读取 of_card_asset…";
        try
        {
            var progress = new Action<int, int>((done, total) => BeginInvoke(() => _status.Text = $"首次定位超框表：{done:N0}/{total:N0} Bundle…"));
            var gate = await Task.Run(() => _service.FindGate(_gameRoot, progress));
            var mappings = await Task.Run(() => _service.Read(_gameRoot));
            _mappings.BeginUpdate(); _mappings.Items.Clear();
            foreach (var x in mappings) _mappings.Items.Add(new ListViewItem([x.CardId.ToString(), x.ArtId.ToString(), x.UsesOwnArt ? "使用本卡高图" : "复用其他卡高图"]) { Tag = x });
            _mappings.EndUpdate();
            _status.Text = $"已读取 {mappings.Count} 条超框映射  ·  {gate.RelativeBundlePath}  ·  {(await Task.Run(() => _service.HasBackup(_gameRoot)) ? "已有备份" : "首次写入时自动备份")}";
        }
        catch (Exception ex) { _status.Text = "读取失败：" + ex.Message; }
        finally { UseWaitCursor = false; }
    }

    bool TryIds(out ushort card, out ushort art)
    {
        if (!ushort.TryParse(_cardId.Text.Trim(), out card)) { MessageBox.Show(this, "显示卡号必须是 0–65535 的整数。", Text); art = 0; return false; }
        if (_artId.Text.Trim().Length == 0) _artId.Text = card.ToString();
        if (!ushort.TryParse(_artId.Text.Trim(), out art)) { MessageBox.Show(this, "高图卡号必须是 0–65535 的整数。", Text); return false; }
        return true;
    }

    async Task EnableAsync()
    {
        if (!TryIds(out var card, out var art)) return;
        if (MessageBox.Show(this, $"启用超框：{card} → {art}\n\n将修改 of_card_asset 并自动创建一次备份。卡图本身必须是 704×1024。继续？", "确认超框", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { UseWaitCursor = true; await Task.Run(() => _service.EnableOrUpdate(_gameRoot, card, art)); await LoadAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "超框写入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    async Task DisableAsync()
    {
        if (!TryIds(out var card, out _)) return;
        if (MessageBox.Show(this, $"关闭卡号 {card} 的超框登记？不会还原已经替换的高图。", "确认关闭", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { UseWaitCursor = true; await Task.Run(() => _service.Disable(_gameRoot, card)); await LoadAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "关闭失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }

    async Task RestoreAsync()
    {
        if (MessageBox.Show(this, "将恢复本工具第一次修改前备份的完整超框表，并覆盖当前超框登记。继续？", "确认还原超框表", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        try { UseWaitCursor = true; await Task.Run(() => _service.RestoreBackup(_gameRoot)); await LoadAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { UseWaitCursor = false; }
    }
}
