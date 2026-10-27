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
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace MdCardModTool;

internal static class FfmpegMediaTests
{
    internal static void Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        string ffmpeg = MonsterAnimationMedia.FindFfmpeg() ?? throw new FileNotFoundException("Need the published FFmpeg fixture.");
        string sources = Path.Combine(output, "素材 & video input");
        Directory.CreateDirectory(sources);
        for (int i = 0; i < 24; i++)
        {
            using var frame = new Image<Rgba32>(320, 180, new Rgba32((byte)(i * 7), 90, 80, 255));
            for (int y = 0; y < 36; y++) for (int x = 0; x < 36; x++) frame[x, y] = new Rgba32(0, 255, 0, 255);
            frame.SaveAsPng(Path.Combine(sources, $"frame_{i + 1:00000}.png"));
        }
        string video = Path.Combine(sources, "原像素 视频 & test.mkv");
        Ffmpeg(ffmpeg, "-y", "-framerate", "12", "-i", Path.Combine(sources, "frame_%05d.png"), "-frames:v", "24", "-c:v", "ffv1", "-pix_fmt", "bgra", video);
        string gifPath = Path.Combine(sources, "可变帧时长 & gif.gif");
        using (var gif = new Image<Rgba32>(96, 54, new Rgba32(200, 20, 30, 255)))
        {
            gif.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 10;
            using var frame2 = new Image<Rgba32>(96, 54, new Rgba32(20, 180, 30, 255));
            frame2.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 20;
            gif.Frames.AddFrame(frame2.Frames.RootFrame);
            using var frame3 = new Image<Rgba32>(96, 54, new Rgba32(20, 30, 200, 255));
            frame3.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 30;
            gif.Frames.AddFrame(frame3.Frames.RootFrame);
            gif.SaveAsGif(gifPath);
        }
        int modernCalls = 0;
        string? directory = null;
        using (var media = MonsterAnimationMedia.ExtractWithFfmpegAsync(video, 12, 40, 0, 0, false, default, async (start, token) =>
        {
            modernCalls++;
            Check(!start.ArgumentList.Contains("-vsync") && start.ArgumentList.Contains("-fps_mode"), "modern argv never sends removed vsync");
            directory = Path.GetDirectoryName(start.ArgumentList.Last());
            return await MonsterAnimationMedia.RunFfmpegAsync(start, token).ConfigureAwait(false);
        }).GetAwaiter().GetResult())
        {
            Check(modernCalls == 1 && media.FramePaths.Count == 24 && media.FramesPerSecond == 12 && media.DurationSeconds == 2, "real FFV1 exact frame count/rate/duration");
            for (int i = 0; i < 24; i++)
            {
                using var decoded = Image.Load<Rgba32>(media.FramePaths[i]);
                using var source = Image.Load<Rgba32>(Path.Combine(sources, $"frame_{i + 1:00000}.png"));
                Check(decoded.Width == 320 && decoded.Height == 180 && Pixels(decoded) == Pixels(source), "original-resolution RGBA pixels " + i);
            }
        }
        Check(directory != null && !Directory.Exists(directory), "successful extraction cleanup on dispose");
        using (var scaled = MonsterAnimationMedia.ExtractAsync(video, 6, 5, 128, 1).GetAwaiter().GetResult())
        {
            using var frame = Image.Load<Rgba32>(scaled.FramePaths[0]);
            Check(scaled.FramePaths.Count == 5 && frame.Width == 128 && frame.Height == 72 && frame[64, 36].R >= 70, "FPS / seek / limit / aspect ratio");
        }
        using (var gif = MonsterAnimationMedia.ExtractAsync(gifPath, 10, 20, 0).GetAwaiter().GetResult())
            Check(gif.FramePaths.Count == 6 && gif.FramePaths.Select(path => { using var frame = Image.Load<Rgba32>(path); return frame[48, 27]; }).Distinct().Count() == 3,
                "real variable-duration GIF resampled without repeated-loop frames");
        using (var chroma = MonsterAnimationMedia.ExtractAsync(video, 12, 2, 0, removeGreenScreen: true).GetAwaiter().GetResult())
        {
            using var frame = Image.Load<Rgba32>(chroma.FramePaths[0]);
            Check(chroma.GreenScreenRemoved && frame[8, 8].A == 0 && frame[160, 90].A == 255, "real video chroma key alpha");
        }
        int fallbackCalls = 0;
        using (var fallback = MonsterAnimationMedia.ExtractWithFfmpegAsync(video, 12, 3, 128, 0, false, default, async (start, token) =>
        {
            fallbackCalls++;
            Check(!start.ArgumentList.Contains("-vsync"), "legacy branch never restores obsolete vsync");
            string folder = Path.GetDirectoryName(start.ArgumentList.Last())!;
            if (fallbackCalls == 1)
            {
                File.Copy(Path.Combine(sources, "frame_00001.png"), Path.Combine(folder, "frame_00099.png"));
                return new MonsterAnimationMedia.FfmpegResult(1, "Unrecognized option 'fps_mode'.\nError splitting the argument list: Option not found");
            }
            Check(fallbackCalls == 2 && !start.ArgumentList.Contains("-fps_mode") && !Directory.EnumerateFiles(folder).Any(), "targeted old-version fallback clears partial output");
            return await MonsterAnimationMedia.RunFfmpegAsync(start, token).ConfigureAwait(false);
        }).GetAwaiter().GetResult()) Check(fallbackCalls == 2 && fallback.FramePaths.Count == 3, "simulated old flag rejection then real compatible extraction");

