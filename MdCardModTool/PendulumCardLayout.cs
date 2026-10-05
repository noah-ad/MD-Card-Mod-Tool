using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;

namespace MdCardModTool;

/// <summary>
/// Layout of the game's CardPicture prefab, expressed in a 704 x 1024 frame.
/// The pendulum RawImage samples the entire tall texture; the card frame hides
/// the lower part. The visible illustration must NOT be stretched to that whole
/// RawImage. Constants were read from the PC prefab, not inferred from a preview.
/// </summary>
internal static class PendulumCardLayout
{
    private static readonly ConcurrentDictionary<string, RectangleF> ArtWindows = new(StringComparer.OrdinalIgnoreCase);

    // CardPicture / Cardframe: HeightControlsWidth, aspect 590 / 860, height 1054.
    // Illust_pendulum: size (610, 782) * 1.02, anchoredPosition (0, -62.22).
    // RawImage.uvRect = (0, 0, 1, 1); its shader samples these UVs unchanged.
    internal static RectangleF TextureRectangle
    {
        get
        {
            const double cardHeight = 1054;
            const double cardWidth = cardHeight * 590 / 860;
            double width = 622.2 * 704 / cardWidth;
            double height = 797.64 * 1024 / cardHeight;
            double top = 512 + (62.22 - 797.64 / 2) * 1024 / cardHeight;
            return new RectangleF((float)((704 - width) / 2), (float)top, (float)width, (float)height);
        }
    }

    internal static RectangleF ArtWindow(string? frameKey)
    {
        string key = CardFrameCatalog.BaseKey(frameKey ?? "");
        if (!CardFrameCatalog.IsPendulum(key)) key = "card_frame14";
        return ArtWindows.GetOrAdd(key, name =>
        {
            TexRef frame = BuiltInCardFrameCatalog.Load().FirstOrDefault(x => x.Name == name)
                ?? throw new FileNotFoundException("缺少普通灵摆卡框模板，无法确定实装插图区。请保留完整 data 文件夹。");
            using Bitmap bitmap = FrameComposer.BitmapFrom(File.ReadAllBytes(frame.ActiveBundlePath));
            return CardFrameRenderer.FindArtWindow(bitmap);
        });
    }

    internal static RectangleF StorageWindow(RectangleF artWindow, int width, int height)
    {
        RectangleF quad = TextureRectangle;
        return new RectangleF(
            (artWindow.Left - quad.Left) * width / quad.Width,
            (artWindow.Top - quad.Top) * height / quad.Height,
            artWindow.Width * width / quad.Width,
            artWindow.Height * height / quad.Height);
    }

    internal static byte[] RenderEditorToStorage(string path, ImageRenderSpec spec, RectangleF artWindow, int width, int height)
    {
        if (artWindow.IsEmpty) throw new InvalidDataException("灵摆构图必须先载入卡框，不能以整张纹理比例写入。");
        RectangleF quad = TextureRectangle;
        // Convert the editor's visible-window origin to the full RawImage origin.
        // This preserves a single image scale before the game's non-square UV map.
        var fullCanvas = new ImageRenderSpec(quad.Width, quad.Height, spec.ImageScale,
            artWindow.Left + artWindow.Width / 2 + spec.OffsetX - (quad.Left + quad.Width / 2),
            artWindow.Top + artWindow.Height / 2 + spec.OffsetY - (quad.Top + quad.Height / 2));
        return ImageCropService.RenderToTarget(path, fullCanvas, width, height);
    }
}
