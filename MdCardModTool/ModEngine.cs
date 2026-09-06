using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MdCardModTool;

public sealed class ModEngine
{
	private readonly string _classData = AppPaths.ResolveFile("classdata.tpk");

	[ThreadStatic]
	private static AssetsManager? _threadManager;

	private AssetsManager NewManager()
	{
		AssetsManager manager = _threadManager ?? (_threadManager = new AssetsManager());
		if (manager.ClassPackage == null && File.Exists(_classData))
		{
			manager.LoadClassPackage(_classData);
		}
		return manager;
	}

	private void EnsureDatabase(AssetsManager manager, AssetsFileInstance assets)
	{
		if (manager.ClassPackage != null)
		{
			// A single worker thread may scan bundles written by different Unity
			// versions. AssetsManager keeps the previously selected database after
			// UnloadAll(), so select it for every serialized file instead of reusing
			// a stale schema.
			manager.LoadClassDatabaseFromPackage(assets.file.Metadata.UnityVersion);
		}
	}

	public List<TexRef> ListTextures(string bundlePath, string root, string sourceKind = "")
	{
		return ScanBundle(bundlePath, root, sourceKind).Textures;
	}

	public BundleScan ScanBundle(string bundlePath, string root, string sourceKind = "", bool includeDependencies = true)
	{
		BundleScan result = new BundleScan();
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					EnsureDatabase(manager, assets);
					foreach (AssetFileInfo info in assets.file.GetAssetsOfType(AssetClassID.Texture2D))
					{
						string name;
						int width = 0;
						int height = 0;
						try
						{
							AssetTypeValueField field = manager.GetBaseField(assets, info);
							name = field["m_Name"].AsString;
							width = field["m_Width"].AsInt;
							height = field["m_Height"].AsInt;
						}
						catch
						{
							// m_Name is the first serialized Texture2D field. Reading it
							// directly keeps stale/manual bundles discoverable even when a
							// class database cannot materialize the complete type tree.
							try
							{
								AssetsFileReader reader = assets.file.Reader;
								long start = info.GetAbsoluteByteStart(assets.file);
								long end = start + info.ByteSize;
								reader.Position = start;
								name = ReadAlignedString(reader, end);
							}
							catch
							{
								continue;
							}
						}
						if (string.IsNullOrWhiteSpace(name))
						{
							name = $"Texture2D_{info.PathId}";
						}
						result.Textures.Add(new TexRef
						{
							BundlePath = bundlePath,
							RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
							AssetFileName = entry,
							PathId = info.PathId,
							Name = name,
							Width = width,
							Height = height,
							Category = CategoryForSource(name, width, height, sourceKind),
							CardKey = sourceKind == "本地卡图" && CardBundleIdentity.Find(bundlePath) is { Length: > 0 } targetId
								? targetId : CardKey(name),
							SourceKind = sourceKind
						});
					}
					if (!includeDependencies)
					{
						continue;
					}
					foreach (AssetFileInfo info2 in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						string name2 = manager.GetBaseField(assets, info2)["m_Name"].AsString;
						string key = CardKey(name2);
						if (key.Length > 0)
						{
							result.Dependencies.Add(new AssetDependency
							{
								Name = name2,
								CardKey = key,
								Kind = "TextAsset",
								BundlePath = bundlePath,
								RelativeBundlePath = Path.GetRelativePath(root, bundlePath)
							});
						}
					}
				}
				catch
				{
				}
			}
			return result;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public List<MonsterAnimationAssetRef> ScanAnimationAssetsFast(string bundlePath, string root)
	{
		List<MonsterAnimationAssetRef> result = new List<MonsterAnimationAssetRef>();
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					AssetClassID[] array = new AssetClassID[2]
					{
						AssetClassID.Texture2D,
						AssetClassID.TextAsset
					};
					foreach (AssetClassID type in array)
					{
						foreach (AssetFileInfo info in assets.file.GetAssetsOfType(type))
						{
							AssetsFileReader reader = assets.file.Reader;
							long end = info.GetAbsoluteByteStart(assets.file) + info.ByteSize;
							reader.Position = info.GetAbsoluteByteStart(assets.file);
							string name = ReadAlignedString(reader, end);
							if (TryAnimationName(name, type, out string cardId, out MonsterAnimationAssetKind kind))
							{
								result.Add(new MonsterAnimationAssetRef
								{
									BundlePath = bundlePath,
									RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
									AssetFileName = entry,
									PathId = info.PathId,
									Name = name,
									CardId = cardId,
									Kind = kind
								});
							}
						}
					}
				}
				catch
				{
				}
			}
			return result;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	private static bool TryAnimationName(string name, AssetClassID type, out string cardId, out MonsterAnimationAssetKind kind)
	{
		cardId = "";
		kind = (MonsterAnimationAssetKind)0;
		Match match;
		if (type == AssetClassID.Texture2D)
		{
			match = Regex.Match(name, "^P(?<id>\\d+)$", RegexOptions.IgnoreCase);
			kind = MonsterAnimationAssetKind.Texture;
		}
		else
		{
			match = Regex.Match(name, "^P(?<id>\\d+)(?<suffix>JS|\\.atlas)$", RegexOptions.IgnoreCase);
			kind = (name.EndsWith("JS", StringComparison.OrdinalIgnoreCase) ? MonsterAnimationAssetKind.Skeleton : MonsterAnimationAssetKind.Atlas);
		}
		if (!match.Success)
		{
			return false;
		}
		cardId = match.Groups["id"].Value;
		return true;
	}

	private static string CategoryFor(string name, int width, int height)
	{
		if (name.StartsWith("card_frame", StringComparison.OrdinalIgnoreCase))
		{
			return "卡框 card_frame";
		}
		if (Regex.IsMatch(name, "^P\\d+$", RegexOptions.IgnoreCase))
		{
			return "卡图原画";
		}
		if (Regex.IsMatch(name, "^\\d+$"))
		{
			if (width != 512 || height != 1024)
			{
				return "卡图缩略图";
			}
			return "灵摆卡图";
		}
		if (width >= 1024 && height >= 1024)
		{
			return "大图贴图";
		}
		return "其他贴图";
	}

	private static string CategoryForSource(string name, int width, int height, string sourceKind)
	{
		if (sourceKind == VisualAssetIndexService.LocalSourceKind || sourceKind == VisualAssetIndexService.BuiltInSourceKind)
		{
			string? visualCategory = VisualAssetClassifier.CategoryFor(name);
			if (visualCategory != null)
			{
				return visualCategory;
			}
		}
		return CategoryFor(name, width, height);
	}

	public static string CardKey(string name)
	{
		Match match = Regex.Match(name, "^P?(\\d+)(?:JS|\\.atlas)?$", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return "";
		}
		return match.Groups[1].Value;
	}

	public BundleSummary InspectBundle(string bundlePath, string root)
	{
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			Dictionary<string, int> types = new Dictionary<string, int>();
			int serialized = 0;
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = manager.LoadAssetsFileFromBundle(bundle, entry);
					serialized++;
					foreach (AssetFileInfo assetInfo in assetsFileInstance.file.AssetInfos)
					{
						string name = ((AssetClassID)assetInfo.TypeId/*cast due to .constrained prefix*/).ToString();
						types[name] = types.GetValueOrDefault(name) + 1;
					}
				}
				catch
				{
				}
			}
			return new BundleSummary
			{
				RelativePath = Path.GetRelativePath(root, bundlePath),
				SerializedFiles = serialized,
				AssetTypes = types
			};
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public List<string> ReadAssetBundleContainerPaths(string bundlePath)
	{
		List<string> result = new List<string>();
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					EnsureDatabase(manager, assets);
					foreach (AssetFileInfo info in assets.file.GetAssetsOfType(AssetClassID.AssetBundle))
					{
						foreach (AssetTypeValueField child in manager.GetBaseField(assets, info)["m_Container"]["Array"].Children)
						{
							string path = child["first"].AsString;
							if (!string.IsNullOrWhiteSpace(path))
							{
								result.Add(path);
							}
						}
					}
				}
				catch
				{
				}
			}
			return result;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public List<TextAssetRef> ReadTextAssets(string bundlePath)
	{
		List<TextAssetRef> result = new List<TextAssetRef>();
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					EnsureDatabase(manager, assets);
					foreach (AssetFileInfo info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetTypeValueField field = manager.GetBaseField(assets, info);
						result.Add(new TextAssetRef
						{
							BundlePath = bundlePath,
							RelativeBundlePath = Path.GetFileName(bundlePath),
							AssetFileName = entry,
							Name = field["m_Name"].AsString,
							PathName = (field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString),
							PathId = info.PathId,
							Data = field["m_Script"].AsByteArray
						});
					}
				}
				catch
				{
				}
			}
			return result;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public TextAssetRef? FindTextAsset(string bundlePath, string root, string name)
	{
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					EnsureDatabase(manager, assets);
					foreach (AssetFileInfo info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetTypeValueField field = manager.GetBaseField(assets, info);
						if (string.Equals(field["m_Name"].AsString, name, StringComparison.OrdinalIgnoreCase))
						{
							return new TextAssetRef
							{
								BundlePath = bundlePath,
								RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
								AssetFileName = entry,
								Name = field["m_Name"].AsString,
								PathName = (field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString),
								PathId = info.PathId,
								Data = field["m_Script"].AsByteArray
							};
						}
					}
				}
				catch
				{
				}
			}
			return null;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public TextAssetRef ReadTextAsset(MonsterAnimationAssetRef asset)
	{
		if (asset.Kind == MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("动画纹理不是 TextAsset。", "asset");
		}
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, asset.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetFileInfo info = assets.file.GetAssetsOfType(AssetClassID.TextAsset).First((AssetFileInfo x) => x.PathId == asset.PathId);
			AssetTypeValueField field = manager.GetBaseField(assets, info);
			return new TextAssetRef
			{
				BundlePath = asset.BundlePath,
				RelativeBundlePath = asset.RelativeBundlePath,
				AssetFileName = asset.AssetFileName,
				Name = field["m_Name"].AsString,
				PathName = (field["m_PathName"].IsDummy ? "" : field["m_PathName"].AsString),
				PathId = info.PathId,
				Data = field["m_Script"].AsByteArray
			};
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	public TextAssetRef? FindTextAssetFast(string bundlePath, string root, string name)
	{
		AssetsManager manager = new AssetsManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			foreach (string entry in bundle.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, entry);
					foreach (AssetFileInfo info in assets.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetsFileReader reader = assets.file.Reader;
						long start = info.GetAbsoluteByteStart(assets.file);
						long end = start + info.ByteSize;
						reader.Position = start;
						string assetName = ReadAlignedString(reader, end);
						if (string.Equals(assetName, name, StringComparison.OrdinalIgnoreCase))
						{
							byte[] data = ReadAlignedBytes(reader, end);
							string pathName = ((reader.Position < end) ? ReadAlignedString(reader, end) : "");
							return new TextAssetRef
							{
								BundlePath = bundlePath,
								RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
								AssetFileName = entry,
								Name = assetName,
								PathName = pathName,
								PathId = info.PathId,
								Data = data
							};
						}
					}
				}
				catch
				{
				}
			}
			return null;
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	private static string ReadAlignedString(AssetsFileReader reader, long end)
	{
		if (reader.Position + 4 > end)
		{
			throw new InvalidDataException("TextAsset string length is missing.");
		}
		int length = reader.ReadInt32();
		if (length < 0 || reader.Position + length > end)
		{
			throw new InvalidDataException("TextAsset string length is invalid.");
		}
		string result = Encoding.UTF8.GetString(reader.ReadBytes(length));
		reader.Align();
		return result;
	}

	private static byte[] ReadAlignedBytes(AssetsFileReader reader, long end)
	{
		if (reader.Position + 4 > end)
		{
			throw new InvalidDataException("TextAsset data length is missing.");
		}
		int length = reader.ReadInt32();
		if (length < 0 || reader.Position + length > end)
		{
			throw new InvalidDataException("TextAsset data length is invalid.");
		}
		byte[] result = reader.ReadBytes(length);
		reader.Align();
		return result;
	}

	public void ReplaceTextAsset(TextAssetRef asset, byte[] data, string backupRoot)
	{
		string backup = Path.Combine(backupRoot, asset.RelativeBundlePath);
		Directory.CreateDirectory(Path.GetDirectoryName(backup));
		if (!File.Exists(backup))
		{
			File.Copy(asset.BundlePath, backup);
		}
		string temporary = asset.BundlePath + ".mdcardtool.tmp";
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, asset.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetFileInfo info = assets.file.GetAssetsOfType(AssetClassID.TextAsset).First((AssetFileInfo x) => x.PathId == asset.PathId);
			AssetTypeValueField field = manager.GetBaseField(assets, info);
			field["m_Script"].AsByteArray = data;
			List<AssetsReplacer> replacements = new List<AssetsReplacer>
			{
				new AssetsReplacerFromMemory(assets.file, info, field)
			};
			byte[] serialized;
			using (MemoryStream stream = new MemoryStream())
			{
				using AssetsFileWriter writer = new AssetsFileWriter(stream);
				assets.file.Write(writer, 0L, replacements);
				serialized = stream.ToArray();
			}
			using AssetsFileWriter bundleWriter = new AssetsFileWriter(temporary);
			AssetBundleFile file = bundle.file;
			int num = 1;
			List<BundleReplacer> list = new List<BundleReplacer>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<BundleReplacer> span = CollectionsMarshal.AsSpan(list);
			int num2 = 0;
			span[num2] = new BundleReplacerFromMemory(assets.name, assets.name, hasSerializedData: true, serialized, -1L);
			num2++;
			file.Write(bundleWriter, list);
		}
		finally
		{
			manager.UnloadAll();
		}
		File.Move(temporary, asset.BundlePath, overwrite: true);
	}

	public byte[] DecodePng(TexRef texture, int maxSize = 0)
	{
		if (BuiltInCardFrameCatalog.IsTransparentGradientFrame(texture))
		{
			byte[] generated = BuiltInCardFrameCatalog.DecodeTransparentGradientFrame(texture);
			if (maxSize <= 0) return generated;
			using Image<Rgba32> image = Image.Load<Rgba32>(generated);
			if (image.Width > maxSize || image.Height > maxSize)
			{
				image.Mutate(context => context.Resize(new ResizeOptions
				{
					Size = new Size(maxSize, maxSize),
					Mode = ResizeMode.Max
				}));
			}
			using MemoryStream output = new();
			image.Save(output, TransparentRgbPngEncoder());
			return output.ToArray();
		}
		if (BuiltInCardFrameCatalog.IsPackagedFrame(texture))
		{
			return DecodeLocalPng(texture.ActiveBundlePath, maxSize);
		}
		Exception firstError;
		try
		{
			return DecodePngCore(texture, maxSize);
		}
		catch (Exception ex)
		{
			firstError = ex;
		}
		TexRef? resolved = ResolveTextureReference(texture);
		if (resolved == null)
		{
			throw new InvalidDataException($"找不到可对应 {texture.Name} 的 Texture2D；索引或手工 Mod 的资源结构可能已经改变。", firstError);
		}
		bool changed = !PathEquals(texture.ActiveBundlePath, resolved.BundlePath) || texture.PathId != resolved.PathId || !string.Equals(texture.AssetFileName, resolved.AssetFileName, StringComparison.Ordinal);
		ApplyResolvedReference(texture, resolved);
		if (!changed)
		{
			throw new InvalidDataException($"已定位 Texture2D {texture.Name}，但当前纹理格式无法解码：{firstError.Message}", firstError);
		}
		try
		{
			return DecodePngCore(texture, maxSize);
		}
		catch (Exception retryError)
		{
			throw new InvalidDataException($"已重新定位 Texture2D {texture.Name}，但仍无法解码：{retryError.Message}", retryError);
		}
	}

	private static byte[] DecodeLocalPng(string path, int maxSize)
	{
		if (!File.Exists(path)) throw new FileNotFoundException("内置卡框文件不存在。", path);
		using Image<Rgba32> image = Image.Load<Rgba32>(path);
		if (maxSize > 0 && (image.Width > maxSize || image.Height > maxSize))
		{
			image.Mutate(context => context.Resize(new ResizeOptions
			{
				Size = new Size(maxSize, maxSize),
				Mode = ResizeMode.Max
			}));
		}
		using MemoryStream output = new();
		image.Save(output, TransparentRgbPngEncoder());
		return output.ToArray();
	}

	private byte[] DecodePngCore(TexRef texture, int maxSize)
	{
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(texture.ActiveBundlePath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, texture.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetFileInfo info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).First((AssetFileInfo x) => x.PathId == texture.PathId);
			AssetTypeValueField baseField = manager.GetBaseField(assets, info);
			int width = baseField["m_Width"].AsInt;
			int height = baseField["m_Height"].AsInt;
			TextureFile textureFile = TextureFile.ReadTextureFile(baseField);
			byte[] pixels = textureFile.GetTextureData(assets);
			if (pixels == null || pixels.Length < width * height * 4)
			{
				throw new InvalidDataException($"贴图 {texture.Name} 的解码数据不足（格式 {textureFile.m_TextureFormat}）。");
			}
			using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(pixels.AsSpan(0, width * height * 4), width, height);
			image.Mutate(delegate(IImageProcessingContext x)
			{
				x.Flip(FlipMode.Vertical);
			});
			if (maxSize <= 0)
			{
				using (MemoryStream original = new MemoryStream())
				{
					image.Save(original, TransparentRgbPngEncoder());
					return original.ToArray();
				}
			}
			if (image.Width > maxSize || image.Height > maxSize)
			{
				image.Mutate(delegate(IImageProcessingContext x)
				{
					x.Resize(new ResizeOptions
					{
						Size = new Size(maxSize, maxSize),
						Mode = ResizeMode.Max
					});
				});
			}
			using MemoryStream resized = new MemoryStream();
			image.Save(resized, TransparentRgbPngEncoder());
			return resized.ToArray();
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	private static PngEncoder TransparentRgbPngEncoder() => new()
	{
		ColorType = PngColorType.RgbWithAlpha,
		// Master Duel stores shader-visible RGB below zero alpha. Explicitly
		// preserving it keeps decode -> edit -> write round-trips lossless.
		TransparentColorMode = PngTransparentColorMode.Preserve,
		CompressionLevel = PngCompressionLevel.BestCompression
	};

	public TexRef? ResolveTextureReference(TexRef texture)
	{
		string root = FindReferenceRoot(texture);
		string? streamingRoot = FindStreamingRoot(root);
		List<string> bundlePaths = CandidateBundlePaths(texture, root, streamingRoot).ToList();
		List<TexRef> candidates = new List<TexRef>();
		HashSet<string> identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string bundlePath in bundlePaths)
		{
			try
			{
				string scanRoot = IsInsideRoot(bundlePath, root)
					? root
					: (streamingRoot != null && IsInsideRoot(bundlePath, streamingRoot)
						? streamingRoot
						: (Path.GetDirectoryName(bundlePath) ?? root));
				foreach (TexRef candidate in ScanBundle(bundlePath, scanRoot, texture.SourceKind, includeDependencies: false).Textures)
				{
					if (identities.Add($"{Path.GetFullPath(candidate.BundlePath)}\0{candidate.AssetFileName}\0{candidate.PathId}"))
					{
						candidates.Add(candidate);
					}
				}
			}
			catch
			{
			}
		}
		if (candidates.Count == 0)
		{
			return null;
		}
		TexRef? resolved = SelectUnambiguous(candidates.Where((TexRef x) => string.Equals(x.Name, texture.Name, StringComparison.OrdinalIgnoreCase)), texture);
		if (resolved != null)
		{
			return resolved;
		}
		string normalizedName = NormalizeTextureName(texture.Name);
		if (normalizedName.Length > 0)
		{
			resolved = SelectUnambiguous(candidates.Where((TexRef x) => NormalizeTextureName(x.Name) == normalizedName), texture);
			if (resolved != null)
			{
				return resolved;
			}
		}
		if (texture.CardKey.Length > 0)
		{
			resolved = SelectUnambiguous(candidates.Where((TexRef x) => x.CardKey == texture.CardKey), texture);
			if (resolved != null)
			{
				return resolved;
			}
		}
		if (texture.Width > 0 && texture.Height > 0)
		{
			resolved = SelectUnambiguous(candidates.Where((TexRef x) => x.Width == texture.Width && x.Height == texture.Height), texture);
			if (resolved != null)
			{
				return resolved;
			}
		}
		return SelectUnambiguous(candidates, texture);
	}

	private static TexRef? SelectUnambiguous(IEnumerable<TexRef> source, TexRef expected)
	{
		TexRef[] choices = source.ToArray();
		if (choices.Length == 0)
		{
			return null;
		}
		if (choices.Length == 1)
		{
			return choices[0];
		}
		int Score(TexRef candidate)
		{
			int score = 0;
			if (PathEquals(candidate.BundlePath, expected.ActiveBundlePath))
			{
				score += 16;
			}
			if (PathEquals(candidate.BundlePath, expected.BundlePath))
			{
				score += 8;
			}
			if (string.Equals(candidate.AssetFileName, expected.AssetFileName, StringComparison.Ordinal))
			{
				score += 4;
			}
			if (candidate.PathId == expected.PathId)
			{
				score += 2;
			}
			if (candidate.Width == expected.Width && candidate.Height == expected.Height)
			{
				score++;
			}
			return score;
		}
		int bestScore = choices.Max(Score);
		TexRef[] best = choices.Where((TexRef x) => Score(x) == bestScore).Take(2).ToArray();
		return (best.Length == 1) ? best[0] : null;
	}

	private static IEnumerable<string> CandidateBundlePaths(TexRef texture, string root, string? streamingRoot)
	{
		HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		IEnumerable<string> Add(string? path)
		{
			if (!string.IsNullOrWhiteSpace(path))
			{
				string full = Path.GetFullPath(path);
				if (File.Exists(full) && yielded.Add(full))
				{
					yield return full;
				}
			}
		}
		foreach (string path in Add(texture.ActiveBundlePath))
		{
			yield return path;
		}
		foreach (string path in Add(texture.BundlePath))
		{
			yield return path;
		}
		if (!Path.IsPathRooted(texture.RelativeBundlePath))
		{
			foreach (string path in Add(Path.Combine(root, texture.RelativeBundlePath)))
			{
				yield return path;
			}
			if (streamingRoot != null && !PathEquals(streamingRoot, root))
			{
				foreach (string path in Add(Path.Combine(streamingRoot, texture.RelativeBundlePath)))
				{
					yield return path;
				}
			}
		}
		if (texture.SourceKind == "本地卡图" && texture.CardKey.Length > 0 && texture.CardKey.All(char.IsAsciiDigit))
		{
			foreach (string path in IndexService.CardIllustrationBundleCandidates(root, texture.CardKey).SelectMany(Add))
			{
				yield return path;
			}
			if (streamingRoot != null && !PathEquals(streamingRoot, root))
			{
				foreach (string path in IndexService.CardIllustrationBundleCandidates(streamingRoot, texture.CardKey).SelectMany(Add))
				{
					yield return path;
				}
			}
		}
	}

	private static string? FindStreamingRoot(string referenceRoot)
	{
		try
		{
			DirectoryInfo root = new(Path.GetFullPath(referenceRoot));
			if (root.Name.Equals("AssetBundle", StringComparison.OrdinalIgnoreCase)
				&& root.Parent?.Name.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase) == true)
			{
				return root.FullName;
			}
			if (root.Name.Equals("0000", StringComparison.OrdinalIgnoreCase)
				&& root.Parent?.Parent?.Name.Equals("LocalData", StringComparison.OrdinalIgnoreCase) == true
				&& root.Parent.Parent.Parent != null)
			{
				return IndexService.StreamingRoot(root.Parent.Parent.Parent.FullName);
			}
		}
		catch
		{
		}
		return null;
	}

	private static string FindReferenceRoot(TexRef texture)
	{
		string bundlePath = Path.GetFullPath(texture.BundlePath);
		if (!Path.IsPathRooted(texture.RelativeBundlePath) && texture.RelativeBundlePath.Length > 0)
		{
			string[] parts = texture.RelativeBundlePath.Split(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			}, StringSplitOptions.RemoveEmptyEntries);
			string? candidate = bundlePath;
			for (int i = 0; i < parts.Length && candidate != null; i++)
			{
				candidate = Path.GetDirectoryName(candidate);
			}
			if (candidate != null && PathEquals(Path.Combine(candidate, texture.RelativeBundlePath), bundlePath))
			{
				return candidate;
			}
		}
		if (texture.SourceKind == "本地卡图")
		{
			DirectoryInfo? directory = new FileInfo(bundlePath).Directory;
			while (directory != null)
			{
				if (directory.Name == "0000")
				{
					return directory.FullName;
				}
				directory = directory.Parent;
			}
		}
		return Path.GetDirectoryName(bundlePath) ?? bundlePath;
	}

	private static bool IsInsideRoot(string path, string root)
	{
		string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
		return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
	}

	private static bool PathEquals(string left, string right)
	{
		try
		{
			return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
		}
	}

	private static string NormalizeTextureName(string value)
	{
		return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
	}

	private static void ApplyResolvedReference(TexRef texture, TexRef resolved)
	{
		if (texture.OverrideBundlePath != null && !PathEquals(resolved.BundlePath, texture.BundlePath))
		{
			texture.OverrideBundlePath = resolved.BundlePath;
		}
		else
		{
			texture.BundlePath = resolved.BundlePath;
			texture.RelativeBundlePath = resolved.RelativeBundlePath;
		}
		texture.PathId = resolved.PathId;
		texture.AssetFileName = resolved.AssetFileName;
		if (resolved.Width > 0 && resolved.Height > 0)
		{
			texture.Width = resolved.Width;
			texture.Height = resolved.Height;
		}
	}

	private static AssetTypeValueField FindTextureImageDataField(AssetTypeValueField root)
	{
		static string FieldKey(string value)
		{
			return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
		}
		foreach (AssetTypeValueField child in root.Children)
		{
			string key = FieldKey(child.TemplateField.Name);
			if (key == "imagedata" || key == "mimagedata" || key == "picturedata" || key == "texturedata")
			{
				AssetTypeValueField array = child.IsDummy ? AssetTypeValueField.DUMMY_FIELD : child["Array"];
				return array.IsDummy ? child : array;
			}
		}
		foreach (AssetTypeValueField child in root.Children)
		{
			if (child.TemplateField.IsArray)
			{
				continue;
			}
			AssetTypeValueField nested = FindTextureImageDataField(child);
			if (!nested.IsDummy)
			{
				return nested;
			}
		}
		return AssetTypeValueField.DUMMY_FIELD;
	}

	private static void SetTextureImageData(AssetTypeValueField field, byte[] data)
	{
		if (field.TemplateField.ValueType == AssetValueType.ByteArray)
		{
			field.AsByteArray = data;
			return;
		}
		field.AsArray = new AssetTypeArrayInfo(data.Length);
		List<AssetTypeValueField> children = new List<AssetTypeValueField>(data.Length);
		foreach (byte value in data)
		{
			AssetTypeValueField child = ValueBuilder.DefaultValueFieldFromArrayTemplate(field);
			child.AsByte = value;
			children.Add(child);
		}
		field.Children = children;
	}

	public void Replace(TexRef texture, string imagePath, string backupRoot)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(imagePath);
		ReplaceImage(texture, image, backupRoot);
	}

	public void Replace(TexRef texture, byte[] encodedImage, string backupRoot)
	{
		using Image<Rgba32> image = Image.Load<Rgba32>(encodedImage);
		ReplaceImage(texture, image, backupRoot);
	}

	private void ReplaceImage(TexRef texture, Image<Rgba32> image, string backupRoot)
	{
		image.Mutate(delegate(IImageProcessingContext x)
		{
			x.Flip(FlipMode.Vertical);
		});
		byte[] pixels = new byte[image.Width * image.Height * 4];
		image.CopyPixelDataTo(pixels);
		ReplaceTextureData(texture, image.Width, image.Height, 4, pixels, backupRoot);
	}

	public void ReplaceAnimationAtlas(MonsterAnimationAssetRef asset, Image<Rgba32> atlas, string backupRoot)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("目标不是动画 Texture2D。", "asset");
		}
		ReplaceAnimationAtlas(asset, EncodeAnimationAtlas(atlas), backupRoot);
	}

	public AnimationAtlasTextureData EncodeAnimationAtlas(Image<Rgba32> atlas)
	{
		string texconv = AppPaths.ResolveFile("tools", "texconv.exe");
		if (!File.Exists(texconv))
		{
			throw new FileNotFoundException("缺少动画图集编码器 data\\tools\\texconv.exe，请使用完整分享包。", texconv);
		}
		string temporary = Path.Combine(Path.GetTempPath(), "MDCardModTool", "texconv_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporary);
		try
		{
			string input = Path.Combine(temporary, "atlas.png");
			using (Image<Rgba32> flipped = atlas.Clone(delegate(IImageProcessingContext x)
			{
				x.Flip(FlipMode.Vertical);
			}))
			{
				flipped.SaveAsPng(input);
			}
			ProcessStartInfo start = new ProcessStartInfo
			{
				FileName = texconv,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			// Use the encoder's default BC7 search instead of forcing exhaustive
			// search for every HD/SD atlas. Format and alpha handling are unchanged.
			string[] array = new string[9]
			{
				"-nologo", "-y", "-f", "BC7_UNORM", "-m", "1", "-o", temporary,
				input
			};
			foreach (string arg in array)
			{
				start.ArgumentList.Add(arg);
			}
			using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 texconv.exe。");
			Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
			Task<string> standardError = process.StandardError.ReadToEndAsync();
			process.WaitForExit();
			Task.WaitAll(standardOutput, standardError);
			if (process.ExitCode != 0)
			{
				throw new InvalidDataException("texconv 无法压缩动画图集：" + (standardError.Result + " " + standardOutput.Result).Trim());
			}
			byte[] bytes = File.ReadAllBytes(Directory.EnumerateFiles(temporary, "*.dds", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? Directory.EnumerateFiles(temporary, "*.DDS", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? throw new InvalidDataException("texconv 没有生成 DDS 数据。"));
			int expected = (atlas.Width + 3) / 4 * ((atlas.Height + 3) / 4) * 16;
			if (bytes.Length < expected + 128 || !bytes.AsSpan(0, 4).SequenceEqual("DDS "u8))
			{
				throw new InvalidDataException("texconv 生成的 DDS/BC7 数据不完整。");
			}
			return new AnimationAtlasTextureData(atlas.Width, atlas.Height, bytes.AsSpan(bytes.Length - expected, expected).ToArray());
		}
		finally
		{
			try
			{
				Directory.Delete(temporary, recursive: true);
			}
			catch
			{
			}
		}
	}

	public void ReplaceAnimationAtlas(MonsterAnimationAssetRef asset, AnimationAtlasTextureData encoded, string backupRoot)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("目标不是动画 Texture2D。", "asset");
		}
		ReplaceTextureData(asset.AsTexture(), encoded.Width, encoded.Height, 25, encoded.Data, backupRoot, forceLinearColorSpace: true);
	}

	public AnimationTextureMetadata ReadAnimationTextureMetadata(MonsterAnimationAssetRef asset)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("目标不是动画 Texture2D。", nameof(asset));
		}
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, asset.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetFileInfo info = assets.file.GetAssetsOfType(AssetClassID.Texture2D)
				.First(item => item.PathId == asset.PathId);
			AssetTypeValueField field = manager.GetBaseField(assets, info);
			AssetTypeValueField imageData = FindTextureImageDataField(field);
			AssetTypeValueField streamData = field["m_StreamData"];
			int inlineSize = 0;
			if (imageData != null && !imageData.IsDummy)
			{
				inlineSize = imageData.TemplateField.ValueType == AssetValueType.ByteArray
					? imageData.AsByteArray?.Length ?? 0
					: imageData.Children.Count;
			}
			return new AnimationTextureMetadata(
				field["m_Width"].AsInt,
				field["m_Height"].AsInt,
				field["m_TextureFormat"].AsInt,
				OptionalInt(field["m_ColorSpace"], -1),
				OptionalInt(field["m_MipCount"], 1),
				OptionalLong(field["m_CompleteImageSize"], inlineSize),
				streamData == null || streamData.IsDummy ? 0 : OptionalLong(streamData["size"], 0),
				streamData == null || streamData.IsDummy ? "" : OptionalString(streamData["path"]),
				inlineSize);
		}
		finally
		{
			manager.UnloadAll();
		}
	}

	private static int OptionalInt(AssetTypeValueField? field, int fallback) =>
		field == null || field.IsDummy ? fallback : field.AsInt;

	private static long OptionalLong(AssetTypeValueField? field, long fallback) =>
		field == null || field.IsDummy ? fallback : field.AsLong;

	private static string OptionalString(AssetTypeValueField? field) =>
		field == null || field.IsDummy ? "" : field.AsString;

	public void RewriteAnimationTemplateBundle(string bundlePath, MonsterAnimationAssetRef templateAsset,
		string targetCardId, string targetContainerPath, AnimationAtlasTextureData? textureData, byte[]? textData)
	{
		if (string.IsNullOrWhiteSpace(targetCardId) || !targetCardId.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("目标卡号必须是纯数字。", nameof(targetCardId));
		}
		if (!File.Exists(bundlePath))
		{
			throw new FileNotFoundException("动画模板 Bundle 不存在。", bundlePath);
		}
		string targetName = templateAsset.Kind switch
		{
			MonsterAnimationAssetKind.Texture => "P" + targetCardId,
			MonsterAnimationAssetKind.Atlas => "P" + targetCardId + ".atlas",
			MonsterAnimationAssetKind.Skeleton => "P" + targetCardId + "JS",
			_ => throw new ArgumentOutOfRangeException(nameof(templateAsset))
		};
		if (templateAsset.Kind == MonsterAnimationAssetKind.Texture && textureData == null)
		{
			throw new ArgumentNullException(nameof(textureData));
		}
		if (templateAsset.Kind != MonsterAnimationAssetKind.Texture && textData == null)
		{
			throw new ArgumentNullException(nameof(textData));
		}

		string temporary = bundlePath + ".rewrite.tmp";
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(bundlePath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, templateAsset.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetClassID classId = templateAsset.Kind == MonsterAnimationAssetKind.Texture
				? AssetClassID.Texture2D
				: AssetClassID.TextAsset;
			AssetFileInfo targetInfo = assets.file.GetAssetsOfType(classId)
				.FirstOrDefault(info => info.PathId == templateAsset.PathId)
				?? throw new InvalidDataException("模板 Bundle 中找不到预期的动画资源。" );
			AssetTypeValueField target = manager.GetBaseField(assets, targetInfo);
			target["m_Name"].AsString = targetName;
			if (templateAsset.Kind == MonsterAnimationAssetKind.Texture)
			{
				AnimationAtlasTextureData encoded = textureData!;
				ConfigureTextureField(target, encoded.Width, encoded.Height, 25, encoded.Data);
			}
			else
			{
				target["m_Script"].AsByteArray = textData!;
				AssetTypeValueField pathName = target["m_PathName"];
				if (pathName != null && !pathName.IsDummy && pathName.AsString.Length > 0)
				{
					pathName.AsString = Regex.Replace(pathName.AsString, "P" + Regex.Escape(templateAsset.CardId),
						"P" + targetCardId, RegexOptions.IgnoreCase);
				}
			}

			List<AssetsReplacer> replacements = [new AssetsReplacerFromMemory(assets.file, targetInfo, target)];
			foreach (AssetFileInfo bundleInfo in assets.file.GetAssetsOfType(AssetClassID.AssetBundle))
			{
				AssetTypeValueField bundleField = manager.GetBaseField(assets, bundleInfo);
				AssetTypeValueField container = bundleField["m_Container"]["Array"];
				bool changed = false;
				if (container != null && !container.IsDummy)
				{
					foreach (AssetTypeValueField child in container.Children)
					{
						AssetTypeValueField first = child["first"];
						if (first != null && !first.IsDummy
							&& Regex.IsMatch(first.AsString, "(?:^|/)p" + Regex.Escape(templateAsset.CardId) + "(?:/|\\.|$)", RegexOptions.IgnoreCase))
						{
							first.AsString = targetContainerPath;
							changed = true;
						}
					}
				}
				if (changed)
				{
					replacements.Add(new AssetsReplacerFromMemory(assets.file, bundleInfo, bundleField));
				}
			}

			byte[] serialized;
			using (MemoryStream stream = new())
			{
				using AssetsFileWriter writer = new(stream);
				assets.file.Write(writer, 0L, replacements);
				serialized = stream.ToArray();
			}
			using (AssetsFileWriter writer = new(temporary))
			{
				bundle.file.Write(writer,
				[
					new BundleReplacerFromMemory(assets.name, assets.name, hasSerializedData: true, serialized, -1L)
				]);
			}
		}
		catch
		{
			try
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			manager.UnloadAll();
		}
		File.Move(temporary, bundlePath, overwrite: true);
	}

	private static void ConfigureTextureField(AssetTypeValueField field, int width, int height, int textureFormat, byte[] pixels)
	{
		AssetTypeValueField widthField = field["m_Width"];
		AssetTypeValueField heightField = field["m_Height"];
		AssetTypeValueField formatField = field["m_TextureFormat"];
		AssetTypeValueField imageDataField = FindTextureImageDataField(field);
		if (widthField == null || widthField.IsDummy || heightField == null || heightField.IsDummy
			|| formatField == null || formatField.IsDummy || imageDataField == null || imageDataField.IsDummy)
		{
			throw new InvalidDataException("动画模板 Texture2D 缺少必要像素字段。" );
		}
		widthField.AsInt = width;
		heightField.AsInt = height;
		formatField.AsInt = textureFormat;
		AssetTypeValueField mipCount = field["m_MipCount"];
		if (mipCount != null && !mipCount.IsDummy) mipCount.AsInt = 1;
		AssetTypeValueField completeSize = field["m_CompleteImageSize"];
		if (completeSize != null && !completeSize.IsDummy) completeSize.AsInt = pixels.Length;
		// Master Duel's cut-in atlases are sampled as linear PMA textures. Leaving
		// the template's sRGB flag enabled produces dark fringes and can make a
		// generated atlas fail the same path used by official Spine resources.
		AssetTypeValueField colorSpace = field["m_ColorSpace"];
		if (colorSpace != null && !colorSpace.IsDummy) colorSpace.AsInt = 0;
		SetTextureImageData(imageDataField, pixels);
		AssetTypeValueField streamData = field["m_StreamData"];
		if (streamData != null && !streamData.IsDummy)
		{
			AssetTypeValueField offset = streamData["offset"];
			AssetTypeValueField size = streamData["size"];
			AssetTypeValueField path = streamData["path"];
			if (offset != null && !offset.IsDummy) offset.AsLong = 0;
			if (size != null && !size.IsDummy) size.AsLong = 0;
			if (path != null && !path.IsDummy) path.AsString = "";
		}
	}

	private void ReplaceTextureData(TexRef texture, int width, int height, int textureFormat, byte[] pixels,
		string backupRoot, bool forceLinearColorSpace = false)
	{
		TexRef resolved = ResolveTextureReference(texture) ?? throw new InvalidDataException($"当前 Bundle 中找不到可写入的 Texture2D：{texture.Name}（卡号 {texture.CardKey}）。");
		ApplyResolvedReference(texture, resolved);
		string targetPath = texture.ActiveBundlePath;
		string backup = Path.Combine(backupRoot, texture.RelativeBundlePath);
		Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? backupRoot);
		if (!File.Exists(backup))
		{
			File.Copy(targetPath, backup);
		}
		string temporary = targetPath + ".mdcardtool.tmp";
		AssetsManager manager = NewManager();
		try
		{
			BundleFileInstance bundle = manager.LoadBundleFile(targetPath);
			AssetsFileInstance assets = manager.LoadAssetsFileFromBundle(bundle, texture.AssetFileName);
			EnsureDatabase(manager, assets);
			AssetFileInfo info = assets.file.GetAssetsOfType(AssetClassID.Texture2D).First((AssetFileInfo x) => x.PathId == texture.PathId);
			AssetTypeValueField field = manager.GetBaseField(assets, info);
			AssetTypeValueField widthField = field["m_Width"];
			if (widthField == null || widthField.IsDummy)
			{
				goto IL_01c6;
			}
			AssetTypeValueField heightField = field["m_Height"];
			if (heightField == null || heightField.IsDummy)
			{
				goto IL_01c6;
			}
			AssetTypeValueField formatField = field["m_TextureFormat"];
			if (formatField == null || formatField.IsDummy)
			{
				goto IL_01c6;
			}
			AssetTypeValueField imageDataField = FindTextureImageDataField(field);
			if (imageDataField == null || imageDataField.IsDummy)
			{
				goto IL_01c6;
			}
			widthField.AsInt = width;
			heightField.AsInt = height;
			formatField.AsInt = textureFormat;
			AssetTypeValueField mipCountField = field["m_MipCount"];
			if (mipCountField != null && !mipCountField.IsDummy)
			{
				mipCountField.AsInt = 1;
			}
			AssetTypeValueField size = field["m_CompleteImageSize"];
			if (size != null && !size.IsDummy)
			{
				size.AsInt = pixels.Length;
			}
			if (forceLinearColorSpace)
			{
				AssetTypeValueField colorSpace = field["m_ColorSpace"];
				if (colorSpace != null && !colorSpace.IsDummy) colorSpace.AsInt = 0;
			}
			SetTextureImageData(imageDataField, pixels);
			AssetTypeValueField streamData = field["m_StreamData"];
			if (streamData != null && !streamData.IsDummy)
			{
				AssetTypeValueField offsetField = streamData["offset"];
				if (offsetField != null && !offsetField.IsDummy)
				{
					offsetField.AsLong = 0L;
				}
				AssetTypeValueField streamSizeField = streamData["size"];
				if (streamSizeField != null && !streamSizeField.IsDummy)
				{
					streamSizeField.AsLong = 0L;
				}
				AssetTypeValueField pathField = streamData["path"];
				if (pathField != null && !pathField.IsDummy)
				{
					pathField.AsString = "";
				}
			}
			List<AssetsReplacer> replacements = new List<AssetsReplacer>
			{
				new AssetsReplacerFromMemory(assets.file, info, field)
			};
			byte[] serialized;
			using (MemoryStream stream = new MemoryStream())
			{
				using AssetsFileWriter writer = new AssetsFileWriter(stream);
				assets.file.Write(writer, 0L, replacements);
				serialized = stream.ToArray();
			}
			using (AssetsFileWriter bundleWriter = new AssetsFileWriter(temporary))
			{
				bundle.file.Write(bundleWriter, new List<BundleReplacer>
				{
					new BundleReplacerFromMemory(assets.name, assets.name, hasSerializedData: true, serialized, -1L)
				});
			}
			goto end_IL_00f5;
			IL_01c6:
			throw new InvalidDataException("当前手工 Mod 的 Texture2D 缺少必要像素字段，无法安全写入。");
			end_IL_00f5:;
		}
		catch
		{
			try
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			manager.UnloadAll();
		}
		File.Move(temporary, targetPath, overwrite: true);
		texture.Width = width;
		texture.Height = height;
		texture.Category = CategoryForSource(texture.Name, width, height, texture.SourceKind);
	}

	public (int Width, int Height) ImageDimensions(string imagePath)
	{
		ImageInfo info = Image.Identify(imagePath) ?? throw new InvalidDataException("无法读取图片尺寸。");
		return (Width: info.Width, Height: info.Height);
	}
}
