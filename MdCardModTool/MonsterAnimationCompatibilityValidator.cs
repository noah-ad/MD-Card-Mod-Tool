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

public sealed record MonsterAnimationValidationResult(
	string CardId,
	string Region,
	int BundleCount,
	int HdAtlasWidth,
	int HdAtlasHeight,
	int SdAtlasWidth,
	int SdAtlasHeight,
	string RenderPair);

/// <summary>
/// Verifies that an existing official cut-in remains structurally complete after
/// replacement: one region, HD/SD triplets, linear PMA textures, matching atlas
/// pages and Spine 4.2 attachment timelines.
/// </summary>
public static class MonsterAnimationCompatibilityValidator
{
	private sealed record AtlasRegion(string Name, int X, int Y, int Width, int Height);
	private sealed record AtlasDocument(string Page, int Width, int Height, bool Pma, IReadOnlyList<AtlasRegion> Regions);

	public static MonsterAnimationValidationResult Validate(MonsterAnimationSet set,
		bool requireExactlySixBundles = true, bool renderProbe = true)
	{
		if (string.IsNullOrWhiteSpace(set.CardId) || !set.CardId.All(char.IsAsciiDigit))
		{
			throw new InvalidDataException("动画卡号无效。" );
		}
		int bundleCount = set.Assets.Select(asset => Path.GetFullPath(asset.BundlePath))
			.Distinct(StringComparer.OrdinalIgnoreCase).Count();
		if (requireExactlySixBundles && (bundleCount != 6 || set.Textures.Count != 2
			|| set.Atlases.Count != 2 || set.Skeletons.Count != 2))
		{
			throw new InvalidDataException("动画必须由 HD/SD 各 Texture、Atlas、Skeleton 共六个独立 Bundle 组成。" );
		}

		IReadOnlyList<MonsterAnimationAssetTriplet> pairs = MonsterAnimationAssetPairing.FindComplete(set);
		IGrouping<string, MonsterAnimationAssetTriplet>? region = pairs
			.GroupBy(pair => pair.Region, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault(group => group.Any(pair => pair.Tier == "HighEnd_HD")
				&& group.Any(pair => pair.Tier == "SD"));
		if (region == null)
		{
			throw new InvalidDataException("六个动画 Bundle 没有形成同一地区的 HD/SD 配对。" );
		}
		MonsterAnimationAssetTriplet hd = Best(region, "HighEnd_HD");
		MonsterAnimationAssetTriplet sd = Best(region, "SD");
		ModEngine engine = new();
		AnimationTextureMetadata hdTexture = ValidateTier(engine, set.CardId, hd);
		AnimationTextureMetadata sdTexture = ValidateTier(engine, set.CardId, sd);
		if (hd.Texture.BundlePath.Equals(sd.Texture.BundlePath, StringComparison.OrdinalIgnoreCase)
			|| hd.Atlas.BundlePath.Equals(sd.Atlas.BundlePath, StringComparison.OrdinalIgnoreCase)
			|| hd.Skeleton.BundlePath.Equals(sd.Skeleton.BundlePath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("HD 与 SD 动画错误地共用了同一个 Bundle。" );
		}

		string renderPair = "not-run";
		if (renderProbe)
		{
			Spine42CompatibilityResult probe = Spine42PreviewRenderer.Probe(set, 96);
			if (!probe.Success || probe.OpaquePixels <= 0)
			{
				throw new InvalidDataException("Spine 4.2 最终渲染检查失败：" + probe.Message);
			}
			renderPair = probe.PairKey;
		}
		return new MonsterAnimationValidationResult(set.CardId, region.Key, bundleCount,
			hdTexture.Width, hdTexture.Height, sdTexture.Width, sdTexture.Height, renderPair);
	}

	private static MonsterAnimationAssetTriplet Best(IEnumerable<MonsterAnimationAssetTriplet> pairs, string tier) =>
		pairs.Where(pair => pair.Tier == tier)
			.OrderByDescending(pair => ParseScale(pair.Scale))
			.First();

	private static AnimationTextureMetadata ValidateTier(ModEngine engine, string cardId,
		MonsterAnimationAssetTriplet pair)
	{
		AnimationTextureMetadata texture = engine.ReadAnimationTextureMetadata(pair.Texture);
		if (texture.Width <= 0 || texture.Height <= 0 || texture.Width > 4096 || texture.Height > 4096)
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集尺寸 {texture.Width}×{texture.Height} 无效。" );
		}
		if (texture.TextureFormat != 25)
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集不是当前游戏与 Astra 流程使用的 BC7 格式（当前 {texture.TextureFormat}）。" );
		}
		if (texture.ColorSpace != 0)
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集没有使用线性色彩空间。" );
		}
		bool inline = texture.InlineDataSize == texture.CompleteImageSize
			&& texture.StreamSize == 0 && texture.StreamPath.Length == 0;
		bool streamed = texture.InlineDataSize == 0
			&& texture.StreamSize == texture.CompleteImageSize && texture.StreamPath.Length > 0;
		if (texture.MipCount != 1 || texture.CompleteImageSize <= 0 || (!inline && !streamed))
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集的 mip、内嵌／流式像素状态不完整。" );
		}
		using Image decoded = Image.Load(engine.DecodePng(pair.Texture.AsTexture()));
		if (decoded.Width != texture.Width || decoded.Height != texture.Height)
		{
			throw new InvalidDataException($"{pair.Tier} 动画图集重新解码后的尺寸不一致。" );
		}

		AtlasDocument atlas = ParseAtlas(engine.ReadTextAsset(pair.Atlas).Data);
		if (!atlas.Page.Equals("P" + cardId + ".png", StringComparison.OrdinalIgnoreCase)
			|| atlas.Width != texture.Width || atlas.Height != texture.Height || atlas.Regions.Count == 0)
		{
			throw new InvalidDataException($"{pair.Tier} Atlas 页名、尺寸或区域列表与 Texture2D 不一致。" );
		}
		if (!atlas.Pma)
		{
			throw new InvalidDataException($"{pair.Tier} Atlas 缺少 pma:true，半透明边缘会被游戏错误混合。" );
		}
		foreach (AtlasRegion region in atlas.Regions)
		{
			if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
				|| region.X + region.Width > atlas.Width || region.Y + region.Height > atlas.Height)
			{
				throw new InvalidDataException($"{pair.Tier} Atlas 区域 {region.Name} 越界。" );
			}
		}
		ValidateSkeleton(engine.ReadTextAsset(pair.Skeleton).Data, cardId,
			atlas.Regions.Select(region => region.Name).ToHashSet(StringComparer.Ordinal));
		return texture;
	}

	private static AtlasDocument ParseAtlas(byte[] bytes)
	{
		string[] lines = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Replace("\r", "")
			.Split('\n');
		int index = 0;
		while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
		if (index >= lines.Length) throw new InvalidDataException("Atlas 文本为空。" );
		string page = lines[index++].Trim();
		int width = 0, height = 0;
		bool pma = false;
		List<AtlasRegion> regions = [];
		while (index < lines.Length)
		{
			string line = lines[index];
			string trimmed = line.Trim();
			if (trimmed.StartsWith("size:", StringComparison.OrdinalIgnoreCase) && regions.Count == 0)
			{
				(width, height) = Pair(trimmed[5..]);
				index++;
				continue;
			}
			if (trimmed.StartsWith("pma:", StringComparison.OrdinalIgnoreCase) && regions.Count == 0)
			{
				pma = trimmed[4..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
				index++;
				continue;
			}
			if (line.Length == 0 || trimmed.Contains(':'))
			{
				index++;
				continue;
			}
			if (regions.Count > 0 && trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) break;
			string name = trimmed;
			int x = -1, y = -1, regionWidth = -1, regionHeight = -1;
			index++;
			while (index < lines.Length)
			{
				string property = lines[index].Trim();
				if (property.Length == 0)
				{
					index++;
					continue;
				}
				if (!property.Contains(':')) break;
				if (property.StartsWith("xy:", StringComparison.OrdinalIgnoreCase))
					(x, y) = Pair(property[3..]);
				else if (property.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
					(regionWidth, regionHeight) = Pair(property[5..]);
				else if (property.StartsWith("bounds:", StringComparison.OrdinalIgnoreCase))
					(x, y, regionWidth, regionHeight) = Quad(property[7..]);
				index++;
			}
			regions.Add(new AtlasRegion(name, x, y, regionWidth, regionHeight));
		}
		return new AtlasDocument(page, width, height, pma, regions);
	}

	private static (int First, int Second) Pair(string value)
	{
		string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first)
			|| !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int second))
		{
			throw new InvalidDataException("Atlas 坐标或尺寸字段无效。" );
		}
		return (first, second);
	}

	private static (int First, int Second, int Third, int Fourth) Quad(string value)
	{
		string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length != 4 || parts.Any(part => !int.TryParse(part, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out _)))
		{
			throw new InvalidDataException("Atlas bounds 字段无效。" );
		}
		return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
			int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture));
	}

	private static void ValidateSkeleton(byte[] bytes, string cardId, HashSet<string> atlasRegions)
	{
		using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimEnd('\0', '\r', '\n', ' '));
		JsonElement root = document.RootElement;
		JsonElement skeleton = root.GetProperty("skeleton");
		string spine = skeleton.GetProperty("spine").GetString() ?? "";
		if (!spine.StartsWith("4.2", StringComparison.Ordinal)
			|| Math.Abs(skeleton.GetProperty("width").GetDouble() - MonsterAnimationBuilder.GameCanvasWidth) > 0.1
			|| Math.Abs(skeleton.GetProperty("height").GetDouble() - MonsterAnimationBuilder.GameCanvasHeight) > 0.1)
		{
			throw new InvalidDataException("Skeleton 不是 4800×2700 的 Spine 4.2 JSON。" );
		}
		JsonElement slot = root.GetProperty("slots").EnumerateArray()
			.FirstOrDefault(item => item.TryGetProperty("attachment", out JsonElement attachment)
				&& atlasRegions.Contains(attachment.GetString() ?? ""));
		if (slot.ValueKind == JsonValueKind.Undefined)
		{
			throw new InvalidDataException("Skeleton 缺少引用 Atlas 首帧的槽位。" );
		}
		string slotName = slot.GetProperty("name").GetString() ?? "";
		JsonElement defaultSkin = root.GetProperty("skins").EnumerateArray()
			.First(item => string.Equals(item.GetProperty("name").GetString(), "default", StringComparison.Ordinal));
		JsonElement attachments = defaultSkin.GetProperty("attachments").GetProperty(slotName);
		HashSet<string> attachmentNames = attachments.EnumerateObject().Select(property => property.Name)
			.ToHashSet(StringComparer.Ordinal);
		if (!attachmentNames.SetEquals(atlasRegions)
			|| attachmentNames.Any(name => !Regex.IsMatch(name, "^frame_\\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
		{
			throw new InvalidDataException("Skeleton 附件名与 Atlas 的 frame_# 区域不完整对应。" );
		}
		bool hasDuration = false;
		foreach (JsonProperty animation in root.GetProperty("animations").EnumerateObject())
		{
			JsonElement timeline = animation.Value.GetProperty("slots").GetProperty(slotName).GetProperty("attachment");
			foreach (JsonElement keyframe in timeline.EnumerateArray())
			{
				string name = keyframe.GetProperty("name").GetString() ?? "";
				if (!attachmentNames.Contains(name)) throw new InvalidDataException("Skeleton 时间线引用了不存在的附件。" );
				if (keyframe.TryGetProperty("time", out JsonElement time) && time.GetDouble() > 0) hasDuration = true;
			}
			if (animation.Value.TryGetProperty("bones", out JsonElement bones)
				&& bones.GetProperty("root").GetProperty("scale").EnumerateArray()
					.Any(frame => frame.TryGetProperty("time", out JsonElement time) && time.GetDouble() > 0))
			{
				hasDuration = true;
			}
		}
		if (!hasDuration) throw new InvalidDataException("Skeleton 动画时长为零。" );
	}

	private static double ParseScale(string value) => double.TryParse(value, NumberStyles.Float,
		CultureInfo.InvariantCulture, out double scale) ? scale : 0d;
}
