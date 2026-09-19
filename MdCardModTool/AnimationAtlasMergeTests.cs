using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class AnimationAtlasMergeTests
{
    internal static void Run()
    {
        byte[] Png(Rgba32 color) { using var image = new Image<Rgba32>(8, 8, color); using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray(); }
        byte[] a = Png(new Rgba32(99, 77, 55, 0)), b = Png(new Rgba32(20, 30, 40, 80));
        string atlas = "a.png\nsize:8,8\npma:true\nfilter:Linear,Linear\nfirst\nbounds:1,2,3,4\nrotate:90\noffsets:2,3,9,10\n\nb.png\nsize:8,8\npma:true\nfilter:Linear,Linear\nsecond\nxy:2,1\nsize:3,4\nrotate:true\noffset:1,2\norig:8,9\n";
        var result = AnimationAtlasMerge.Merge(atlas, "target", name => name == "a.png" ? a : b);
        using (result.Image)
        {
            Require(result.Image.Width == 8 && result.Image.Height == 16, "layout");
            Require(result.Image[0, 0].Equals(new Rgba32(99, 77, 55, 0)), "hidden RGB");
            Require(result.Image[0, 8].Equals(new Rgba32(20, 30, 40, 80)), "PMA bytes");
            Require(result.Atlas.Contains("xy:2,9") && result.Atlas.Contains("bounds:1,2,3,4") && result.Atlas.Contains("offsets:2,3,9,10") && result.Atlas.Contains("rotate:true"), "positions and trim/rotation");
        }
        Reject(() => AnimationAtlasMerge.Merge(atlas.Replace("b.png\nsize:8,8\npma:true", "b.png\nsize:8,8\npma:false"), "target", _ => a), "mixed PMA");
        Reject(() => AnimationAtlasMerge.Merge(atlas.Replace("size:8,8", "size:8192,8192"), "target", _ => a), "oversize");
        Reject(() => AnimationAtlasMerge.Merge(atlas.Replace("second", "first"), "target", _ => a), "duplicate region");
        Reject(() => AnimationAtlasMerge.Merge(atlas.Replace("size:8,8", "size:7,7"), "target", _ => a), "dimension mismatch");
        Console.WriteLine("PASS multiPageMerge; modern/legacy coordinates; rotation/trim; hiddenRGB/PMA; unsafe inputs rejected");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Func<(Image<Rgba32> Image, string Atlas)> call, string message)
    {
        try { var result = call(); result.Image.Dispose(); } catch (InvalidDataException) { return; }
        throw new Exception("Not rejected: " + message);
    }
}
