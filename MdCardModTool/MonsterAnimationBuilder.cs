using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public static class MonsterAnimationBuilder
{
	private sealed class AtlasRegion
	{
		public int Index { get; init; }

		public string Name { get; init; } = "";

		public string SourcePath { get; init; } = "";

		public int OriginalWidth { get; init; }

		public int OriginalHeight { get; init; }

		public int SourceX { get; init; }

		public int SourceY { get; init; }

		public int Width { get; init; }

		public int Height { get; init; }

		public int X { get; set; }

		public int Y { get; set; }
	}

	private sealed record PackingCandidate(int Width, int Height, List<(AtlasRegion Region, int X, int Y)> Placements);

	private const int Padding = 2;
	private const int SdCanvasWidth = 854;
	private const int SdCanvasHeight = 480;

	private static readonly int[] AutomaticFrameEdges = new int[10] { 1920, 1600, 1280, 1024, 896, 768, 640, 512, 384, 256 };

	public const double GameCanvasWidth = 4800.0;

	public const double GameCanvasHeight = 2700.0;

	public static int ChooseAutomaticFrameEdge(int frameCount, int sourceWidth, int sourceHeight, int maxAtlasEdge)
	{
		if (frameCount < 1)
		{
			throw new ArgumentOutOfRangeException("frameCount");
		}
		if (sourceWidth < 1)
		{
			throw new ArgumentOutOfRangeException("sourceWidth");
		}
		if (sourceHeight < 1)
		{
			throw new ArgumentOutOfRangeException("sourceHeight");
		}
		bool flag;
		switch (maxAtlasEdge)
		{
		case 2048:
		case 4096:
		case 8192:
		case 16384:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("maxAtlasEdge");
		}
		int[] automaticFrameEdges = AutomaticFrameEdges;
		foreach (int edge in automaticFrameEdges)
		{
			var (width, height) = ScaleToLongestEdge(sourceWidth, sourceHeight, edge);
			if (CanPackUniformFrames(frameCount, width, height, maxAtlasEdge))
			{
				return edge;
			}
		}
		throw new InvalidOperationException($"{frameCount:N0} 帧无法放进单张 {maxAtlasEdge}×{maxAtlasEdge} 图集。请降低帧率、时长或最多读取帧数。");
	}

	public static bool CanPackUniformFrames(int frameCount, int frameWidth, int frameHeight, int maxAtlasEdge)
	{
		if (frameCount < 1 || frameWidth < 1 || frameHeight < 1)
		{
			return false;
		}
		for (int atlasWidth = 256; atlasWidth <= maxAtlasEdge; atlasWidth *= 2)
		{
			if (frameWidth + 4 <= atlasWidth)
			{
				int x = 2;
				int y = 2;
				int rowHeight = 0;
				bool failed = false;
				for (int i = 0; i < frameCount; i++)
				{
					if (x + frameWidth + 2 > atlasWidth)
					{
						x = 2;
						y += rowHeight + 2;
						rowHeight = 0;
					}
					if (y + frameHeight + 2 > maxAtlasEdge)
					{
						failed = true;
						break;
					}
					x += frameWidth + 2;
					rowHeight = Math.Max(rowHeight, frameHeight);
				}
				if (!failed && NextPowerOfTwo(y + rowHeight + 2) <= maxAtlasEdge)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static (int Width, int Height) ScaleToLongestEdge(int width, int height, int edge)
	{
		double ratio = (double)edge / (double)Math.Max(width, height);
		return (Width: Math.Max(1, (int)Math.Round((double)width * ratio)), Height: Math.Max(1, (int)Math.Round((double)height * ratio)));
	}

	public static MonsterAnimationBuildResult Build(IReadOnlyList<string> framePaths, string cardId, int framesPerSecond, int scalePercent, int maxAtlasEdge = 4096, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Build(framePaths, cardId, framesPerSecond, scalePercent, new MonsterAnimationTemplate("3.8.75", "animation", 0.0, 0.0, 0.0, 0.0), maxAtlasEdge, cancellationToken);
	}

	public static MonsterAnimationBuildResult Build(IReadOnlyList<string> framePaths, string cardId, int framesPerSecond, int scalePercent, MonsterAnimationTemplate template, int maxAtlasEdge = 4096, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (framePaths.Count == 0)
		{
			throw new InvalidOperationException("没有可打包的动画帧。");
		}
		if ((framesPerSecond < 1 || framesPerSecond > 60) ? true : false)
		{
			throw new ArgumentOutOfRangeException("framesPerSecond");
		}
		if ((scalePercent < 10 || scalePercent > 500) ? true : false)
		{
			throw new ArgumentOutOfRangeException("scalePercent");
		}
		bool flag;
		switch (maxAtlasEdge)
		{
		case 2048:
		case 4096:
		case 8192:
		case 16384:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("maxAtlasEdge");
		}
		int longestEdge = 0;
		for (int i = 0; i < framePaths.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using Image<Rgba32> frame = Image.Load<Rgba32>(framePaths[i]);
			longestEdge = Math.Max(longestEdge, Math.Max(frame.Width, frame.Height));
		}
		int hdCanvasWidth = Math.Max(64, longestEdge);
		int hdCanvasHeight = Math.Max(36, (int)Math.Round(hdCanvasWidth * 9d / 16d));
		int sdCanvasWidth = Math.Min(SdCanvasWidth, hdCanvasWidth);
		int sdCanvasHeight = Math.Min(SdCanvasHeight, hdCanvasHeight);
		double displayWidth = GameCanvasWidth * scalePercent / 100d;
		double displayHeight = GameCanvasHeight * scalePercent / 100d;
		MonsterAnimationTierBuildResult? hd = null;
		MonsterAnimationTierBuildResult? sd = null;
		try
		{
			hd = BuildTier(framePaths, cardId, framesPerSecond, template, maxAtlasEdge,
				"HighEnd_HD", hdCanvasWidth, hdCanvasHeight, displayWidth, displayHeight, cancellationToken);
			sd = BuildTier(framePaths, cardId, framesPerSecond, template, maxAtlasEdge,
				"SD", sdCanvasWidth, sdCanvasHeight, displayWidth, displayHeight, cancellationToken);
			return new MonsterAnimationBuildResult
			{
				Hd = hd,
				Sd = sd,
				FrameCount = framePaths.Count,
				FramesPerSecond = framesPerSecond,
				DisplayWidth = displayWidth,
				DisplayHeight = displayHeight
			};
		}
		catch
		{
			hd?.Dispose();
			sd?.Dispose();
			throw;
		}
	}

	private static MonsterAnimationTierBuildResult BuildTier(IReadOnlyList<string> framePaths, string cardId,
		int framesPerSecond, MonsterAnimationTemplate template, int maxAtlasEdge, string tier,
		int canvasWidth, int canvasHeight, double displayWidth, double displayHeight,
		CancellationToken cancellationToken)
	{
		List<AtlasRegion> regions = new(framePaths.Count);
		for (int i = 0; i < framePaths.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using Image<Rgba32> frame = LoadCanvasFrame(framePaths[i], canvasWidth, canvasHeight);
			Rectangle bounds = FindOpaqueBounds(frame);
			regions.Add(new AtlasRegion
			{
				Index = i,
				Name = $"frame_{i}",
				SourcePath = framePaths[i],
				OriginalWidth = canvasWidth,
				OriginalHeight = canvasHeight,
				SourceX = bounds.X,
				SourceY = bounds.Y,
				Width = bounds.Width,
				Height = bounds.Height
			});
		}
		(int, int) packed = Pack(regions, maxAtlasEdge);
		Image<Rgba32> atlas = new(packed.Item1, packed.Item2, Color.Transparent);
		try
		{
			foreach (AtlasRegion region in regions)
			{
				cancellationToken.ThrowIfCancellationRequested();
				using Image<Rgba32> frame = LoadCanvasFrame(region.SourcePath, canvasWidth, canvasHeight);
				using Image<Rgba32> trimmed = frame.Clone(x =>
					x.Crop(new Rectangle(region.SourceX, region.SourceY, region.Width, region.Height)));
				PremultiplyAlpha(trimmed);
				atlas.Mutate(x => x.DrawImage(trimmed, new Point(region.X, region.Y), 1f));
			}
			return new MonsterAnimationTierBuildResult
			{
				Tier = tier,
				AtlasImage = atlas,
				AtlasText = BuildAtlasText(cardId, packed.Item1, packed.Item2, regions),
				SkeletonJson = BuildSkeletonJson(template, cardId, regions, framesPerSecond,
					displayWidth, displayHeight)
			};
		}
		catch
		{
			atlas.Dispose();
			throw;
		}
	}

	private static Image<Rgba32> LoadCanvasFrame(string path, int canvasWidth, int canvasHeight)
	{
		using Image<Rgba32> source = Image.Load<Rgba32>(path);
		double ratio = Math.Min((double)canvasWidth / source.Width, (double)canvasHeight / source.Height);
		int width = Math.Max(1, (int)Math.Round(source.Width * ratio));
		int height = Math.Max(1, (int)Math.Round(source.Height * ratio));
		using Image<Rgba32> resized = source.Clone(x => x.Resize(new ResizeOptions
		{
			Size = new Size(width, height),
			Mode = ResizeMode.Stretch,
			Sampler = KnownResamplers.Lanczos3,
			Compand = true
		}));
		Image<Rgba32> canvas = new(canvasWidth, canvasHeight, Color.Transparent);
		canvas.Mutate(x => x.DrawImage(resized,
			new Point((canvasWidth - width) / 2, (canvasHeight - height) / 2), 1f));
		return canvas;
	}

	private static Rectangle FindOpaqueBounds(Image<Rgba32> image)
	{
		int left = image.Width;
		int top = image.Height;
		int right = -1;
		int bottom = -1;
		image.ProcessPixelRows(delegate(PixelAccessor<Rgba32> accessor)
		{
			for (int i = 0; i < accessor.Height; i++)
			{
				Span<Rgba32> rowSpan = accessor.GetRowSpan(i);
				for (int j = 0; j < rowSpan.Length; j++)
				{
					if (rowSpan[j].A > 3)
					{
						left = Math.Min(left, j);
						top = Math.Min(top, i);
						right = Math.Max(right, j);
						bottom = Math.Max(bottom, i);
					}
				}
			}
		});
		if (right >= left && bottom >= top)
		{
			return new Rectangle(left, top, right - left + 1, bottom - top + 1);
		}
		return new Rectangle(0, 0, 1, 1);
	}

	private static void PremultiplyAlpha(Image<Rgba32> image)
	{
		image.ProcessPixelRows(delegate(PixelAccessor<Rgba32> accessor)
		{
			for (int i = 0; i < accessor.Height; i++)
			{
				Span<Rgba32> rowSpan = accessor.GetRowSpan(i);
				for (int j = 0; j < rowSpan.Length; j++)
				{
					Rgba32 rgba = rowSpan[j];
					rgba.R = (byte)((rgba.R * rgba.A + 127) / 255);
					rgba.G = (byte)((rgba.G * rgba.A + 127) / 255);
					rgba.B = (byte)((rgba.B * rgba.A + 127) / 255);
					rowSpan[j] = rgba;
				}
			}
		});
	}

	private static (int Width, int Height) Pack(List<AtlasRegion> regions, int maxEdge)
	{
		AtlasRegion[] sorted = (from atlasRegion in regions
			orderby atlasRegion.Height descending, atlasRegion.Width descending
			select atlasRegion).ToArray();
		PackingCandidate best = null;
		int width;
		for (width = 256; width <= maxEdge; width *= 2)
		{
			if (sorted.Any((AtlasRegion atlasRegion) => atlasRegion.Width + 4 > width))
			{
				continue;
			}
			List<(AtlasRegion, int, int)> placements = new List<(AtlasRegion, int, int)>();
			int x = 2;
			int y = 2;
			int rowHeight = 0;
			bool failed = false;
			AtlasRegion[] array = sorted;
			foreach (AtlasRegion region in array)
			{
				if (x + region.Width + 2 > width)
				{
					x = 2;
					y += rowHeight + 2;
					rowHeight = 0;
				}
				if (y + region.Height + 2 > maxEdge)
				{
					failed = true;
					break;
				}
				placements.Add((region, x, y));
				x += region.Width + 2;
				rowHeight = Math.Max(rowHeight, region.Height);
			}
			if (failed)
			{
				continue;
			}
			int height = NextPowerOfTwo(y + rowHeight + 2);
			if (height <= maxEdge)
			{
				PackingCandidate candidate = new PackingCandidate(width, Math.Max(16, height), placements);
				if ((object)best == null || (long)candidate.Width * (long)candidate.Height < (long)best.Width * (long)best.Height)
				{
					best = candidate;
				}
			}
		}
		if ((object)best == null)
		{
			throw new InvalidOperationException($"全部帧无法放进单张 {maxEdge}×{maxEdge} 图集。请降低帧率、时长或单帧清晰度。");
		}
		foreach (var placement in best.Placements)
		{
			placement.Region.X = placement.X;
			placement.Region.Y = placement.Y;
		}
		return (Width: best.Width, Height: best.Height);
	}

	private static int NextPowerOfTwo(int value)
	{
		int result;
		for (result = 1; result < value; result <<= 1)
		{
		}
		return result;
	}

	private static string BuildAtlasText(string cardId, int width, int height, IEnumerable<AtlasRegion> regions)
	{
		StringBuilder output = new StringBuilder();
		output.AppendLine();
		StringBuilder stringBuilder = output;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
		handler.AppendLiteral("P");
		handler.AppendFormatted(cardId);
		handler.AppendLiteral(".png");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = output;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder);
		handler.AppendLiteral("size: ");
		handler.AppendFormatted(width);
		handler.AppendLiteral(",");
		handler.AppendFormatted(height);
		stringBuilder3.AppendLine(ref handler);
		output.AppendLine("filter:Linear,Linear");
		output.AppendLine("pma:true");
		output.AppendLine("scale:1");
		foreach (AtlasRegion region in regions.OrderBy((AtlasRegion x) => x.Index))
		{
			output.AppendLine(region.Name);
			stringBuilder = output;
			StringBuilder stringBuilder4 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 4, stringBuilder);
			handler.AppendLiteral("bounds:");
			handler.AppendFormatted(region.X);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.Y);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.Width);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.Height);
			stringBuilder4.AppendLine(ref handler);
			stringBuilder = output;
			StringBuilder stringBuilder5 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder);
			handler.AppendLiteral("offsets:");
			handler.AppendFormatted(region.SourceX);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.OriginalHeight - region.SourceY - region.Height);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.OriginalWidth);
			handler.AppendLiteral(",");
			handler.AppendFormatted(region.OriginalHeight);
			stringBuilder5.AppendLine(ref handler);
		}
		return output.ToString();
	}

	private static byte[] BuildSkeletonJson(MonsterAnimationTemplate template, string cardId,
		IReadOnlyList<AtlasRegion> regions, int framesPerSecond, double width, double height)
	{
		const string slotName = "frame";
		JsonObject attachments = new JsonObject();
		foreach (AtlasRegion region in regions)
		{
			attachments[region.Name] = new JsonObject
			{
				["width"] = Round(width),
				["height"] = Round(height)
			};
		}
		JsonObject animations = new JsonObject();
		foreach (string animationName in template.EffectiveAnimationNames.Distinct<string>(StringComparer.Ordinal))
		{
			animations[string.IsNullOrWhiteSpace(animationName) ? "animation" : animationName] = BuildAttachmentAnimation(slotName, regions, framesPerSecond);
		}
		JsonObject jsonObject = new JsonObject();
		jsonObject["skeleton"] = new JsonObject
		{
			["hash"] = "MDCardModTool",
			["spine"] = template.SpineVersion.StartsWith("4.2", StringComparison.Ordinal) ? template.SpineVersion : "4.2.43",
			["x"] = -GameCanvasWidth / 2d,
			["y"] = -1470d,
			["width"] = GameCanvasWidth,
			["height"] = GameCanvasHeight,
			["fps"] = framesPerSecond,
			["images"] = "/images/",
			["audio"] = ""
		};
		jsonObject["bones"] = new JsonArray(
			new JsonObject { ["name"] = "root" },
			new JsonObject { ["name"] = "Body", ["parent"] = "root" });
		jsonObject["slots"] = new JsonArray(new JsonObject
		{
			["name"] = slotName,
			["bone"] = "Body",
			["attachment"] = regions[0].Name
		});
		jsonObject["skins"] = new JsonArray(new JsonObject
		{
			["name"] = "default",
			["attachments"] = new JsonObject { [slotName] = attachments }
		});
		jsonObject["animations"] = animations;
		JsonObject root = jsonObject;
		return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static JsonObject BuildAttachmentAnimation(string slotName, IReadOnlyList<AtlasRegion> regions, int framesPerSecond)
	{
		JsonArray keyframes = new JsonArray();
		for (int i = 0; i < regions.Count; i++)
		{
			keyframes.Add(new JsonObject
			{
				["time"] = Round((double)i / (double)framesPerSecond),
				["name"] = regions[i].Name
			});
		}
		double duration = Math.Max(2d, regions.Count / (double)framesPerSecond);
		JsonArray scale = new()
		{
			new JsonObject
			{
				["time"] = 0d, ["x"] = 0.7d, ["y"] = 0.7d,
				["curve"] = new JsonArray(0.08d, 0.816d, 0.176d, 0.931d, 0.08d, 0.816d, 0.176d, 0.931d)
			},
			new JsonObject { ["time"] = Math.Min(0.367d, duration), ["x"] = 0.95d, ["y"] = 0.95d },
			new JsonObject { ["time"] = duration, ["x"] = 1.1d, ["y"] = 1.1d }
		};
		JsonArray translate = new()
		{
			new JsonObject { ["time"] = 0d, ["x"] = 0d, ["y"] = -65.52d },
			new JsonObject { ["time"] = duration, ["x"] = 0d, ["y"] = 47.65d }
		};
		return new JsonObject
		{
			["bones"] = new JsonObject
			{
				["root"] = new JsonObject { ["scale"] = scale },
				["Body"] = new JsonObject { ["translate"] = translate }
			},
			["slots"] = new JsonObject { [slotName] = new JsonObject { ["attachment"] = keyframes } }
		};
	}

	private static double Round(double value)
	{
		return Math.Round(value, 4, MidpointRounding.AwayFromZero);
	}
}
