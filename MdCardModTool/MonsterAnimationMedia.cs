using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public static class MonsterAnimationMedia
{
	public const int DefaultMaxFrames = 180;

	public static string? FindFfmpeg()
	{
		string[] array = new string[2]
		{
			AppPaths.ResolveFile("tools", "ffmpeg.exe"),
			AppPaths.ResolveFile("ffmpeg.exe")
		};
		foreach (string candidate in array)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		string executable = (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
		array = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string directory in array)
		{
			try
			{
				string candidate2 = Path.Combine(directory.Trim('"'), executable);
				if (File.Exists(candidate2))
				{
					return candidate2;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	public static async Task<ExtractedAnimation> ExtractAsync(string sourcePath, int framesPerSecond, int maxFrames, int maxFrameEdge, double startSeconds = 0.0, bool removeGreenScreen = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(sourcePath))
		{
			throw new FileNotFoundException("找不到动画源文件。", sourcePath);
		}
		if ((framesPerSecond < 1 || framesPerSecond > 60) ? true : false)
		{
			throw new ArgumentOutOfRangeException("framesPerSecond");
		}
		if ((maxFrames < 1 || maxFrames > 600) ? true : false)
		{
			throw new ArgumentOutOfRangeException("maxFrames");
		}
		if ((maxFrameEdge < 64 || maxFrameEdge > 2048) ? true : false)
		{
			throw new ArgumentOutOfRangeException("maxFrameEdge");
		}
		if ((startSeconds < 0.0 || startSeconds > 86400.0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("startSeconds");
		}
		string ffmpeg = FindFfmpeg() ?? throw new FileNotFoundException("未找到 FFmpeg。请使用完整分享包，或把 ffmpeg.exe 放到 data\\tools 文件夹。", "ffmpeg.exe");
		string directory = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string output = Path.Combine(directory, "frame_%05d.png");
		List<string> filters = new List<string>
		{
			"fps=" + framesPerSecond.ToString(CultureInfo.InvariantCulture),
			$"scale=w='min(iw\\,{maxFrameEdge})':h='min(ih\\,{maxFrameEdge})':force_original_aspect_ratio=decrease"
		};
		if (removeGreenScreen)
		{
			filters.Add("colorkey=color=0x00FF00:similarity=0.25:blend=0.08");
			filters.Add("despill=type=green:mix=0.5");
		}
		filters.Add("format=rgba");
		string filter = string.Join(',', filters);
		ProcessStartInfo start = new ProcessStartInfo
		{
			FileName = ffmpeg,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		string[] array = new string[4] { "-hide_banner", "-loglevel", "error", "-y" };
		foreach (string arg in array)
		{
			start.ArgumentList.Add(arg);
		}
		if (startSeconds > 0.0)
		{
			start.ArgumentList.Add("-ss");
			start.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
		}
		array = new string[9]
		{
			"-i",
			sourcePath,
			"-vf",
			filter,
			"-frames:v",
			maxFrames.ToString(CultureInfo.InvariantCulture),
			"-vsync",
			"0",
			output
		};
		foreach (string arg2 in array)
		{
			start.ArgumentList.Add(arg2);
		}
		try
		{
			using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
			Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
			try
			{
				await process.WaitForExitAsync(cancellationToken);
			}
			catch
			{
				try
				{
					if (!process.HasExited)
					{
						process.Kill(entireProcessTree: true);
					}
				}
				catch
				{
				}
				throw;
			}
			string error = await errorTask;
			await outputTask;
			if (process.ExitCode != 0)
			{
				throw new InvalidDataException("FFmpeg 无法读取该文件：" + error.Trim());
			}
			List<string> frames = Directory.EnumerateFiles(directory, "frame_*.png").OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToList();
			if (frames.Count == 0)
			{
				throw new InvalidDataException("视频或 GIF 中没有可读取的画面。");
			}
			return new ExtractedAnimation
			{
				SourcePath = sourcePath,
				WorkingDirectory = directory,
				FramePaths = frames,
				FramesPerSecond = framesPerSecond,
				GreenScreenRemoved = removeGreenScreen
			};
		}
		catch
		{
			try
			{
				Directory.Delete(directory, recursive: true);
			}
			catch
			{
			}
			throw;
		}
	}

	public static async Task<ExtractedAnimation> ExtractSequenceAsync(IReadOnlyList<string> sourcePaths, int framesPerSecond,
		int maxFrames, int maxFrameEdge, bool removeGreenScreen = false, CancellationToken cancellationToken = default)
	{
		if (sourcePaths.Count == 0) throw new ArgumentException("图片序列不能为空。", nameof(sourcePaths));
		if (framesPerSecond is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
		if (maxFrames is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(maxFrames));
		if (maxFrameEdge is < 64 or > 2048) throw new ArgumentOutOfRangeException(nameof(maxFrameEdge));
		string[] supported = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"];
		string[] ordered = sourcePaths.Where(File.Exists)
			.Where(path => supported.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
			.OrderBy(path => Path.GetFileName(path), NaturalFileNameComparer.Instance)
			.Take(maxFrames).ToArray();
		if (ordered.Length == 0) throw new FileNotFoundException("所选文件中没有可读取的图片。", sourcePaths[0]);
		string directory = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			for (int i = 0; i < ordered.Length; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(ordered[i], cancellationToken);
				if (Math.Max(image.Width, image.Height) > maxFrameEdge)
				{
					image.Mutate(context => context.Resize(new ResizeOptions
					{
						Size = new SixLabors.ImageSharp.Size(maxFrameEdge, maxFrameEdge),
						Mode = ResizeMode.Max,
						Sampler = KnownResamplers.Lanczos3
					}));
				}
				if (removeGreenScreen) RemoveGreenScreen(image);
				await image.SaveAsPngAsync(Path.Combine(directory, $"frame_{i + 1:00000}.png"), cancellationToken);
			}
			return new ExtractedAnimation
			{
				SourcePath = ordered.Length == 1 ? ordered[0] : $"{Path.GetFileName(ordered[0])} 等 {ordered.Length} 张图片",
				WorkingDirectory = directory,
				FramePaths = Directory.EnumerateFiles(directory, "frame_*.png").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(),
				FramesPerSecond = framesPerSecond,
				GreenScreenRemoved = removeGreenScreen
			};
		}
		catch
		{
			try { Directory.Delete(directory, recursive: true); } catch { }
			throw;
		}
	}

	private static void RemoveGreenScreen(Image<Rgba32> image)
	{
		image.ProcessPixelRows(accessor =>
		{
			for (int y = 0; y < accessor.Height; y++)
			{
				Span<Rgba32> row = accessor.GetRowSpan(y);
				for (int x = 0; x < row.Length; x++)
				{
					Rgba32 pixel = row[x];
					int dominance = pixel.G - Math.Max(pixel.R, pixel.B);
					if (pixel.G > 80 && dominance > 20)
					{
						float removal = Math.Clamp((dominance - 20) / 120f, 0f, 1f);
						pixel.A = (byte)Math.Round(pixel.A * (1f - removal));
						pixel.G = (byte)Math.Min(pixel.G, Math.Max(pixel.R, pixel.B) + 20);
						row[x] = pixel;
					}
				}
			}
		});
	}

	private sealed class NaturalFileNameComparer : IComparer<string>
	{
		public static readonly NaturalFileNameComparer Instance = new();

		public int Compare(string? left, string? right)
		{
			left ??= ""; right ??= "";
			int li = 0, ri = 0;
			while (li < left.Length && ri < right.Length)
			{
				if (char.IsDigit(left[li]) && char.IsDigit(right[ri]))
				{
					int ls = li, rs = ri;
					while (li < left.Length && char.IsDigit(left[li])) li++;
					while (ri < right.Length && char.IsDigit(right[ri])) ri++;
					string ln = left[ls..li].TrimStart('0'), rn = right[rs..ri].TrimStart('0');
					int length = ln.Length.CompareTo(rn.Length);
					if (length != 0) return length;
					int digits = string.Compare(ln, rn, StringComparison.Ordinal);
					if (digits != 0) return digits;
					continue;
				}
				int character = char.ToUpperInvariant(left[li]).CompareTo(char.ToUpperInvariant(right[ri]));
				if (character != 0) return character;
				li++; ri++;
			}
			return left.Length.CompareTo(right.Length);
		}
	}
}
