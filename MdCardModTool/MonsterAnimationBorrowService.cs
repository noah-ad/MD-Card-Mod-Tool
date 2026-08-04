using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MdCardModTool;

public sealed record BorrowedMonsterAnimation(
    string TargetCardId,
    string DonorCardId,
    string Region,
    List<string> CreatedBundlePaths,
    bool IsIndependent = false);

/// <summary>
/// Adds a current-version summon-animation registration to a card that did not originally have
/// one. Read-only borrowing clones four entry bundles and shares dependencies. Independent mode
/// clones the complete dependency graph so a dropped GIF/video can safely overwrite the target.
/// </summary>
public sealed class MonsterAnimationBorrowService
{
    public const string BackupKind = "召唤动画借用";
    public const string CurrentGateLogicalPath = "Duel/ScriptableObject/CardIndividualData";
    public const string LegacyGateLogicalPath = "Duel/Data/CardIndividual";
    const string RecordsFileName = "borrowed-animation.json";
    readonly ModEngine _engine = new();

    public BorrowedMonsterAnimation? Find(string gameRoot, string targetCardId) =>
        LoadRecords(gameRoot).FirstOrDefault(x => x.TargetCardId == targetCardId);

    public IReadOnlyList<BorrowedMonsterAnimation> List(string gameRoot) => LoadRecords(gameRoot);

    public bool IsBorrowed(string gameRoot, string targetCardId) => Find(gameRoot, targetCardId) is not null;

    public bool IsReadOnlyBorrowed(string gameRoot, string targetCardId) => Find(gameRoot, targetCardId) is { IsIndependent: false };

    public BorrowedMonsterAnimation Install(string gameRoot, string targetCardId, string donorCardId) =>
        InstallCore(gameRoot, targetCardId, donorCardId, independent: false);

    public BorrowedMonsterAnimation InstallIndependent(string gameRoot, string targetCardId, string? donorCardId = null)
    {
        donorCardId = string.IsNullOrWhiteSpace(donorCardId) ? FindAutomaticDonor(gameRoot, targetCardId) : donorCardId;
        return InstallCore(gameRoot, targetCardId, donorCardId, independent: true);
    }

