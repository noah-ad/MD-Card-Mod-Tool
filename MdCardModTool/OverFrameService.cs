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

	public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string path = GateCachePath(localRoot);
		if (File.Exists(path))
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string text = File.ReadAllText(path).Trim();
				if (File.Exists(text))
				{
					TextAssetRef textAssetRef = _engine.FindTextAssetFast(text, localRoot, "of_card_asset");
					if (textAssetRef != null)
					{
						return textAssetRef;
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
			File.Delete(path);
		}
		string[] files = (from x in (ResourceSource.IsMobile(gameRoot) ? StandaloneResourceService.EnumerateBundles(localRoot) : Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)).Where(delegate(string path2)
			{
				cancellationToken.ThrowIfCancellationRequested();
				return IsUnityBundle(path2);
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
					TextAssetRef textAssetRef2 = _engine.FindTextAssetFast(file, localRoot, "of_card_asset");
					if (textAssetRef2 != null)
					{
						lock (sync)
						{
							if (textAssetRef2.Data.Length != 0)
							{
								found = textAssetRef2;
								state.Stop();
							}
							else if (emptyCandidate == null)
							{
								emptyCandidate = textAssetRef2;
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
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllText(path, found.BundlePath);
		return found;
	}

	public TextAssetRef? FindCachedGate(string gameRoot)
	{
		string text = IndexService.FindLocalRoot(gameRoot);
		if (text == null)
		{
			return null;
		}
		string path = GateCachePath(text);
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			string text2 = File.ReadAllText(path).Trim();
			return File.Exists(text2) ? _engine.FindTextAssetFast(text2, text, "of_card_asset") : null;
		}
		catch
		{
			return null;
		}
	}

	public List<OverFrameMapping> Read(string gameRoot, Action<int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Read(FindGate(gameRoot, progress, cancellationToken));
	}

	public List<OverFrameMapping> Read(TextAssetRef gate)
	{
		if (gate.Data.Length % 4 != 0)
		{
			throw new InvalidDataException($"{"of_card_asset"} 数据长度 {gate.Data.Length} 不是 4 的倍数，已停止写入以保护文件。");
		}
		List<OverFrameMapping> list = new List<OverFrameMapping>(gate.Data.Length / 4);
		for (int i = 0; i < gate.Data.Length; i += 4)
		{
			list.Add(new OverFrameMapping(BitConverter.ToUInt16(gate.Data, i), BitConverter.ToUInt16(gate.Data, i + 2)));
		}
		return (from x in list
			orderby x.CardId, x.ArtId
			select x).ToList();
	}

	public void EnableOrUpdate(string gameRoot, ushort cardId, ushort artId)
	{
		TextAssetRef textAssetRef = FindGate(gameRoot);
		List<OverFrameMapping> list = Parse(textAssetRef.Data);
		int num = list.FindIndex((OverFrameMapping x) => x.CardId == cardId);
		if (num >= 0)
		{
			list[num] = new OverFrameMapping(cardId, artId);
		}
		else
		{
			list.Add(new OverFrameMapping(cardId, artId));
		}
		Save(gameRoot, textAssetRef, list);
	}

	public void Disable(string gameRoot, ushort cardId)
	{
		TextAssetRef textAssetRef = FindGate(gameRoot);
		List<OverFrameMapping> list = Parse(textAssetRef.Data);
		list.RemoveAll((OverFrameMapping x) => x.CardId == cardId);
		Save(gameRoot, textAssetRef, list);
	}

	public bool HasBackup(string gameRoot)
	{
		return HasBackup(gameRoot, FindGate(gameRoot));
	}

	public bool HasBackup(string gameRoot, TextAssetRef gate)
	{
		return File.Exists(BackupPath(gameRoot, gate));
	}

	public void RestoreBackup(string gameRoot)
	{
		TextAssetRef textAssetRef = FindGate(gameRoot);
		string text = BackupPath(gameRoot, textAssetRef);
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("尚未找到本工具创建的超框表备份。", text);
		}
		File.Copy(text, textAssetRef.BundlePath, overwrite: true);
	}

	public string GateLocation(string gameRoot)
	{
		return FindGate(gameRoot).RelativeBundlePath;
	}

	public List<OverFrameMapping> ReadCached(string gameRoot)
	{
		TextAssetRef textAssetRef = FindCachedGate(gameRoot);
		if (textAssetRef != null)
		{
			return (from x in Parse(textAssetRef.Data)
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
		List<OverFrameMapping> list = new List<OverFrameMapping>(data.Length / 4);
		for (int i = 0; i < data.Length; i += 4)
		{
			list.Add(new OverFrameMapping(BitConverter.ToUInt16(data, i), BitConverter.ToUInt16(data, i + 2)));
		}
		return list;
	}

	private void Save(string gameRoot, TextAssetRef gate, List<OverFrameMapping> mappings)
	{
		byte[] array = new byte[mappings.Count * 4];
		for (int i = 0; i < mappings.Count; i++)
		{
			BitConverter.TryWriteBytes(array.AsSpan(i * 4, 2), mappings[i].CardId);
			BitConverter.TryWriteBytes(array.AsSpan(i * 4 + 2, 2), mappings[i].ArtId);
		}
		_engine.ReplaceTextAsset(gate, array, Path.Combine(gameRoot, "_MD卡图备份", "超框开关"));
	}

	private static string BackupPath(string gameRoot, TextAssetRef gate)
	{
		return Path.Combine(gameRoot, "_MD卡图备份", "超框开关", gate.RelativeBundlePath);
	}

	private static string GateCachePath(string localRoot)
	{
		string text = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(localRoot))).Substring(0, 12);
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", "overframe_gate_" + text + ".txt");
	}

	private static bool IsUnityBundle(string path)
	{
		try
		{
			using FileStream fileStream = File.OpenRead(path);
			Span<byte> span = stackalloc byte[7];
			return fileStream.Read(span) == 7 && Encoding.ASCII.GetString(span) == "UnityFS";
		}
		catch
		{
			return false;
		}
	}
}
