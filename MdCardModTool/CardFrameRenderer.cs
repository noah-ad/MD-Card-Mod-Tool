using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MdCardModTool;

/// <summary>把 Bundle 内的存储纹理按游戏卡框插图区拉伸显示，用于正常视觉比例预览。</summary>
public static class CardFrameRenderer
{
    public static RectangleF FindArtWindow(Bitmap frame)
    {
        if (frame.Width != FrameComposer.Width || frame.Height != FrameComposer.Height)
            throw new InvalidDataException($"卡框必须为 {FrameComposer.Width}×{FrameComposer.Height}。当前为 {frame.Width}×{frame.Height}。");

        using var argb = frame.PixelFormat == PixelFormat.Format32bppArgb ? null : new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var source = argb ?? frame;
        if (argb is not null) using (var graphics = Graphics.FromImage(argb)) graphics.DrawImageUnscaled(frame, 0, 0);

        var data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * source.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
            for (var y = 0; y < source.Height; y++)
            {
                var row = data.Stride >= 0 ? y * stride : (source.Height - 1 - y) * stride;
                for (var x = 0; x < source.Width; x++)
                {
                    if (bytes[row + x * 4 + 3] != 0) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            }
            if (right < left || bottom < top) throw new InvalidDataException("卡框中没有找到透明插图区。");
            return RectangleF.FromLTRB(left, top, right + 1, bottom + 1);
        }
        finally { source.UnlockBits(data); }
    }

    public static byte[] ComposeStoredArtPreview(byte[] storedArtPng, byte[] framePng)
    {
        using var art = FrameComposer.BitmapFrom(storedArtPng);
        using var frame = FrameComposer.BitmapFrom(framePng);
        using var output = ComposeStoredArtPreview(art, frame);
        using var stream = new MemoryStream(); output.Save(stream, ImageFormat.Png); return stream.ToArray();
    }

    public static Bitmap ComposeStoredArtPreview(Bitmap storedArt, Bitmap frame)
    {
        var artWindow = FindArtWindow(frame);
        var output = new Bitmap(FrameComposer.Width, FrameComposer.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        Configure(graphics);
        graphics.Clear(Color.White);
        graphics.DrawImage(storedArt, artWindow);
        graphics.DrawImageUnscaled(frame, 0, 0);
        return output;
    }

    internal static void Configure(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }
}
