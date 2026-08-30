using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public static class MonsterAnimationCurrentPreview
{
	private sealed record ParsedAtlas(int Width, int Height, Dictionary<string, AtlasRegion> Regions);

	private sealed record AtlasRegion(int X, int Y, int Width, int Height, int OriginalWidth, int OriginalHeight, int OffsetX, int OffsetY, bool Rotate);

	public static CurrentMonsterAnimationPreview? TryLoad(MonsterAnimationSet set, int previewMaxEdge = 768)
	{
		if (!set.IsComplete)
		{
			return null;
		}
		ModEngine engine = new ModEngine();
		byte[] skeletonBytes = engine.ReadTextAsset(set.Skeletons[0]).Data;
		using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(skeletonBytes).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("bones", out var bones) || bones.ValueKind != JsonValueKind.Array || bones.GetArrayLength() > 2)
		{
			return null;
		}
		if (!root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() != 1)
		{
			return null;
		}
		if (!TrySequence(root, out string animationName, out List<string> names, out int framesPerSecond))
		{
			return null;
		}
		byte[] atlasBytes = engine.ReadTextAsset(set.Atlases[0]).Data;
		ParsedAtlas atlas = ParseAtlas(Encoding.UTF8.GetString(atlasBytes).TrimEnd('\0'));
		if (atlas.Regions.Count == 0 || names.Any((string key) => !atlas.Regions.ContainsKey(key)))
		{
			return null;
		}
		byte[] texturePng = null;
		foreach (MonsterAnimationAssetRef texture in set.Textures)
		{
			try
			{
				byte[] candidate = engine.DecodePng(texture.AsTexture());
				ImageInfo info = SixLabors.ImageSharp.Image.Identify(candidate);
				if (info?.Width == atlas.Width && info.Height == atlas.Height)
				{
					texturePng = candidate;
					break;
				}
				if (texturePng == null)
				{
					texturePng = candidate;
				}
			}
			catch
			{
			}
		}
		if (texturePng == null)
		{
			return null;
		}
		List<Bitmap> frames = new List<Bitmap>(names.Count);
		try
		{
			using Image<Rgba32> atlasImage = SixLabors.ImageSharp.Image.Load<Rgba32>(texturePng);
			foreach (string name in names)
			{
				AtlasRegion region = atlas.Regions[name];
				if (region.Rotate || region.Width < 1 || region.Height < 1 || region.X < 0 || region.Y < 0 || region.X + region.Width > atlasImage.Width || region.Y + region.Height > atlasImage.Height)
				{
					throw new InvalidDataException("动画图集区域超出纹理范围。");
				}
				Image<Rgba32> part = atlasImage.Clone(delegate(IImageProcessingContext x)
				{
					x.Crop(new SixLabors.ImageSharp.Rectangle(region.X, region.Y, region.Width, region.Height));
				});
				try
				{
					UnpremultiplyAlpha(part);
					using Image<Rgba32> canvas = new Image<Rgba32>(Math.Max(1, region.OriginalWidth), Math.Max(1, region.OriginalHeight), SixLabors.ImageSharp.Color.Transparent);
					int top = region.OriginalHeight - region.OffsetY - region.Height;
					canvas.Mutate(delegate(IImageProcessingContext x)
					{
						x.DrawImage(part, new SixLabors.ImageSharp.Point(region.OffsetX, top), 1f);
					});
					if (Math.Max(canvas.Width, canvas.Height) > previewMaxEdge)
					{
						canvas.Mutate(delegate(IImageProcessingContext x)
						{
							x.Resize(new ResizeOptions
							{
								Size = new SixLabors.ImageSharp.Size(previewMaxEdge, previewMaxEdge),
								Mode = ResizeMode.Max
							});
						});
					}
					using MemoryStream encoded = new MemoryStream();
					canvas.SaveAsPng(encoded);
					encoded.Position = 0L;
					using System.Drawing.Image bitmap = System.Drawing.Image.FromStream(encoded);
					frames.Add(new Bitmap(bitmap));
				}
				finally
				{
					if (part != null)
					{
						((IDisposable)part).Dispose();
					}
				}
			}
			AtlasRegion first = atlas.Regions[names[0]];
			double fullFit = Math.Min(4800.0 / (double)Math.Max(1, first.OriginalWidth), 2700.0 / (double)Math.Max(1, first.OriginalHeight));
			double fullWidth = (double)first.OriginalWidth * fullFit;
			double fullHeight = (double)first.OriginalHeight * fullFit;
			JsonElement value;
			JsonElement element = (root.TryGetProperty("skeleton", out value) ? value : default(JsonElement));
			double storedWidth = Number(element, "width", fullWidth);
			double storedHeight = Number(element, "height", fullHeight);
			int scalePercent = Math.Clamp((int)Math.Round(Math.Min(storedWidth / Math.Max(1.0, fullWidth), storedHeight / Math.Max(1.0, fullHeight)) * 100.0), 10, 500);
			return new CurrentMonsterAnimationPreview
			{
				Frames = frames,
				FramesPerSecond = framesPerSecond,
				AnimationName = animationName,
				ScalePercent = scalePercent
			};
		}
		catch
		{
			foreach (Bitmap item in frames)
			{
				item.Dispose();
			}
			return null;
		}
	}

	private static bool TrySequence(JsonElement root, out string animationName, out List<string> names, out int framesPerSecond)
	{
		animationName = "animation";
		names = new List<string>();
		framesPerSecond = 15;
		if (!root.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		JsonProperty animation = animations.EnumerateObject().FirstOrDefault();
		if (animation.Value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		animationName = animation.Name;
		if (!animation.Value.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		JsonElement timeline = default(JsonElement);
		foreach (JsonProperty slot in slots.EnumerateObject())
		{
			if (slot.Value.ValueKind == JsonValueKind.Object && slot.Value.TryGetProperty("attachment", out timeline) && timeline.ValueKind == JsonValueKind.Array)
			{
				break;
			}
		}
		if (timeline.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		List<double> times = new List<double>();
		foreach (JsonElement frame in timeline.EnumerateArray())
		{
			if (!frame.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
			{
				continue;
			}
			string value = name.GetString();
			if (string.IsNullOrWhiteSpace(value))
			{
				continue;
			}
			if (names.Count != 0)
			{
				List<string> obj = names;
				if (string.Equals(obj[obj.Count - 1], value, StringComparison.Ordinal))
				{
					goto IL_0183;
				}
			}
			names.Add(value);
			goto IL_0183;
			IL_0183:
			if (frame.TryGetProperty("time", out var time) && time.TryGetDouble(out var seconds))
			{
				times.Add(seconds);
			}
		}
		if (names.Count < 2)
		{
			return false;
		}
		double[] steps = (from x in times.Zip(times.Skip(1), (double left, double right) => right - left)
			where x > 0.0001
			orderby x
			select x).ToArray();
		if (steps.Length != 0)
		{
			framesPerSecond = Math.Clamp((int)Math.Round(1.0 / steps[steps.Length / 2]), 1, 60);
		}
		return true;
	}

	private static ParsedAtlas ParseAtlas(string text)
	{
		string[] lines = text.Replace("\r", "").Split('\n');
		int width = 0;
		int height = 0;
		string currentPage = "";
		Dictionary<string, AtlasRegion> regions = new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);
		for (int i = 0; i < lines.Length; i++)
		{
			string raw = lines[i];
			if (string.IsNullOrWhiteSpace(raw) || char.IsWhiteSpace(raw[0]) || raw.Contains(':'))
			{
				continue;
			}
			string name = raw.Trim();
			string next = ((i + 1 < lines.Length) ? lines[i + 1].Trim() : "");
			if (next.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
			{
				currentPage = name;
				string text2 = next;
				(width, height) = Pair(text2.Substring(5, text2.Length - 5));
			}
			else
			{
				if (currentPage.Length == 0)
				{
					continue;
				}
				Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				for (int j = i + 1; j < lines.Length && (string.IsNullOrWhiteSpace(lines[j]) || char.IsWhiteSpace(lines[j][0])); j++)
				{
					string line = lines[j].Trim();
					int colon = line.IndexOf(':');
					if (colon > 0)
					{
						string key = line.Substring(0, colon).Trim();
						string text2 = line;
						int num = colon + 1;
						values[key] = text2.Substring(num, text2.Length - num).Trim();
					}
				}
				(int, int) xy = Pair(values.GetValueOrDefault("xy", "0,0"));
				(int, int) size = Pair(values.GetValueOrDefault("size", "0,0"));
				(int, int) original = Pair(values.GetValueOrDefault("orig", $"{size.Item1},{size.Item2}"));
				(int, int) offset = Pair(values.GetValueOrDefault("offset", "0,0"));
				regions[name] = new AtlasRegion(xy.Item1, xy.Item2, size.Item1, size.Item2, original.Item1, original.Item2, offset.Item1, offset.Item2, values.GetValueOrDefault("rotate", "false").Equals("true", StringComparison.OrdinalIgnoreCase));
			}
		}
		return new ParsedAtlas(width, height, regions);
	}

	private static (int X, int Y) Pair(string value)
	{
		string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
		int x;
		int y;
		return (X: (parts.Length != 0 && int.TryParse(parts[0], out x)) ? x : 0, Y: (parts.Length > 1 && int.TryParse(parts[1], out y)) ? y : 0);
	}

	private static double Number(JsonElement element, string name, double fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var number))
		{
			return fallback;
		}
		return number;
	}

	private static void UnpremultiplyAlpha(Image<Rgba32> image)
	{
		image.ProcessPixelRows(delegate(PixelAccessor<Rgba32> accessor)
		{
			for (int i = 0; i < accessor.Height; i++)
			{
				Span<Rgba32> rowSpan = accessor.GetRowSpan(i);
				for (int j = 0; j < rowSpan.Length; j++)
				{
					Rgba32 rgba = rowSpan[j];
					byte a = rgba.A;
					if (a > 0 && a < byte.MaxValue)
					{
						rgba.R = (byte)Math.Min(255, (rgba.R * 255 + rgba.A / 2) / rgba.A);
						rgba.G = (byte)Math.Min(255, (rgba.G * 255 + rgba.A / 2) / rgba.A);
						rgba.B = (byte)Math.Min(255, (rgba.B * 255 + rgba.A / 2) / rgba.A);
						rowSpan[j] = rgba;
					}
				}
			}
		});
	}
}
