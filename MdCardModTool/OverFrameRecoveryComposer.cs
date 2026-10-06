using System;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class OverFrameRecoveryComposer
{
	internal static byte[] Compose(string gameRoot, ushort cardId)
	{
		if (!OverFrameArtStore.HasSettings(gameRoot, cardId)) throw new InvalidDataException("缺少超框构图设置：" + cardId);
		var settings = OverFrameArtStore.ReadSettings(gameRoot, cardId);
		if (!settings.UserSelected) throw new InvalidDataException("没有保存已选择的卡框：" + cardId);
		byte[] art = File.ReadAllBytes(OverFrameArtStore.ArtPath(gameRoot, cardId));
		string baseKey = CardFrameCatalog.BaseKey(settings.FrameKey);
		byte[]? background = null;
		string backdrop = OverFrameArtStore.BackgroundPath(gameRoot, cardId);
		if (File.Exists(backdrop)) background = settings.BackgroundImageScale > 0
			? ImageCropService.RenderToTarget(backdrop, new ImageRenderSpec(704, 1024, settings.BackgroundImageScale, settings.BackgroundOffsetX, settings.BackgroundOffsetY), 704, 1024)
			: ImageCropService.RenderCoverToTarget(backdrop, 704, 1024);
		var engine = new ModEngine();
		byte[] frame = settings.UsesCustomFrame ? File.ReadAllBytes(OverFrameArtStore.CustomFramePath(gameRoot, cardId))
			: engine.DecodePng(BuiltInCardFrameCatalog.Load().FirstOrDefault(x => x.Name == settings.FrameKey)
				?? throw new InvalidDataException("无法找到已保存卡框：" + settings.FrameKey));
		byte[] png = settings.CompositionMode switch
		{
			"AstellarTransparent" when !settings.UsesCustomFrame => AstellarOverFrameComposer.Compose(art, AstellarOverFrameTemplateCatalog.Load(baseKey), background).GamePng,
			"TransparentGradient" when !settings.UsesCustomFrame => AstellarOverFrameComposer.ComposeTransparentFlatFrame(art, frame, AstellarOverFrameTemplateCatalog.Load(baseKey), background).GamePng,
			"FloowanGradient" or "StandardComplete" => AstellarOverFrameComposer.ComposeFlatFrame(art, frame, background),
			_ => throw new InvalidDataException("无法安全重建旧超框合成模式：" + settings.CompositionMode)
		};
		if (settings.RemoveFoilInnerFrame)
		{
			if (settings.UsesCustomFrame) baseKey = CardFrameCatalog.RecommendedKey(CardCatalogService.LoadBestAvailable().Find(cardId), 704, 1024);
			png = AstellarOverFrameComposer.RemoveFoilInnerFrame(png, AstellarOverFrameTemplateCatalog.Load(baseKey));
		}
		return png;
	}
}
