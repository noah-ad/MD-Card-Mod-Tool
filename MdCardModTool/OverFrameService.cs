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
	private readonly string? _catalogCacheDirectory;
	public OverFrameService() { }
	internal OverFrameService(string catalogCacheDirectory) => _catalogCacheDirectory = catalogCacheDirectory;

	public const string GateName = "of_card_asset";

	public TextAssetRef FindGate(string gameRoot, Action<int, int>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		string localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
		string currentVersion = GameCardCatalogUpdater.RefreshCurrentFamilyVersion(gameRoot, cancellationToken, _catalogCacheDirectory,
			(done, total, _) => progress?.Invoke(done, total));
		string path = GateCachePath(localRoot);
		if (File.Exists(path))
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				string text = File.ReadLines(path).FirstOrDefault()?.Trim() ?? "";
				if (File.Exists(text) && ModRecoveryStore.IsUnder(text, localRoot))
				{
					TextAssetRef textAssetRef = _engine.FindTextAssetFast(text, localRoot, "of_card_asset");
					if (textAssetRef != null && MatchesFamily(text, currentVersion))
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
					if (textAssetRef2 != null && MatchesFamily(file, currentVersion))
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
			throw new FileNotFoundException("没有在 LocalData 中找到与当前卡片数据版本配套的 of_card_asset。请先启动游戏完成资源下载。" + (currentVersion.Length > 0 ? "\n数据版本：" + currentVersion : ""));
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllText(path, found.BundlePath);
		return found;
	}

	private bool MatchesFamily(string path, string version) => version.Length == 0 || _engine.ReadAssetBundleContainerPaths(path)
		.Any(x => x.Replace('\\', '/').Equals($"assets/resourcesassetbundle/card/data/{version}/md/of_card_asset.bytes", StringComparison.OrdinalIgnoreCase));

	public TextAssetRef? FindCachedGate(string gameRoot)
	{
		string text = IndexService.FindLocalRoot(gameRoot);
		if (text == null)
		{
			return null;
		}
		string path = GateCachePath(text);
		string folder = Path.GetDirectoryName(path)!;
		// Earlier builds used different path casing / cache keys and some wrote a
		// build ID on line two. Reuse only a cache that resolves inside this account
		// and matches the currently known dictionary family.
		var candidates = new[] { path }.Concat(Directory.Exists(folder)
			? Directory.EnumerateFiles(folder, "overframe_gate_*.txt") : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (string candidate in candidates)
		{
			try
			{
				if (!File.Exists(candidate)) continue;
				string location = File.ReadLines(candidate).FirstOrDefault()?.Trim() ?? "";
				if (File.Exists(location) && ModRecoveryStore.IsUnder(location, text)
					&& MatchesFamily(location, GameCardCatalogUpdater.ReadCurrentFamilyVersion(gameRoot, _catalogCacheDirectory)))
				{
					var gate = _engine.FindTextAssetFast(location, text, GateName);
					if (gate != null) return gate;
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
		}
		return null;
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

	internal (TextAssetRef Gate, byte[] Data)? PrepareRegistrationRestore(string gameRoot, IReadOnlyCollection<ushort> cards)
	{
		if (cards.Count == 0) return null;
		var selected = cards.ToHashSet();
		TextAssetRef gate = FindGate(gameRoot);
		var current = Parse(gate.Data);
		string backup = BackupPath(gameRoot, gate);
		if (!File.Exists(backup))
		{
			if (!current.Any(row => selected.Contains(row.CardId))) return null;
			throw new FileNotFoundException("缺少当前版本的超框登记原始备份，无法安全区分官方超框与 Mod；尚未还原任何贴图。", backup);
		}
		TextAssetRef original = _engine.FindTextAssetFast(backup, Path.Combine(gameRoot, "_MD卡图备份", "超框开关"), GateName)
			?? throw new InvalidDataException("超框登记备份无法读取，尚未开始还原。");
		var rows = Parse(original.Data).Where(row => selected.Contains(row.CardId)).ToArray();
		var activeRows = current.Where(row => selected.Contains(row.CardId)).OrderBy(row => row.CardId).ThenBy(row => row.ArtId);
		if (activeRows.SequenceEqual(rows.OrderBy(row => row.CardId).ThenBy(row => row.ArtId))) return null;
		// Restore just the selected rows; never restore the entire shared table.
		current.RemoveAll(row => selected.Contains(row.CardId));
		current.AddRange(rows);
		return (gate, ModRecoveryService.EncodeMappings(current));
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
