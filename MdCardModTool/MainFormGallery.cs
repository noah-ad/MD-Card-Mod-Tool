using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public sealed partial class MainForm
{
	private void SyncResourceGallery()
	{
		if (_resourceGallery == null) return;
		_resourceGallery.SetItems(_visibleTextures);
		int selected = _list.SelectedIndices.Count == 1 ? _list.SelectedIndices[0] : -1;
		_resourceGallery.SetSelectedIndex(selected);
		bool showGallery = !_modsOnly && (_currentPage == WorkspacePage.Cards || _currentPage == WorkspacePage.Visuals);
		_resourceGallery.Visible = showGallery;
		_list.Visible = !showGallery;
		if (showGallery) _resourceGallery.BringToFront();
		else _list.BringToFront();
	}

	private async Task<Bitmap?> LoadGalleryImageAsync(TexRef texture, CancellationToken token)
	{
		if (texture.IsCatalogOnly || string.IsNullOrWhiteSpace(texture.BundlePath) || !File.Exists(texture.ActiveBundlePath)) return null;
		try
		{
			byte[] data = await Task.Run(() => _engine.DecodePng(texture, maxSize: 360, cancellationToken: token), token);
			token.ThrowIfCancellationRequested();
			GameTextureDisplayMapping mapping = DisplayMappingFor(texture);
			if (mapping.RequiresMapping)
				data = await Task.Run(() => mapping.DecodeForDisplay(data), token);
			token.ThrowIfCancellationRequested();
			return CardPreviewRenderer.RenderRaw(data);
		}
		catch (OperationCanceledException) { throw; }
		catch { return null; }
	}

	private async Task LoadGameUiThemeAsync(string gameRoot)
	{
		GameUiHeaderPanel? banner = _gameUiBanner;
		if (banner == null || !Directory.Exists(gameRoot)) return;
		try
		{
			string dataPath = Path.Combine(gameRoot, "masterduel_Data", "data.unity3d");
			if (!File.Exists(dataPath)) return;
			var textures = await Task.Run(() => _engine.ListTextureHeaders(dataPath, gameRoot, "游戏基础视觉资源"));
			TexRef? background = textures.Find(x => x.Name.Equals("GUI_TitleBG", StringComparison.OrdinalIgnoreCase))
				?? textures.Find(x => x.Name.Equals("ShopBGBase02", StringComparison.OrdinalIgnoreCase));
			TexRef? logo = textures.Find(x => x.Name.Equals("GUI_TitleLogo", StringComparison.OrdinalIgnoreCase))
				?? textures.Find(x => x.Name.Equals("TitleLogo01", StringComparison.OrdinalIgnoreCase));
			if (background == null && logo == null) return;
			Bitmap? bg = background == null ? null : await Task.Run(() => CardPreviewRenderer.RenderRaw(_engine.DecodePng(background, maxSize: 1920)), CancellationToken.None);
			Bitmap? mark = logo == null ? null : await Task.Run(() => CardPreviewRenderer.RenderRaw(_engine.DecodePng(logo, maxSize: 900)), CancellationToken.None);
			if (IsDisposed || banner.IsDisposed || !string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
			{
				bg?.Dispose(); mark?.Dispose(); return;
			}
			banner.BackgroundImageFromGame = bg;
			banner.LogoImageFromGame = mark;
		}
		catch
		{
			// Missing or partially downloaded UI bundles keep the deterministic
			// dark fallback; the card workspace remains usable.
		}
	}
}
