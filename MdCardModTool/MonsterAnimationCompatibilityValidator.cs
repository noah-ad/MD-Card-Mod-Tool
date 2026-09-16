using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;

namespace MdCardModTool;

public static class MonsterAnimationCompatibilityValidator
{
	private sealed record AtlasRegion(string Name, int X, int Y, int Width, int Height);

	private sealed record AtlasDocument(string Page, int Width, int Height, bool Pma, IReadOnlyList<AtlasRegion> Regions);

	public static MonsterAnimationValidationResult Validate(MonsterAnimationSet set, bool requireExactlySixBundles = true, bool renderProbe = true)
	{
		if (string.IsNullOrWhiteSpace(set.CardId) || !set.CardId.All(char.IsAsciiDigit))
		{
			throw new InvalidDataException("动画卡号无效。");
		}
		int num = set.Assets.Select((MonsterAnimationAssetRef asset) => Path.GetFullPath(asset.BundlePath)).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count();
		if (requireExactlySixBundles && !set.IsMobile && (num != 6 || set.Textures.Count != 2 || set.Atlases.Count != 2 || set.Skeletons.Count != 2))
		{
			throw new InvalidDataException("动画必须由 HD/SD 各 Texture、Atlas、Skeleton 共六个独立 Bundle 组成。");
		}
		IGrouping<string, MonsterAnimationAssetTriplet> grouping = MonsterAnimationAssetPairing.FindComplete(set).GroupBy<MonsterAnimationAssetTriplet, string>((MonsterAnimationAssetTriplet pair) => pair.Region, StringComparer.OrdinalIgnoreCase).FirstOrDefault((IGrouping<string, MonsterAnimationAssetTriplet> group) => set.IsMobile || (group.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "HighEnd_HD") && group.Any((MonsterAnimationAssetTriplet pair) => pair.Tier == "SD")));
		if (grouping == null)
		{
			throw new InvalidDataException("六个动画 Bundle 没有形成同一地区的 HD/SD 配对。");
		}
		MonsterAnimationAssetTriplet monsterAnimationAssetTriplet = (grouping.Any((MonsterAnimationAssetTriplet p) => p.Tier == "HighEnd_HD") ? Best(grouping, "HighEnd_HD") : grouping.First());
		MonsterAnimationAssetTriplet monsterAnimationAssetTriplet2 = (grouping.Any((MonsterAnimationAssetTriplet p) => p.Tier == "SD") ? Best(grouping, "SD") : grouping.First());
		ModEngine engine = new ModEngine();
		AnimationTextureMetadata animationTextureMetadata = ValidateTier(engine, set.CardId, monsterAnimationAssetTriplet);
		AnimationTextureMetadata animationTextureMetadata2 = ValidateTier(engine, set.CardId, monsterAnimationAssetTriplet2);
		if (!set.IsMobile && (monsterAnimationAssetTriplet.Texture.BundlePath.Equals(monsterAnimationAssetTriplet2.Texture.BundlePath, StringComparison.OrdinalIgnoreCase) || monsterAnimationAssetTriplet.Atlas.BundlePath.Equals(monsterAnimationAssetTriplet2.Atlas.BundlePath, StringComparison.OrdinalIgnoreCase) || monsterAnimationAssetTriplet.Skeleton.BundlePath.Equals(monsterAnimationAssetTriplet2.Skeleton.BundlePath, StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidDataException("HD 与 SD 动画错误地共用了同一个 Bundle。");
		}
		string renderPair = "not-run";
		if (renderProbe)
		{
			Spine42CompatibilityResult spine42CompatibilityResult = Spine42PreviewRenderer.Probe(set);
			if (!spine42CompatibilityResult.Success || spine42CompatibilityResult.OpaquePixels <= 0)
			{
				throw new InvalidDataException("Spine 4.2 最终渲染检查失败：" + spine42CompatibilityResult.Message);
			}
			renderPair = spine42CompatibilityResult.PairKey;
		}
		return new MonsterAnimationValidationResult(set.CardId, grouping.Key, num, animationTextureMetadata.Width, animationTextureMetadata.Height, animationTextureMetadata2.Width, animationTextureMetadata2.Height, renderPair);
	}

	private static MonsterAnimationAssetTriplet Best(IEnumerable<MonsterAnimationAssetTriplet> pairs, string tier)
	{
		return (from pair in pairs
			where pair.Tier == tier
			orderby ParseScale(pair.Scale) descending
			select pair).First();
	}

	private static AnimationTextureMetadata ValidateTier(ModEngine engine, string cardId, MonsterAnimationAssetTriplet pair)
	{
		AnimationTextureMetadata animationTextureMetadata = engine.ReadAnimationTextureMetadata(pair.Texture);
		if (animationTextureMetadata.Width <= 0 || animationTextureMetadata.Height <= 0 || animationTextureMetadata.Width > 8192 || animationTextureMetadata.Height > 8192)
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集尺寸 {animationTextureMetadata.Width}×{animationTextureMetadata.Height} 无效。");
		}
		if (animationTextureMetadata.TextureFormat != 25 && animationTextureMetadata.TextureFormat != 4)
		{
			throw new InvalidDataException($"{pair.Tier} 新生成动画图集不是 BC7 或 RGBA32 格式（当前 {animationTextureMetadata.TextureFormat}）。");
		}
		if (animationTextureMetadata.ColorSpace != 0)
		{
			throw new InvalidDataException(pair.Tier + " 动画图集没有使用线性色彩空间。");
		}
		bool flag = animationTextureMetadata.InlineDataSize == animationTextureMetadata.CompleteImageSize && animationTextureMetadata.StreamSize == 0L && animationTextureMetadata.StreamPath.Length == 0;
		bool flag2 = animationTextureMetadata.InlineDataSize == 0 && animationTextureMetadata.StreamSize == animationTextureMetadata.CompleteImageSize && animationTextureMetadata.StreamPath.Length > 0;
		if (animationTextureMetadata.MipCount != 1 || animationTextureMetadata.CompleteImageSize <= 0 || (!flag && !flag2))
		{
			throw new InvalidDataException(pair.Tier + " 动画图集的 mip、内嵌／流式像素状态不完整。");
		}
		using Image image = Image.Load(engine.DecodePng(pair.Texture.AsTexture()));
		if (image.Width != animationTextureMetadata.Width || image.Height != animationTextureMetadata.Height)
		{
			throw new InvalidDataException(pair.Tier + " 动画图集重新解码后的尺寸不一致。");
		}
		AtlasDocument atlasDocument = ParseAtlas(engine.ReadTextAsset(pair.Atlas).Data);
		if (!atlasDocument.Page.Equals(pair.Texture.Name + ".png", StringComparison.OrdinalIgnoreCase) || atlasDocument.Width != animationTextureMetadata.Width || atlasDocument.Height != animationTextureMetadata.Height || atlasDocument.Regions.Count == 0)
		{
			throw new InvalidDataException(pair.Tier + " Atlas 页名、尺寸或区域列表与 Texture2D 不一致。");
		}
		if (!atlasDocument.Pma)
		{
			throw new InvalidDataException(pair.Tier + " Atlas 缺少 pma:true，半透明边缘会被游戏错误混合。");
		}
		foreach (AtlasRegion region in atlasDocument.Regions)
		{
			if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || region.X + region.Width > atlasDocument.Width || region.Y + region.Height > atlasDocument.Height)
			{
				throw new InvalidDataException(pair.Tier + " Atlas 区域 " + region.Name + " 越界。");
			}
		}
		ValidateSkeleton(engine.ReadTextAsset(pair.Skeleton).Data, cardId, atlasDocument.Regions.Select((AtlasRegion region) => region.Name).ToHashSet<string>(StringComparer.Ordinal));
		return animationTextureMetadata;
	}

	private static AtlasDocument ParseAtlas(byte[] bytes)
	{
		string[] array = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Replace("\r", "")
			.Split('\n');
		int i;
		for (i = 0; i < array.Length && string.IsNullOrWhiteSpace(array[i]); i++)
		{
		}
		if (i >= array.Length)
		{
			throw new InvalidDataException("Atlas 文本为空。");
		}
		string page = array[i++].Trim();
		int width = 0;
		int height = 0;
		bool pma = false;
		List<AtlasRegion> list = new List<AtlasRegion>();
		while (i < array.Length)
		{
			string text = array[i];
			string text2 = text.Trim();
			if (text2.StartsWith("size:", StringComparison.OrdinalIgnoreCase) && list.Count == 0)
			{
				string text3 = text2;
				(int First, int Second) tuple = Pair(text3.Substring(5, text3.Length - 5));
				width = tuple.First;
				height = tuple.Second;
				i++;
				continue;
			}
			if (text2.StartsWith("pma:", StringComparison.OrdinalIgnoreCase) && list.Count == 0)
			{
				string text3 = text2;
				pma = text3.Substring(4, text3.Length - 4).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
				i++;
				continue;
			}
			if (text.Length == 0 || text2.Contains(':'))
			{
				i++;
				continue;
			}
			if (list.Count > 0 && text2.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			string name = text2;
			int x = -1;
			int y = -1;
			int width2 = -1;
			int height2 = -1;
			i++;
			while (i < array.Length)
			{
				string text4 = array[i].Trim();
				if (text4.Length == 0)
				{
					i++;
					continue;
				}
				if (!text4.Contains(':'))
				{
					break;
				}
				if (text4.StartsWith("xy:", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text4;
					(x, y) = Pair(text3.Substring(3, text3.Length - 3));
				}
				else if (text4.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text4;
					(width2, height2) = Pair(text3.Substring(5, text3.Length - 5));
				}
				else if (text4.StartsWith("bounds:", StringComparison.OrdinalIgnoreCase))
				{
					string text3 = text4;
					(x, y, width2, height2) = Quad(text3.Substring(7, text3.Length - 7));
				}
				i++;
			}
			list.Add(new AtlasRegion(name, x, y, width2, height2));
		}
		return new AtlasDocument(page, width, height, pma, list);
	}

	private static (int First, int Second) Pair(string value)
	{
		string[] array = value.Split(',', StringSplitOptions.TrimEntries);
		if (array.Length != 2 || !int.TryParse(array[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || !int.TryParse(array[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
		{
			throw new InvalidDataException("Atlas 坐标或尺寸字段无效。");
		}
		return (First: result, Second: result2);
	}

	private static (int First, int Second, int Third, int Fourth) Quad(string value)
	{
		string[] array = value.Split(',', StringSplitOptions.TrimEntries);
		if (array.Length != 4 || array.Any((string part) => !int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var _)))
		{
			throw new InvalidDataException("Atlas bounds 字段无效。");
		}
		return (First: int.Parse(array[0], CultureInfo.InvariantCulture), Second: int.Parse(array[1], CultureInfo.InvariantCulture), Third: int.Parse(array[2], CultureInfo.InvariantCulture), Fourth: int.Parse(array[3], CultureInfo.InvariantCulture));
	}

	private static void ValidateSkeleton(byte[] bytes, string cardId, HashSet<string> atlasRegions)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement property = rootElement.GetProperty("skeleton");
		if (!(property.GetProperty("spine").GetString() ?? "").StartsWith("4.2", StringComparison.Ordinal) || Math.Abs(property.GetProperty("width").GetDouble() - 6720.0) > 0.1 || Math.Abs(property.GetProperty("height").GetDouble() - 3779.9999999999995) > 0.1)
		{
			throw new InvalidDataException($"Skeleton 不是 {6720.0}×{3779.9999999999995} 的 Spine 4.2 JSON。");
		}
		JsonElement value3;
		JsonElement jsonElement = rootElement.GetProperty("slots").EnumerateArray().FirstOrDefault((JsonElement jsonElement2) => jsonElement2.TryGetProperty("attachment", out value3) && atlasRegions.Contains(value3.GetString() ?? ""));
		if (jsonElement.ValueKind == JsonValueKind.Undefined)
		{
			throw new InvalidDataException("Skeleton 缺少引用 Atlas 首帧的槽位。");
		}
		string propertyName = jsonElement.GetProperty("name").GetString() ?? "";
		HashSet<string> hashSet = (from jsonProperty in rootElement.GetProperty("skins").EnumerateArray().First((JsonElement jsonElement2) => string.Equals(jsonElement2.GetProperty("name").GetString(), "default", StringComparison.Ordinal))
				.GetProperty("attachments")
				.GetProperty(propertyName)
				.EnumerateObject()
			select jsonProperty.Name).ToHashSet<string>(StringComparer.Ordinal);
		if (!hashSet.SetEquals(atlasRegions) || hashSet.Any((string name) => !Regex.IsMatch(name, "^frame_\\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
		{
			throw new InvalidDataException("Skeleton 附件名与 Atlas 的 frame_# 区域不完整对应。");
		}
		bool flag = false;
		foreach (JsonProperty item2 in rootElement.GetProperty("animations").EnumerateObject())
		{
			foreach (JsonElement item3 in item2.Value.GetProperty("slots").GetProperty(propertyName).GetProperty("attachment")
				.EnumerateArray())
			{
				string item = item3.GetProperty("name").GetString() ?? "";
				if (!hashSet.Contains(item))
				{
					throw new InvalidDataException("Skeleton 时间线引用了不存在的附件。");
				}
				if (item3.TryGetProperty("time", out var value) && value.GetDouble() > 0.0)
				{
					flag = true;
				}
			}
			if (item2.Value.TryGetProperty("bones", out var value2) && value2.GetProperty("root").GetProperty("scale").EnumerateArray()
				.Any((JsonElement frame) => frame.TryGetProperty("time", out value3) && value3.GetDouble() > 0.0))
			{
				flag = true;
			}
		}
		if (!flag)
		{
			throw new InvalidDataException("Skeleton 动画时长为零。");
		}
	}

	private static double ParseScale(string value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}
}
