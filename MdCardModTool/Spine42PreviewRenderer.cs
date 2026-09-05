using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;

namespace MdCardModTool;

public sealed record Spine42CompatibilityResult
{
	public required string CardId { get; init; }
	public bool Success { get; init; }
	public string PairKey { get; init; } = "";
	public string AnimationName { get; init; } = "";
	public int FrameWidth { get; init; }
	public int FrameHeight { get; init; }
	public int OpaquePixels { get; init; }
	public IReadOnlyList<string> UnsupportedFeatures { get; init; } = Array.Empty<string>();
	public string Message { get; init; } = "";
}

/// <summary>
/// Independent, read-only Spine 4.2 JSON/atlas previewer. It intentionally does not
/// reference or bundle Esoteric Software's Spine Runtime.
/// </summary>
public static class Spine42PreviewRenderer
{
	private sealed class Bone
	{
		public required string Name;
		public Bone? Parent;
		public double X;
		public double Y;
		public double Length;
		public double Rotation;
		public double ScaleX = 1;
		public double ScaleY = 1;
		public double ShearX;
		public double ShearY;
		public string Inherit = "normal";
		public double A;
		public double B;
		public double C;
		public double D;
		public double WorldX;
		public double WorldY;
	}

	private sealed class PhysicsState
	{
		public double LastTime = -1;
		public double PreviousWorldX;
		public double PreviousWorldY;
		public double PreviousRotation;
		public double OffsetX;
		public double OffsetY;
		public double OffsetRotation;
		public double VelocityX;
		public double VelocityY;
		public double VelocityRotation;
	}

	private sealed record Slot(string Name, string Bone, string? Attachment, string Color, string Blend);
	private sealed record Region(int X, int Y, int Width, int Height, int Rotate, int OriginalWidth,
		int OriginalHeight, int OffsetX, int OffsetY, double TextureScaleX = 1, double TextureScaleY = 1);
	private sealed record Attachment(string Type, string Name, string Path, JsonElement Data);
	private sealed record Skin(Dictionary<string, Dictionary<string, Attachment>> Attachments);
	private sealed record AtlasPage(string Name, int Width, int Height, bool Pma, Dictionary<string, Region> Regions);

	private sealed class SkeletonData : IDisposable
	{
		public required JsonDocument Json;
		public required JsonElement Root;
		public required List<Bone> Bones;
		public required Dictionary<string, Bone> BonesByName;
		public required List<Slot> Slots;
		public required Skin Skin;
		public required AtlasPage Atlas;
		public required SKBitmap Texture;
		public required string AnimationName;
		public required JsonElement Animation;
		public required double Duration;
		public required double BoundsX;
		public required double BoundsY;
		public required double BoundsWidth;
		public required double BoundsHeight;
		public required string PairKey;
		public Dictionary<string, PhysicsState> PhysicsStates { get; } = new(StringComparer.Ordinal);

		public void Dispose()
		{
			Texture.Dispose();
			Json.Dispose();
		}
	}

	public static CurrentMonsterAnimationPreview? TryLoad(MonsterAnimationSet set, string? requestedAnimation = null,
		int framesPerSecond = 24, int maxFrames = 120, int previewMaxEdge = 256,
		CancellationToken cancellationToken = default, Action<Bitmap, int, int>? frameRendered = null)
	{
		if (!set.IsComplete) return null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			using SkeletonData data = Load(set, requestedAnimation);
			int fps = Math.Clamp(framesPerSecond, 1, 60);
			int frameCount = Math.Clamp((int)Math.Ceiling(Math.Max(1.0 / fps, data.Duration) * fps), 1, maxFrames);
			List<Bitmap> frames = new(frameCount);
			try
			{
				for (int i = 0; i < frameCount; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					double time = frameCount == 1 ? 0 : Math.Min(data.Duration, i / (double)fps);
					Bitmap frame = Render(data, time, previewMaxEdge);
					frames.Add(frame);
					frameRendered?.Invoke(frame, i, frameCount);
				}
				return new CurrentMonsterAnimationPreview
				{
					Frames = frames,
					FramesPerSecond = fps,
					AnimationName = data.AnimationName,
					ScalePercent = 100
				};
			}
			catch
			{
				foreach (Bitmap frame in frames) frame.Dispose();
				throw;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	public static Spine42CompatibilityResult Probe(MonsterAnimationSet set, int previewMaxEdge = 96)
	{
		try
		{
			using SkeletonData data = Load(set, null);
			using Bitmap frame = Render(data, data.Duration / 2, Math.Clamp(previewMaxEdge, 32, 256));
			int opaque = 0;
			for (int y = 0; y < frame.Height; y++)
			for (int x = 0; x < frame.Width; x++)
				if (frame.GetPixel(x, y).A > 8) opaque++;
			string[] unsupported = FindUnsupportedFeatures(data.Root, data.Animation);
			return new Spine42CompatibilityResult
			{
				CardId = set.CardId,
				Success = true,
				PairKey = data.PairKey,
				AnimationName = data.AnimationName,
				FrameWidth = frame.Width,
				FrameHeight = frame.Height,
				OpaquePixels = opaque,
				UnsupportedFeatures = unsupported,
				Message = unsupported.Length == 0 ? "OK" : "Rendered with diagnostics: " + string.Join(", ", unsupported)
			};
		}
		catch (Exception ex)
		{
			return new Spine42CompatibilityResult
			{
				CardId = set.CardId,
				Success = false,
				Message = ex.GetBaseException().Message
			};
		}
	}

	public static string Diagnose(MonsterAnimationSet set)
	{
		using SkeletonData data = Load(set, null);
		Dictionary<string, (string? Attachment, SKColor Color)> states = data.Slots.ToDictionary(
			s => s.Name, s => (s.Attachment, ParseColor(s.Color)), StringComparer.Ordinal);
		ResetBones(data);
		ApplyBoneTimelines(data, data.Duration / 2);
		UpdateWorldTransforms(data.Bones);
		ApplySlotTimelines(data, data.Duration / 2, states);
		ApplyConstraints(data, data.Duration / 2, states);
		int visibleSlots = states.Values.Count(state => state.Color.Alpha > 8 && state.Attachment != null);
		int matched = 0, vertices = 0, sampledOpaque = 0;
		float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
		foreach (Slot slot in data.Slots)
		{
			if (!states.TryGetValue(slot.Name, out var state) || state.Attachment == null
				|| !data.Skin.Attachments.TryGetValue(slot.Name, out Dictionary<string, Attachment>? attachments)
				|| !attachments.TryGetValue(state.Attachment, out Attachment? attachment)
				|| !data.Atlas.Regions.TryGetValue(attachment.Path, out Region? region)
				|| !data.BonesByName.TryGetValue(slot.Bone, out Bone? bone)) continue;
			(SKPoint[] points, SKPoint[] textureCoordinates, _) = attachment.Type == "mesh" ? BuildMesh(data, bone, attachment, region) : BuildRegion(bone, attachment, region);
			matched++; vertices += points.Length;
			foreach (SKPoint point in points) { minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y); maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y); }
			foreach (SKPoint uv in textureCoordinates)
			{
				int tx = Math.Clamp((int)uv.X, 0, data.Texture.Width - 1), ty = Math.Clamp((int)uv.Y, 0, data.Texture.Height - 1);
				if (data.Texture.GetPixel(tx, ty).Alpha > 0) sampledOpaque++;
			}
		}
		int opaquePixels = data.Texture.Pixels.Count(pixel => pixel.Alpha > 0);
		ModEngine engine = new();
		string candidateScores = string.Join(",", set.Textures.Select(texture =>
		{
			try { using SKBitmap bitmap = SKBitmap.Decode(engine.DecodePng(texture.AsTexture())); return texture.RelativeBundlePath + ":" + bitmap.Width + "x" + bitmap.Height + ":" + ScoreTexture(bitmap, data.Atlas); }
			catch { return texture.RelativeBundlePath + ":error"; }
		}));
		return $"spine=4.2; pair={data.PairKey}; bones={data.Bones.Count}; slots={data.Slots.Count}; visibleSlots={visibleSlots}; skinSlots={data.Skin.Attachments.Count}; regions={data.Atlas.Regions.Count}; matched={matched}; vertices={vertices}; sampledOpaque={sampledOpaque}; world={minX:0.##},{minY:0.##}..{maxX:0.##},{maxY:0.##}; bounds={data.BoundsX:0.##},{data.BoundsY:0.##},{data.BoundsWidth:0.##},{data.BoundsHeight:0.##}; texture={data.Texture.Width}x{data.Texture.Height}; opaquePixels={opaquePixels}; candidates={candidateScores}; duration={data.Duration:0.###}";
	}

