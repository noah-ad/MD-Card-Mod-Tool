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
		ModEngine modEngine = new ModEngine();
		MonsterAnimationAssetTriplet monsterAnimationAssetTriplet = MonsterAnimationAssetPairing.SelectPreview(set);
		byte[] data = modEngine.ReadTextAsset(monsterAnimationAssetTriplet.Skeleton).Data;
		using JsonDocument jsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(data).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement rootElement = jsonDocument.RootElement;
		if (!rootElement.TryGetProperty("bones", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 2)
		{
			return null;
		}
		if (!rootElement.TryGetProperty("slots", out var value2) || value2.ValueKind != JsonValueKind.Array || value2.GetArrayLength() != 1)
		{
			return null;
		}
		if (!TrySequence(rootElement, out string animationName, out List<string> names, out int framesPerSecond))
		{
			return null;
		}
		byte[] data2 = modEngine.ReadTextAsset(monsterAnimationAssetTriplet.Atlas).Data;
		ParsedAtlas atlas = ParseAtlas(Encoding.UTF8.GetString(data2).TrimEnd('\0'));
		if (atlas.Regions.Count == 0 || names.Any((string key) => !atlas.Regions.ContainsKey(key)))
		{
			return null;
		}
		byte[] array = null;
		MonsterAnimationAssetRef[] array2 = new MonsterAnimationAssetRef[1] { monsterAnimationAssetTriplet.Texture };
		foreach (MonsterAnimationAssetRef monsterAnimationAssetRef in array2)
		{
			try
			{
				byte[] array3 = modEngine.DecodePng(monsterAnimationAssetRef.AsTexture());
				ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(array3);
				if (imageInfo?.Width == atlas.Width && imageInfo.Height == atlas.Height)
				{
					array = array3;
					break;
				}
				if (array == null)
				{
					array = array3;
				}
			}
			catch
			{
			}
		}
		if (array == null)
		{
			return null;
		}
		List<Bitmap> list = new List<Bitmap>(names.Count);
		try
		{
			using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(array);
			foreach (string item in names)
			{
				AtlasRegion region = atlas.Regions[item];
				if (region.Rotate || region.Width < 1 || region.Height < 1 || region.X < 0 || region.Y < 0 || region.X + region.Width > image.Width || region.Y + region.Height > image.Height)
				{
					throw new InvalidDataException("动画图集区域超出纹理范围。");
				}
				Image<Rgba32> part = image.Clone(delegate(IImageProcessingContext x)
				{
					x.Crop(new SixLabors.ImageSharp.Rectangle(region.X, region.Y, region.Width, region.Height));
				});
				try
				{
					UnpremultiplyAlpha(part);
					using Image<Rgba32> image2 = new Image<Rgba32>(Math.Max(1, region.OriginalWidth), Math.Max(1, region.OriginalHeight), SixLabors.ImageSharp.Color.Transparent);
					int top = region.OriginalHeight - region.OffsetY - region.Height;
					image2.Mutate(delegate(IImageProcessingContext x)
					{
						x.DrawImage(part, new SixLabors.ImageSharp.Point(region.OffsetX, top), 1f);
					});
					if (Math.Max(image2.Width, image2.Height) > previewMaxEdge)
					{
						image2.Mutate(delegate(IImageProcessingContext x)
						{
							x.Resize(new ResizeOptions
							{
								Size = new SixLabors.ImageSharp.Size(previewMaxEdge, previewMaxEdge),
								Mode = ResizeMode.Max
							});
						});
					}
					using MemoryStream memoryStream = new MemoryStream();
					image2.SaveAsPng(memoryStream);
					memoryStream.Position = 0L;
					using System.Drawing.Image original = System.Drawing.Image.FromStream(memoryStream);
					list.Add(new Bitmap(original));
				}
				finally
				{
					if (part != null)
					{
						((IDisposable)part).Dispose();
					}
				}
			}
			AtlasRegion atlasRegion = atlas.Regions[names[0]];
			double num2 = Math.Min(6720.0 / (double)Math.Max(1, atlasRegion.OriginalWidth), 3779.9999999999995 / (double)Math.Max(1, atlasRegion.OriginalHeight));
			double num3 = (double)atlasRegion.OriginalWidth * num2;
			double num4 = (double)atlasRegion.OriginalHeight * num2;
			JsonElement value3;
			JsonElement element = (rootElement.TryGetProperty("skeleton", out value3) ? value3 : default(JsonElement));
			double num5 = Number(element, "width", num3);
			double num6 = Number(element, "height", num4);
			JsonElement property = rootElement.GetProperty("skins");
			foreach (JsonProperty item2 in ((property.ValueKind == JsonValueKind.Array) ? property[0].GetProperty("attachments") : property.GetProperty("default")).EnumerateObject())
			{
				if (item2.Value.TryGetProperty(names[0], out var value4))
				{
					num5 = Number(value4, "width", num5);
					num6 = Number(value4, "height", num6);
					break;
				}
			}
			int scalePercent = Math.Clamp((int)Math.Round(Math.Min(num5 / Math.Max(1.0, num3), num6 / Math.Max(1.0, num4)) * 100.0), 10, 500);
			return new CurrentMonsterAnimationPreview
			{
				Frames = list,
				FramesPerSecond = framesPerSecond,
				AnimationName = animationName,
				ScalePercent = scalePercent
			};
		}
		catch
		{
			foreach (Bitmap item3 in list)
			{
				item3.Dispose();
			}
			return null;
		}
	}

	private static bool TrySequence(JsonElement root, out string animationName, out List<string> names, out int framesPerSecond)
	{
		animationName = "animation";
		names = new List<string>();
		framesPerSecond = 15;
		if (!root.TryGetProperty("animations", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		JsonProperty jsonProperty = value.EnumerateObject().FirstOrDefault();
		if (jsonProperty.Value.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		animationName = jsonProperty.Name;
		if (!jsonProperty.Value.TryGetProperty("slots", out var value2) || value2.ValueKind != JsonValueKind.Object)
		{
			return false;
		}
		JsonElement value3 = default(JsonElement);
		foreach (JsonProperty item in value2.EnumerateObject())
		{
			if (item.Value.ValueKind == JsonValueKind.Object && item.Value.TryGetProperty("attachment", out value3) && value3.ValueKind == JsonValueKind.Array)
			{
				break;
			}
		}
		if (value3.ValueKind != JsonValueKind.Array)
		{
			return false;
		}
		List<double> list = new List<double>();
		foreach (JsonElement item2 in value3.EnumerateArray())
		{
			if (!item2.TryGetProperty("name", out var value4) || value4.ValueKind != JsonValueKind.String)
			{
				continue;
			}
			string text = value4.GetString();
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			if (names.Count != 0)
			{
				List<string> obj = names;
				if (string.Equals(obj[obj.Count - 1], text, StringComparison.Ordinal))
				{
					goto IL_0183;
				}
			}
			names.Add(text);
			goto IL_0183;
			IL_0183:
			if (item2.TryGetProperty("time", out var value5) && value5.TryGetDouble(out var value6))
			{
				list.Add(value6);
			}
		}
		if (names.Count < 1)
		{
			return false;
		}
		double[] array = (from x in list.Zip(list.Skip(1), (double left, double right) => right - left)
			where x > 0.0001
			orderby x
			select x).ToArray();
		if (array.Length != 0)
		{
			framesPerSecond = Math.Clamp((int)Math.Round(1.0 / array[array.Length / 2]), 1, 60);
		}
		return true;
	}

	private static ParsedAtlas ParseAtlas(string text)
	{
		string[] array = text.Replace("\r", "").Split('\n');
		int width = 0;
		int height = 0;
		string text2 = "";
		Dictionary<string, AtlasRegion> dictionary = new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);
		for (int i = 0; i < array.Length; i++)
		{
			string text3 = array[i];
			if (string.IsNullOrWhiteSpace(text3) || char.IsWhiteSpace(text3[0]) || text3.Contains(':'))
			{
				continue;
			}
			string text4 = text3.Trim();
			string text5 = ((i + 1 < array.Length) ? array[i + 1].Trim() : "");
			if (text5.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
			{
				text2 = text4;
				string text6 = text5;
				(width, height) = Pair(text6.Substring(5, text6.Length - 5));
			}
			else
			{
				if (text2.Length == 0)
				{
					continue;
				}
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				for (int j = i + 1; j < array.Length && (string.IsNullOrWhiteSpace(array[j]) || array[j].Contains(':')); j++)
				{
					string text7 = array[j].Trim();
					int num = text7.IndexOf(':');
					if (num > 0)
					{
						string key = text7.Substring(0, num).Trim();
						string text8 = text7;
						int num2 = num + 1;
						dictionary2[key] = text8.Substring(num2, text8.Length - num2).Trim();
					}
				}
				(int, int) tuple2 = Pair(dictionary2.GetValueOrDefault("xy", "0,0"));
				(int, int) tuple3 = Pair(dictionary2.GetValueOrDefault("size", "0,0"));
				(int, int) tuple4 = Pair(dictionary2.GetValueOrDefault("orig", $"{tuple3.Item1},{tuple3.Item2}"));
				(int, int) tuple5 = Pair(dictionary2.GetValueOrDefault("offset", "0,0"));
				if (dictionary2.TryGetValue("bounds", out var value))
				{
					int[] array2 = value.Split(',').Select(int.Parse).ToArray();
					tuple2 = (array2[0], array2[1]);
					tuple3 = (array2[2], array2[3]);
					tuple4 = tuple3;
				}
				if (dictionary2.TryGetValue("offsets", out var value2))
				{
					int[] array3 = value2.Split(',').Select(int.Parse).ToArray();
					tuple5 = (array3[0], array3[1]);
					tuple4 = (array3[2], array3[3]);
				}
				dictionary[text4] = new AtlasRegion(tuple2.Item1, tuple2.Item2, tuple3.Item1, tuple3.Item2, tuple4.Item1, tuple4.Item2, tuple5.Item1, tuple5.Item2, dictionary2.GetValueOrDefault("rotate", "false").Equals("true", StringComparison.OrdinalIgnoreCase));
			}
		}
		return new ParsedAtlas(width, height, dictionary);
	}

	private static (int X, int Y) Pair(string value)
	{
		string[] array = value.Split(',', StringSplitOptions.TrimEntries);
		int result;
		int result2;
		return (X: (array.Length != 0 && int.TryParse(array[0], out result)) ? result : 0, Y: (array.Length > 1 && int.TryParse(array[1], out result2)) ? result2 : 0);
	}

	private static double Number(JsonElement element, string name, double fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var value2))
		{
			return fallback;
		}
		return value2;
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
