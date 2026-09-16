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
		AssetsManager assetsManager = _threadManager ?? (_threadManager = new AssetsManager());
		if (assetsManager.ClassPackage == null && File.Exists(_classData))
		{
			assetsManager.LoadClassPackage(_classData);
		}
		return assetsManager;
	}

	private void EnsureDatabase(AssetsManager manager, AssetsFileInstance assets)
	{
		if (manager.ClassPackage != null)
		{
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

	public List<MonsterAnimationAssetRef> ScanAnimationAssetsFast(string bundlePath, string root, string? ownerCardId = null)
	{
		List<string> source = ((ownerCardId == null) ? new List<string>() : (from p in ReadAssetBundleContainerPaths(bundlePath)
			where Regex.IsMatch(p.Replace('\\', '/'), "/monstercutin/(?:tcg|ocg)/p" + Regex.Escape(ownerCardId) + "/", RegexOptions.IgnoreCase)
			select p).ToList());
		List<MonsterAnimationAssetRef> list = new List<MonsterAnimationAssetRef>();
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					AssetClassID[] array = new AssetClassID[2]
					{
						AssetClassID.Texture2D,
						AssetClassID.TextAsset
					};
					foreach (AssetClassID assetClassID in array)
					{
						foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(assetClassID))
						{
							AssetsFileReader reader = assetsFileInstance.file.Reader;
							long end = item.GetAbsoluteByteStart(assetsFileInstance.file) + item.ByteSize;
							reader.Position = item.GetAbsoluteByteStart(assetsFileInstance.file);
							string name = ReadAlignedString(reader, end);
							string cardId;
							MonsterAnimationAssetKind kind;
							bool flag = TryAnimationName(name, assetClassID, out cardId, out kind);
							if (ownerCardId != null)
							{
								flag = source.Any((string p) => p.EndsWith("/" + name + ".json", StringComparison.OrdinalIgnoreCase)) && kind == MonsterAnimationAssetKind.Skeleton;
								string extension = ((assetClassID == AssetClassID.Texture2D) ? ".png" : ".txt");
								if (source.Any((string p) => p.EndsWith("/" + name + extension, StringComparison.OrdinalIgnoreCase)) && (assetClassID == AssetClassID.Texture2D || name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)))
								{
									flag = true;
									kind = ((assetClassID == AssetClassID.Texture2D) ? MonsterAnimationAssetKind.Texture : MonsterAnimationAssetKind.Atlas);
								}
								if (flag)
								{
									cardId = ownerCardId;
								}
							}
							if (flag)
							{
								list.Add(new MonsterAnimationAssetRef
								{
									BundlePath = bundlePath,
									RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
									AssetFileName = allFileName,
									PathId = item.PathId,
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
			return list;
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	private static bool TryAnimationName(string name, AssetClassID type, out string cardId, out MonsterAnimationAssetKind kind)
	{
		cardId = "";
		kind = (MonsterAnimationAssetKind)0;
		Match match;
		if (type == AssetClassID.Texture2D)
		{
			match = Regex.Match(name, "^P(?<id>\\d+)(?:_\\d+)?$", RegexOptions.IgnoreCase);
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
		if (sourceKind == "视觉资源" || sourceKind == "基础视觉资源")
		{
			string text = VisualAssetClassifier.CategoryFor(name);
			if (text != null)
			{
				return text;
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
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			int num = 0;
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					num++;
					foreach (AssetFileInfo assetInfo in assetsFileInstance.file.AssetInfos)
					{
						string key = ((AssetClassID)assetInfo.TypeId/*cast due to .constrained prefix*/).ToString();
						dictionary[key] = dictionary.GetValueOrDefault(key) + 1;
					}
				}
				catch
				{
				}
			}
			return new BundleSummary
			{
				RelativePath = Path.GetRelativePath(root, bundlePath),
				SerializedFiles = num,
				AssetTypes = dictionary
			};
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	public List<string> ReadAssetBundleContainerPaths(string bundlePath, bool dependencies = false)
	{
		List<string> list = new List<string>();
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					EnsureDatabase(assetsManager, assetsFileInstance);
					foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(AssetClassID.AssetBundle))
					{
						if (dependencies)
						{
							foreach (AssetTypeValueField child in assetsManager.GetBaseField(assetsFileInstance, item)["m_Dependencies"]["Array"].Children)
							{
								list.Add(child.AsString);
							}
							continue;
						}
						foreach (AssetTypeValueField child2 in assetsManager.GetBaseField(assetsFileInstance, item)["m_Container"]["Array"].Children)
						{
							string asString = child2["first"].AsString;
							if (!string.IsNullOrWhiteSpace(asString))
							{
								list.Add(asString);
							}
						}
					}
				}
				catch
				{
				}
			}
			return list;
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	public List<TextAssetRef> ReadTextAssets(string bundlePath)
	{
		List<TextAssetRef> list = new List<TextAssetRef>();
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					EnsureDatabase(assetsManager, assetsFileInstance);
					foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, item);
						list.Add(new TextAssetRef
						{
							BundlePath = bundlePath,
							RelativeBundlePath = Path.GetFileName(bundlePath),
							AssetFileName = allFileName,
							Name = baseField["m_Name"].AsString,
							PathName = (baseField["m_PathName"].IsDummy ? "" : baseField["m_PathName"].AsString),
							PathId = item.PathId,
							Data = baseField["m_Script"].AsByteArray
						});
					}
				}
				catch
				{
				}
			}
			return list;
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	public TextAssetRef? FindTextAsset(string bundlePath, string root, string name)
	{
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					EnsureDatabase(assetsManager, assetsFileInstance);
					foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, item);
						if (string.Equals(baseField["m_Name"].AsString, name, StringComparison.OrdinalIgnoreCase))
						{
							return new TextAssetRef
							{
								BundlePath = bundlePath,
								RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
								AssetFileName = allFileName,
								Name = baseField["m_Name"].AsString,
								PathName = (baseField["m_PathName"].IsDummy ? "" : baseField["m_PathName"].AsString),
								PathId = item.PathId,
								Data = baseField["m_Script"].AsByteArray
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
			assetsManager.UnloadAll();
		}
	}

	public TextAssetRef ReadTextAsset(MonsterAnimationAssetRef asset)
	{
		if (asset.Kind == MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("动画纹理不是 TextAsset。", "asset");
		}
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bunInst = assetsManager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bunInst, asset.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetFileInfo assetFileInfo = assetsFileInstance.file.GetAssetsOfType(AssetClassID.TextAsset).First((AssetFileInfo x) => x.PathId == asset.PathId);
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, assetFileInfo);
			return new TextAssetRef
			{
				BundlePath = asset.BundlePath,
				RelativeBundlePath = asset.RelativeBundlePath,
				AssetFileName = asset.AssetFileName,
				Name = baseField["m_Name"].AsString,
				PathName = (baseField["m_PathName"].IsDummy ? "" : baseField["m_PathName"].AsString),
				PathId = assetFileInfo.PathId,
				Data = baseField["m_Script"].AsByteArray
			};
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	public TextAssetRef? FindTextAssetFast(string bundlePath, string root, string name)
	{
		AssetsManager assetsManager = new AssetsManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			foreach (string allFileName in bundleFileInstance.file.GetAllFileNames())
			{
				try
				{
					AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, allFileName);
					foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(AssetClassID.TextAsset))
					{
						AssetsFileReader reader = assetsFileInstance.file.Reader;
						long absoluteByteStart = item.GetAbsoluteByteStart(assetsFileInstance.file);
						long num = absoluteByteStart + item.ByteSize;
						reader.Position = absoluteByteStart;
						string text = ReadAlignedString(reader, num);
						if (string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
						{
							byte[] data = ReadAlignedBytes(reader, num);
							string pathName = ((reader.Position < num) ? ReadAlignedString(reader, num) : "");
							return new TextAssetRef
							{
								BundlePath = bundlePath,
								RelativeBundlePath = Path.GetRelativePath(root, bundlePath),
								AssetFileName = allFileName,
								Name = text,
								PathName = pathName,
								PathId = item.PathId,
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
			assetsManager.UnloadAll();
		}
	}

	private static string ReadAlignedString(AssetsFileReader reader, long end)
	{
		if (reader.Position + 4 > end)
		{
			throw new InvalidDataException("TextAsset string length is missing.");
		}
		int num = reader.ReadInt32();
		if (num < 0 || reader.Position + num > end)
		{
			throw new InvalidDataException("TextAsset string length is invalid.");
		}
		string result = Encoding.UTF8.GetString(reader.ReadBytes(num));
		reader.Align();
		return result;
	}

	private static byte[] ReadAlignedBytes(AssetsFileReader reader, long end)
	{
		if (reader.Position + 4 > end)
		{
			throw new InvalidDataException("TextAsset data length is missing.");
		}
		int num = reader.ReadInt32();
		if (num < 0 || reader.Position + num > end)
		{
			throw new InvalidDataException("TextAsset data length is invalid.");
		}
		byte[] result = reader.ReadBytes(num);
		reader.Align();
		return result;
	}

	public void ReplaceTextAsset(TextAssetRef asset, byte[] data, string backupRoot)
	{
		string text = Path.Combine(backupRoot, asset.RelativeBundlePath);
		Directory.CreateDirectory(Path.GetDirectoryName(text));
		if (!File.Exists(text))
		{
			File.Copy(asset.BundlePath, text);
		}
		string text2 = asset.BundlePath + ".mdcardtool.tmp";
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, asset.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetFileInfo info = assetsFileInstance.file.GetAssetsOfType(AssetClassID.TextAsset).First((AssetFileInfo x) => x.PathId == asset.PathId);
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, info);
			baseField["m_Script"].AsByteArray = data;
			List<AssetsReplacer> replacers = new List<AssetsReplacer>
			{
				new AssetsReplacerFromMemory(assetsFileInstance.file, info, baseField)
			};
			byte[] buffer;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				using AssetsFileWriter writer = new AssetsFileWriter(memoryStream);
				assetsFileInstance.file.Write(writer, 0L, replacers);
				buffer = memoryStream.ToArray();
			}
			using AssetsFileWriter writer2 = new AssetsFileWriter(text2);
			AssetBundleFile file = bundleFileInstance.file;
			int num = 1;
			List<BundleReplacer> list = new List<BundleReplacer>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<BundleReplacer> span = CollectionsMarshal.AsSpan(list);
			int num2 = 0;
			span[num2] = new BundleReplacerFromMemory(assetsFileInstance.name, assetsFileInstance.name, hasSerializedData: true, buffer, -1L);
			num2++;
			file.Write(writer2, list);
		}
		finally
		{
			assetsManager.UnloadAll();
		}
		File.Move(text2, asset.BundlePath, overwrite: true);
	}

	public byte[] DecodePng(TexRef texture, int maxSize = 0)
	{
		if (BuiltInCardFrameCatalog.IsTransparentGradientFrame(texture))
		{
			byte[] array = BuiltInCardFrameCatalog.DecodeTransparentGradientFrame(texture);
			if (maxSize <= 0)
			{
				return array;
			}
			using Image<Rgba32> image = Image.Load<Rgba32>(array);
			if (image.Width > maxSize || image.Height > maxSize)
			{
				image.Mutate(delegate(IImageProcessingContext context)
				{
					context.Resize(new ResizeOptions
					{
						Size = new Size(maxSize, maxSize),
						Mode = ResizeMode.Max
					});
				});
			}
			using MemoryStream memoryStream = new MemoryStream();
			image.Save(memoryStream, TransparentRgbPngEncoder());
			return memoryStream.ToArray();
		}
		if (BuiltInCardFrameCatalog.IsPackagedFrame(texture))
		{
			return DecodeLocalPng(texture.ActiveBundlePath, maxSize);
		}
		Exception ex2;
		try
		{
			return DecodePngCore(texture, maxSize);
		}
		catch (Exception ex)
		{
			ex2 = ex;
		}
		TexRef texRef = ResolveTextureReference(texture);
		if (texRef == null)
		{
			throw new InvalidDataException("找不到可对应 " + texture.Name + " 的 Texture2D；索引或手工 Mod 的资源结构可能已经改变。", ex2);
		}
		bool num = !PathEquals(texture.ActiveBundlePath, texRef.BundlePath) || texture.PathId != texRef.PathId || !string.Equals(texture.AssetFileName, texRef.AssetFileName, StringComparison.Ordinal);
		ApplyResolvedReference(texture, texRef);
		if (!num)
		{
			throw new InvalidDataException("已定位 Texture2D " + texture.Name + "，但当前纹理格式无法解码：" + ex2.Message, ex2);
		}
		try
		{
			return DecodePngCore(texture, maxSize);
		}
		catch (Exception ex3)
		{
			throw new InvalidDataException("已重新定位 Texture2D " + texture.Name + "，但仍无法解码：" + ex3.Message, ex3);
		}
	}

	private static byte[] DecodeLocalPng(string path, int maxSize)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("内置卡框文件不存在。", path);
		}
		using Image<Rgba32> image = Image.Load<Rgba32>(path);
		if (maxSize > 0 && (image.Width > maxSize || image.Height > maxSize))
		{
			image.Mutate(delegate(IImageProcessingContext context)
			{
				context.Resize(new ResizeOptions
				{
					Size = new Size(maxSize, maxSize),
					Mode = ResizeMode.Max
				});
			});
		}
		using MemoryStream memoryStream = new MemoryStream();
		image.Save(memoryStream, TransparentRgbPngEncoder());
		return memoryStream.ToArray();
	}

	private byte[] DecodePngCore(TexRef texture, int maxSize)
	{
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bunInst = assetsManager.LoadBundleFile(texture.ActiveBundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bunInst, texture.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetFileInfo info = assetsFileInstance.file.GetAssetsOfType(AssetClassID.Texture2D).First((AssetFileInfo x) => x.PathId == texture.PathId);
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, info);
			int asInt = baseField["m_Width"].AsInt;
			int asInt2 = baseField["m_Height"].AsInt;
			TextureFile textureFile = TextureFile.ReadTextureFile(baseField);
			byte[] textureData = textureFile.GetTextureData(assetsFileInstance);
			if (textureData == null || textureData.Length < asInt * asInt2 * 4)
			{
				throw new InvalidDataException($"贴图 {texture.Name} 的解码数据不足（格式 {textureFile.m_TextureFormat}）。");
			}
			using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(textureData.AsSpan(0, asInt * asInt2 * 4), asInt, asInt2);
			image.Mutate(delegate(IImageProcessingContext x)
			{
				x.Flip(FlipMode.Vertical);
			});
			if (maxSize <= 0)
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					image.Save(memoryStream, TransparentRgbPngEncoder());
					return memoryStream.ToArray();
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
			using MemoryStream memoryStream2 = new MemoryStream();
			image.Save(memoryStream2, TransparentRgbPngEncoder());
			return memoryStream2.ToArray();
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	private static PngEncoder TransparentRgbPngEncoder()
	{
		return new PngEncoder
		{
			ColorType = PngColorType.RgbWithAlpha,
			TransparentColorMode = PngTransparentColorMode.Preserve,
			CompressionLevel = PngCompressionLevel.Level9
		};
	}

	public TexRef? ResolveTextureReference(TexRef texture)
	{
		string text = FindReferenceRoot(texture);
		string text2 = FindStreamingRoot(text);
		List<string> list = CandidateBundlePaths(texture, text, text2).ToList();
		List<TexRef> list2 = new List<TexRef>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in list)
		{
			try
			{
				string root = (IsInsideRoot(item, text) ? text : ((text2 != null && IsInsideRoot(item, text2)) ? text2 : (Path.GetDirectoryName(item) ?? text)));
				foreach (TexRef texture2 in ScanBundle(item, root, texture.SourceKind, includeDependencies: false).Textures)
				{
					if (hashSet.Add($"{Path.GetFullPath(texture2.BundlePath)}\0{texture2.AssetFileName}\0{texture2.PathId}"))
					{
						list2.Add(texture2);
					}
				}
			}
			catch
			{
			}
		}
		if (list2.Count == 0)
		{
			return null;
		}
		TexRef texRef = SelectUnambiguous(list2.Where((TexRef x) => string.Equals(x.Name, texture.Name, StringComparison.OrdinalIgnoreCase)), texture);
		if (texRef != null)
		{
			return texRef;
		}
		string normalizedName = NormalizeTextureName(texture.Name);
		if (normalizedName.Length > 0)
		{
			texRef = SelectUnambiguous(list2.Where((TexRef x) => NormalizeTextureName(x.Name) == normalizedName), texture);
			if (texRef != null)
			{
				return texRef;
			}
		}
		if (texture.CardKey.Length > 0)
		{
			texRef = SelectUnambiguous(list2.Where((TexRef x) => x.CardKey == texture.CardKey), texture);
			if (texRef != null)
			{
				return texRef;
			}
		}
		if (texture.Width > 0 && texture.Height > 0)
		{
			texRef = SelectUnambiguous(list2.Where((TexRef x) => x.Width == texture.Width && x.Height == texture.Height), texture);
			if (texRef != null)
			{
				return texRef;
			}
		}
		return SelectUnambiguous(list2, texture);
	}

	private static TexRef? SelectUnambiguous(IEnumerable<TexRef> source, TexRef expected)
	{
		TexRef[] array = source.ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		if (array.Length == 1)
		{
			return array[0];
		}
		int bestScore = ((IEnumerable<TexRef>)array).Max((Func<TexRef, int>)Score);
		TexRef[] array2 = array.Where((TexRef x) => Score(x) == bestScore).Take(2).ToArray();
		if (array2.Length != 1)
		{
			return null;
		}
		return array2[0];
		int Score(TexRef candidate)
		{
			int num = 0;
			if (PathEquals(candidate.BundlePath, expected.ActiveBundlePath))
			{
				num += 16;
			}
			if (PathEquals(candidate.BundlePath, expected.BundlePath))
			{
				num += 8;
			}
			if (string.Equals(candidate.AssetFileName, expected.AssetFileName, StringComparison.Ordinal))
			{
				num += 4;
			}
			if (candidate.PathId == expected.PathId)
			{
				num += 2;
			}
			if (candidate.Width == expected.Width && candidate.Height == expected.Height)
			{
				num++;
			}
			return num;
		}
	}

	private static IEnumerable<string> CandidateBundlePaths(TexRef texture, string root, string? streamingRoot)
	{
		HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in Add(texture.ActiveBundlePath))
		{
			yield return item;
		}
		foreach (string item2 in Add(texture.BundlePath))
		{
			yield return item2;
		}
		if (!Path.IsPathRooted(texture.RelativeBundlePath))
		{
			foreach (string item3 in Add(Path.Combine(root, texture.RelativeBundlePath)))
			{
				yield return item3;
			}
			if (streamingRoot != null && !PathEquals(streamingRoot, root))
			{
				foreach (string item4 in Add(Path.Combine(streamingRoot, texture.RelativeBundlePath)))
				{
					yield return item4;
				}
			}
		}
		if (!(texture.SourceKind == "本地卡图") || texture.CardKey.Length <= 0 || !texture.CardKey.All(char.IsAsciiDigit))
		{
			yield break;
		}
		foreach (string item5 in IndexService.CardIllustrationBundleCandidates(root, texture.CardKey).SelectMany(Add))
		{
			yield return item5;
		}
		if (streamingRoot == null || PathEquals(streamingRoot, root))
		{
			yield break;
		}
		foreach (string item6 in IndexService.CardIllustrationBundleCandidates(streamingRoot, texture.CardKey).SelectMany(Add))
		{
			yield return item6;
		}
		IEnumerable<string> Add(string? path)
		{
			if (!string.IsNullOrWhiteSpace(path))
			{
				string fullPath = Path.GetFullPath(path);
				if (File.Exists(fullPath) && yielded.Add(fullPath))
				{
					yield return fullPath;
				}
			}
		}
	}

	private static string? FindStreamingRoot(string referenceRoot)
	{
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(Path.GetFullPath(referenceRoot));
			if (directoryInfo.Name.Equals("AssetBundle", StringComparison.OrdinalIgnoreCase))
			{
				DirectoryInfo? parent = directoryInfo.Parent;
				if (parent != null && parent.Name.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase))
				{
					return directoryInfo.FullName;
				}
			}
			if (directoryInfo.Name.Equals("0000", StringComparison.OrdinalIgnoreCase))
			{
				DirectoryInfo? parent2 = directoryInfo.Parent;
				if (parent2 != null && parent2.Parent?.Name.Equals("LocalData", StringComparison.OrdinalIgnoreCase) == true && directoryInfo.Parent.Parent.Parent != null)
				{
					return IndexService.StreamingRoot(directoryInfo.Parent.Parent.Parent.FullName);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static string FindReferenceRoot(TexRef texture)
	{
		string fullPath = Path.GetFullPath(texture.BundlePath);
		if (!Path.IsPathRooted(texture.RelativeBundlePath) && texture.RelativeBundlePath.Length > 0)
		{
			string[] array = texture.RelativeBundlePath.Split(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			}, StringSplitOptions.RemoveEmptyEntries);
			string text = fullPath;
			for (int i = 0; i < array.Length; i++)
			{
				if (text == null)
				{
					break;
				}
				text = Path.GetDirectoryName(text);
			}
			if (text != null && PathEquals(Path.Combine(text, texture.RelativeBundlePath), fullPath))
			{
				return text;
			}
		}
		if (texture.SourceKind == "本地卡图")
		{
			for (DirectoryInfo directoryInfo = new FileInfo(fullPath).Directory; directoryInfo != null; directoryInfo = directoryInfo.Parent)
			{
				if (directoryInfo.Name == "0000")
				{
					return directoryInfo.FullName;
				}
			}
		}
		return Path.GetDirectoryName(fullPath) ?? fullPath;
	}

	private static bool IsInsideRoot(string path, string root)
	{
		string relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
		if (relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
		{
			return !Path.IsPathRooted(relativePath);
		}
		return false;
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
		foreach (AssetTypeValueField child in root.Children)
		{
			switch (FieldKey(child.TemplateField.Name))
			{
			case "imagedata":
			case "mimagedata":
			case "picturedata":
			case "texturedata":
			{
				AssetTypeValueField assetTypeValueField = (child.IsDummy ? AssetTypeValueField.DUMMY_FIELD : child["Array"]);
				return assetTypeValueField.IsDummy ? child : assetTypeValueField;
			}
			}
		}
		foreach (AssetTypeValueField child2 in root.Children)
		{
			if (!child2.TemplateField.IsArray)
			{
				AssetTypeValueField assetTypeValueField2 = FindTextureImageDataField(child2);
				if (!assetTypeValueField2.IsDummy)
				{
					return assetTypeValueField2;
				}
			}
		}
		return AssetTypeValueField.DUMMY_FIELD;
		static string FieldKey(string value)
		{
			return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
		}
	}

	private static void SetTextureImageData(AssetTypeValueField field, byte[] data)
	{
		if (field.TemplateField.ValueType == AssetValueType.ByteArray)
		{
			field.AsByteArray = data;
			return;
		}
		field.AsArray = new AssetTypeArrayInfo(data.Length);
		List<AssetTypeValueField> list = new List<AssetTypeValueField>(data.Length);
		foreach (byte asByte in data)
		{
			AssetTypeValueField assetTypeValueField = ValueBuilder.DefaultValueFieldFromArrayTemplate(field);
			assetTypeValueField.AsByte = asByte;
			list.Add(assetTypeValueField);
		}
		field.Children = list;
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
		byte[] array = new byte[image.Width * image.Height * 4];
		image.CopyPixelDataTo(array);
		ReplaceTextureData(texture, image.Width, image.Height, 4, array, backupRoot);
	}

	public void ReplaceAnimationAtlas(MonsterAnimationAssetRef asset, Image<Rgba32> atlas, string backupRoot)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("目标不是动画 Texture2D。", "asset");
		}
		ReplaceAnimationAtlas(asset, EncodeAnimationAtlas(atlas), backupRoot);
	}

	public AnimationAtlasTextureData EncodeAnimationAtlas(Image<Rgba32> atlas, bool mobile = false)
	{
		if (mobile)
		{
			using (Image<Rgba32> image = atlas.Clone(delegate(IImageProcessingContext x)
			{
				x.Flip(FlipMode.Vertical);
			}))
			{
				byte[] array = new byte[checked(atlas.Width * atlas.Height * 4)];
				image.CopyPixelDataTo(array);
				return new AnimationAtlasTextureData(atlas.Width, atlas.Height, array, 4);
			}
		}
		string text = AppPaths.ResolveFile("tools", "texconv.exe");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("缺少动画图集编码器 data\\tools\\texconv.exe，请使用完整分享包。", text);
		}
		string text2 = Path.Combine(Path.GetTempPath(), "MDCardModTool", "texconv_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text2);
		try
		{
			string text3 = Path.Combine(text2, "atlas.png");
			using (Image<Rgba32> source = atlas.Clone(delegate(IImageProcessingContext x)
			{
				x.Flip(FlipMode.Vertical);
			}))
			{
				source.SaveAsPng(text3);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			};
			string[] array2 = new string[9] { "-nologo", "-y", "-f", "BC7_UNORM", "-m", "1", "-o", text2, text3 };
			foreach (string item in array2)
			{
				processStartInfo.ArgumentList.Add(item);
			}
			using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动 texconv.exe。");
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			Task<string> task2 = process.StandardError.ReadToEndAsync();
			process.WaitForExit();
			Task.WaitAll(task, task2);
			if (process.ExitCode != 0)
			{
				throw new InvalidDataException("texconv 无法压缩动画图集：" + (task2.Result + " " + task.Result).Trim());
			}
			byte[] array3 = File.ReadAllBytes(Directory.EnumerateFiles(text2, "*.dds", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? Directory.EnumerateFiles(text2, "*.DDS", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? throw new InvalidDataException("texconv 没有生成 DDS 数据。"));
			int num2 = (atlas.Width + 3) / 4 * ((atlas.Height + 3) / 4) * 16;
			if (array3.Length < num2 + 128 || !array3.AsSpan(0, 4).SequenceEqual("DDS "u8))
			{
				throw new InvalidDataException("texconv 生成的 DDS/BC7 数据不完整。");
			}
			return new AnimationAtlasTextureData(atlas.Width, atlas.Height, array3.AsSpan(array3.Length - num2, num2).ToArray());
		}
		finally
		{
			try
			{
				Directory.Delete(text2, recursive: true);
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
		ReplaceTextureData(asset.AsTexture(), encoded.Width, encoded.Height, encoded.TextureFormat, encoded.Data, backupRoot, forceLinearColorSpace: true);
	}

	public AnimationTextureMetadata ReadAnimationTextureMetadata(MonsterAnimationAssetRef asset)
	{
		if (asset.Kind != MonsterAnimationAssetKind.Texture)
		{
			throw new ArgumentException("目标不是动画 Texture2D。", "asset");
		}
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bunInst = assetsManager.LoadBundleFile(asset.BundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bunInst, asset.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetFileInfo info = assetsFileInstance.file.GetAssetsOfType(AssetClassID.Texture2D).First((AssetFileInfo item) => item.PathId == asset.PathId);
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, info);
			AssetTypeValueField assetTypeValueField = FindTextureImageDataField(baseField);
			AssetTypeValueField assetTypeValueField2 = baseField["m_StreamData"];
			int num = 0;
			if (assetTypeValueField != null && !assetTypeValueField.IsDummy)
			{
				int num2;
				if (assetTypeValueField.TemplateField.ValueType != AssetValueType.ByteArray)
				{
					num2 = assetTypeValueField.Children.Count;
				}
				else
				{
					byte[] asByteArray = assetTypeValueField.AsByteArray;
					num2 = ((asByteArray != null) ? asByteArray.Length : 0);
				}
				num = num2;
			}
			return new AnimationTextureMetadata(baseField["m_Width"].AsInt, baseField["m_Height"].AsInt, baseField["m_TextureFormat"].AsInt, OptionalInt(baseField["m_ColorSpace"], -1), OptionalInt(baseField["m_MipCount"], 1), OptionalLong(baseField["m_CompleteImageSize"], num), (assetTypeValueField2 == null || assetTypeValueField2.IsDummy) ? 0 : OptionalLong(assetTypeValueField2["size"], 0L), (assetTypeValueField2 == null || assetTypeValueField2.IsDummy) ? "" : OptionalString(assetTypeValueField2["path"]), num);
		}
		finally
		{
			assetsManager.UnloadAll();
		}
	}

	private static int OptionalInt(AssetTypeValueField? field, int fallback)
	{
		if (field != null && !field.IsDummy)
		{
			return field.AsInt;
		}
		return fallback;
	}

	private static long OptionalLong(AssetTypeValueField? field, long fallback)
	{
		if (field != null && !field.IsDummy)
		{
			return field.AsLong;
		}
		return fallback;
	}

	private static string OptionalString(AssetTypeValueField? field)
	{
		if (field != null && !field.IsDummy)
		{
			return field.AsString;
		}
		return "";
	}

	public void RewriteAnimationTemplateBundle(string bundlePath, MonsterAnimationAssetRef templateAsset, string targetCardId, string targetContainerPath, AnimationAtlasTextureData? textureData, byte[]? textData)
	{
		if (string.IsNullOrWhiteSpace(targetCardId) || !targetCardId.All(char.IsAsciiDigit))
		{
			throw new ArgumentException("目标卡号必须是纯数字。", "targetCardId");
		}
		if (!File.Exists(bundlePath))
		{
			throw new FileNotFoundException("动画模板 Bundle 不存在。", bundlePath);
		}
		string asString = templateAsset.Kind switch
		{
			MonsterAnimationAssetKind.Texture => "P" + targetCardId,
			MonsterAnimationAssetKind.Atlas => "P" + targetCardId + ".atlas",
			MonsterAnimationAssetKind.Skeleton => "P" + targetCardId + "JS",
			_ => throw new ArgumentOutOfRangeException("templateAsset"),
		};
		if (templateAsset.Kind == MonsterAnimationAssetKind.Texture && textureData == null)
		{
			throw new ArgumentNullException("textureData");
		}
		if (templateAsset.Kind != MonsterAnimationAssetKind.Texture && textData == null)
		{
			throw new ArgumentNullException("textData");
		}
		string text = bundlePath + ".rewrite.tmp";
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(bundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, templateAsset.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetClassID typeId = ((templateAsset.Kind == MonsterAnimationAssetKind.Texture) ? AssetClassID.Texture2D : AssetClassID.TextAsset);
			AssetFileInfo info = assetsFileInstance.file.GetAssetsOfType(typeId).FirstOrDefault((AssetFileInfo assetFileInfo) => assetFileInfo.PathId == templateAsset.PathId) ?? throw new InvalidDataException("模板 Bundle 中找不到预期的动画资源。");
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, info);
			baseField["m_Name"].AsString = asString;
			if (templateAsset.Kind == MonsterAnimationAssetKind.Texture)
			{
				ConfigureTextureField(baseField, textureData.Width, textureData.Height, 25, textureData.Data);
			}
			else
			{
				baseField["m_Script"].AsByteArray = textData;
				AssetTypeValueField assetTypeValueField = baseField["m_PathName"];
				if (assetTypeValueField != null && !assetTypeValueField.IsDummy && assetTypeValueField.AsString.Length > 0)
				{
					assetTypeValueField.AsString = Regex.Replace(assetTypeValueField.AsString, "P" + Regex.Escape(templateAsset.CardId), "P" + targetCardId, RegexOptions.IgnoreCase);
				}
			}
			int num = 1;
			List<AssetsReplacer> list = new List<AssetsReplacer>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<AssetsReplacer> span = CollectionsMarshal.AsSpan(list);
			int num2 = 0;
			span[num2] = new AssetsReplacerFromMemory(assetsFileInstance.file, info, baseField);
			num2++;
			List<AssetsReplacer> list2 = list;
			foreach (AssetFileInfo item in assetsFileInstance.file.GetAssetsOfType(AssetClassID.AssetBundle))
			{
				AssetTypeValueField baseField2 = assetsManager.GetBaseField(assetsFileInstance, item);
				AssetTypeValueField assetTypeValueField2 = baseField2["m_Container"]["Array"];
				bool flag = false;
				if (assetTypeValueField2 != null && !assetTypeValueField2.IsDummy)
				{
					foreach (AssetTypeValueField child in assetTypeValueField2.Children)
					{
						AssetTypeValueField assetTypeValueField3 = child["first"];
						if (assetTypeValueField3 != null && !assetTypeValueField3.IsDummy && Regex.IsMatch(assetTypeValueField3.AsString, "(?:^|/)p" + Regex.Escape(templateAsset.CardId) + "(?:/|\\.|$)", RegexOptions.IgnoreCase))
						{
							assetTypeValueField3.AsString = targetContainerPath;
							flag = true;
						}
					}
				}
				if (flag)
				{
					list2.Add(new AssetsReplacerFromMemory(assetsFileInstance.file, item, baseField2));
				}
			}
			byte[] buffer;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				using AssetsFileWriter writer = new AssetsFileWriter(memoryStream);
				assetsFileInstance.file.Write(writer, 0L, list2);
				buffer = memoryStream.ToArray();
			}
			using AssetsFileWriter writer2 = new AssetsFileWriter(text);
			AssetBundleFile file = bundleFileInstance.file;
			num2 = 1;
			List<BundleReplacer> list3 = new List<BundleReplacer>(num2);
			CollectionsMarshal.SetCount(list3, num2);
			Span<BundleReplacer> span2 = CollectionsMarshal.AsSpan(list3);
			num = 0;
			span2[num] = new BundleReplacerFromMemory(assetsFileInstance.name, assetsFileInstance.name, hasSerializedData: true, buffer, -1L);
			num++;
			file.Write(writer2, list3);
		}
		catch
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			assetsManager.UnloadAll();
		}
		File.Move(text, bundlePath, overwrite: true);
	}

	private static void ConfigureTextureField(AssetTypeValueField field, int width, int height, int textureFormat, byte[] pixels)
	{
		AssetTypeValueField assetTypeValueField = field["m_Width"];
		AssetTypeValueField assetTypeValueField2 = field["m_Height"];
		AssetTypeValueField assetTypeValueField3 = field["m_TextureFormat"];
		AssetTypeValueField assetTypeValueField4 = FindTextureImageDataField(field);
		if (assetTypeValueField == null || assetTypeValueField.IsDummy || assetTypeValueField2 == null || assetTypeValueField2.IsDummy || assetTypeValueField3 == null || assetTypeValueField3.IsDummy || assetTypeValueField4 == null || assetTypeValueField4.IsDummy)
		{
			throw new InvalidDataException("动画模板 Texture2D 缺少必要像素字段。");
		}
		assetTypeValueField.AsInt = width;
		assetTypeValueField2.AsInt = height;
		assetTypeValueField3.AsInt = textureFormat;
		AssetTypeValueField assetTypeValueField5 = field["m_MipCount"];
		if (assetTypeValueField5 != null && !assetTypeValueField5.IsDummy)
		{
			assetTypeValueField5.AsInt = 1;
		}
		AssetTypeValueField assetTypeValueField6 = field["m_CompleteImageSize"];
		if (assetTypeValueField6 != null && !assetTypeValueField6.IsDummy)
		{
			assetTypeValueField6.AsInt = pixels.Length;
		}
		AssetTypeValueField assetTypeValueField7 = field["m_ColorSpace"];
		if (assetTypeValueField7 != null && !assetTypeValueField7.IsDummy)
		{
			assetTypeValueField7.AsInt = 0;
		}
		SetTextureImageData(assetTypeValueField4, pixels);
		AssetTypeValueField assetTypeValueField8 = field["m_StreamData"];
		if (assetTypeValueField8 != null && !assetTypeValueField8.IsDummy)
		{
			AssetTypeValueField assetTypeValueField9 = assetTypeValueField8["offset"];
			AssetTypeValueField assetTypeValueField10 = assetTypeValueField8["size"];
			AssetTypeValueField assetTypeValueField11 = assetTypeValueField8["path"];
			if (assetTypeValueField9 != null && !assetTypeValueField9.IsDummy)
			{
				assetTypeValueField9.AsLong = 0L;
			}
			if (assetTypeValueField10 != null && !assetTypeValueField10.IsDummy)
			{
				assetTypeValueField10.AsLong = 0L;
			}
			if (assetTypeValueField11 != null && !assetTypeValueField11.IsDummy)
			{
				assetTypeValueField11.AsString = "";
			}
		}
	}

	private void ReplaceTextureData(TexRef texture, int width, int height, int textureFormat, byte[] pixels, string backupRoot, bool forceLinearColorSpace = false)
	{
		TexRef resolved = ResolveTextureReference(texture) ?? throw new InvalidDataException($"当前 Bundle 中找不到可写入的 Texture2D：{texture.Name}（卡号 {texture.CardKey}）。");
		ApplyResolvedReference(texture, resolved);
		string activeBundlePath = texture.ActiveBundlePath;
		string text = Path.Combine(backupRoot, texture.RelativeBundlePath);
		Directory.CreateDirectory(Path.GetDirectoryName(text) ?? backupRoot);
		if (!File.Exists(text))
		{
			File.Copy(activeBundlePath, text);
		}
		string text2 = activeBundlePath + ".mdcardtool.tmp";
		AssetsManager assetsManager = NewManager();
		try
		{
			BundleFileInstance bundleFileInstance = assetsManager.LoadBundleFile(activeBundlePath);
			AssetsFileInstance assetsFileInstance = assetsManager.LoadAssetsFileFromBundle(bundleFileInstance, texture.AssetFileName);
			EnsureDatabase(assetsManager, assetsFileInstance);
			AssetFileInfo info = assetsFileInstance.file.GetAssetsOfType(AssetClassID.Texture2D).First((AssetFileInfo x) => x.PathId == texture.PathId);
			AssetTypeValueField baseField = assetsManager.GetBaseField(assetsFileInstance, info);
			AssetTypeValueField assetTypeValueField = baseField["m_Width"];
			if (assetTypeValueField == null || assetTypeValueField.IsDummy)
			{
				goto IL_0383;
			}
			AssetTypeValueField assetTypeValueField2 = baseField["m_Height"];
			if (assetTypeValueField2 == null || assetTypeValueField2.IsDummy)
			{
				goto IL_0383;
			}
			AssetTypeValueField assetTypeValueField3 = baseField["m_TextureFormat"];
			if (assetTypeValueField3 == null || assetTypeValueField3.IsDummy)
			{
				goto IL_0383;
			}
			AssetTypeValueField assetTypeValueField4 = FindTextureImageDataField(baseField);
			if (assetTypeValueField4 == null || assetTypeValueField4.IsDummy)
			{
				goto IL_0383;
			}
			assetTypeValueField.AsInt = width;
			assetTypeValueField2.AsInt = height;
			assetTypeValueField3.AsInt = textureFormat;
			AssetTypeValueField assetTypeValueField5 = baseField["m_MipCount"];
			if (assetTypeValueField5 != null && !assetTypeValueField5.IsDummy)
			{
				assetTypeValueField5.AsInt = 1;
			}
			AssetTypeValueField assetTypeValueField6 = baseField["m_CompleteImageSize"];
			if (assetTypeValueField6 != null && !assetTypeValueField6.IsDummy)
			{
				assetTypeValueField6.AsInt = pixels.Length;
			}
			if (forceLinearColorSpace)
			{
				AssetTypeValueField assetTypeValueField7 = baseField["m_ColorSpace"];
				if (assetTypeValueField7 != null && !assetTypeValueField7.IsDummy)
				{
					assetTypeValueField7.AsInt = 0;
				}
			}
			SetTextureImageData(assetTypeValueField4, pixels);
			AssetTypeValueField assetTypeValueField8 = baseField["m_StreamData"];
			if (assetTypeValueField8 != null && !assetTypeValueField8.IsDummy)
			{
				AssetTypeValueField assetTypeValueField9 = assetTypeValueField8["offset"];
				if (assetTypeValueField9 != null && !assetTypeValueField9.IsDummy)
				{
					assetTypeValueField9.AsLong = 0L;
				}
				AssetTypeValueField assetTypeValueField10 = assetTypeValueField8["size"];
				if (assetTypeValueField10 != null && !assetTypeValueField10.IsDummy)
				{
					assetTypeValueField10.AsLong = 0L;
				}
				AssetTypeValueField assetTypeValueField11 = assetTypeValueField8["path"];
				if (assetTypeValueField11 != null && !assetTypeValueField11.IsDummy)
				{
					assetTypeValueField11.AsString = "";
				}
			}
			List<AssetsReplacer> replacers = new List<AssetsReplacer>
			{
				new AssetsReplacerFromMemory(assetsFileInstance.file, info, baseField)
			};
			byte[] buffer;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				using AssetsFileWriter writer = new AssetsFileWriter(memoryStream);
				assetsFileInstance.file.Write(writer, 0L, replacers);
				buffer = memoryStream.ToArray();
			}
			using (AssetsFileWriter writer2 = new AssetsFileWriter(text2))
			{
				bundleFileInstance.file.Write(writer2, new List<BundleReplacer>
				{
					new BundleReplacerFromMemory(assetsFileInstance.name, assetsFileInstance.name, hasSerializedData: true, buffer, -1L)
				});
			}
			goto end_IL_00de;
			IL_0383:
			throw new InvalidDataException("当前手工 Mod 的 Texture2D 缺少必要像素字段，无法安全写入。");
			end_IL_00de:;
		}
		catch
		{
			try
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			catch
			{
			}
			throw;
		}
		finally
		{
			assetsManager.UnloadAll();
		}
		File.Move(text2, activeBundlePath, overwrite: true);
		texture.Width = width;
		texture.Height = height;
		texture.Category = CategoryForSource(texture.Name, width, height, texture.SourceKind);
	}

	public (int Width, int Height) ImageDimensions(string imagePath)
	{
		ImageInfo imageInfo = Image.Identify(imagePath) ?? throw new InvalidDataException("无法读取图片尺寸。");
		return (Width: imageInfo.Width, Height: imageInfo.Height);
	}
}
