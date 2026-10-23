using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace MdCardModTool;

internal sealed class DuelBundleDocument : IDisposable
{
	private readonly AssetsManager _manager = new();
	internal readonly BundleFileInstance Bundle;
	internal readonly AssetsFileInstance Assets;
	internal readonly AssetFileInfo BundleInfo;
	internal readonly AssetTypeValueField Metadata;
	internal string Container => Metadata["m_Container"]["Array"].Children.Single()["first"].AsString;
	internal long RootId => Metadata["m_Container"]["Array"].Children.Single()["second"]["asset"]["m_PathID"].AsLong;
	internal string[] Dependencies => Metadata["m_Dependencies"]["Array"].Children.Select(x => x.AsString).ToArray();
	internal DuelBundleDocument(string path, string? expectedContainer = null)
	{
		try
		{
			_manager.LoadClassPackage(AppPaths.ResolveFile("classdata.tpk"));
			Bundle = _manager.LoadBundleFile(path);
			string[] files = Bundle.file.GetAllFileNames().ToArray();
			var serialized = Enumerable.Range(0, files.Length).Where(Bundle.file.IsAssetsFile).ToArray();
			if (serialized.Length != 1) throw new NotSupportedException("仅支持单序列化文件 Bundle：" + path);
			Assets = _manager.LoadAssetsFileFromBundle(Bundle, files[serialized[0]]);
			_manager.LoadClassDatabaseFromPackage(Assets.file.Metadata.UnityVersion);
			BundleInfo = Assets.file.GetAssetsOfType(AssetClassID.AssetBundle).Single();
			Metadata = _manager.GetBaseField(Assets, BundleInfo);
			if (Metadata["m_Container"]["Array"].Children.Count != 1 || RootId == 0
				|| Metadata["m_Container"]["Array"].Children.Single()["second"]["asset"]["m_FileID"].AsInt != 0)
				throw new NotSupportedException("仅支持单一内置主资源的 Bundle。");
			if (expectedContainer != null && !Container.Equals(expectedContainer, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("资源容器与目标不匹配：" + Container);
		}
		catch { _manager.UnloadAll(); throw; }
	}
	internal AssetTypeValueField Field(AssetFileInfo info) => _manager.GetBaseField(Assets, info);
	internal AssetFileInfo AudioInfo => Assets.file.GetAssetsOfType(AssetClassID.AudioClip).Single(x => x.PathId == RootId);
	internal AssetTypeValueField Audio => Field(AudioInfo);
	internal byte[] ResourceBytes(string name)
	{
		int index = Bundle.file.GetFileIndex(name);
		if (index < 0) throw new InvalidDataException("Bundle 中缺少资源流：" + name);
		Bundle.file.GetFileRange(index, out long offset, out long size);
		if (size < 1 || size > 256 * 1024 * 1024) throw new InvalidDataException("资源流大小异常。");
		Bundle.file.DataReader.Position = offset;
		return Bundle.file.DataReader.ReadBytes((int)size);
	}
	internal byte[] AudioBytes()
	{
		var audio = Audio;
		var resource = audio["m_Resource"];
		byte[] stream = ResourceBytes(Path.GetFileName(resource["m_Source"].AsString));
		long offset = resource["m_Offset"].AsLong, size = resource["m_Size"].AsLong;
		if (offset < 0 || size < 1 || offset > stream.LongLength - size) throw new InvalidDataException("音频范围越界。");
		return stream.AsSpan((int)offset, (int)size).ToArray();
	}

	internal void WriteDonor(string output, DuelBundleDocument target)
	{
		if (Assets.file.Metadata.UnityVersion != target.Assets.file.Metadata.UnityVersion)
			throw new NotSupportedException("来源与目标 Unity 版本不同。");
		bool music = target.Container.Contains("/sound/audioclip/bgm/", StringComparison.OrdinalIgnoreCase);
		if (music != Container.Contains("/sound/audioclip/bgm/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源类型不同。");
		Metadata["m_Container"]["Array"].Children.Single()["first"].AsString = target.Container;
		Metadata["m_Name"].AsString = target.Metadata["m_Name"].AsString;
		Metadata["m_AssetBundleName"].AsString = target.Metadata["m_AssetBundleName"].AsString;
		string sourceCab = Assets.name, targetCab = target.Assets.name;
		var replacements = new List<AssetsReplacer> { new AssetsReplacerFromMemory(Assets.file, BundleInfo, Metadata) };
		foreach (var external in Assets.file.Metadata.Externals)
			external.PathName = external.PathName.Replace(sourceCab, targetCab, StringComparison.Ordinal);
		foreach (AssetClassID type in new[] { AssetClassID.Texture2D, AssetClassID.Mesh, AssetClassID.AudioClip })
		foreach (var info in Assets.file.GetAssetsOfType(type))
		{
			var field = Field(info);
			bool changed = RewriteResource(field, "m_StreamData", "path", sourceCab, targetCab)
				| RewriteResource(field, "m_Resource", "m_Source", sourceCab, targetCab);
			if (music && type == AssetClassID.AudioClip && info.PathId == RootId)
			{
				field["m_Name"].AsString = target.Audio["m_Name"].AsString;
				changed = true;
			}
			if (changed) replacements.Add(new AssetsReplacerFromMemory(Assets.file, info, field));
		}
		Save(output, replacements, targetCab, sourceCab);
	}
	private static bool RewriteResource(AssetTypeValueField field, string group, string member, string source, string target)
	{
		var nested = field[group];
		if (nested.IsDummy || nested[member].IsDummy || !nested[member].AsString.Contains(source, StringComparison.Ordinal)) return false;
		nested[member].AsString = nested[member].AsString.Replace(source, target, StringComparison.Ordinal);
		return true;
	}
	internal void WriteAudio(string output, byte[] fsb, int frames)
	{
		if (Assets.file.GetAssetsOfType(AssetClassID.AudioClip).Count() != 1) throw new NotSupportedException("音频 Bundle 包含多个音轨。");
		var field = Audio;
		string resourceName = Path.GetFileName(field["m_Resource"]["m_Source"].AsString);
		if (Bundle.file.GetFileIndex(resourceName) < 0) throw new NotSupportedException("音频流不在当前 Bundle 中。");
		field["m_Channels"].AsInt = 2;
		field["m_Frequency"].AsInt = 48000;
		field["m_BitsPerSample"].AsInt = 16;
		field["m_Length"].AsFloat = frames / 48000f;
		field["m_CompressionFormat"].AsInt = 0;
		field["m_SubsoundIndex"].AsInt = 0;
		field["m_Resource"]["m_Offset"].AsLong = 0;
		field["m_Resource"]["m_Size"].AsLong = fsb.Length;
		Save(output, new() { new AssetsReplacerFromMemory(Assets.file, AudioInfo, field) }, Assets.name, Assets.name,
			new BundleReplacerFromMemory(resourceName, resourceName, false, fsb, -1));
	}
	private void Save(string output, List<AssetsReplacer> replacements, string targetCab, string sourceCab, BundleReplacer? extra = null)
	{
		byte[] serialized;
		using (var memory = new MemoryStream())
		{
			using var writer = new AssetsFileWriter(memory);
			Assets.file.Write(writer, 0, replacements);
			serialized = memory.ToArray();
		}
		var files = new List<BundleReplacer> { new BundleReplacerFromMemory(Assets.name, targetCab, true, serialized, -1) };
		foreach (string name in Bundle.file.GetAllFileNames().Where(x => sourceCab != targetCab && x != Assets.name && x.Contains(sourceCab, StringComparison.Ordinal)))
			files.Add(new BundleReplacerFromMemory(name, name.Replace(sourceCab, targetCab, StringComparison.Ordinal), false, ResourceBytes(name), -1));
		if (extra != null) files.Add(extra);
		using var outputWriter = new AssetsFileWriter(output);
		Bundle.file.Write(outputWriter, files);
	}
	public void Dispose() => _manager.UnloadAll();
}
