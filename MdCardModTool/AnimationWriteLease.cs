using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MdCardModTool;

internal sealed class AnimationWriteLease : IDisposable
{
	public Dictionary<string, FileStream> Streams { get; } = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

	public static AnimationWriteLease Acquire(IEnumerable<string> paths)
	{
		DirectModArchive.EnsureGameClosed();
		AnimationWriteLease animationWriteLease = new AnimationWriteLease();
		string text = "";
		try
		{
			foreach (string item in from p in paths.Distinct<string>(StringComparer.OrdinalIgnoreCase)
				orderby p
				select p)
			{
				text = item;
				int num = 0;
				while (true)
				{
					try
					{
						animationWriteLease.Streams.Add(item, new FileStream(item, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete));
					}
					catch (IOException ex) when ((uint)((ex.HResult & 0xFFFF) - 32) <= 1u && num < 10)
					{
						Thread.Sleep(100);
						goto IL_00b5;
					}
					break;
					IL_00b5:
					num++;
				}
			}
			return animationWriteLease;
		}
		catch (Exception innerException)
		{
			animationWriteLease.Dispose();
			throw new IOException("动画文件无法写入（可能被游戏、扫描或其他程序占用）：" + text + "\n请退出游戏并等待扫描结束后重试。尚未开始本次提交。", innerException);
		}
	}

	public static void Preflight(MonsterAnimationSet set)
	{
		using (Acquire(set.Assets.Select((MonsterAnimationAssetRef a) => a.BundlePath)))
		{
		}
	}

	public void Dispose()
	{
		foreach (FileStream value in Streams.Values)
		{
			value.Dispose();
		}
		Streams.Clear();
	}
}
