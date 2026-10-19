using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MdCardModTool;

internal sealed class TextureModRestoreService
{
    private readonly ModEngine _engine = new();
    private readonly OverFrameService _frames;
    private readonly ModRecoveryStore _store;

    internal TextureModRestoreService(ModRecoveryStore? store = null, OverFrameService? frames = null)
    {
        _store = store ?? new ModRecoveryStore();
        _frames = frames ?? new OverFrameService();
    }

    private sealed record Pending(string Target, string Stage, string Original, string Hash);

    internal void Restore(string gameRoot, IReadOnlyList<TexRef> selection, Action<int>? afterCommit = null)
    {
        if (selection.Count == 0) throw new ArgumentException("请先选择要还原的 Mod。");
        if (selection.Any(texture => !IndexWorkspaceGuard.Belongs(gameRoot, texture)))
            throw new InvalidOperationException("目标账号或目录已切换，请重新选择资源。");
        var selected = selection.DistinctBy(texture => Path.GetFullPath(texture.BundlePath), StringComparer.OrdinalIgnoreCase).ToArray();
        var cards = selection.Where(CardTextureRules.HasCardIdentity).Select(texture => ushort.Parse(texture.CardKey)).Distinct().ToArray();
        var registration = _frames.PrepareRegistrationRestore(gameRoot, cards);
        var pending = new List<Pending>();
        bool keep = false;
        string staging = Path.Combine(gameRoot, "_MD卡图备份", "还原事务", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var texture in selected)
            {
                string backup = Path.Combine(gameRoot, "_MD卡图备份", texture.SourceKind, texture.RelativeBundlePath);
                if (!File.Exists(backup)) throw new FileNotFoundException("该 Bundle 尚未备份，未开始还原。", backup);
                DirectModArchive.EnsureNoLinks(backup);
                var write = Stage(texture.BundlePath);
                File.Copy(backup, write.Stage, true);
                if (_engine.ScanBundle(write.Stage, staging, texture.SourceKind, false).Textures.Count == 0)
                    throw new InvalidDataException("贴图备份无法解析，未开始还原：" + backup);
            }
            if (registration.HasValue)
            {
                var plan = registration.Value;
                var write = Stage(plan.Gate.BundlePath);
                var stagedGate = _engine.FindTextAssetFast(write.Stage, staging, OverFrameService.GateName)
                    ?? throw new InvalidDataException("超框登记表无法读取。");
                _engine.ReplaceTextAsset(stagedGate, plan.Data, Path.Combine(staging, "stage-backups"));
                var check = _engine.FindTextAssetFast(write.Stage, staging, OverFrameService.GateName);
                if (check == null || !check.Data.SequenceEqual(plan.Data)) throw new InvalidDataException("超框登记还原验证失败。");
            }
            using var lease = AnimationWriteLease.Acquire(pending.Select(write => write.Target));
            foreach (var write in pending)
            {
                var access = lease.Streams[write.Target];
                if (Hash(write.Target, access) != write.Hash) throw new IOException("资源已被其他程序修改，未开始还原。");
            }
            var committed = new List<Pending>();
            try
            {
                foreach (var write in pending)
                {
                    if (Hash(write.Target, lease.Streams[write.Target]) != write.Hash) throw new IOException("资源在还原期间发生变化。");
                    // Atomic per-file replacement; the transaction compensates
                    // already committed files if any later step fails.
                    File.Replace(write.Stage, write.Target, null);
                    committed.Add(write);
                    afterCommit?.Invoke(committed.Count);
                }
                _store.DeactivateBundles(gameRoot, selection);
            }
            catch (Exception error)
            {
                var failures = new List<Exception>();
                foreach (var write in committed.AsEnumerable().Reverse())
                    try { File.Replace(write.Original, write.Target, null); } catch (Exception ex) { failures.Add(ex); }
                if (failures.Count > 0)
                {
                    keep = true;
                    throw new AggregateException("还原回滚未完成，请保留事务目录：" + staging, failures.Prepend(error));
                }
                throw new IOException("还原失败；贴图与超框开关已一起回滚。", error);
            }
        }
        finally { if (!keep && Directory.Exists(staging)) Directory.Delete(staging, true); }

        Pending Stage(string target)
        {
            DirectModArchive.EnsureNoLinks(target);
            string name = pending.Count.ToString("D5");
            string original = Path.Combine(staging, name + ".original");
            string stage = Path.Combine(staging, name + ".bundle");
            string before = Hash(target);
            File.Copy(target, original); File.Copy(original, stage);
            if (Hash(original) != before || Hash(target) != before) throw new IOException("资源正在更新，未开始还原。");
            var write = new Pending(target, stage, original, before);
            pending.Add(write);
            return write;
        }
    }

    private static string Hash(string path, Stream? stream = null)
    {
        if (stream != null) { stream.Position = 0; return Convert.ToHexString(SHA256.HashData(stream)); }
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
