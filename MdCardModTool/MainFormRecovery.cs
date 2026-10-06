using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed partial class MainForm
{
	private readonly ModRecoveryStore _recoveryStore = new();
	private Button? _recoverModsButton;
	private Button? _snapshotModsButton;
	private bool _recoveringMods;
	private readonly System.Windows.Forms.Timer _gameResourcePoll = new() { Interval = 60000 };
	private string _lastCheckedResourceSignature = "";
	private bool _checkingGameResources;

	private void ConfigureRecoveryControls()
	{
		_recoverModsButton = Bind(Button("", async delegate { await RecoverModsAsync(); }, ButtonTone.Primary), "mods.recovery.action");
		_snapshotModsButton = Bind(Button("", async delegate { await SnapshotCurrentModsAsync(); }), "mods.recovery.snapshot");
		_gameResourcePoll.Tick += async delegate { await CheckGameResourceChangesAsync(); };
		if (_allowBackgroundRefresh) _gameResourcePoll.Start();
		FormClosed += delegate { _gameResourcePoll.Stop(); _gameResourcePoll.Dispose(); };
	}

	private async Task SaveRecoveryTextureAsync(string gameRoot, TexRef texture, byte[] png, ushort? artId = null)
	{
		try { await Task.Run(() => _recoveryStore.RecordTexture(gameRoot, texture, png, artId)); }
		catch (Exception ex)
		{
			MessageBox.Show(this, Localizer.F("mods.recovery.saveWarning", ex.Message), Localizer.T("mods.recovery.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	private async Task SnapshotCurrentModsAsync()
	{
		if (_recoveringMods || _gameRoot == null) return;
		string root = _gameRoot;
		TexRef[] textures = _textures.Where(x => x.IsModded && !BuiltInCardFrameCatalog.IsPackagedFrame(x)).ToArray();
		if (textures.Length == 0) { MessageBox.Show(this, Localizer.T("mods.recovery.snapshotEmpty"), Text); return; }
		if (MessageBox.Show(this, Localizer.F("mods.recovery.snapshotConfirm", textures.Length), Localizer.T("mods.recovery.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
		await RunRecoveryWorkAsync(async progress =>
		{
			var errors = new System.Collections.Generic.List<string>();
			int saved = await Task.Run(() =>
			{
				var mappings = _overFrames.ReadCached(root).GroupBy(x => x.CardId).ToDictionary(x => x.Key, x => x.Last().ArtId);
				var changedPaths = _mods.ChangedTextureBundlePaths(root, textures);
				var imageMods = textures.Where(x => changedPaths.Contains(Path.GetFullPath(x.BundlePath))).ToArray();
				int done = 0;
				foreach (var texture in imageMods)
				{
					try
					{
						progress.Token.ThrowIfCancellationRequested();
						ushort? art = ushort.TryParse(texture.CardKey, out ushort card) && mappings.TryGetValue(card, out ushort mapped) ? mapped : null;
						_recoveryStore.RecordTexture(root, texture, _engine.DecodePng(texture), art);
						done++;
					}
					catch (OperationCanceledException) { throw; }
					catch (Exception ex) { errors.Add(texture.Name + ": " + ex.Message); }
					progress.Report(done + errors.Count, imageMods.Length, texture.Name);
				}
				return done;
			}, progress.Token);
			MessageBox.Show(this, Localizer.F("mods.recovery.snapshotDone", saved) + (errors.Count > 0 ? "\n\n" + string.Join("\n", errors.Take(12)) : ""),
				Localizer.T("mods.recovery.title"), MessageBoxButtons.OK, errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
		});
	}

	private async Task RecoverModsAsync()
	{
		if (_recoveringMods || _gameRoot == null) return;
		string root = _gameRoot;
		try
		{
			var service = new ModRecoveryService(_recoveryStore);
			ModRecoveryPlan plan = service.Plan(root, _textures.ToArray());
			if (plan.Count == 0) { MessageBox.Show(this, Localizer.T("mods.recovery.empty"), Localizer.T("mods.recovery.title")); return; }
			string legacy = plan.LegacyCards.Count == 0 ? "" : "\n\n" + Localizer.F("mods.recovery.legacy", string.Join(", ", plan.LegacyCards));
			if (MessageBox.Show(this, Localizer.F("mods.recovery.confirm", plan.Saved.Count, plan.LegacyCards.Count, Path.GetFileName(Path.GetDirectoryName(IndexService.FindLocalRoot(root)))) + legacy,
				Localizer.T("mods.recovery.title"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
			await RunRecoveryWorkAsync(async progress =>
			{
				ModRecoveryResult result = await Task.Run(() => service.Reapply(root, plan, progress.Report, progress.Token), progress.Token);
				await ReloadChangedBundlesAsync(result.ChangedPaths);
				await ApplyOverFrameTagsAsync();
				await RefreshModFlagsAsync();
				await Task.Run(() => IndexService.Save(root, _textures));
				RefreshCategories();
				RenderList();
				string text = Localizer.F("mods.recovery.done", result.Reapplied, result.AlreadyApplied, result.Skipped.Count);
				if (result.ChangedPaths.Count > 0) text += "\n\n" + Localizer.F("mods.recovery.history", result.HistoryFolder);
				if (result.Skipped.Count > 0)
				{
					string report = Path.Combine(_recoveryStore.Folder(root), "last-recovery-report.txt");
					ModRecoveryStore.AtomicWrite(report, System.Text.Encoding.UTF8.GetBytes(text + "\n\n" + string.Join("\n", result.Skipped)));
					text += "\n\n" + string.Join("\n", result.Skipped.Take(12)) + "\n\n" + report;
				}
				_status.Text = Localizer.F("mods.recovery.done", result.Reapplied, result.AlreadyApplied, result.Skipped.Count);
				MessageBox.Show(this, text, Localizer.T("mods.recovery.title"), MessageBoxButtons.OK, result.Skipped.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
			});
		}
		catch (Exception ex) { MessageBox.Show(this, ex.Message, Localizer.T("mods.recovery.title"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
	}

	private async Task RunRecoveryWorkAsync(Func<ModRecoveryProgressForm, Task> work)
	{
		_recoveringMods = true;
		_backgroundRefreshCancellation?.Cancel();
		Enabled = false;
		try
		{
			if (_backgroundRefreshTask != null) await _backgroundRefreshTask;
			using var progress = new ModRecoveryProgressForm();
			progress.Show(this);
			try { await work(progress); }
			finally { progress.Close(); }
		}
		catch (OperationCanceledException) { _status.Text = Localizer.T("mods.recovery.cancelled"); }
		catch (Exception ex) { MessageBox.Show(this, ex.Message, Localizer.T("mods.recovery.title"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
		finally
		{
			_recoveringMods = false;
			if (!IsDisposed) { Enabled = true; UpdateSelectedModAction(); }
		}
	}

	// Resource downloads can happen without a Steam build change. Check metadata in
	// the background, coalesce refreshes, and never write a Mod automatically.
	private async Task CheckGameResourceChangesAsync()
	{
		if (_checkingGameResources || _recoveringMods || !Enabled || UseWaitCursor || PausesBackgroundRefresh(_currentPage) || !_allowBackgroundRefresh || _gameRoot == null || _index == null
			|| (_backgroundRefreshTask != null && !_backgroundRefreshTask.IsCompleted) || _overFrameManager is { IsDisposed: false }) return;
		_checkingGameResources = true;
		string root = _gameRoot;
		string local = IndexService.FindLocalRoot(root) ?? "";
		var index = _index;
		try
		{
			string signature = await Task.Run(() => PortableIndexService.GetGameBuildId(root) + ":" + GameCardCatalogUpdater.CaptureResourceFingerprint(local, IndexService.StreamingRoot(root)));
			if (!IsSameWorkspace(root, local, index)) return;
			if (signature != _lastCheckedResourceSignature)
			{
				_lastCheckedResourceSignature = signature;
				StartGameBuildRefresh();
			}
		}
		catch (IOException) { } // A download in progress is retried on the next tick.
		catch (UnauthorizedAccessException) { }
		finally { _checkingGameResources = false; }
	}
}

internal sealed class ModRecoveryProgressForm : Form
{
	private readonly Label _message = new() { Dock = DockStyle.Top, Height = 76, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
	private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Marquee };
	private readonly Button _cancel = UiTheme.Button("", delegate { }, ButtonTone.Danger);
	private readonly CancellationTokenSource _cancellation = new();
	private bool _committing;
	internal CancellationToken Token => _cancellation.Token;
	internal ModRecoveryProgressForm()
	{
		Text = Localizer.T("mods.recovery.title");
		BackColor = UiTheme.Window;
		AutoScaleDimensions = new SizeF(96, 96);
		AutoScaleMode = AutoScaleMode.Dpi;
		Font = new Font("Microsoft YaHei UI", 9);
		StartPosition = FormStartPosition.CenterParent;
		ClientSize = new Size(500, 190);
		Padding = new Padding(20);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = MinimizeBox = false;
		ControlBox = false;
		_cancel.Dock = DockStyle.Bottom;
		_cancel.Height = 38;
		_cancel.AutoSize = false;
		UiTheme.ApplyDarkTitleBar(this);
		_cancel.Text = Localizer.T("mods.recovery.cancel");
		_cancel.Click += delegate { if (!_committing) { _cancellation.Cancel(); _cancel.Enabled = false; } };
		Controls.Add(_bar); Controls.Add(_message); Controls.Add(_cancel);
		FormClosed += delegate { _cancellation.Dispose(); };
	}
	internal void Report(int done, int total, string detail)
	{
		if (IsDisposed || !IsHandleCreated) return;
		try
		{
			BeginInvoke((Action)(() =>
			{
				if (IsDisposed) return;
				_committing |= detail == "commit";
				_cancel.Enabled = !_committing && !_cancellation.IsCancellationRequested;
				_bar.Style = ProgressBarStyle.Continuous;
				_bar.Value = total <= 0 ? 0 : Math.Clamp((int)((long)done * 100 / total), 0, 100);
				_message.Text = Localizer.F(_committing ? "mods.recovery.committing" : "mods.recovery.progress", done, total, detail);
			}));
		}
		catch (InvalidOperationException) { } // Closing cancels queued progress safely.
	}
}
