using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MdCardModTool;

internal static class DuelMusicAudio
{
	internal static byte[] BuildPcmBank(byte[] pcm)
	{
		if (pcm.Length == 0 || pcm.Length % 4 != 0 || pcm.Length > 48000 * 4 * 600)
			throw new InvalidDataException("音乐须为有效音频，时长不超过 10 分钟。");
		byte[] result = new byte[80 + pcm.Length];
		"FSB5"u8.CopyTo(result);
		void UInt(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
		UInt(4, 1); UInt(8, 1); UInt(12, 20); UInt(20, (uint)pcm.Length); UInt(24, 2);
		BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(60), ((ulong)(pcm.Length / 4) << 34) | (9UL << 1) | (1UL << 5) | 1UL);
		UInt(68, (3u << 25) | (8u << 1)); UInt(72, 0); UInt(76, (uint)(pcm.Length / 4 - 1));
		pcm.CopyTo(result, 80);
		return result;
	}
	internal static void Convert(string input, string output, bool raw)
	{
		bool bank = Path.GetExtension(input).Equals(".fsb", StringComparison.OrdinalIgnoreCase);
		if (bank && raw) throw new ArgumentException("FSB 预览应输出 WAV。");
		var start = new ProcessStartInfo(bank ? AppPaths.ResolveFile("tools", "vgmstream", "vgmstream-cli.exe") : AppPaths.ResolveFile("tools", "ffmpeg.exe"))
		{ UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
		string[] arguments = bank ? new[] { "-i", "-o", output, input } : new[]
		{ "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-vn", "-t", "601", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", raw ? "s16le" : "wav", output };
		foreach (string arg in arguments) start.ArgumentList.Add(arg);
		using var process = Process.Start(start) ?? throw new IOException("无法启动 FFmpeg。");
		Task<string> error = process.StandardError.ReadToEndAsync(), stdout = process.StandardOutput.ReadToEndAsync();
		if (!process.WaitForExit(120000)) { process.Kill(true); process.WaitForExit(); throw new IOException("音频转换超时。"); }
		Task.WaitAll(error, stdout);
		if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidDataException("音频转换失败：" + error.Result + stdout.Result);
		if (raw && new FileInfo(output).Length > 48000L * 4 * 600) throw new InvalidDataException("音乐超过 10 分钟。");
	}
}