        string? failedDirectory = null;
        int decoderCalls = 0;
        try
        {
            using var ignored = MonsterAnimationMedia.ExtractWithFfmpegAsync(video, 12, 3, 128, 0, false, default, (start, _) =>
            {
                decoderCalls++; failedDirectory = Path.GetDirectoryName(start.ArgumentList.Last());
                return Task.FromResult(new MonsterAnimationMedia.FfmpegResult(1, "Decoder unavailable for input fps_mode_video.mkv\nError opening filters: Option not found"));
            }).GetAwaiter().GetResult();
            throw new Exception("Decoder failure was ignored");
        }
        catch (InvalidDataException ex) { Check(ex.Message.Contains("Decoder unavailable") && ex.Message.Contains(ffmpeg), "decoder error includes executable and original reason"); }
        Check(decoderCalls == 1 && !Directory.Exists(failedDirectory), "decoder error not disguised as compatibility retry; failed output removed");
        int fallbackFailureCalls = 0;
        try
        {
            using var ignored = MonsterAnimationMedia.ExtractWithFfmpegAsync(video, 12, 3, 128, 0, false, default, (start, _) =>
            {
                fallbackFailureCalls++; failedDirectory = Path.GetDirectoryName(start.ArgumentList.Last());
                return Task.FromResult(new MonsterAnimationMedia.FfmpegResult(1, fallbackFailureCalls == 1 ? "Unknown option fps_mode" : "actual decode failure"));
            }).GetAwaiter().GetResult();
            throw new Exception("Fallback failure was ignored");
        }
        catch (InvalidDataException ex) { Check(ex.Message.Contains("actual decode failure"), "fallback keeps final decode error"); }
        Check(fallbackFailureCalls == 2 && !Directory.Exists(failedDirectory), "fallback bounded to one retry");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.CancelAfter(400);
            var clock = Stopwatch.StartNew();
            try
            {
                using var ignored = MonsterAnimationMedia.ExtractWithFfmpegAsync(video, 12, 600, 128, 0, false, cancelled.Token, (start, token) =>
                {
                    failedDirectory = Path.GetDirectoryName(start.ArgumentList.Last());
                    int input = start.ArgumentList.IndexOf("-i");
                    start.ArgumentList.Insert(input, "-re");
                    start.ArgumentList.Insert(input, "-1"); start.ArgumentList.Insert(input, "-stream_loop");
                    return MonsterAnimationMedia.RunFfmpegAsync(start, token);
                }).GetAwaiter().GetResult();
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
            Check(clock.Elapsed < TimeSpan.FromSeconds(8) && !Directory.Exists(failedDirectory), "cancel waits for child exit and removes partial frames");
        }
        string invalid = Path.Combine(sources, "invalid video.mkv"); File.WriteAllText(invalid, "not a media file");
        try { using var ignored = MonsterAnimationMedia.ExtractAsync(invalid, 12, 3, 128).GetAwaiter().GetResult(); throw new Exception("Invalid video accepted"); }
        catch (InvalidDataException ex) { Check(!ex.Message.Contains("Unrecognized option"), "real corrupt media is reported as input failure, not vsync"); }
        UiImport(output, video);
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { RealVideo = true, RealGif = true, NativePixels = true,
            Seek = true, Chroma = true, SimulatedLegacyOptions = true, BoundedRetry = true, DecoderFailure = true, Cancellation = true, UiAutoAndFixed = true, GameWrites = false }));
        Console.WriteLine("PASS FFmpeg media compatibility / video / GIF / original RGBA / auto+fixed UI / gameWrites=False");
    }

    private static void UiImport(string output, string video)
    {
        using var form = new MonsterAnimationForm(output) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MonsterAnimationForm);
        var status = (Label)type.GetField("_resourceStatus", flags)!.GetValue(form)!;
        var choose = (ComboBox)type.GetField("_frameEdge", flags)!.GetValue(form)!;
        string previous = status.Text;
        foreach (int quality in new[] { 0, 1 })
        {
            choose.SelectedIndex = quality;
            var method = type.GetMethod("LoadMediaAsync", flags, null, new[] { typeof(string) }, null)!;
            var task = (Task)method.Invoke(form, new object[] { video })!;
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(45)) { Application.DoEvents(); Thread.Sleep(10); }
            Check(task.IsCompleted, "UI import completes"); task.GetAwaiter().GetResult();
            Check(type.GetField("_media", flags)!.GetValue(form) is ExtractedAnimation media && media.FramePaths.Count == 24, "real UI automatic/fixed import uses actual frames");
            Check(!(bool)type.GetField("_busy", flags)!.GetValue(form)! && status.Text == previous, "UI leaves probing state and restores resource status");
        }
        using var screenshot = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(screenshot, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
        screenshot.Save(Path.Combine(output, "video-import-ui.png"));
        form.Close();
    }

    private static string Pixels(Image<Rgba32> image)
    {
        byte[] pixels = new byte[image.Width * image.Height * 4]; image.CopyPixelDataTo(pixels);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
    private static void Ffmpeg(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error" }.Concat(args)) start.ArgumentList.Add(argument);
        var result = MonsterAnimationMedia.RunFfmpegAsync(start, default).GetAwaiter().GetResult();
        Check(result.ExitCode == 0, "fixture FFmpeg: " + result.Error);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
}
