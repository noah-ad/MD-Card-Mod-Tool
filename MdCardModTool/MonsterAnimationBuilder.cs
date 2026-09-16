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

	public const double GameCanvasWidth = 6720.0;

	public const double GameCanvasHeight = 3779.9999999999995;

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
		foreach (int num in automaticFrameEdges)
		{
			var (frameWidth, frameHeight) = ScaleToLongestEdge(sourceWidth, sourceHeight, num);
			if (CanPackUniformFrames(frameCount, frameWidth, frameHeight, maxAtlasEdge))
			{
				return num;
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
		for (int num = 256; num <= maxAtlasEdge; num *= 2)
		{
			if (frameWidth + 4 <= num)
			{
				int num2 = 2;
				int num3 = 2;
				int num4 = 0;
				bool flag = false;
				for (int i = 0; i < frameCount; i++)
				{
					if (num2 + frameWidth + 2 > num)
					{
						num2 = 2;
						num3 += num4 + 2;
						num4 = 0;
					}
					if (num3 + frameHeight + 2 > maxAtlasEdge)
					{
						flag = true;
						break;
					}
					num2 += frameWidth + 2;
					num4 = Math.Max(num4, frameHeight);
				}
				if (!flag && NextPowerOfTwo(num3 + num4 + 2) <= maxAtlasEdge)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static (int Width, int Height) ScaleToLongestEdge(int width, int height, int edge)
	{
		double num = (double)edge / (double)Math.Max(width, height);
		return (Width: Math.Max(1, (int)Math.Round((double)width * num)), Height: Math.Max(1, (int)Math.Round((double)height * num)));
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
		if (framesPerSecond < 1 || framesPerSecond > 60)
		{
			throw new ArgumentOutOfRangeException("framesPerSecond");
		}
		if (scalePercent < 10 || scalePercent > 500)
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
		int num = 0;
		for (int i = 0; i < framePaths.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using Image<Rgba32> image = Image.Load<Rgba32>(framePaths[i]);
			num = Math.Max(num, Math.Max(image.Width, image.Height));
		}
		int num2 = Math.Max(64, num);
		int num3 = Math.Max(36, (int)Math.Round((double)num2 * 9.0 / 16.0));
		int canvasWidth = Math.Min(854, num2);
		int canvasHeight = Math.Min(480, num3);
		double displayWidth = 6720.0 * (double)scalePercent / 100.0;
		double displayHeight = 3779.9999999999995 * (double)scalePercent / 100.0;
		MonsterAnimationTierBuildResult monsterAnimationTierBuildResult = null;
		MonsterAnimationTierBuildResult monsterAnimationTierBuildResult2 = null;
		try
		{
			monsterAnimationTierBuildResult = BuildTier(framePaths, cardId, framesPerSecond, template, maxAtlasEdge, "HighEnd_HD", num2, num3, displayWidth, displayHeight, cancellationToken);
			monsterAnimationTierBuildResult2 = BuildTier(framePaths, cardId, framesPerSecond, template, maxAtlasEdge, "SD", canvasWidth, canvasHeight, displayWidth, displayHeight, cancellationToken);
			return new MonsterAnimationBuildResult
			{
				Hd = monsterAnimationTierBuildResult,
				Sd = monsterAnimationTierBuildResult2,
				FrameCount = framePaths.Count,
				FramesPerSecond = framesPerSecond,
				DisplayWidth = displayWidth,
				DisplayHeight = displayHeight
			};
		}
		catch
		{
			monsterAnimationTierBuildResult?.Dispose();
			monsterAnimationTierBuildResult2?.Dispose();
			throw;
		}
	}

	private static MonsterAnimationTierBuildResult BuildTier(IReadOnlyList<string> framePaths, string cardId, int framesPerSecond, MonsterAnimationTemplate template, int maxAtlasEdge, string tier, int canvasWidth, int canvasHeight, double displayWidth, double displayHeight, CancellationToken cancellationToken)
	{
		List<AtlasRegion> list = new List<AtlasRegion>(framePaths.Count);
		for (int i = 0; i < framePaths.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using Image<Rgba32> image = LoadCanvasFrame(framePaths[i], canvasWidth, canvasHeight);
			Rectangle rectangle = FindOpaqueBounds(image);
			list.Add(new AtlasRegion
			{
				Index = i,
				Name = $"frame_{i}",
				SourcePath = framePaths[i],
				OriginalWidth = canvasWidth,
				OriginalHeight = canvasHeight,
				SourceX = rectangle.X,
				SourceY = rectangle.Y,
				Width = rectangle.Width,
				Height = rectangle.Height
			});
		}
		(int, int) tuple = Pack(list, maxAtlasEdge);
		Image<Rgba32> image2 = new Image<Rgba32>(tuple.Item1, tuple.Item2, Color.Transparent);
		try
		{
			foreach (AtlasRegion region in list)
			{
				cancellationToken.ThrowIfCancellationRequested();
				using Image<Rgba32> source = LoadCanvasFrame(region.SourcePath, canvasWidth, canvasHeight);
				Image<Rgba32> trimmed = source.Clone(delegate(IImageProcessingContext x)
				{
					x.Crop(new Rectangle(region.SourceX, region.SourceY, region.Width, region.Height));
				});
				try
				{
					PremultiplyAlpha(trimmed);
					image2.Mutate(delegate(IImageProcessingContext x)
					{
						x.DrawImage(trimmed, new Point(region.X, region.Y), 1f);
					});
				}
				finally
				{
					if (trimmed != null)
					{
						((IDisposable)trimmed).Dispose();
					}
				}
			}
			return new MonsterAnimationTierBuildResult
			{
				Tier = tier,
				AtlasImage = image2,
				AtlasText = BuildAtlasText(cardId, tuple.Item1, tuple.Item2, list),
				SkeletonJson = BuildSkeletonJson(template, cardId, list, framesPerSecond, displayWidth, displayHeight)
			};
		}
		catch
		{
			image2.Dispose();
			throw;
		}
	}

	private static Image<Rgba32> LoadCanvasFrame(string path, int canvasWidth, int canvasHeight)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(path);
		double num = Math.Min((double)canvasWidth / (double)image.Width, (double)canvasHeight / (double)image.Height);
		int width = Math.Max(1, (int)Math.Round((double)image.Width * num));
		int height = Math.Max(1, (int)Math.Round((double)image.Height * num));
		Image<Rgba32> resized = image.Clone(delegate(IImageProcessingContext x)
		{
			x.Resize(new ResizeOptions
			{
				Size = new Size(width, height),
				Mode = ResizeMode.Stretch,
				Sampler = KnownResamplers.Lanczos3,
				Compand = true
			});
		});
		try
		{
			Image<Rgba32> image2 = new Image<Rgba32>(canvasWidth, canvasHeight, Color.Transparent);
			image2.Mutate(delegate(IImageProcessingContext x)
			{
				x.DrawImage(resized, new Point((canvasWidth - width) / 2, (canvasHeight - height) / 2), 1f);
			});
			return image2;
		}
		finally
		{
			if (resized != null)
			{
				((IDisposable)resized).Dispose();
			}
		}
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
		AtlasRegion[] array = (from atlasRegion2 in regions
			orderby atlasRegion2.Height descending, atlasRegion2.Width descending
			select atlasRegion2).ToArray();
		PackingCandidate packingCandidate = null;
		int width;
		for (width = 256; width <= maxEdge; width *= 2)
		{
			if (array.Any((AtlasRegion atlasRegion2) => atlasRegion2.Width + 4 > width))
			{
				continue;
			}
			List<(AtlasRegion, int, int)> list = new List<(AtlasRegion, int, int)>();
			int num = 2;
			int num2 = 2;
			int num3 = 0;
			bool flag = false;
			AtlasRegion[] array2 = array;
			foreach (AtlasRegion atlasRegion in array2)
			{
				if (num + atlasRegion.Width + 2 > width)
				{
					num = 2;
					num2 += num3 + 2;
					num3 = 0;
				}
				if (num2 + atlasRegion.Height + 2 > maxEdge)
				{
					flag = true;
					break;
				}
				list.Add((atlasRegion, num, num2));
				num += atlasRegion.Width + 2;
				num3 = Math.Max(num3, atlasRegion.Height);
			}
			if (flag)
			{
				continue;
			}
			int num5 = NextPowerOfTwo(num2 + num3 + 2);
			if (num5 <= maxEdge)
			{
				PackingCandidate packingCandidate2 = new PackingCandidate(width, Math.Max(16, num5), list);
				if ((object)packingCandidate == null || (long)packingCandidate2.Width * (long)packingCandidate2.Height < (long)packingCandidate.Width * (long)packingCandidate.Height)
				{
					packingCandidate = packingCandidate2;
				}
			}
		}
		if ((object)packingCandidate == null)
		{
			throw new InvalidOperationException($"全部帧无法放进单张 {maxEdge}×{maxEdge} 图集。请降低帧率、时长或单帧清晰度。");
		}
		foreach (var placement in packingCandidate.Placements)
		{
			placement.Region.X = placement.X;
			placement.Region.Y = placement.Y;
		}
		return (Width: packingCandidate.Width, Height: packingCandidate.Height);
	}

	private static int NextPowerOfTwo(int value)
	{
		int num;
		for (num = 1; num < value; num <<= 1)
		{
		}
		return num;
	}

	private static string BuildAtlasText(string cardId, int width, int height, IEnumerable<AtlasRegion> regions)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("P");
		handler.AppendFormatted(cardId);
		handler.AppendLiteral(".png");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 2, stringBuilder2);
		handler.AppendLiteral("size: ");
		handler.AppendFormatted(width);
		handler.AppendLiteral(",");
		handler.AppendFormatted(height);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine("filter:Linear,Linear");
		stringBuilder.AppendLine("pma:true");
		stringBuilder.AppendLine("scale:1");
		foreach (AtlasRegion item in regions.OrderBy((AtlasRegion x) => x.Index))
		{
			stringBuilder.AppendLine(item.Name);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 4, stringBuilder2);
			handler.AppendLiteral("bounds:");
			handler.AppendFormatted(item.X);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.Y);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.Width);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.Height);
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 4, stringBuilder2);
			handler.AppendLiteral("offsets:");
			handler.AppendFormatted(item.SourceX);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.OriginalHeight - item.SourceY - item.Height);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.OriginalWidth);
			handler.AppendLiteral(",");
			handler.AppendFormatted(item.OriginalHeight);
			stringBuilder6.AppendLine(ref handler);
		}
		return stringBuilder.ToString();
	}

	private static byte[] BuildSkeletonJson(MonsterAnimationTemplate template, string cardId, IReadOnlyList<AtlasRegion> regions, int framesPerSecond, double width, double height)
	{
		JsonObject jsonObject = new JsonObject();
		foreach (AtlasRegion region in regions)
		{
			jsonObject[region.Name] = new JsonObject
			{
				["width"] = Round(width),
				["height"] = Round(height)
			};
		}
		JsonObject jsonObject2 = new JsonObject();
		foreach (string item in template.EffectiveAnimationNames.Distinct<string>(StringComparer.Ordinal))
		{
			jsonObject2[string.IsNullOrWhiteSpace(item) ? "animation" : item] = BuildAttachmentAnimation("frame", regions, framesPerSecond);
		}
		JsonObject jsonObject3 = new JsonObject();
		jsonObject3["skeleton"] = new JsonObject
		{
			["hash"] = "MDCardModTool",
			["spine"] = (template.SpineVersion.StartsWith("4.2", StringComparison.Ordinal) ? template.SpineVersion : "4.2.43"),
			["x"] = -3360.0,
			["y"] = -2009.9999999999998,
			["width"] = 6720.0,
			["height"] = 3779.9999999999995,
			["fps"] = framesPerSecond,
			["images"] = "/images/",
			["audio"] = ""
		};
		jsonObject3["bones"] = new JsonArray(new JsonObject { ["name"] = "root" }, new JsonObject
		{
			["name"] = "Body",
			["parent"] = "root",
			["y"] = -120.0
		});
		jsonObject3["slots"] = new JsonArray(new JsonObject
		{
			["name"] = "frame",
			["bone"] = "Body",
			["attachment"] = regions[0].Name
		});
		jsonObject3["skins"] = new JsonArray(new JsonObject
		{
			["name"] = "default",
			["attachments"] = new JsonObject { ["frame"] = jsonObject }
		});
		jsonObject3["animations"] = jsonObject2;
		JsonObject jsonObject4 = jsonObject3;
		return Encoding.UTF8.GetBytes(jsonObject4.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	private static JsonObject BuildAttachmentAnimation(string slotName, IReadOnlyList<AtlasRegion> regions, int framesPerSecond)
	{
		JsonArray jsonArray = new JsonArray();
		for (int i = 0; i < regions.Count; i++)
		{
			jsonArray.Add(new JsonObject
			{
				["time"] = Round((double)i / (double)framesPerSecond),
				["name"] = regions[i].Name
			});
		}
		double num = Math.Max(2.0, (double)regions.Count / (double)framesPerSecond);
		JsonArray value = new JsonArray
		{
			new JsonObject
			{
				["time"] = 0.0,
				["x"] = 1.0,
				["y"] = 1.0
			},
			new JsonObject
			{
				["time"] = num,
				["x"] = 1.0,
				["y"] = 1.0
			}
		};
		return new JsonObject
		{
			["bones"] = new JsonObject { ["root"] = new JsonObject { ["scale"] = value } },
			["slots"] = new JsonObject { [slotName] = new JsonObject { ["attachment"] = jsonArray } }
		};
	}

	private static double Round(double value)
	{
		return Math.Round(value, 4, MidpointRounding.AwayFromZero);
	}
}