	private static SkeletonData Load(MonsterAnimationSet set, string? requestedAnimation)
	{
		ModEngine engine = new();
		MonsterAnimationAssetTriplet pair = MonsterAnimationAssetPairing.SelectPreview(set);
		byte[] skeletonBytes = engine.ReadTextAsset(pair.Skeleton).Data;
		JsonDocument json = JsonDocument.Parse(Encoding.UTF8.GetString(skeletonBytes).TrimEnd('\0', '\r', '\n', ' '));
		try
		{
			JsonElement root = json.RootElement;
			string spine = String(root.GetProperty("skeleton"), "spine", "");
			if (!spine.StartsWith("4.2", StringComparison.Ordinal)) throw new NotSupportedException("Only Spine 4.2 JSON is supported.");
			List<Bone> bones = [];
			Dictionary<string, Bone> byName = new(StringComparer.Ordinal);
			foreach (JsonElement item in root.GetProperty("bones").EnumerateArray())
			{
				string name = String(item, "name", "");
				Bone bone = new()
				{
					Name = name,
					X = Number(item, "x", 0),
					Y = Number(item, "y", 0),
					Length = Number(item, "length", 0),
					Rotation = Number(item, "rotation", 0),
					ScaleX = Number(item, "scaleX", 1),
					ScaleY = Number(item, "scaleY", 1),
					ShearX = Number(item, "shearX", 0),
					ShearY = Number(item, "shearY", 0),
					Inherit = String(item, "inherit", "normal")
				};
				string parent = String(item, "parent", "");
				if (parent.Length > 0) bone.Parent = byName.GetValueOrDefault(parent);
				bones.Add(bone);
				byName[name] = bone;
			}
			List<Slot> slots = [];
			foreach (JsonElement item in root.GetProperty("slots").EnumerateArray())
			{
				slots.Add(new Slot(String(item, "name", ""), String(item, "bone", ""),
					NullableString(item, "attachment"), String(item, "color", "ffffffff"), String(item, "blend", "normal")));
			}
			Skin skin = ParseSkin(root);
			AtlasPage atlas = ParseAtlas(Encoding.UTF8.GetString(engine.ReadTextAsset(pair.Atlas).Data).TrimEnd('\0'));
			byte[] texturePng = engine.DecodePng(pair.Texture.AsTexture());
			SKBitmap texture = DecodeAtlasTexture(texturePng, atlas.Pma);
			if (atlas.Width > 0 && atlas.Height > 0 && (texture.Width != atlas.Width || texture.Height != atlas.Height))
			{
				double textureScaleX = texture.Width / (double)atlas.Width;
				double textureScaleY = texture.Height / (double)atlas.Height;
				atlas = atlas with
				{
					Regions = atlas.Regions.ToDictionary(item => item.Key,
						item => item.Value with { TextureScaleX = textureScaleX, TextureScaleY = textureScaleY },
						StringComparer.Ordinal)
				};
			}
			JsonElement animations = root.GetProperty("animations");
			JsonProperty animation = animations.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, requestedAnimation, StringComparison.Ordinal));
			if (animation.Value.ValueKind != JsonValueKind.Object) animation = animations.EnumerateObject().First();
			JsonElement skeleton = root.GetProperty("skeleton");
			double width = Math.Max(1, Number(skeleton, "width", 4800));
			double height = Math.Max(1, Number(skeleton, "height", 2700));
			return new SkeletonData
			{
				Json = json,
				Root = root,
				Bones = bones,
				BonesByName = byName,
				Slots = slots,
				Skin = skin,
				Atlas = atlas,
				Texture = texture,
				AnimationName = animation.Name,
				Animation = animation.Value,
				Duration = FindDuration(animation.Value),
				BoundsX = Number(skeleton, "x", -width / 2),
				BoundsY = Number(skeleton, "y", -height / 2),
				BoundsWidth = width,
				BoundsHeight = height,
				PairKey = pair.Key
			};
		}
		catch
		{
			json.Dispose();
			throw;
		}
	}

	private static Bitmap Render(SkeletonData data, double time, int maxEdge)
	{
		Dictionary<string, (string? Attachment, SKColor Color)> slotState = data.Slots.ToDictionary(
			s => s.Name, s => (s.Attachment, ParseColor(s.Color)), StringComparer.Ordinal);
		ResetBones(data);
		ApplyBoneTimelines(data, time);
		UpdateWorldTransforms(data.Bones);
		ApplySlotTimelines(data, time, slotState);
		ApplyConstraints(data, time, slotState);
		List<Slot> drawOrder = ApplyDrawOrder(data, time);

		double fit = Math.Min(maxEdge / data.BoundsWidth, maxEdge / data.BoundsHeight);
		int width = Math.Max(1, (int)Math.Ceiling(data.BoundsWidth * fit));
		int height = Math.Max(1, (int)Math.Ceiling(data.BoundsHeight * fit));
		using SKBitmap rendered = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using SKCanvas canvas = new(rendered);
		canvas.Clear(SKColors.Transparent);
		canvas.SetMatrix(new SKMatrix
		{
			ScaleX = (float)fit,
			ScaleY = (float)-fit,
			TransX = (float)(-data.BoundsX * fit),
			TransY = (float)((data.BoundsY + data.BoundsHeight) * fit),
			Persp2 = 1
		});
		bool clipping = false;
		string? clipEnd = null;
		SKPath? clipPath = null;
		foreach (Slot slot in drawOrder)
		{
			(string? attachmentName, SKColor color) = slotState[slot.Name];
			bool endsClip = clipping && string.Equals(clipEnd, slot.Name, StringComparison.Ordinal);
			if (!string.IsNullOrWhiteSpace(attachmentName)
				&& data.Skin.Attachments.TryGetValue(slot.Name, out Dictionary<string, Attachment>? attachments)
				&& attachments.TryGetValue(attachmentName, out Attachment? attachment)
				&& data.BonesByName.TryGetValue(slot.Bone, out Bone? bone))
			{
				if (attachment.Type == "clipping")
				{
					if (clipping) { canvas.Restore(); clipPath?.Dispose(); }
					SKPoint[] polygon = BuildPathPoints(data, slot, attachment);
					if (polygon.Length >= 3)
					{
						clipPath = new SKPath();
						clipPath.MoveTo(polygon[0]);
						for (int i = 1; i < polygon.Length; i++) clipPath.LineTo(polygon[i]);
						clipPath.Close();
						canvas.Save();
						canvas.ClipPath(clipPath, SKClipOperation.Intersect, antialias: true);
						clipping = true;
						clipEnd = NullableString(attachment.Data, "end");
					}
				}
				else
				{
					DrawAttachment(canvas, data, slot.Name, attachmentName, bone, attachment, color, slot.Blend, time);
				}
			}
			if (endsClip && clipping)
			{
				canvas.Restore();
				clipPath?.Dispose();
				clipPath = null;
				clipEnd = null;
				clipping = false;
			}
		}
		if (clipping) canvas.Restore();
		clipPath?.Dispose();
		// Both surfaces are BGRA premultiplied. Copy pixels, not PNG encode/decode
		// on every animation frame. The returned bitmap owns its storage.
		Bitmap result = new(width, height, PixelFormat.Format32bppPArgb);
		BitmapData pixels = result.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
		try
		{
			unsafe
			{
				for (int row = 0; row < height; row++)
					Buffer.MemoryCopy((byte*)rendered.GetPixels() + row * rendered.RowBytes,
						(byte*)pixels.Scan0 + row * pixels.Stride, Math.Abs(pixels.Stride), width * 4L);
			}
		}
		finally { result.UnlockBits(pixels); }
		return result;
	}

	private static void DrawAttachment(SKCanvas canvas, SkeletonData data, string slotName, string attachmentName,
		Bone bone, Attachment attachment, SKColor color, string blend, double time)
	{
		if (attachment.Type is not ("mesh" or "region") || !data.Atlas.Regions.TryGetValue(attachment.Path, out Region? region)) return;
		float[] verticesData = Floats(attachment.Data, "vertices");
		int vertexCount = Floats(attachment.Data, "uvs").Length / 2;
		int deformLength = attachment.Type == "mesh" ? DeformLength(verticesData, vertexCount) : 0;
		float[]? deform = attachment.Type == "mesh" ? SampleDeform(data, slotName, attachmentName, time, deformLength) : null;
		(SKPoint[] positions, SKPoint[] textureCoordinates, ushort[] triangles) = attachment.Type == "mesh"
			? BuildMesh(data, bone, attachment, region, deform)
			: BuildRegion(bone, attachment, region);
		if (positions.Length == 0 || triangles.Length == 0) return;
		SKColor[] colors = Enumerable.Repeat(SKColors.White, positions.Length).ToArray();
		using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, textureCoordinates, colors, triangles);
		SKColor attachmentColor = ParseColor(String(attachment.Data, "color", "ffffffff"));
		SKColor combinedColor = Multiply(color, attachmentColor);
		using SKColorFilter tint = SKColorFilter.CreateBlendMode(combinedColor, SKBlendMode.Modulate);
		using SKShader shader = SKShader.CreateBitmap(data.Texture, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
		using SKPaint paint = new()
		{
			IsAntialias = true,
			Color = SKColors.White,
			BlendMode = blend switch
			{
				"additive" => SKBlendMode.Plus,
				"multiply" => SKBlendMode.Multiply,
				"screen" => SKBlendMode.Screen,
				_ => SKBlendMode.SrcOver
			},
			ColorFilter = tint
		};
		paint.Shader = shader;
		// The vertex blend combines the texture shader with per-vertex white. Src
		// discards the shader on some Skia backends and presents as shuffled/solid
		// layers; Modulate is Spine's expected textured-vertex operation.
		canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
	}

	private static (SKPoint[], SKPoint[], ushort[]) BuildMesh(SkeletonData data, Bone slotBone, Attachment attachment,
		Region region, float[]? deform = null)
	{
		float[] vertices = Floats(attachment.Data, "vertices");
		float[] uvs = Floats(attachment.Data, "uvs");
		ushort[] triangles = UShorts(attachment.Data, "triangles");
		int vertexCount = uvs.Length / 2;
		SKPoint[] world = new SKPoint[vertexCount];
		if (vertices.Length == vertexCount * 2)
		{
			for (int i = 0; i < vertexCount; i++) world[i] = Transform(slotBone,
				vertices[i * 2] + (deform != null && deform.Length > i * 2 ? deform[i * 2] : 0),
				vertices[i * 2 + 1] + (deform != null && deform.Length > i * 2 + 1 ? deform[i * 2 + 1] : 0));
		}
		else
		{
			int cursor = 0;
			int deformCursor = 0;
			for (int i = 0; i < vertexCount && cursor < vertices.Length; i++)
			{
				int count = (int)vertices[cursor++];
				double x = 0, y = 0;
				for (int n = 0; n < count && cursor + 3 < vertices.Length; n++)
				{
					int boneIndex = (int)vertices[cursor++];
					float localX = vertices[cursor++] + (deform != null && deform.Length > deformCursor ? deform[deformCursor] : 0);
					deformCursor++;
					float localY = vertices[cursor++] + (deform != null && deform.Length > deformCursor ? deform[deformCursor] : 0);
					deformCursor++;
					float weight = vertices[cursor++];
					Bone weighted = boneIndex >= 0 && boneIndex < data.Bones.Count ? data.Bones[boneIndex] : slotBone;
					SKPoint p = Transform(weighted, localX, localY);
					x += p.X * weight;
					y += p.Y * weight;
				}
				world[i] = new SKPoint((float)x, (float)y);
			}
		}
		SKPoint[] texture = new SKPoint[vertexCount];
		for (int i = 0; i < vertexCount; i++) texture[i] = MeshUvToTexture(region, uvs[i * 2], uvs[i * 2 + 1]);
		return (world, texture, triangles);
	}

	private static int DeformLength(float[] vertices, int vertexCount)
	{
		if (vertices.Length == vertexCount * 2) return vertices.Length;
		int cursor = 0, length = 0;
		for (int vertex = 0; vertex < vertexCount && cursor < vertices.Length; vertex++)
		{
			int influences = Math.Max(0, (int)vertices[cursor++]);
			length += influences * 2;
			cursor = Math.Min(vertices.Length, cursor + influences * 4);
		}
		return length;
	}

	private static float[]? SampleDeform(SkeletonData data, string slotName, string attachmentName, double time, int valueCount)
	{
		if (valueCount <= 0) return null;
		JsonElement deform;
		bool modernAttachments = data.Animation.TryGetProperty("attachments", out deform);
		if (!modernAttachments && !data.Animation.TryGetProperty("deform", out deform)) return null;
		if (deform.ValueKind != JsonValueKind.Object) return null;
		JsonElement skin = deform.TryGetProperty("default", out JsonElement defaultSkin)
			? defaultSkin : deform.EnumerateObject().Select(property => property.Value).FirstOrDefault();
		if (skin.ValueKind != JsonValueKind.Object || !skin.TryGetProperty(slotName, out JsonElement slot)
			|| slot.ValueKind != JsonValueKind.Object || !slot.TryGetProperty(attachmentName, out JsonElement timeline)) return null;
		if (modernAttachments && (timeline.ValueKind != JsonValueKind.Object
			|| !timeline.TryGetProperty("deform", out timeline))) return null;
		if (!TrySegment(timeline, time, out JsonElement left, out JsonElement right, out bool hasRight)) return null;
		float[] start = DeformValues(left, valueCount);
		if (!hasRight) return start;
		float[] end = DeformValues(right, valueCount);
		double amount = Interpolate(left, Number(left, "time", 0), 0, Number(right, "time", 0), 1, time, 0);
		float[] result = new float[valueCount];
		for (int i = 0; i < result.Length; i++) result[i] = (float)(start[i] + (end[i] - start[i]) * amount);
		return result;
	}

	private static float[] DeformValues(JsonElement frame, int valueCount)
	{
		float[] result = new float[valueCount];
		if (!frame.TryGetProperty("vertices", out JsonElement values) || values.ValueKind != JsonValueKind.Array) return result;
		int offset = Math.Clamp((int)Number(frame, "offset", 0), 0, valueCount);
		foreach (JsonElement value in values.EnumerateArray())
		{
			if (offset >= result.Length) break;
			if (value.TryGetSingle(out float number)) result[offset] = number;
			offset++;
		}
		return result;
	}

	private static (SKPoint[], SKPoint[], ushort[]) BuildRegion(Bone bone, Attachment attachment, Region region)
	{
		double width = Number(attachment.Data, "width", region.OriginalWidth);
		double height = Number(attachment.Data, "height", region.OriginalHeight);
		double x = Number(attachment.Data, "x", 0), y = Number(attachment.Data, "y", 0);
		double rotation = Number(attachment.Data, "rotation", 0) * Math.PI / 180.0;
		double sx = Number(attachment.Data, "scaleX", 1), sy = Number(attachment.Data, "scaleY", 1);
		double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
		SKPoint[] world = new SKPoint[4];
		double regionScaleX = width / Math.Max(1, region.OriginalWidth) * sx;
		double regionScaleY = height / Math.Max(1, region.OriginalHeight) * sy;
		double packedWidth = region.Width;
		double packedHeight = region.Height;
		double left = -width * sx / 2 + region.OffsetX * regionScaleX;
		double bottom = -height * sy / 2 + region.OffsetY * regionScaleY;
		double right = left + packedWidth * regionScaleX;
		double top = bottom + packedHeight * regionScaleY;
		double[] local = [left, bottom, right, bottom, right, top, left, top];
		for (int i = 0; i < 4; i++)
		{
			double lx = local[i * 2], ly = local[i * 2 + 1];
			world[i] = Transform(bone, x + lx * cos - ly * sin, y + lx * sin + ly * cos);
		}
		SKPoint[] texture = [UvToTexture(region, 0, 1), UvToTexture(region, 1, 1), UvToTexture(region, 1, 0), UvToTexture(region, 0, 0)];
		return (world, texture, [0, 1, 2, 2, 3, 0]);
	}

	private static SKPoint UvToTexture(Region region, float u, float v)
	{
		SKPoint logical = region.Rotate switch
		{
			90 => new SKPoint(region.X + v * region.Height, region.Y + (1 - u) * region.Width),
			180 => new SKPoint(region.X + (1 - u) * region.Width, region.Y + (1 - v) * region.Height),
			270 => new SKPoint(region.X + (1 - v) * region.Height, region.Y + u * region.Width),
			_ => new SKPoint(region.X + u * region.Width, region.Y + v * region.Height)
		};
		return new SKPoint((float)(logical.X * region.TextureScaleX), (float)(logical.Y * region.TextureScaleY));
	}

	private static SKPoint MeshUvToTexture(Region region, float u, float v)
	{
		// Mesh UVs describe the original, untrimmed attachment, while region UVs
		// describe the trimmed rectangle. Offsets use a bottom-left origin.
		float trimmedU = (u * region.OriginalWidth - region.OffsetX) / Math.Max(1, region.Width);
		float trimmedV = (v * region.OriginalHeight - (region.OriginalHeight - region.OffsetY - region.Height)) / Math.Max(1, region.Height);
		return UvToTexture(region, trimmedU, trimmedV);
	}

	internal static void TestAtlasGeometry()
	{
		foreach (int rotation in new[] { 0, 90, 180, 270 })
		{
			Region region = new(10, 20, 80, 40, rotation, 100, 60, 5, 7);
			SKPoint topLeft = UvToTexture(region, 0, 0), bottomRight = UvToTexture(region, 1, 1);
			SKPoint expectedTop = rotation switch { 90 => new(10,100), 180 => new(90,60), 270 => new(50,20), _ => new(10,20) };
			SKPoint expectedBottom = rotation switch { 90 => new(50,20), 180 => new(10,20), 270 => new(10,100), _ => new(90,60) };
			if (topLeft != expectedTop || bottomRight != expectedBottom) throw new InvalidDataException("Atlas rotation " + rotation);
			SKPoint trimmedTop = MeshUvToTexture(region, 5f/100, 13f/60);
			if (Math.Abs(trimmedTop.X-topLeft.X) > .001 || Math.Abs(trimmedTop.Y-topLeft.Y) > .001) throw new InvalidDataException("Mesh whitespace offset");
			using JsonDocument json = JsonDocument.Parse("{\"width\":100,\"height\":60}");
			Bone bone = new() { Name="root", A=1, D=1 };
			var (world, _, _) = BuildRegion(bone, new Attachment("region", "test", "test", json.RootElement), region);
			if (Math.Abs(world[1].X-world[0].X-80)>.001 || Math.Abs(world[2].Y-world[1].Y-40)>.001) throw new InvalidDataException("Rotated region dimensions");
		}
		Bone parent = new() { Name="parent", X=10,Y=20,Rotation=90,ScaleX=2,ScaleY=2 };
		Bone child = new() { Name="child",Parent=parent,X=30 };
		UpdateWorldTransforms([parent,child]);
		if (Math.Abs(child.WorldX-10)>.001 || Math.Abs(child.WorldY-80)>.001) throw new InvalidDataException("Bone joint transform");
	}

	private static void ResetBones(SkeletonData data)
	{
		JsonElement.ArrayEnumerator setup = data.Root.GetProperty("bones").EnumerateArray();
		int i = 0;
		foreach (JsonElement item in setup)
		{
			Bone bone = data.Bones[i++];
			bone.X = Number(item, "x", 0); bone.Y = Number(item, "y", 0); bone.Rotation = Number(item, "rotation", 0);
			bone.ScaleX = Number(item, "scaleX", 1); bone.ScaleY = Number(item, "scaleY", 1);
			bone.ShearX = Number(item, "shearX", 0); bone.ShearY = Number(item, "shearY", 0);
			bone.Inherit = String(item, "inherit", "normal");
		}
	}

	private static void ApplyBoneTimelines(SkeletonData data, double time)
	{
		if (!data.Animation.TryGetProperty("bones", out JsonElement bones)) return;
		foreach (JsonProperty boneProperty in bones.EnumerateObject())
		{
			if (!data.BonesByName.TryGetValue(boneProperty.Name, out Bone? bone)) continue;
			foreach (JsonProperty timeline in boneProperty.Value.EnumerateObject())
			{
				switch (timeline.Name)
				{
					case "rotate":
						if (TrySampleNumber(timeline.Value, time, "value", 0, 0, out double rotation)) bone.Rotation += rotation;
						break;
					case "translate":
						if (TrySampleNumber(timeline.Value, time, "x", 0, 0, out double x)) bone.X += x;
						if (TrySampleNumber(timeline.Value, time, "y", 0, 1, out double y)) bone.Y += y;
						break;
					case "translatex":
						if (TrySampleNumber(timeline.Value, time, "value", 0, 0, out double translateX)) bone.X += translateX;
						break;
					case "translatey":
						if (TrySampleNumber(timeline.Value, time, "value", 0, 0, out double translateY)) bone.Y += translateY;
						break;
					case "scale":
						if (TrySampleNumber(timeline.Value, time, "x", 1, 0, out double scaleX)) bone.ScaleX *= scaleX;
						if (TrySampleNumber(timeline.Value, time, "y", 1, 1, out double scaleY)) bone.ScaleY *= scaleY;
						break;
					case "scalex":
						if (TrySampleNumber(timeline.Value, time, "value", 1, 0, out double singleScaleX)) bone.ScaleX *= singleScaleX;
						break;
					case "scaley":
						if (TrySampleNumber(timeline.Value, time, "value", 1, 0, out double singleScaleY)) bone.ScaleY *= singleScaleY;
						break;
					case "shear":
						if (TrySampleNumber(timeline.Value, time, "x", 0, 0, out double shearX)) bone.ShearX += shearX;
						if (TrySampleNumber(timeline.Value, time, "y", 0, 1, out double shearY)) bone.ShearY += shearY;
						break;
					case "shearx":
						if (TrySampleNumber(timeline.Value, time, "value", 0, 0, out double singleShearX)) bone.ShearX += singleShearX;
						break;
					case "sheary":
						if (TrySampleNumber(timeline.Value, time, "value", 0, 0, out double singleShearY)) bone.ShearY += singleShearY;
						break;
					case "inherit":
						if (TrySegment(timeline.Value, time, out JsonElement inheritFrame, out _, out _))
							bone.Inherit = String(inheritFrame, "inherit", bone.Inherit);
						break;
				}
			}
		}
	}

	private static void ApplySlotTimelines(SkeletonData data, double time, Dictionary<string, (string? Attachment, SKColor Color)> states)
	{
		if (!data.Animation.TryGetProperty("slots", out JsonElement slots)) return;
		foreach (JsonProperty slot in slots.EnumerateObject())
		{
			if (!states.TryGetValue(slot.Name, out var current)) continue;
			foreach (JsonProperty timeline in slot.Value.EnumerateObject())
			{
				if (timeline.Name == "attachment")
				{
					if (TrySampleAttachment(timeline.Value, time, out string? attachment)) current.Attachment = attachment;
				}
				else if (timeline.Name is "rgba" or "rgb" or "rgba2" or "rgb2" or "alpha"
					&& TrySampleSlotColor(timeline.Value, time, timeline.Name, current.Color, out SKColor color))
				{
					current.Color = color;
				}
			}
			states[slot.Name] = current;
		}
	}

	private static List<Slot> ApplyDrawOrder(SkeletonData data, double time)
	{
		List<Slot> setup = data.Slots;
		if (!data.Animation.TryGetProperty("drawOrder", out JsonElement timeline) || timeline.ValueKind != JsonValueKind.Array) return setup.ToList();
		JsonElement? selected = null;
		foreach (JsonElement frame in timeline.EnumerateArray()) if (Number(frame, "time", 0) <= time) selected = frame; else break;
		if (selected is not JsonElement current || !current.TryGetProperty("offsets", out JsonElement offsets)
			|| offsets.ValueKind != JsonValueKind.Array) return setup.ToList();

		// Spine offsets are relative to setup indices. Applying remove/insert in
		// sequence changes the later indices and corrupts the layer order whenever
		// a frame contains more than one offset.
		JsonElement[] changes = offsets.EnumerateArray().ToArray();
		int[] drawOrder = Enumerable.Repeat(-1, setup.Count).ToArray();
		int[] unchanged = new int[Math.Max(0, setup.Count - changes.Length)];
		int originalIndex = 0;
		int unchangedIndex = 0;
		foreach (JsonElement change in changes)
		{
			string name = String(change, "slot", "");
			int slotIndex = setup.FindIndex(slot => slot.Name == name);
			if (slotIndex < originalIndex) continue;
			while (originalIndex < slotIndex && unchangedIndex < unchanged.Length)
			{
				unchanged[unchangedIndex++] = originalIndex++;
			}
			int target = originalIndex + (int)Number(change, "offset", 0);
			if (target >= 0 && target < drawOrder.Length) drawOrder[target] = originalIndex;
			originalIndex++;
		}
		while (originalIndex < setup.Count && unchangedIndex < unchanged.Length)
		{
			unchanged[unchangedIndex++] = originalIndex++;
		}
		for (int index = drawOrder.Length - 1; index >= 0; index--)
		{
			if (drawOrder[index] < 0 && unchangedIndex > 0) drawOrder[index] = unchanged[--unchangedIndex];
		}
		return drawOrder.Select(index => setup[Math.Clamp(index, 0, setup.Count - 1)]).ToList();
	}

	private static void ApplyConstraints(SkeletonData data, double time,
		IReadOnlyDictionary<string, (string? Attachment, SKColor Color)> slotStates)
	{
		List<(int Order, int Sequence, string Kind, JsonElement Value)> constraints = [];
		int sequence = 0;
		foreach (string kind in new[] { "ik", "transform", "path", "physics" })
		{
			if (!data.Root.TryGetProperty(kind, out JsonElement values) || values.ValueKind != JsonValueKind.Array) continue;
			foreach (JsonElement value in values.EnumerateArray())
			{
				constraints.Add(((int)Number(value, "order", 0), sequence++, kind, value));
			}
		}
		foreach ((_, _, string kind, JsonElement value) in constraints.OrderBy(item => item.Order).ThenBy(item => item.Sequence))
		{
			switch (kind)
			{
				case "ik": ApplyIkConstraint(data, value, time); break;
				case "transform": ApplyTransformConstraint(data, value, time); break;
				case "path": ApplyPathConstraint(data, value, time, slotStates); break;
				case "physics": ApplyPhysicsConstraint(data, value, time); break;
			}
			UpdateWorldTransforms(data.Bones);
		}
	}

	private static void ApplyPhysicsConstraint(SkeletonData data, JsonElement constraint, double time)
	{
		string name = String(constraint, "name", "");
		Bone? bone = data.BonesByName.GetValueOrDefault(String(constraint, "bone", ""));
		if (bone == null || name.Length == 0) return;
		if (!data.PhysicsStates.TryGetValue(name, out PhysicsState? state))
		{
			state = new PhysicsState();
			data.PhysicsStates[name] = state;
		}
		double worldRotation = Math.Atan2(bone.C, bone.A) * 180 / Math.PI;
		if (state.LastTime < 0 || time < state.LastTime)
		{
			state.LastTime = time;
			state.PreviousWorldX = bone.WorldX;
			state.PreviousWorldY = bone.WorldY;
			state.PreviousRotation = worldRotation;
			state.OffsetX = state.OffsetY = state.OffsetRotation = 0;
			state.VelocityX = state.VelocityY = state.VelocityRotation = 0;
			return;
		}
		double elapsed = Math.Clamp(time - state.LastTime, 0, 1.0 / 15.0);
		if (elapsed <= 0) return;
		double inertia = ConstraintNumber(data, "physics", name, time, "inertia", Number(constraint, "inertia", 1));
		double strength = Math.Max(0, ConstraintNumber(data, "physics", name, time, "strength", Number(constraint, "strength", 100)));
		double damping = Math.Clamp(ConstraintNumber(data, "physics", name, time, "damping", Number(constraint, "damping", 1)), 0, 1);
		double mass = Math.Max(0.01, ConstraintNumber(data, "physics", name, time, "mass", Number(constraint, "mass", 1)));
		double wind = ConstraintNumber(data, "physics", name, time, "wind", Number(constraint, "wind", 0));
		double gravity = ConstraintNumber(data, "physics", name, time, "gravity", Number(constraint, "gravity", 0));
		double movementX = bone.WorldX - state.PreviousWorldX;
		double movementY = bone.WorldY - state.PreviousWorldY;
		double movementRotation = NormalizeDegrees(worldRotation - state.PreviousRotation);
		double spring = strength * 0.01;
		double forceX = -movementX * inertia - state.OffsetX * spring + wind * 0.01;
		double forceY = -movementY * inertia - state.OffsetY * spring - gravity * 0.01;
		double forceRotation = -movementRotation * inertia - state.OffsetRotation * spring + (wind - gravity) * 0.002;
		state.VelocityX += forceX / mass * elapsed;
		state.VelocityY += forceY / mass * elapsed;
		state.VelocityRotation += forceRotation / mass * elapsed;
		double dampingFactor = Math.Pow(damping, elapsed * 60);
		state.VelocityX *= dampingFactor;
		state.VelocityY *= dampingFactor;
		state.VelocityRotation *= dampingFactor;
		state.OffsetX += state.VelocityX * elapsed;
		state.OffsetY += state.VelocityY * elapsed;
		state.OffsetRotation += state.VelocityRotation * elapsed;
		bone.X += state.OffsetX * Number(constraint, "x", 0);
		bone.Y += state.OffsetY * Number(constraint, "y", 0);
		bone.Rotation += state.OffsetRotation * Number(constraint, "rotate", 0);
		state.PreviousWorldX = bone.WorldX;
		state.PreviousWorldY = bone.WorldY;
		state.PreviousRotation = worldRotation;
		state.LastTime = time;
	}

	private static void ApplyIkConstraint(SkeletonData data, JsonElement constraint, double time)
	{
		if (!constraint.TryGetProperty("bones", out JsonElement constrained) || constrained.ValueKind != JsonValueKind.Array) return;
		Bone[] bones = constrained.EnumerateArray().Select(item => data.BonesByName.GetValueOrDefault(item.GetString() ?? ""))
			.Where(bone => bone != null).Cast<Bone>().ToArray();
		Bone? target = data.BonesByName.GetValueOrDefault(String(constraint, "target", ""));
		if (target == null || bones.Length is < 1 or > 2) return;
		double mix = ConstraintNumber(data, "ik", String(constraint, "name", ""), time, "mix", Number(constraint, "mix", 1));
		if (mix <= 0) return;
		bool bendPositive = Boolean(constraint, "bendPositive", true);
		if (bones.Length == 1) ApplyOneBoneIk(bones[0], target, mix, Boolean(constraint, "compress", false), Boolean(constraint, "stretch", false));
		else ApplyTwoBoneIk(bones[0], bones[1], target, mix, bendPositive, Boolean(constraint, "stretch", false));
	}

	private static void ApplyOneBoneIk(Bone bone, Bone target, double mix, bool compress, bool stretch)
	{
		if (bone.Parent == null) return;
		(double targetX, double targetY) = WorldToLocal(bone.Parent, target.WorldX, target.WorldY);
		double desired = Math.Atan2(targetY - bone.Y, targetX - bone.X) * 180 / Math.PI - bone.ShearX;
		if (bone.ScaleX < 0) desired += 180;
		bone.Rotation += NormalizeDegrees(desired - bone.Rotation) * mix;
		if (bone.Length <= 0 || (!compress && !stretch)) return;
		double distance = Math.Sqrt(Math.Pow(target.WorldX - bone.WorldX, 2) + Math.Pow(target.WorldY - bone.WorldY, 2));
		double worldLength = bone.Length * Math.Sqrt(bone.A * bone.A + bone.C * bone.C);
		if (worldLength <= 0.0001 || (distance < worldLength && !compress) || (distance > worldLength && !stretch)) return;
		double ratio = distance / worldLength;
		bone.ScaleX *= 1 + (ratio - 1) * mix;
	}

	private static void ApplyTwoBoneIk(Bone parent, Bone child, Bone target, double mix, bool bendPositive, bool stretch)
	{
		if (parent.Parent == null || child.Parent != parent) return;
		(double targetX, double targetY) = WorldToLocal(parent.Parent, target.WorldX, target.WorldY);
		double dx = targetX - parent.X, dy = targetY - parent.Y;
		double childX = child.X * parent.ScaleX, childY = child.Y * parent.ScaleY;
		double firstLength = Math.Sqrt(childX * childX + childY * childY);
		double secondLength = Math.Abs(child.Length * child.ScaleX);
		if (firstLength <= 0.0001 || secondLength <= 0.0001) return;
		double distance = Math.Sqrt(dx * dx + dy * dy);
		if (stretch && distance > firstLength + secondLength)
		{
			double ratio = distance / (firstLength + secondLength);
			parent.ScaleX *= 1 + (ratio - 1) * mix;
			childX = child.X * parent.ScaleX;
			firstLength = Math.Sqrt(childX * childX + childY * childY);
		}
		double cosine = Math.Clamp((distance * distance - firstLength * firstLength - secondLength * secondLength)
			/ (2 * firstLength * secondLength), -1, 1);
		double secondAngle = Math.Acos(cosine) * (bendPositive ? 1 : -1);
		double firstAngle = Math.Atan2(dy, dx) - Math.Atan2(secondLength * Math.Sin(secondAngle),
			firstLength + secondLength * Math.Cos(secondAngle));
		double linkOffset = Math.Atan2(childY, childX);
		double desiredParent = (firstAngle - linkOffset) * 180 / Math.PI - parent.ShearX;
		double desiredChild = (linkOffset + secondAngle) * 180 / Math.PI - child.ShearX;
		parent.Rotation += NormalizeDegrees(desiredParent - parent.Rotation) * mix;
		child.Rotation += NormalizeDegrees(desiredChild - child.Rotation) * mix;
	}

	private static void ApplyTransformConstraint(SkeletonData data, JsonElement constraint, double time)
	{
		if (!constraint.TryGetProperty("bones", out JsonElement constrained) || constrained.ValueKind != JsonValueKind.Array) return;
		Bone? target = data.BonesByName.GetValueOrDefault(String(constraint, "target", ""));
		if (target == null) return;
		string name = String(constraint, "name", "");
		double mixRotate = ConstraintNumber(data, "transform", name, time, "mixRotate", Number(constraint, "mixRotate", 1));
		double mixX = ConstraintNumber(data, "transform", name, time, "mixX", Number(constraint, "mixX", 1));
		double mixY = ConstraintNumber(data, "transform", name, time, "mixY", Number(constraint, "mixY", mixX));
		double mixScaleX = ConstraintNumber(data, "transform", name, time, "mixScaleX", Number(constraint, "mixScaleX", 1));
		double mixScaleY = ConstraintNumber(data, "transform", name, time, "mixScaleY", Number(constraint, "mixScaleY", mixScaleX));
		double mixShearY = ConstraintNumber(data, "transform", name, time, "mixShearY", Number(constraint, "mixShearY", 1));
		bool local = Boolean(constraint, "local", false), relative = Boolean(constraint, "relative", false);
		foreach (JsonElement boneName in constrained.EnumerateArray())
		{
			Bone? bone = data.BonesByName.GetValueOrDefault(boneName.GetString() ?? "");
			if (bone?.Parent == null) continue;
			double offsetX = Number(constraint, "x", 0), offsetY = Number(constraint, "y", 0);
			(double desiredX, double desiredY) = local
				? (target.X + offsetX, target.Y + offsetY)
				: WorldToLocal(bone.Parent, Transform(target, offsetX, offsetY).X, Transform(target, offsetX, offsetY).Y);
			if (relative) { desiredX += bone.X; desiredY += bone.Y; }
			bone.X += (desiredX - bone.X) * mixX;
			bone.Y += (desiredY - bone.Y) * mixY;
			double targetWorldRotation = Math.Atan2(target.C, target.A) * 180 / Math.PI;
			double parentWorldRotation = Math.Atan2(bone.Parent.C, bone.Parent.A) * 180 / Math.PI;
			double desiredRotation = (local ? target.Rotation : targetWorldRotation - parentWorldRotation)
				+ Number(constraint, "rotation", 0);
			if (relative) desiredRotation += bone.Rotation;
			bone.Rotation += NormalizeDegrees(desiredRotation - bone.Rotation) * mixRotate;
			double desiredScaleX = target.ScaleX + Number(constraint, "scaleX", 0);
			double desiredScaleY = target.ScaleY + Number(constraint, "scaleY", 0);
			if (relative) { desiredScaleX *= bone.ScaleX; desiredScaleY *= bone.ScaleY; }
			bone.ScaleX += (desiredScaleX - bone.ScaleX) * mixScaleX;
			bone.ScaleY += (desiredScaleY - bone.ScaleY) * mixScaleY;
			double desiredShearY = target.ShearY + Number(constraint, "shearY", 0);
			if (relative) desiredShearY += bone.ShearY;
			bone.ShearY += NormalizeDegrees(desiredShearY - bone.ShearY) * mixShearY;
		}
	}

	private static void ApplyPathConstraint(SkeletonData data, JsonElement constraint, double time,
		IReadOnlyDictionary<string, (string? Attachment, SKColor Color)> slotStates)
	{
		string targetName = String(constraint, "target", "");
		Slot? targetSlot = data.Slots.FirstOrDefault(slot => slot.Name == targetName);
		if (targetSlot == null || !data.Skin.Attachments.TryGetValue(targetSlot.Name, out Dictionary<string, Attachment>? attachments)) return;
		string attachmentName = slotStates.GetValueOrDefault(targetSlot.Name).Attachment ?? targetSlot.Attachment ?? targetName;
		if (!attachments.TryGetValue(attachmentName, out Attachment? attachment) || attachment.Type != "path") return;
		SKPoint[] points = BuildBezierPathPoints(data, targetSlot, attachment);
		if (points.Length < 2 || !constraint.TryGetProperty("bones", out JsonElement constrained)) return;
		Bone[] bones = constrained.EnumerateArray().Select(item => data.BonesByName.GetValueOrDefault(item.GetString() ?? ""))
			.Where(bone => bone != null).Cast<Bone>().ToArray();
		if (bones.Length == 0) return;
		double[] cumulative = new double[points.Length];
		for (int i = 1; i < points.Length; i++) cumulative[i] = cumulative[i - 1] + Distance(points[i - 1], points[i]);
		double total = cumulative[^1];
		if (total <= 0.0001) return;
		string name = String(constraint, "name", "");
		double position = ConstraintNumber(data, "path", name, time, "position", Number(constraint, "position", 0));
		double spacing = ConstraintNumber(data, "path", name, time, "spacing", Number(constraint, "spacing", 0));
		double rotateMix = ConstraintNumber(data, "path", name, time, "mixRotate", Number(constraint, "mixRotate", 1));
		double translateMix = ConstraintNumber(data, "path", name, time, "mixX", Number(constraint, "mixX", 1));
		if (String(constraint, "positionMode", "fixed") == "percent") position *= total;
		string spacingMode = String(constraint, "spacingMode", "length");
		string rotateMode = String(constraint, "rotateMode", "tangent");
		double cursor = position;
		foreach (Bone bone in bones)
		{
			(double sampleX, double sampleY, double angle) = PointAtDistance(points, cumulative, cursor);
			if (bone.Parent != null)
			{
				(double localX, double localY) = WorldToLocal(bone.Parent, sampleX, sampleY);
				bone.X += (localX - bone.X) * translateMix;
				bone.Y += (localY - bone.Y) * translateMix;
				double parentRotation = Math.Atan2(bone.Parent.C, bone.Parent.A) * 180 / Math.PI;
				double desired = angle * 180 / Math.PI - parentRotation + Number(constraint, "rotation", 0);
				bone.Rotation += NormalizeDegrees(desired - bone.Rotation) * rotateMix;
			}
			double step = spacingMode switch
			{
				"percent" => spacing * total,
				"fixed" => spacing,
				_ => Math.Abs(bone.Length * bone.ScaleX) + spacing
			};
			if (rotateMode == "chainScale" && bone.Length > 0 && step > 0)
				bone.ScaleX *= Math.Max(0.01, step / bone.Length);
			cursor += Math.Max(0, step);
		}
	}

	private static SKPoint[] BuildBezierPathPoints(SkeletonData data, Slot slot, Attachment attachment)
	{
		SKPoint[] controls = BuildPathPoints(data, slot, attachment);
		if (controls.Length < 4) return controls;
		bool closed = Boolean(attachment.Data, "closed", false);
		List<SKPoint> sampled = [];
		if (!closed)
		{
			// Spine stores one extra control vertex at each end of an open path.
			// The actual cubic curves begin at vertex 1 and advance three vertices.
			for (int start = 1; start + 3 < controls.Length; start += 3)
				AppendCubic(sampled, controls[start], controls[start + 1], controls[start + 2], controls[start + 3]);
		}
		else
		{
			for (int start = 0; start < controls.Length; start += 3)
			{
				SKPoint p0 = controls[start % controls.Length];
				SKPoint p1 = controls[(start + 1) % controls.Length];
				SKPoint p2 = controls[(start + 2) % controls.Length];
				SKPoint p3 = controls[(start + 3) % controls.Length];
				AppendCubic(sampled, p0, p1, p2, p3);
			}
		}
		return sampled.Count >= 2 ? sampled.ToArray() : controls;
	}

	private static void AppendCubic(List<SKPoint> output, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
	{
		const int subdivisions = 16;
		if (output.Count == 0) output.Add(p0);
		for (int step = 1; step <= subdivisions; step++)
		{
			double t = step / (double)subdivisions, inverse = 1 - t;
			double a = inverse * inverse * inverse;
			double b = 3 * inverse * inverse * t;
			double c = 3 * inverse * t * t;
			double d = t * t * t;
			output.Add(new SKPoint((float)(a * p0.X + b * p1.X + c * p2.X + d * p3.X),
				(float)(a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y)));
		}
	}

	private static void UpdateWorldTransforms(IEnumerable<Bone> bones)
	{
		foreach (Bone bone in bones)
		{
			double rotationX = (bone.Rotation + bone.ShearX) * Math.PI / 180.0;
			double rotationY = (bone.Rotation + 90 + bone.ShearY) * Math.PI / 180.0;
			double la = Math.Cos(rotationX) * bone.ScaleX, lb = Math.Cos(rotationY) * bone.ScaleY;
			double lc = Math.Sin(rotationX) * bone.ScaleX, ld = Math.Sin(rotationY) * bone.ScaleY;
			if (bone.Parent == null)
			{
				bone.A = la; bone.B = lb; bone.C = lc; bone.D = ld; bone.WorldX = bone.X; bone.WorldY = bone.Y;
			}
			else
			{
				Bone p = bone.Parent;
				bone.WorldX = p.A * bone.X + p.B * bone.Y + p.WorldX;
				bone.WorldY = p.C * bone.X + p.D * bone.Y + p.WorldY;
				switch (bone.Inherit)
				{
					case "onlyTranslation":
						bone.A = la; bone.B = lb; bone.C = lc; bone.D = ld;
						break;
					case "noRotationOrReflection":
					{
						double scaleX = Math.Sqrt(p.A * p.A + p.C * p.C);
						double determinant = p.A * p.D - p.B * p.C;
						double scaleY = scaleX <= 0.000001 ? 1 : determinant / scaleX;
						bone.A = scaleX * la; bone.B = scaleX * lb;
						bone.C = scaleY * lc; bone.D = scaleY * ld;
						break;
					}
					case "noScale":
					case "noScaleOrReflection":
					{
						double length = Math.Sqrt(p.A * p.A + p.C * p.C);
						double pa = length <= 0.000001 ? 1 : p.A / length;
						double pc = length <= 0.000001 ? 0 : p.C / length;
						double sign = p.A * p.D - p.B * p.C < 0 && bone.Inherit == "noScale" ? -1 : 1;
						double pb = -pc * sign, pd = pa * sign;
						bone.A = pa * la + pb * lc; bone.B = pa * lb + pb * ld;
						bone.C = pc * la + pd * lc; bone.D = pc * lb + pd * ld;
						break;
					}
					default:
						bone.A = p.A * la + p.B * lc; bone.B = p.A * lb + p.B * ld;
						bone.C = p.C * la + p.D * lc; bone.D = p.C * lb + p.D * ld;
						break;
				}
			}
		}
	}

	private static SKPoint[] BuildPathPoints(SkeletonData data, Slot slot, Attachment attachment)
	{
		int vertexCount = (int)Number(attachment.Data, "vertexCount", 0);
		float[] vertices = Floats(attachment.Data, "vertices");
		if (vertexCount <= 0 || vertices.Length == 0 || !data.BonesByName.TryGetValue(slot.Bone, out Bone? slotBone)) return [];
		SKPoint[] points = new SKPoint[vertexCount];
		if (vertices.Length == vertexCount * 2)
		{
			for (int i = 0; i < vertexCount; i++) points[i] = Transform(slotBone, vertices[i * 2], vertices[i * 2 + 1]);
			return points;
		}
		int cursor = 0;
		for (int vertex = 0; vertex < vertexCount && cursor < vertices.Length; vertex++)
		{
			int influenceCount = Math.Max(0, (int)vertices[cursor++]);
			double x = 0, y = 0;
			for (int influence = 0; influence < influenceCount && cursor + 3 < vertices.Length; influence++)
			{
				int boneIndex = (int)vertices[cursor++];
				float localX = vertices[cursor++], localY = vertices[cursor++], weight = vertices[cursor++];
				Bone bone = boneIndex >= 0 && boneIndex < data.Bones.Count ? data.Bones[boneIndex] : slotBone;
				SKPoint transformed = Transform(bone, localX, localY);
				x += transformed.X * weight;
				y += transformed.Y * weight;
			}
			points[vertex] = new SKPoint((float)x, (float)y);
		}
		return points;
	}

	private static double ConstraintNumber(SkeletonData data, string group, string name, double time,
		string property, double fallback)
	{
		if (!data.Animation.TryGetProperty(group, out JsonElement constraints)
			&& !(group == "path" && data.Animation.TryGetProperty("paths", out constraints))) return fallback;
		if (constraints.ValueKind != JsonValueKind.Object
			|| !constraints.TryGetProperty(name, out JsonElement value)) return fallback;
		if (value.ValueKind == JsonValueKind.Array
			&& TrySampleNumber(value, time, property, fallback, 0, out double direct)) return direct;
		if (value.ValueKind != JsonValueKind.Object) return fallback;
		string timelineName = property.StartsWith("mix", StringComparison.Ordinal) ? "mix" : property;
		if (!value.TryGetProperty(timelineName, out JsonElement timeline) || timeline.ValueKind != JsonValueKind.Array) return fallback;
		string sampleProperty = property is "position" or "spacing" ? "value" : property;
		return TrySampleNumber(timeline, time, sampleProperty, fallback, 0, out double sampled) ? sampled : fallback;
	}

	private static (double X, double Y) WorldToLocal(Bone bone, double worldX, double worldY)
	{
		double determinant = bone.A * bone.D - bone.B * bone.C;
		if (Math.Abs(determinant) < 0.0000001) return (0, 0);
		double inverse = 1.0 / determinant;
		double x = worldX - bone.WorldX, y = worldY - bone.WorldY;
		return ((x * bone.D - y * bone.B) * inverse, (y * bone.A - x * bone.C) * inverse);
	}

	private static double Distance(SKPoint first, SKPoint second)
	{
		double x = second.X - first.X, y = second.Y - first.Y;
		return Math.Sqrt(x * x + y * y);
	}

	private static (double X, double Y, double Angle) PointAtDistance(SKPoint[] points, double[] cumulative, double distance)
	{
		double clamped = Math.Clamp(distance, 0, cumulative[^1]);
		int index = 1;
		while (index < cumulative.Length - 1 && cumulative[index] < clamped) index++;
		double segment = cumulative[index] - cumulative[index - 1];
		double amount = segment <= 0.0001 ? 0 : (clamped - cumulative[index - 1]) / segment;
		double x = points[index - 1].X + (points[index].X - points[index - 1].X) * amount;
		double y = points[index - 1].Y + (points[index].Y - points[index - 1].Y) * amount;
		return (x, y, Math.Atan2(points[index].Y - points[index - 1].Y, points[index].X - points[index - 1].X));
	}

	private static double NormalizeDegrees(double value)
	{
		value %= 360;
		if (value > 180) value -= 360;
		else if (value < -180) value += 360;
		return value;
	}

	private static SKPoint Transform(Bone bone, double x, double y) => new((float)(bone.A * x + bone.B * y + bone.WorldX), (float)(bone.C * x + bone.D * y + bone.WorldY));

	private static bool TrySegment(JsonElement timeline, double time, out JsonElement left, out JsonElement right, out bool hasRight)
	{
		left = default; right = default; hasRight = false;
		if (timeline.ValueKind != JsonValueKind.Array) return false;
		int count = timeline.GetArrayLength();
		if (count == 0 || time < Number(timeline[0], "time", 0)) return false;
		int low = 0, high = count - 1;
		while (low < high)
		{
			int middle = (low + high + 1) / 2;
			if (Number(timeline[middle], "time", 0) <= time) low = middle;
			else high = middle - 1;
		}
		int index = low;
		left = timeline[index];
		if (index + 1 < count)
		{
			right = timeline[index + 1];
			hasRight = true;
		}
		return true;
	}

	private static bool TrySampleNumber(JsonElement timeline, double time, string property, double fallback,
		int curveChannel, out double value)
	{
		value = fallback;
		if (!TrySegment(timeline, time, out JsonElement left, out JsonElement right, out bool hasRight)) return false;
		double start = Number(left, property, fallback);
		if (!hasRight)
		{
			value = start;
			return true;
		}
		double end = Number(right, property, fallback);
		value = Interpolate(left, Number(left, "time", 0), start, Number(right, "time", 0), end, time, curveChannel);
		return true;
	}

	private static double Interpolate(JsonElement frame, double startTime, double startValue,
		double endTime, double endValue, double time, int curveChannel)
	{
		if (endTime <= startTime) return startValue;
		if (frame.TryGetProperty("curve", out JsonElement curve))
		{
			if (curve.ValueKind == JsonValueKind.String && curve.GetString() == "stepped") return startValue;
			if (curve.ValueKind == JsonValueKind.Array)
			{
				double[] controls = curve.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number)
					.Select(item => item.GetDouble()).ToArray();
				int offset = curveChannel * 4;
				if (controls.Length >= offset + 4)
				{
					double low = 0, high = 1;
					for (int iteration = 0; iteration < 18; iteration++)
					{
						double parameter = (low + high) * 0.5;
						double x = Cubic(startTime, controls[offset], controls[offset + 2], endTime, parameter);
						if (x < time) low = parameter; else high = parameter;
					}
					return Cubic(startValue, controls[offset + 1], controls[offset + 3], endValue, (low + high) * 0.5);
				}
			}
		}
		double amount = Math.Clamp((time - startTime) / (endTime - startTime), 0, 1);
		return startValue + (endValue - startValue) * amount;
	}

	private static double Cubic(double a, double b, double c, double d, double t)
	{
		double inverse = 1 - t;
		return inverse * inverse * inverse * a + 3 * inverse * inverse * t * b
			+ 3 * inverse * t * t * c + t * t * t * d;
	}

	private static bool TrySampleAttachment(JsonElement timeline, double time, out string? attachment)
	{
		attachment = null;
		if (!TrySegment(timeline, time, out JsonElement left, out _, out _)) return false;
		attachment = NullableString(left, "name");
		return true;
	}

	private static bool TrySampleSlotColor(JsonElement timeline, double time, string kind, SKColor current, out SKColor color)
	{
		color = current;
		if (!TrySegment(timeline, time, out JsonElement left, out JsonElement right, out bool hasRight)) return false;
		if (kind == "alpha")
		{
			double startAlpha = Number(left, "value", current.Alpha / 255.0);
			double alpha = hasRight
				? Interpolate(left, Number(left, "time", 0), startAlpha, Number(right, "time", 0), Number(right, "value", current.Alpha / 255.0), time, 0)
				: startAlpha;
			color = current.WithAlpha(ToByte(alpha));
			return true;
		}
		string colorProperty = kind is "rgba2" or "rgb2" ? "light" : "color";
		SKColor start = ParseColor(String(left, colorProperty, ToHex(current)));
		SKColor end = hasRight ? ParseColor(String(right, colorProperty, ToHex(start))) : start;
		byte[] channels = new byte[4];
		double[] starts = [start.Red / 255.0, start.Green / 255.0, start.Blue / 255.0, start.Alpha / 255.0];
		double[] ends = [end.Red / 255.0, end.Green / 255.0, end.Blue / 255.0, end.Alpha / 255.0];
		for (int channel = 0; channel < 4; channel++)
		{
			double sampled = hasRight
				? Interpolate(left, Number(left, "time", 0), starts[channel], Number(right, "time", 0), ends[channel], time, channel)
				: starts[channel];
			channels[channel] = ToByte(sampled);
		}
		if (kind is "rgb" or "rgb2") channels[3] = current.Alpha;
		color = new SKColor(channels[0], channels[1], channels[2], channels[3]);
		return true;
	}

	private static byte ToByte(double normalized) => (byte)Math.Clamp((int)Math.Round(normalized * 255), 0, 255);

	private static SKColor Multiply(SKColor first, SKColor second) => new(
		(byte)((first.Red * second.Red + 127) / 255),
		(byte)((first.Green * second.Green + 127) / 255),
		(byte)((first.Blue * second.Blue + 127) / 255),
		(byte)((first.Alpha * second.Alpha + 127) / 255));

	private static Skin ParseSkin(JsonElement root)
	{
		Dictionary<string, Dictionary<string, Attachment>> result = new(StringComparer.Ordinal);
		JsonElement skin = root.GetProperty("skins").EnumerateArray().FirstOrDefault(s => String(s, "name", "default") == "default");
		if (skin.ValueKind != JsonValueKind.Object) skin = root.GetProperty("skins")[0];
		foreach (JsonProperty slot in skin.GetProperty("attachments").EnumerateObject())
		{
			Dictionary<string, Attachment> attachments = new(StringComparer.Ordinal);
			foreach (JsonProperty item in slot.Value.EnumerateObject())
			{
				string type = String(item.Value, "type", "region");
				string path = String(item.Value, "path", item.Name);
				attachments[item.Name] = new Attachment(type, item.Name, path, item.Value.Clone());
			}
			result[slot.Name] = attachments;
		}
		foreach ((string _, Dictionary<string, Attachment> attachments) in result)
		{
			foreach (string name in attachments.Keys.ToArray())
			{
				Attachment linked = attachments[name];
				if (linked.Type != "linkedmesh") continue;
				string parentName = String(linked.Data, "parent", "");
				if (!attachments.TryGetValue(parentName, out Attachment? parent) || parent.Type != "mesh") continue;
				attachments[name] = new Attachment("mesh", linked.Name,
					String(linked.Data, "path", parent.Path), parent.Data);
			}
		}
		return new Skin(result);
	}

	private static AtlasPage ParseAtlas(string text)
	{
		string[] lines = text.Replace("\r", "").Split('\n');
		string page = lines.First(line => line.Trim().Length > 0).Trim();
		int width = 0, height = 0; bool pma = false;
		Dictionary<string, Region> regions = new(StringComparer.Ordinal);
		for (int i = 1; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.StartsWith("size:", StringComparison.OrdinalIgnoreCase)) (width, height) = Pair(line[5..]);
			else if (line.StartsWith("pma:", StringComparison.OrdinalIgnoreCase)) pma = line[4..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
			else if (line.Length > 0 && !lines[i].StartsWith(' ') && !line.Contains(':'))
			{
				string name = line; Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
				while (++i < lines.Length)
				{
					line = lines[i].Trim();
					int colon = line.IndexOf(':');
					if (colon <= 0) break;
					values[line[..colon].Trim()] = line[(colon + 1)..].Trim();
				}
				i--;
				bool modern = values.TryGetValue("bounds", out string? boundsValue);
				string[] bounds = (modern ? boundsValue! : values.GetValueOrDefault("xy", "0,0") + "," + values.GetValueOrDefault("size", "0,0"))
					.Split(',', StringSplitOptions.TrimEntries);
				int x = bounds.Length > 0 ? Int(bounds[0]) : 0, y = bounds.Length > 1 ? Int(bounds[1]) : 0;
				int rw = bounds.Length > 2 ? Int(bounds[2]) : 0, rh = bounds.Length > 3 ? Int(bounds[3]) : 0;
				string[] offsets = (modern
					? values.GetValueOrDefault("offsets", $"0,0,{rw},{rh}")
					: values.GetValueOrDefault("offset", "0,0") + "," + values.GetValueOrDefault("orig", $"{rw},{rh}"))
					.Split(',', StringSplitOptions.TrimEntries);
				int rotate = Int(values.GetValueOrDefault("rotate", "0"));
				if (values.GetValueOrDefault("rotate", "").Equals("true", StringComparison.OrdinalIgnoreCase)) rotate = 90;
				regions[name] = new Region(x, y, rw, rh, rotate, offsets.Length > 2 ? Int(offsets[2]) : rw, offsets.Length > 3 ? Int(offsets[3]) : rh, offsets.Length > 0 ? Int(offsets[0]) : 0, offsets.Length > 1 ? Int(offsets[1]) : 0);
			}
		}
		return new AtlasPage(page, width, height, pma, regions);
	}

	private static SKBitmap DecodeAtlasTexture(byte[] png, bool declaredPma)
	{
		using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
		// Older Master Duel atlases often omit `pma:true` even though their RGB
		// payload is already premultiplied. PNG decoders otherwise premultiply it a
		// second time, creating dark seams and translucent layers.
		if (declaredPma || LooksPremultiplied(image))
		{
			UnpremultiplyAlpha(image);
		}
		using MemoryStream encoded = new();
		image.SaveAsPng(encoded);
		return SKBitmap.Decode(encoded.ToArray())
			?? throw new InvalidDataException("Spine atlas texture could not be decoded.");
	}

	private static bool LooksPremultiplied(Image<Rgba32> image)
	{
		int step = Math.Max(1, (int)Math.Sqrt((long)image.Width * image.Height / 4096.0));
		int translucent = 0;
		int compatible = 0;
		for (int y = 0; y < image.Height; y += step)
		for (int x = 0; x < image.Width; x += step)
		{
			Rgba32 pixel = image[x, y];
			if (pixel.A is 0 or 255) continue;
			translucent++;
			if (pixel.R <= pixel.A + 2 && pixel.G <= pixel.A + 2 && pixel.B <= pixel.A + 2) compatible++;
		}
		return translucent >= 8 && compatible >= Math.Ceiling(translucent * 0.92);
	}

	private static void UnpremultiplyAlpha(Image<Rgba32> image)
	{
		image.ProcessPixelRows(accessor =>
		{
			for (int y = 0; y < accessor.Height; y++)
			{
				Span<Rgba32> row = accessor.GetRowSpan(y);
				for (int x = 0; x < row.Length; x++)
				{
					Rgba32 pixel = row[x];
					if (pixel.A is 0 or 255) continue;
					pixel.R = (byte)Math.Min(255, (pixel.R * 255 + pixel.A / 2) / pixel.A);
					pixel.G = (byte)Math.Min(255, (pixel.G * 255 + pixel.A / 2) / pixel.A);
					pixel.B = (byte)Math.Min(255, (pixel.B * 255 + pixel.A / 2) / pixel.A);
					row[x] = pixel;
				}
			}
		});
	}

	private static int ScoreTexture(SKBitmap bitmap, AtlasPage atlas)
	{
		int score = 0;
		foreach (Region region in atlas.Regions.Values)
		{
			int startX = (int)Math.Round(region.X * region.TextureScaleX);
			int startY = (int)Math.Round(region.Y * region.TextureScaleY);
			int width = (int)Math.Round(region.Width * region.TextureScaleX);
			int height = (int)Math.Round(region.Height * region.TextureScaleY);
			int stepX = Math.Max(1, width / 12), stepY = Math.Max(1, height / 12);
			for (int y = startY; y < Math.Min(bitmap.Height, startY + height); y += stepY)
			for (int x = startX; x < Math.Min(bitmap.Width, startX + width); x += stepX)
				if (bitmap.GetPixel(x, y).Alpha > 8) score++;
		}
		return score;
	}

	private static double FindDuration(JsonElement value)
	{
		double max = 0;
		void Visit(JsonElement element)
		{
			if (element.ValueKind == JsonValueKind.Object)
			{
				if (element.TryGetProperty("time", out JsonElement time) && time.TryGetDouble(out double t)) max = Math.Max(max, t);
				foreach (JsonProperty property in element.EnumerateObject()) Visit(property.Value);
			}
			else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement child in element.EnumerateArray()) Visit(child);
		}
		Visit(value); return Math.Max(max, 1.0 / 30);
	}

	private static string[] FindUnsupportedFeatures(JsonElement root, JsonElement animation)
	{
		HashSet<string> result = new(StringComparer.Ordinal);
		HashSet<string> attachmentTypes = new(StringComparer.Ordinal)
		{
			"region", "mesh", "linkedmesh", "clipping", "path", "point", "boundingbox"
		};
		if (root.TryGetProperty("skins", out JsonElement skins) && skins.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement skin in skins.EnumerateArray())
			{
				if (!skin.TryGetProperty("attachments", out JsonElement slots) || slots.ValueKind != JsonValueKind.Object) continue;
				foreach (JsonProperty slot in slots.EnumerateObject())
				foreach (JsonProperty item in slot.Value.EnumerateObject())
				{
					string type = String(item.Value, "type", "region");
					if (!attachmentTypes.Contains(type)) result.Add("attachment:" + type);
					if (item.Value.TryGetProperty("sequence", out _)) result.Add("attachment:sequence");
				}
			}
		}
		HashSet<string> animationGroups = new(StringComparer.Ordinal)
		{
			"slots", "bones", "ik", "transform", "path", "paths", "attachments", "deform", "drawOrder", "events", "physics"
		};
		if (animation.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty group in animation.EnumerateObject())
				if (!animationGroups.Contains(group.Name)) result.Add("timeline-group:" + group.Name);
			CollectUnknownTimelines(animation, "slots", new HashSet<string>(StringComparer.Ordinal)
				{ "attachment", "rgba", "rgb", "rgba2", "rgb2", "alpha" }, result);
			CollectUnknownTimelines(animation, "bones", new HashSet<string>(StringComparer.Ordinal)
				{ "rotate", "translate", "translatex", "translatey", "scale", "scalex", "scaley", "shear", "shearx", "sheary", "inherit" }, result);
		}
		return result.OrderBy(item => item, StringComparer.Ordinal).ToArray();
	}

	private static void CollectUnknownTimelines(JsonElement animation, string groupName, HashSet<string> known,
		HashSet<string> result)
	{
		if (!animation.TryGetProperty(groupName, out JsonElement group) || group.ValueKind != JsonValueKind.Object) return;
		foreach (JsonProperty target in group.EnumerateObject())
		{
			if (target.Value.ValueKind != JsonValueKind.Object) continue;
			foreach (JsonProperty timeline in target.Value.EnumerateObject())
				if (!known.Contains(timeline.Name)) result.Add($"{groupName}-timeline:{timeline.Name}");
		}
	}

	private static float[] Floats(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) ? value.EnumerateArray().Select(x => x.GetSingle()).ToArray() : [];
	private static ushort[] UShorts(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) ? value.EnumerateArray().Select(x => checked((ushort)x.GetInt32())).ToArray() : [];
	private static string String(JsonElement element, string name, string fallback) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
	private static string? NullableString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	private static double Number(JsonElement element, string name, double fallback) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double number) ? number : fallback;
	private static bool Boolean(JsonElement element, string name, bool fallback) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
	private static int Int(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0;
	private static (int X, int Y) Pair(string value) { string[] parts = value.Split(',', StringSplitOptions.TrimEntries); return (parts.Length > 0 ? Int(parts[0]) : 0, parts.Length > 1 ? Int(parts[1]) : 0); }
	private static SKColor ParseColor(string value) { if (value.Length is 6 or 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba)) { if (value.Length == 6) rgba = rgba << 8 | 0xff; return new SKColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba); } return SKColors.White; }
	private static string ToHex(SKColor color) => $"{color.Red:x2}{color.Green:x2}{color.Blue:x2}{color.Alpha:x2}";
}
