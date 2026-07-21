using System.Diagnostics;
using System.Globalization;

namespace MdCardModTool;

public sealed class ExtractedAnimation : IDisposable
{
    public required string SourcePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public required List<string> FramePaths { get; init; }
    public required int FramesPerSecond { get; init; }
    public double DurationSeconds => FramePaths.Count / (double)FramesPerSecond;

    public Bitmap LoadFrame(int index, int maxPreviewEdge = 0)
    {
        if (index < 0 || index >= FramePaths.Count) throw new ArgumentOutOfRangeException(nameof(index));
        using var stream = new FileStream(FramePaths[index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var image = Image.FromStream(stream);
        if (maxPreviewEdge > 0 && Math.Max(image.Width, image.Height) > maxPreviewEdge)
        {
            var ratio = maxPreviewEdge / (double)Math.Max(image.Width, image.Height);
            return new Bitmap(image, Math.Max(1, (int)Math.Round(image.Width * ratio)), Math.Max(1, (int)Math.Round(image.Height * ratio)));
        }
        return new Bitmap(image);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(WorkingDirectory)) Directory.Delete(WorkingDirectory, true); }
        catch { }
    }
}

public static class MonsterAnimationMedia
{
    public const int DefaultMaxFrames = 180;

    public static string? FindFfmpeg()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
        };
        foreach (var candidate in candidates) if (File.Exists(candidate)) return candidate;
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    public static async Task<ExtractedAnimation> ExtractAsync(
        string sourcePath,
        int framesPerSecond,
        int maxFrames,
        int maxFrameEdge,
        double startSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("找不到动画源文件。", sourcePath);
        if (framesPerSecond is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        if (maxFrames is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (maxFrameEdge is < 64 or > 2048) throw new ArgumentOutOfRangeException(nameof(maxFrameEdge));
        if (startSeconds is < 0 or > 86400) throw new ArgumentOutOfRangeException(nameof(startSeconds));
        var ffmpeg = FindFfmpeg() ?? throw new FileNotFoundException("未找到 FFmpeg。请使用完整分享包，或把 ffmpeg.exe 放到程序目录的 tools 文件夹。", "ffmpeg.exe");
        var directory = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "frame_%05d.png");
        var filter = $"fps={framesPerSecond.ToString(CultureInfo.InvariantCulture)},scale=w='min(iw\\,{maxFrameEdge})':h='min(ih\\,{maxFrameEdge})':force_original_aspect_ratio=decrease,format=rgba";
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y" }) start.ArgumentList.Add(arg);
        if (startSeconds > 0)
        {
            start.ArgumentList.Add("-ss");
            start.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }
        foreach (var arg in new[] { "-i", sourcePath, "-vf", filter, "-frames:v", maxFrames.ToString(CultureInfo.InvariantCulture), "-vsync", "0", output })
            start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                throw;
            }
            var error = await errorTask;
            _ = await outputTask;
            if (process.ExitCode != 0) throw new InvalidDataException("FFmpeg 无法读取该文件：" + error.Trim());
            var frames = Directory.EnumerateFiles(directory, "frame_*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (frames.Count == 0) throw new InvalidDataException("视频或 GIF 中没有可读取的画面。");
            return new ExtractedAnimation { SourcePath = sourcePath, WorkingDirectory = directory, FramePaths = frames, FramesPerSecond = framesPerSecond };
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch { }
            throw;
        }
    }
}
