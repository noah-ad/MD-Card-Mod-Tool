using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdCardModTool;

public sealed partial class MainForm
{
	private async Task<(TexRef Texture, byte[] Png)?> ResolveCatalogArtworkAsync(TexRef pending, int generation, CancellationToken token)
	{
		string? game = _gameRoot, account = _assetRoot;
		GameIndex? index = _index;
		if (game == null || account == null || index == null) return null;
		string streaming = IndexService.StreamingRoot(game);
		var result = await Task.Run<(TexRef Texture, byte[] Png)?>(() =>
		{
			foreach (string root in new[] { account, streaming })
			{
				foreach (string path in IndexService.CardIllustrationBundleCandidates(root, pending.CardKey))
				{
					token.ThrowIfCancellationRequested();
					string kind = root == account ? "本地卡图" : "游戏内图片";
					TexRef? texture = _engine.ListTextureHeaders(path, root, kind, token).FirstOrDefault(t => t.CardKey == pending.CardKey && CardTextureRules.HasCardIdentity(t));
					if (texture == null) continue;
					byte[] png = _engine.DecodePng(texture, cancellationToken: token);
					return (texture, png);
				}
			}
			return null;
		}, token);
		token.ThrowIfCancellationRequested();
		if (result == null || generation != _previewGeneration || Selected() != pending || !IsSameWorkspace(game, account, index)) return null;
		TexRef found = result.Value.Texture;
		index.Textures.RemoveAll(t => t.BundlePath.Equals(found.BundlePath, StringComparison.OrdinalIgnoreCase) && t.CardKey == found.CardKey);
		_textures.RemoveAll(t => t.BundlePath.Equals(found.BundlePath, StringComparison.OrdinalIgnoreCase) && t.CardKey == found.CardKey);
		index.Textures.Add(found);
		_textures.Add(found);
		int row = _visibleTextures.IndexOf(pending);
		if (row >= 0) _visibleTextures[row] = found;
		await Task.Run(() =>
		{
			if (_backgroundDataDirectory == AppSettingsStore.AppDataRoot) IndexService.Save(game, index);
			else ModRecoveryStore.AtomicWrite(Path.Combine(_backgroundDataDirectory, "index.json"), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index));
		}, token);
		if (row >= 0 && _list.IsHandleCreated) _list.RedrawItems(row, row, true);
		SyncResourceGallery();
		return result;
	}

	private void UpdateResourceActionAvailability(TexRef? texture)
	{
		foreach (Button action in _resourceActionGrid.Controls.OfType<Button>())
		{
			if (action == _scanMissingButton) action.Enabled = _gameRoot != null;
			else action.Enabled = texture != null && !texture.IsCatalogOnly;
		}
	}
}
