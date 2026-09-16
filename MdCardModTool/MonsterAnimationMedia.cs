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
	private sealed class NaturalFileNameComparer : IComparer<string>
	{
		public static readonly NaturalFileNameComparer Instance = new NaturalFileNameComparer();

		public int Compare(string? left, string? right)
		{
			if (left == null)
			{
				left = "";
			}
			if (right == null)
			{
				right = "";
			}
			int i = 0;
			int j = 0;
			while (i < left.Length && j < right.Length)
			{
				if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
				{
					int num = i;
					int num2 = j;
					for (; i < left.Length && char.IsDigit(left[i]); i++)
					{
					}
					for (; j < right.Length && char.IsDigit(right[j]); j++)
					{
					}
					string? text = left;
					int num3 = num;
					string text2 = text.Substring(num3, i - num3).TrimStart('0');
					string? text3 = right;
					num3 = num2;
					string text4 = text3.Substring(num3, j - num3).TrimStart('0');
					int num4 = text2.Length.CompareTo(text4.Length);
					if (num4 != 0)
					{
						return num4;
					}
					int num5 = string.Compare(text2, text4, StringComparison.Ordinal);
					if (num5 != 0)
					{
						return num5;
					}
				}
				else
				{
					int num6 = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));
					if (num6 != 0)
					{
						return num6;
					}
					i++;
					j++;
				}
			}
			return left.Length.CompareTo(right.Length);
		}
	}

	public const int DefaultMaxFrames = 180;

	public static string? FindFfmpeg()
	{
		string[] array = new string[2]
		{
			AppPaths.ResolveFile("tools", "ffmpeg.exe"),
			AppPaths.ResolveFile("ffmpeg.exe")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		string path = (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
		array = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text2 in array)
		{
			try
			{
				string text3 = Path.Combine(text2.Trim('"'), path);
				if (File.Exists(text3))
				{
					return text3;
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
		string fileName = FindFfmpeg() ?? throw new FileNotFoundException("未找到 FFmpeg。请使用完整分享包，或把 ffmpeg.exe 放到 data\\tools 文件夹。", "ffmpeg.exe");
		string directory = Path.Combine(Path.GetTempPath(), "MDCardModTool", "animation_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string text = Path.Combine(directory, "frame_%05d.png");
		List<string> list = new List<string>
		{
			"fps=" + framesPerSecond.ToString(CultureInfo.InvariantCulture),
			$"scale=w='min(iw\\,{maxFrameEdge})':h='min(ih\\,{maxFrameEdge})':force_original_aspect_ratio=decrease"
		};
		if (removeGreenScreen)
		{
			list.Add("colorkey=color=0x00FF00:similarity=0.25:blend=0.08");
			list.Add("despill=type=green:mix=0.5");
		}
		list.Add("format=rgba");
		string text2 = string.Join(',', list);
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		string[] array = new string[4] { "-hide_banner", "-loglevel", "error", "-y" };
		foreach (string item in array)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		if (startSeconds > 0.0)
		{
			processStartInfo.ArgumentList.Add("-ss");
			processStartInfo.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
		}
		array = new string[9]
		{
			"-i",
			sourcePath,
			"-vf",
			text2,
			"-frames:v",
			maxFrames.ToString(CultureInfo.InvariantCulture),
			"-vsync",
			"0",
			text
		};
		foreach (string item2 in array)
		{
			processStartInfo.ArgumentList.Add(item2);
		}
		try
		{
			using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动 FFmpeg。");
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
			List<string> list2 = Directory.EnumerateFiles(directory, "frame_*.png").OrderBy<string, string>((string x) => x, StringComparer.OrdinalIgnoreCase).ToList();
			if (list2.Count == 0)
			{
				throw new InvalidDataException("视频或 GIF 中没有可读取的画面。");
			}
			return new ExtractedAnimation
			{
				SourcePath = sourcePath,
				WorkingDirectory = directory,
				FramePaths = list2,
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

	public static async Task<ExtractedAnimation> ExtractSequenceAsync(IReadOnlyList<string> sourcePaths, int framesPerSecond, int maxFrames, int maxFrameEdge, bool removeGreenScreen = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (sourcePaths.Count == 0)
		{
			throw new ArgumentException("图片序列不能为空。", "sourcePaths");
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
		string[] supported = new string[7] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff" };
		string[] ordered = (from path in sourcePaths.Where(File.Exists)
			where supported.Contains<string>(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
			select path).OrderBy<string, string>((string path) => Path.GetFileName(path), NaturalFileNameComparer.Instance).Take(maxFrames).ToArray();
		if (ordered.Length == 0)
		{
			throw new FileNotFoundException("所选文件中没有可读取的图片。", sourcePaths[0]);
		}
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
					image.Mutate(delegate(IImageProcessingContext context)
					{
						context.Resize(new ResizeOptions
						{
							Size = new Size(maxFrameEdge, maxFrameEdge),
							Mode = ResizeMode.Max,
							Sampler = KnownResamplers.Lanczos3
						});
					});
				}
				if (removeGreenScreen)
				{
					RemoveGreenScreen(image);
				}
				await image.SaveAsPngAsync(Path.Combine(directory, $"frame_{i + 1:00000}.png"), cancellationToken);
			}
			return new ExtractedAnimation
			{
				SourcePath = ((ordered.Length == 1) ? ordered[0] : $"{Path.GetFileName(ordered[0])} 等 {ordered.Length} 张图片"),
				WorkingDirectory = directory,
				FramePaths = Directory.EnumerateFiles(directory, "frame_*.png").OrderBy<string, string>((string path) => path, StringComparer.OrdinalIgnoreCase).ToList(),
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

	private static void RemoveGreenScreen(Image<Rgba32> image)
	{
		image.ProcessPixelRows(delegate(PixelAccessor<Rgba32> accessor)
		{
			for (int i = 0; i < accessor.Height; i++)
			{
				Span<Rgba32> rowSpan = accessor.GetRowSpan(i);
				for (int j = 0; j < rowSpan.Length; j++)
				{
					Rgba32 rgba = rowSpan[j];
					int num = rgba.G - Math.Max(rgba.R, rgba.B);
					if (rgba.G > 80 && num > 20)
					{
						float num2 = Math.Clamp((float)(num - 20) / 120f, 0f, 1f);
						rgba.A = (byte)Math.Round((float)(int)rgba.A * (1f - num2));
						rgba.G = (byte)Math.Min(rgba.G, Math.Max(rgba.R, rgba.B) + 20);
						rowSpan[j] = rgba;
					}
				}
			}
		});
	}
}
