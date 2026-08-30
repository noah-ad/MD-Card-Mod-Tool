using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MdCardModTool;

public sealed class OverFrameService
{
	private readonly ModEngine _engine = new ModEngine();

	public const string GateName = "of_card_asset";

	public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string cached = GateCachePath(localRoot);
		if (File.Exists(cached))
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string path = File.ReadAllText(cached).Trim();
				if (File.Exists(path))
				{
					TextAssetRef value = _engine.FindTextAssetFast(path, localRoot, "of_card_asset");
					if (value != null)
					{
						return value;
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
			}
			File.Delete(cached);
		}
		string[] files = (from x in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories).Where(path =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				return IsUnityBundle(path);
			})
			orderby new FileInfo(x).Length
			select x).ToArray();
		TextAssetRef found = null;
		TextAssetRef emptyCandidate = null;
		int done = 0;
		object sync = new object();
		Parallel.ForEach(files, new ParallelOptions
		{
			CancellationToken = cancellationToken,
			// Do not consume every logical processor: this scan can run beside the UI
			// and parses allocation-heavy Unity Bundles through AssetsTools.
			MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 4, 2, 4)
		}, delegate(string file, ParallelLoopState state)
		{
			if (Volatile.Read(in found) != null)
			{
				state.Stop();
			}
			else
			{
				try
				{
					TextAssetRef textAssetRef = _engine.FindTextAssetFast(file, localRoot, "of_card_asset");
					if (textAssetRef != null)
					{
						lock (sync)
						{
							if (textAssetRef.Data.Length != 0)
							{
								found = textAssetRef;
								state.Stop();
							}
							else if (emptyCandidate == null)
							{
								emptyCandidate = textAssetRef;
							}
						}
					}
				}
				catch
				{
				}
				int num = Interlocked.Increment(ref done);
				if (num % 250 == 0 || num == files.Length)
				{
					progress?.Invoke(num, files.Length);
				}
			}
		});
		if (found == null)
		{
			found = emptyCandidate;
		}
		if (found == null)
		{
			throw new FileNotFoundException("没有在 LocalData 中找到 of_card_asset。请先启动游戏完成资源下载。");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(cached));
		File.WriteAllText(cached, found.BundlePath);
		return found;
	}

	public TextAssetRef? FindCachedGate(string gameRoot)
	{
		string localRoot = IndexService.FindLocalRoot(gameRoot);
		if (localRoot == null)
		{
			return null;
		}
		string cached = GateCachePath(localRoot);
		if (!File.Exists(cached))
		{
			return null;
		}
		try
		{
			string path = File.ReadAllText(cached).Trim();
			return File.Exists(path) ? _engine.FindTextAssetFast(path, localRoot, "of_card_asset") : null;
		}
		catch
		{
			return null;
		}
	}

	public List<OverFrameMapping> Read(string gameRoot, Action<int, int>? progress = null,
		CancellationToken cancellationToken = default)
	{
		return Read(FindGate(gameRoot, progress, cancellationToken));
	}

	public List<OverFrameMapping> Read(TextAssetRef gate)
	{
		if (gate.Data.Length % 4 != 0)
		{
			throw new InvalidDataException($"{"of_card_asset"} 数据长度 {gate.Data.Length} 不是 4 的倍数，已停止写入以保护文件。");
		}
		List<OverFrameMapping> values = new List<OverFrameMapping>(gate.Data.Length / 4);
		for (int i = 0; i < gate.Data.Length; i += 4)
		{
			values.Add(new OverFrameMapping(BitConverter.ToUInt16(gate.Data, i), BitConverter.ToUInt16(gate.Data, i + 2)));
		}
		return (from x in values
			orderby x.CardId, x.ArtId
			select x).ToList();
	}

	public void EnableOrUpdate(string gameRoot, ushort cardId, ushort artId)
	{
		TextAssetRef gate = FindGate(gameRoot);
		List<OverFrameMapping> mappings = Parse(gate.Data);
		int found = mappings.FindIndex((OverFrameMapping x) => x.CardId == cardId);
		if (found >= 0)
		{
			mappings[found] = new OverFrameMapping(cardId, artId);
		}
		else
		{
			mappings.Add(new OverFrameMapping(cardId, artId));
		}
		Save(gameRoot, gate, mappings);
	}

	public void Disable(string gameRoot, ushort cardId)
	{
		TextAssetRef gate = FindGate(gameRoot);
		List<OverFrameMapping> mappings = Parse(gate.Data);
		mappings.RemoveAll((OverFrameMapping x) => x.CardId == cardId);
		Save(gameRoot, gate, mappings);
	}

	public bool HasBackup(string gameRoot)
	{
		return HasBackup(gameRoot, FindGate(gameRoot));
	}

	public bool HasBackup(string gameRoot, TextAssetRef gate) => File.Exists(BackupPath(gameRoot, gate));

	public void RestoreBackup(string gameRoot)
	{
		TextAssetRef gate = FindGate(gameRoot);
		string backup = BackupPath(gameRoot, gate);
		if (!File.Exists(backup))
		{
			throw new FileNotFoundException("尚未找到本工具创建的超框表备份。", backup);
		}
		File.Copy(backup, gate.BundlePath, overwrite: true);
	}

	public string GateLocation(string gameRoot)
	{
		return FindGate(gameRoot).RelativeBundlePath;
	}

	public List<OverFrameMapping> ReadCached(string gameRoot)
	{
		TextAssetRef gate = FindCachedGate(gameRoot);
		if (gate != null)
		{
			return (from x in Parse(gate.Data)
				orderby x.CardId, x.ArtId
				select x).ToList();
		}
		return new List<OverFrameMapping>();
	}

	private static List<OverFrameMapping> Parse(byte[] data)
	{
		if (data.Length % 4 != 0)
		{
			throw new InvalidDataException($"{"of_card_asset"} 数据长度 {data.Length} 不是 4 的倍数，已停止写入以保护文件。");
		}
		List<OverFrameMapping> result = new List<OverFrameMapping>(data.Length / 4);
		for (int i = 0; i < data.Length; i += 4)
		{
			result.Add(new OverFrameMapping(BitConverter.ToUInt16(data, i), BitConverter.ToUInt16(data, i + 2)));
		}
		return result;
	}

	private void Save(string gameRoot, TextAssetRef gate, List<OverFrameMapping> mappings)
	{
		byte[] data = new byte[mappings.Count * 4];
		for (int i = 0; i < mappings.Count; i++)
		{
			BitConverter.TryWriteBytes(data.AsSpan(i * 4, 2), mappings[i].CardId);
			BitConverter.TryWriteBytes(data.AsSpan(i * 4 + 2, 2), mappings[i].ArtId);
		}
		_engine.ReplaceTextAsset(gate, data, Path.Combine(gameRoot, "_MD卡图备份", "超框开关"));
	}

	private static string BackupPath(string gameRoot, TextAssetRef gate)
	{
		return Path.Combine(gameRoot, "_MD卡图备份", "超框开关", gate.RelativeBundlePath);
	}

	private static string GateCachePath(string localRoot)
	{
		string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localRoot))).Substring(0, 12);
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", "overframe_gate_" + id + ".txt");
	}

	private static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			Span<byte> bytes = stackalloc byte[7];
			return stream.Read(bytes) == 7 && Encoding.ASCII.GetString(bytes) == "UnityFS";
		}
		catch
		{
			return false;
		}
	}
}