    public string FindAutomaticDonor(string gameRoot, string targetCardId)
    {
        ValidateCardId(targetCardId, nameof(targetCardId));
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var installed = RegisteredCardIds(gameRoot).Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var preferred = new[] { "22746", "3899", "10001", "13675" };
        foreach (var donor in preferred.Concat(installed.OrderBy(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture))))
        {
            if (donor == targetCardId || !installed.Contains(donor)) continue;
            try { _ = ResolveEntryBundles(localRoot, targetCardId, donor); return donor; }
            catch { }
        }
        throw new InvalidDataException("本机没有找到可用的完整召唤动画模板。请先在游戏中下载任意一张带召唤演出的卡资源。");
    }

    BorrowedMonsterAnimation InstallCore(string gameRoot, string targetCardId, string donorCardId, bool independent)
    {
        ValidateCardId(targetCardId, nameof(targetCardId));
        ValidateCardId(donorCardId, nameof(donorCardId));
        if (targetCardId == donorCardId) throw new InvalidOperationException("目标卡和供体卡不能相同。");
        OverFrameService.EnsureGameStopped(gameRoot);
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        if (Find(gameRoot, targetCardId) is not null) throw new InvalidOperationException($"卡号 {targetCardId} 已经借用了其他卡的召唤动画。");

        var gatePath = FindGatePath(localRoot);
        var table = _engine.ReadMonsterCutinTable(gatePath);
        var targetId = int.Parse(targetCardId, System.Globalization.CultureInfo.InvariantCulture);
        if (table.Contains(targetId)) throw new InvalidOperationException($"卡号 {targetCardId} 已在游戏原生 monsterCutinTable 中，不应使用借用登记。");

        var (region, pairs) = ResolveEntryBundles(localRoot, targetCardId, donorCardId);
        var plans = independent ? BuildIndependentClonePlans(localRoot, pairs, targetCardId) : pairs.Select(ClonePlan.FromEntry).ToList();
        if (plans.Any(x => File.Exists(x.TargetPath))) throw new IOException("目标卡已有部分动画 Bundle。请先还原或移走手工 Mod，避免混合覆盖。");

        var catalogPath = FindCatalog(localRoot);
        var staging = Path.Combine(Path.GetTempPath(), "MDCardModTool", "borrow_cutin_" + Guid.NewGuid().ToString("N"));
        var rollback = Path.Combine(staging, "rollback");
        Directory.CreateDirectory(rollback);
        var gateRollback = Path.Combine(rollback, "gate.bundle");
        var catalogRollback = Path.Combine(rollback, "catalog.json");
        File.Copy(gatePath, gateRollback, true);
        File.Copy(catalogPath, catalogRollback, true);
        var created = new List<string>();
        try
        {
            var dependencyMap = plans.ToDictionary(x => NormalizeRelative(x.SourceRelative), x => NormalizeRelative(x.TargetRelative), StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans)
            {
                var stagedPath = ResolveInside(staging, plan.TargetRelative);
                _engine.CloneAnimationBundle(plan.SourcePath, stagedPath, staging, donorCardId, targetCardId, dependencyMap);
            }

            EnsureBackup(gameRoot, localRoot, gatePath);
            EnsureBackup(gameRoot, localRoot, catalogPath);
            foreach (var plan in plans)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.TargetPath)!);
                File.Move(ResolveInside(staging, plan.TargetRelative), plan.TargetPath);
                created.Add(NormalizeRelative(plan.TargetRelative));
            }
            _engine.SetMonsterCutinRegistration(gatePath, localRoot, targetId, true, Path.Combine(gameRoot, "_MD卡图备份", BackupKind));
            UpdateCatalog(catalogPath, localRoot, gatePath, plans);

            var record = new BorrowedMonsterAnimation(targetCardId, donorCardId, region, created, independent);
            var records = LoadRecords(gameRoot);
            records.RemoveAll(x => x.TargetCardId == targetCardId);
            records.Add(record);
            SaveRecords(gameRoot, records);
            return record;
        }
        catch
        {
            try { File.Copy(gateRollback, gatePath, true); } catch { }
            try { File.Copy(catalogRollback, catalogPath, true); } catch { }
            foreach (var plan in plans) try { if (File.Exists(plan.TargetPath)) File.Delete(plan.TargetPath); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    public bool Remove(string gameRoot, string targetCardId)
    {
        ValidateCardId(targetCardId, nameof(targetCardId));
        OverFrameService.EnsureGameStopped(gameRoot);
        var records = LoadRecords(gameRoot);
        var record = records.FirstOrDefault(x => x.TargetCardId == targetCardId);
        if (record is null) return false;
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        var gatePath = FindGatePath(localRoot);
        var catalogPath = FindCatalog(localRoot);
        var staging = Path.Combine(Path.GetTempPath(), "MDCardModTool", "remove_cutin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var gateRollback = Path.Combine(staging, "gate.bundle");
        var catalogRollback = Path.Combine(staging, "catalog.json");
        File.Copy(gatePath, gateRollback, true);
        File.Copy(catalogPath, catalogRollback, true);
        var moved = new List<(string Live, string Rollback)>();
        try
        {
            foreach (var relative in record.CreatedBundlePaths)
            {
                var live = ResolveInside(localRoot, relative);
                if (!File.Exists(live)) continue;
                var saved = Path.Combine(staging, "created_" + moved.Count.ToString("D2") + ".bundle");
                File.Move(live, saved);
                moved.Add((live, saved));
            }
            _engine.SetMonsterCutinRegistration(
                gatePath,
                localRoot,
                int.Parse(targetCardId, System.Globalization.CultureInfo.InvariantCulture),
                false,
                Path.Combine(gameRoot, "_MD卡图备份", BackupKind));
            RemoveCatalogEntries(catalogPath, localRoot, gatePath, record.CreatedBundlePaths);
            records.RemoveAll(x => x.TargetCardId == targetCardId);
            SaveRecords(gameRoot, records);
            return true;
        }
        catch
        {
            try { File.Copy(gateRollback, gatePath, true); } catch { }
            try { File.Copy(catalogRollback, catalogPath, true); } catch { }
            foreach (var item in moved) try { Directory.CreateDirectory(Path.GetDirectoryName(item.Live)!); File.Move(item.Rollback, item.Live, true); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    public IReadOnlyList<int> RegisteredCardIds(string gameRoot)
    {
        var localRoot = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到 LocalData\\<用户哈希>\\0000。");
        return _engine.ReadMonsterCutinTable(FindGatePath(localRoot));
    }

    static (string Region, List<EntryBundlePair> Pairs) ResolveEntryBundles(string localRoot, string targetCardId, string donorCardId)
    {
        foreach (var region in new[] { "tcg", "ocg" })
        {
            var pairs = new List<EntryBundlePair>();
            foreach (var tier in new[] { "SD", "HighEnd_HD" })
            foreach (var suffix in new[] { "", "JS" })
            {
                var sourceRelative = AnimationEntryRelative(donorCardId, region, tier, suffix);
                var targetRelative = AnimationEntryRelative(targetCardId, region, tier, suffix);
                pairs.Add(new EntryBundlePair(
                    Path.Combine(localRoot, sourceRelative),
                    Path.Combine(localRoot, targetRelative),
                    sourceRelative,
                    targetRelative));
            }
            if (pairs.All(x => File.Exists(x.SourcePath))) return (region, pairs);
        }
        throw new InvalidDataException($"供体卡 {donorCardId} 缺少完整的 SD / HighEnd_HD 入口与 JS Bundle，不能借用。");
    }

    static string AnimationEntryRelative(string cardId, string region, string tier, string suffix) =>
        IndexService.ResourceBundleRelativePath($"Duel/Timeline/Duel/MonsterCutIn/{region}/P{cardId}/{tier}/P{cardId}{suffix}");

    List<ClonePlan> BuildIndependentClonePlans(string localRoot, IReadOnlyList<EntryBundlePair> entries, string targetCardId)
    {
        var entryTargets = entries.ToDictionary(
            x => NormalizeRelative(x.SourceRelative),
            x => NormalizeRelative(x.TargetRelative),
            StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(entryTargets.Keys);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            var relative = NormalizeRelative(queue.Dequeue());
            if (!visited.Add(relative)) continue;
            if (visited.Count > 160) throw new InvalidDataException("动画模板依赖超过 160 个 Bundle，已停止自动复制。");
            var source = ResolveInside(localRoot, relative);
            if (!File.Exists(source)) throw new FileNotFoundException("动画模板缺少依赖 Bundle。", source);
            foreach (var dependency in _engine.ReadBundleDependencies(source))
            {
                var normalized = NormalizeRelative(dependency);
                if (File.Exists(ResolveInside(localRoot, normalized)) && !visited.Contains(normalized)) queue.Enqueue(normalized);
            }
        }

        return visited.Select(sourceRelative =>
        {
            var targetRelative = entryTargets.GetValueOrDefault(sourceRelative) ?? IndependentRelative(targetCardId, sourceRelative);
            return new ClonePlan(
                ResolveInside(localRoot, sourceRelative),
                ResolveInside(localRoot, targetRelative),
                sourceRelative,
                targetRelative);
        }).OrderByDescending(x => entryTargets.ContainsKey(NormalizeRelative(x.SourceRelative))).ThenBy(x => x.SourceRelative, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string IndependentRelative(string targetCardId, string sourceRelative)
    {
        var identity = Encoding.UTF8.GetBytes($"MDCardModTool/MonsterCutIn/P{targetCardId}/{NormalizeRelative(sourceRelative)}");
        var hash = Convert.ToHexString(SHA256.HashData(identity))[..16].ToLowerInvariant();
        return $"{hash[..2]}/{hash}";
    }

    static string NormalizeRelative(string relative) => relative.Replace('\\', '/');

    static string FindGatePath(string localRoot)
    {
        foreach (var logical in new[] { CurrentGateLogicalPath, LegacyGateLogicalPath })
        {
            var path = Path.Combine(localRoot, IndexService.ResourceBundleRelativePath(logical));
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("没有找到 CardIndividualData。当前游戏资源结构可能已更新。");
    }

    static string FindCatalog(string localRoot)
    {
        var cache = CatalogCachePath(localRoot);
        if (File.Exists(cache))
        {
            try
            {
                var candidate = File.ReadAllText(cache).Trim();
                if (File.Exists(candidate) && IsCatalog(candidate)) return candidate;
            }
            catch { }
        }
        foreach (var path in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var length = new FileInfo(path).Length;
                if (length is < 1_000_000 or > 32_000_000 || !IsCatalog(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
                File.WriteAllText(cache, path);
                return path;
            }
            catch { }
        }
        throw new FileNotFoundException("没有找到 LocalData 的 informations 资源目录，无法登记新动画 Bundle。");
    }

    static bool IsCatalog(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        var read = stream.Read(header);
        return read >= 16 && Encoding.UTF8.GetString(header[..read]).StartsWith("{\"informations\":", StringComparison.Ordinal);
    }

    static void UpdateCatalog(string catalogPath, string localRoot, string gatePath, IReadOnlyList<ClonePlan> plans)
    {
        var root = ReadCatalog(catalogPath);
        var array = root["informations"]!.AsArray();
        var entries = array.OfType<JsonObject>().ToDictionary(x => x["assetName"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            var sourceName = NormalizeRelative(plan.SourceRelative);
            var targetName = NormalizeRelative(plan.TargetRelative);
            var version = entries.GetValueOrDefault(sourceName)?["version"]?.GetValue<string>() ?? DateTime.Now.ToString("yyyyMMddHHmm");
            UpsertCatalogEntry(array, entries, targetName, plan.TargetPath, version);
        }
        var gateRelative = Path.GetRelativePath(localRoot, gatePath).Replace('\\', '/');
        var gateVersion = entries.GetValueOrDefault(gateRelative)?["version"]?.GetValue<string>() ?? DateTime.Now.ToString("yyyyMMddHHmm");
        UpsertCatalogEntry(array, entries, gateRelative, gatePath, gateVersion);
        WriteCatalog(catalogPath, root);
    }

    static void RemoveCatalogEntries(string catalogPath, string localRoot, string gatePath, IReadOnlyCollection<string> created)
    {
        var root = ReadCatalog(catalogPath);
        var array = root["informations"]!.AsArray();
        var remove = created.Select(x => x.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = array.Count - 1; i >= 0; i--)
            if (array[i] is JsonObject item && remove.Contains(item["assetName"]?.GetValue<string>() ?? "")) array.RemoveAt(i);
        var entries = array.OfType<JsonObject>().ToDictionary(x => x["assetName"]?.GetValue<string>() ?? "", StringComparer.OrdinalIgnoreCase);
        var gateRelative = Path.GetRelativePath(localRoot, gatePath).Replace('\\', '/');
        var version = entries.GetValueOrDefault(gateRelative)?["version"]?.GetValue<string>() ?? DateTime.Now.ToString("yyyyMMddHHmm");
        UpsertCatalogEntry(array, entries, gateRelative, gatePath, version);
        WriteCatalog(catalogPath, root);
    }

    static JsonObject ReadCatalog(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("informations 资源目录无法读取。");
        if (root["informations"] is not JsonArray) throw new InvalidDataException("informations 资源目录格式不受支持。");
        return root;
    }

    static void UpsertCatalogEntry(JsonArray array, Dictionary<string, JsonObject> entries, string relative, string livePath, string version)
    {
        if (!entries.TryGetValue(relative, out var item))
        {
            item = new JsonObject { ["assetName"] = relative };
            array.Add(item);
            entries[relative] = item;
        }
        item["version"] = version;
        item["bytes"] = new FileInfo(livePath).Length;
        item["crc"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(livePath))).ToLowerInvariant();
    }

    static void WriteCatalog(string path, JsonObject root)
    {
        var temporary = path + ".mdcardtool.catalog.tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    static void EnsureBackup(string gameRoot, string localRoot, string livePath)
    {
        var backup = Path.Combine(gameRoot, "_MD卡图备份", BackupKind, Path.GetRelativePath(localRoot, livePath));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        if (!File.Exists(backup)) File.Copy(livePath, backup);
    }

    static List<BorrowedMonsterAnimation> LoadRecords(string gameRoot)
    {
        var path = RecordsPath(gameRoot);
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<BorrowedMonsterAnimation>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }
    }

    static void SaveRecords(string gameRoot, List<BorrowedMonsterAnimation> records)
    {
        var path = RecordsPath(gameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records.OrderBy(x => int.Parse(x.TargetCardId)).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    static string RecordsPath(string gameRoot) => Path.Combine(gameRoot, "_MD卡图备份", BackupKind, RecordsFileName);

    static string CatalogCachePath(string localRoot)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(localRoot))))[..12];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool", $"asset_catalog_{id}.txt");
    }

    static string ResolveInside(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("动画 Bundle 路径不能是绝对路径。");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("动画 Bundle 路径越出了目标目录。");
        return full;
    }

    static void ValidateCardId(string cardId, string parameter)
    {
        if (cardId.Length == 0 || !cardId.All(char.IsAsciiDigit) || !int.TryParse(cardId, out var id) || id is < 1 or > ushort.MaxValue)
            throw new ArgumentException("卡号必须是 1 到 65535 的纯数字。", parameter);
    }

    sealed record EntryBundlePair(string SourcePath, string TargetPath, string SourceRelative, string TargetRelative);
    sealed record ClonePlan(string SourcePath, string TargetPath, string SourceRelative, string TargetRelative)
    {
        public static ClonePlan FromEntry(EntryBundlePair pair) => new(pair.SourcePath, pair.TargetPath, pair.SourceRelative, pair.TargetRelative);
    }
}
