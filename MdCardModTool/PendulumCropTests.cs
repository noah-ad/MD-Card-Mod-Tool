using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;

namespace MdCardModTool;

internal static class PendulumCropTests
{
    internal static void Run(string gameRoot, string output)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        output = Path.GetFullPath(output);
        if (StandaloneResourceService.IsInside(output, gameRoot)) throw new ArgumentException("Test output must be outside the game.");
        Directory.CreateDirectory(output);
        var engine = new ModEngine();
        string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("LocalData is required.");
        var source = IndexService.CardIllustrationBundleCandidates(local, "19716")
            .SelectMany(x => engine.ScanBundle(x, local, "本地卡图", false).Textures)
            .Single(x => x.CardKey == "19716" && x.Width == 512 && x.Height == 1024);
        string originalHash = Hash(source.BundlePath);
        string mirror = Path.Combine(output, "mirror-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(mirror, "LocalData", "test", "0000");
        string copiedBundle = Path.Combine(root, source.RelativeBundlePath);
        Directory.CreateDirectory(Path.GetDirectoryName(copiedBundle)!);
        File.Copy(source.BundlePath, copiedBundle);
        var texture = engine.ScanBundle(copiedBundle, root, "本地卡图", false).Textures.Single(x => x.PathId == source.PathId);
        byte[] original = engine.DecodePng(texture);
        File.WriteAllBytes(Path.Combine(output, "19716-original-storage.png"), original);
        File.WriteAllBytes(Path.Combine(output, "19716-original-display.png"), GameTextureDisplayMapping.ResolveForInstalledPreview(texture).DecodeForDisplay(original));
        string pattern = Path.Combine(output, "circle-source.png");
        WritePattern(pattern);
        var frames = BuiltInCardFrameCatalog.Load();
        foreach (string frameKey in new[] { "card_frame13", "card_frame14", "card_frame15", "card_frame16", "card_frame17", "card_frame19" })
        {
            var mapping = GameTextureDisplayMapping.ResolveForInstalledPreview(texture, frameKey);
            using var crop = new ImageCropForm(pattern, mapping.DisplayWidth, mapping.DisplayHeight, "19716 regression", frames, frameKey, displayMapping: mapping)
            { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            crop.Show();
            var canvas = (CropCanvas)Field(typeof(ImageCropForm), "_canvas").GetValue(crop)!;
            PumpUntil(() => canvas.HasFrame && crop.SelectedFrameKey == frameKey, "frame " + frameKey);
            // Exercise the actual crop confirmation, not just a mapping round trip.
            if (frameKey is "card_frame15" or "card_frame17")
                canvas.SetRenderSpec(new ImageRenderSpec(canvas.VisualArtSize.Width, canvas.VisualArtSize.Height, 0.73f, 38, -21));
            using var cropScreen = new Bitmap(crop.Width, crop.Height);
            crop.DrawToBitmap(cropScreen, new Rectangle(0, 0, cropScreen.Width, cropScreen.Height));
            cropScreen.Save(Path.Combine(output, frameKey + "-crop.png"));
            ImageRenderSpec renderedSpec = canvas.RenderSpec;
            Method(typeof(ImageCropForm), "ConfirmCrop").Invoke(crop, null);
            Require(crop.DialogResult == DialogResult.OK && crop.OutputPng != null, frameKey + " actual crop confirmed");
            Require(crop.OutputStoragePng != null, frameKey + " native UV write output supplied");
            byte[] storage = crop.OutputStoragePng!;
            engine.Replace(texture, storage, Path.Combine(mirror, "_MD卡图备份", texture.SourceKind));
            texture.PreviewFrameKey = frameKey;
            byte[] reopened = engine.DecodePng(texture);
            Require(SamePixels(storage, reopened), frameKey + " native canvas written without another resize");
            using var main = new MainForm(null, null, false);
            Field(typeof(MainForm), "_gameRoot").SetValue(main, mirror);
            var displayTask = (Task<byte[]>)Method(typeof(MainForm), "DecodeForDisplayAsync").Invoke(main, new object[] { texture, CancellationToken.None })!;
            PumpUntil(() => displayTask.IsCompleted, "main preview");
            byte[] display = displayTask.GetAwaiter().GetResult();
            using var displayImage = SixLabors.ImageSharp.Image.Load<Rgba32>(display);
            Require(IsRound(displayImage, out var bounds), frameKey + " crop -> Bundle -> main preview circle is round");
            var selectedFrame = frames.Single(x => x.Name == frameKey);
            byte[] framePreview = CardFrameRenderer.ComposeStoredArtPreview(reopened, engine.DecodePng(selectedFrame));
            using var installed = SixLabors.ImageSharp.Image.Load<Rgba32>(framePreview);
            using var frameBitmap = FrameComposer.BitmapFrom(engine.DecodePng(selectedFrame));
            var artWindow = Rectangle.Round(CardFrameRenderer.FindArtWindow(frameBitmap));
            artWindow.Inflate(-2, -2);
            Require(IsRound(installed, out var installedBounds, artWindow), frameKey + " installed art-window circle is round");
            using var gamePreview = SimulateGame(reopened, frameBitmap);
            using var gameStream = new MemoryStream();
            gamePreview.Save(gameStream, System.Drawing.Imaging.ImageFormat.Png);
            using var gameImage = SixLabors.ImageSharp.Image.Load<Rgba32>(gameStream.ToArray());
            Require(IsRound(gameImage, out var gameBounds, artWindow), frameKey + " game prefab geometry circle is round");
            float expectedDiameter = 170 * renderedSpec.ImageScale;
            Console.WriteLine($"game circle={gameBounds.Width}x{gameBounds.Height}; editor diameter={expectedDiameter:0.00}");
            Require(Math.Abs(gameBounds.Width - expectedDiameter) <= 3 && Math.Abs(gameBounds.Height - expectedDiameter) <= 3,
                frameKey + " game geometry preserves editor scale");
            float expectedX = artWindow.Left + artWindow.Width / 2f + renderedSpec.OffsetX - 0.5f * renderedSpec.ImageScale;
            float expectedY = artWindow.Top + artWindow.Height / 2f + renderedSpec.OffsetY;
            Require(Math.Abs(gameBounds.Left + gameBounds.Width / 2f - expectedX) <= 2 &&
                Math.Abs(gameBounds.Top + gameBounds.Height / 2f - expectedY) <= 2,
                frameKey + " game geometry preserves editor position");
            Require(gameBounds == installedBounds, frameKey + " frame renderer agrees with actual game geometry");
            if (frameKey == "card_frame14")
            {
                byte[] legacyCrop = ImageCropService.RenderToTarget(pattern, renderedSpec, 512, 683);
                byte[] legacyStorage = GameTextureDisplayMapping.Resolve(texture).EncodeForStorage(legacyCrop);
                using var legacyGame = SimulateGame(legacyStorage, frameBitmap);
                using var legacyStream = new MemoryStream();
                legacyGame.Save(legacyStream, System.Drawing.Imaging.ImageFormat.Png);
                using var legacyImage = SixLabors.ImageSharp.Image.Load<Rgba32>(legacyStream.ToArray());
                Require(!IsRound(legacyImage, out var legacyBounds, artWindow), "2.0.22 full-canvas write reproduces distortion in actual game geometry");
                Console.WriteLine($"legacy game circle={legacyBounds.Width}x{legacyBounds.Height}");
                legacyGame.Save(Path.Combine(output, "legacy-wrong-game-layout.png"));
            }
            File.WriteAllBytes(Path.Combine(output, frameKey + "-main.png"), display);
            File.WriteAllBytes(Path.Combine(output, frameKey + "-installed.png"), framePreview);
            File.WriteAllBytes(Path.Combine(output, frameKey + "-storage.png"), reopened);
            gamePreview.Save(Path.Combine(output, frameKey + "-game-layout.png"));
            Console.WriteLine($"geometry={bounds.Width}x{bounds.Height}; installed={installedBounds.Width}x{installedBounds.Height}; display={mapping.DisplayWidth}x{mapping.DisplayHeight}");
        }
        File.Copy(Path.Combine(mirror, "_MD卡图备份", texture.SourceKind, texture.RelativeBundlePath), texture.BundlePath, true);
        Require(Hash(texture.BundlePath) == originalHash, "first backup restore");
        Require(Hash(source.BundlePath) == originalHash, "game 19716 unchanged");
        Console.WriteLine("PASS actual pendulum crop / write / main preview / frame preview; gameWrites=False; ready=True");
    }

    private static Bitmap SimulateGame(byte[] png, Bitmap frame)
    {
        using var texture = FrameComposer.BitmapFrom(png);
        var output = new Bitmap(704, 1024, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        CardFrameRenderer.Configure(graphics);
        graphics.Clear(System.Drawing.Color.White);
        // Independent coordinates obtained from data.unity3d's CardPicture:
        // frame aspect=0.686046481, H=1054; RawImage size=622.2x797.64,
        // pivot=(0.5,0.5), position=(0,-62.219955), UV=(0,0,1,1).
        double sx = 704 / (1054 * 0.6860464811325073);
        double sy = 1024.0 / 1054;
        var quad = new System.Drawing.RectangleF(
            (float)((704 - 622.2000122070312 * sx) / 2),
            (float)(512 + (62.21995544433594 - 797.6400146484375 / 2) * sy),
            (float)(622.2000122070312 * sx), (float)(797.6400146484375 * sy));
        graphics.DrawImage(texture, quad, new System.Drawing.Rectangle(0, 0, texture.Width, texture.Height), GraphicsUnit.Pixel);
        graphics.DrawImageUnscaled(frame, 0, 0);
        return output;
    }

    private static void WritePattern(string path)
    {
        using var image = new Image<Rgba32>(605, 452, new Rgba32(25, 65, 140, 255));
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
            if ((x - 302) * (x - 302) + (y - 226) * (y - 226) <= 85 * 85) image[x, y] = new Rgba32(245, 25, 25, 255);
        image.Save(path, new PngEncoder());
    }
    private static bool SamePixels(byte[] left, byte[] right)
    {
        using var a = SixLabors.ImageSharp.Image.Load<Rgba32>(left);
        using var b = SixLabors.ImageSharp.Image.Load<Rgba32>(right);
        if (a.Size != b.Size) return false;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
            if (a[x, y] != b[x, y]) return false;
        return true;
    }
    private static bool IsRound(Image<Rgba32> image, out Rectangle bounds, Rectangle? region = null)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        var scan = region ?? new Rectangle(0, 0, image.Width, image.Height);
        for (int y = scan.Top; y < scan.Bottom; y++) for (int x = scan.Left; x < scan.Right; x++)
            if (image[x, y].R > 210 && image[x, y].G < 45 && image[x, y].B < 45)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        bounds = right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return bounds.Width > 70 && Math.Abs(bounds.Width - bounds.Height) <= 3;
    }
    private static void PumpUntil(Func<bool> predicate, string name)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.ElapsedMilliseconds < 20000) { Application.DoEvents(); Thread.Sleep(10); }
        if (!predicate()) throw new TimeoutException(name);
    }
    private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void Require(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
}
