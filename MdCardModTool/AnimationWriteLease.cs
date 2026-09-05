using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MdCardModTool;

internal sealed class AnimationWriteLease : IDisposable
{
    public Dictionary<string, FileStream> Streams { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static AnimationWriteLease Acquire(IEnumerable<string> paths)
    {
        DirectModArchive.EnsureGameClosed();
        var lease = new AnimationWriteLease();
        string current = "";
        try
        {
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p))
            {
                current = path;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        // Deny new preview/scanner handles for the short commit window,
                        // but allow atomic replacement while retaining the old file handle.
                        lease.Streams.Add(path, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete));
                        break;
                    }
                    catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 && attempt < 10)
                    { Thread.Sleep(100); }
                }
            }
            return lease;
        }
        catch (Exception error)
        {
            lease.Dispose();
            throw new IOException("动画文件无法写入（可能被游戏、扫描或其他程序占用）：" + current
                + "\n请退出游戏并等待扫描结束后重试。尚未开始本次提交。", error);
        }
    }
    public static void Preflight(MonsterAnimationSet set)
    { using var lease = Acquire(set.Assets.Select(a => a.BundlePath)); }
    public void Dispose() { foreach (var stream in Streams.Values) stream.Dispose(); Streams.Clear(); }
}
