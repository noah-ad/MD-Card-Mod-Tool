using System.Drawing.Imaging;

namespace MdCardModTool;

/// <summary>按游戏超框层级合成：卡框在下，带透明通道的 704×1024 原画在上。</summary>
public static class FrameComposer
{
    public const int Width = 704;
    public const int Height = 1024;

    public static byte[] Compose(byte[] artPng, byte[] framePng)
    {
        using var art = BitmapFrom(artPng);
        using var frame = BitmapFrom(framePng);
        Validate(art, "透明高图");
        Validate(frame, "卡框");

        using var output = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(frame, 0, 0);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            graphics.DrawImageUnscaled(art, 0, 0);
        }

        using var stream = new MemoryStream();
        output.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public static Bitmap BitmapFrom(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    /// <summary>游戏在超框高图下还有一层白色卡片底板；本地预览叠上同色底板，避免低 Alpha 遮罩显示成黑块。</summary>
    public static Bitmap PreviewBitmap(byte[] data)
    {
        using var source = BitmapFrom(data);
        var output = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.White);
        graphics.DrawImageUnscaled(source, 0, 0);
        return output;
    }

    static void Validate(Image image, string label)
    {
        if (image.Width != Width || image.Height != Height)
            throw new InvalidDataException($"{label}必须严格为 {Width}×{Height}；当前为 {image.Width}×{image.Height}。");
    }
}
