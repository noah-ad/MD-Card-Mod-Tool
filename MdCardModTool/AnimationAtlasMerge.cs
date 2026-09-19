using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class AnimationAtlasMerge
{
    private sealed record Page(string Name, Dictionary<string, string> Header, string[] Regions, int Width, int Height);

    // Pack whole pages without resampling or alpha compositing. Preserve PMA payload,
    // rotation, trim and mesh coordinates; only atlas-space positions change.
    internal static (Image<Rgba32> Image, string Atlas) Merge(string atlas, string targetName, Func<string, byte[]> readPng)
    {
        string[] sections = Regex.Split(atlas.Trim().Replace("\r", ""), @"\n+(?=[^\n:]+\.png\s*\n)");
        var pages = sections.Select(Parse).ToArray();
        if (pages.Length == 0 || pages.Length > 32) throw new InvalidDataException("动画图集页数无效。");
        foreach (var page in pages.Skip(1))
            if (!page.Header.Where(p => p.Key != "size").OrderBy(p => p.Key).SequenceEqual(pages[0].Header.Where(p => p.Key != "size").OrderBy(p => p.Key)))
                throw new InvalidDataException("来源图集各页的 PMA、缩放或采样属性不一致，不能安全合并。");
        int cellW = pages.Max(p => p.Width), cellH = pages.Max(p => p.Height);
        var layouts = Enumerable.Range(1, pages.Length)
            .Select(c => (Columns: c, W: (long)c * cellW, H: (long)((pages.Length + c - 1) / c) * cellH))
            .Where(p => p.W <= 8192 && p.H <= 8192).OrderBy(p => p.W * p.H).ThenBy(p => Math.Max(p.W, p.H)).ToArray();
        if (layouts.Length == 0) throw new InvalidDataException("多页图集按原尺寸合并后超过 8192 上限，未修改目标动画。");
        var layout = layouts[0];
        var merged = new Image<Rgba32>((int)layout.W, (int)layout.H);
        try
        {
            var result = new StringBuilder(targetName + ".png\n");
            foreach (var header in pages[0].Header)
                result.AppendLine(header.Key + ":" + (header.Key == "size" ? $"{layout.W},{layout.H}" : header.Value));
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < pages.Length; i++)
            {
                var page = pages[i]; int dx = i % layout.Columns * cellW, dy = i / layout.Columns * cellH;
                using var image = Image.Load<Rgba32>(readPng(page.Name));
                if (image.Width != page.Width || image.Height != page.Height) throw new InvalidDataException("图集纹理与声明尺寸不匹配：" + page.Name);
                image.ProcessPixelRows(merged, (src, dst) =>
                {
                    for (int y = 0; y < src.Height; y++) src.GetRowSpan(y).CopyTo(dst.GetRowSpan(y + dy).Slice(dx, src.Width));
                });
                bool hasPosition = true;
                foreach (string line in page.Regions)
                {
                    string trimmed = line.Trim(); if (trimmed.Length == 0) continue;
                    int colon = trimmed.IndexOf(':');
                    if (colon < 0)
                    {
                        if (!hasPosition) throw new InvalidDataException("图集区域缺少位置。");
                        if (!names.Add(trimmed)) throw new InvalidDataException("图集存在重复区域名：" + trimmed);
                        hasPosition = false; result.AppendLine(line); continue;
                    }
                    string key = trimmed[..colon].Trim();
                    if (key == "bounds" || key == "xy")
                    {
                        var values = trimmed[(colon + 1)..].Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
                        if (values.Length != (key == "bounds" ? 4 : 2) || values[0] < 0 || values[1] < 0 || values[0] >= page.Width || values[1] >= page.Height)
                            throw new InvalidDataException("图集区域坐标无效。");
                        values[0] += dx; values[1] += dy; hasPosition = true;
                        result.AppendLine(key + ":" + string.Join(",", values));
                    }
                    else result.AppendLine(line);
                }
                if (!hasPosition) throw new InvalidDataException("图集区域缺少位置。");
            }
            return (merged, result.ToString());
        }
        catch { merged.Dispose(); throw; }
    }

    private static Page Parse(string section)
    {
        var lines = section.Trim().Split('\n');
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        int i = 1;
        for (; i < lines.Length && lines[i].Contains(':'); i++)
        {
            int split = lines[i].IndexOf(':');
            header.Add(lines[i][..split].Trim(), lines[i][(split + 1)..].Trim());
        }
        if (!header.TryGetValue("size", out var size)) throw new InvalidDataException("图集缺少页尺寸。");
        var dimensions = size.Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (dimensions.Length != 2 || dimensions.Any(d => d <= 0 || d > 8192)) throw new InvalidDataException("图集页尺寸超出安全范围。");
        return new Page(lines[0].Trim(), header, lines[i..], dimensions[0], dimensions[1]);
    }
}
