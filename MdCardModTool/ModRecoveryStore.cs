using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SixLabors.ImageSharp;

namespace MdCardModTool;

internal sealed record RecoverableTextureMod
{
	public string Id { get; init; } = "";
	public string StorageKind { get; init; } = "LocalData";
	public string SourceKind { get; init; } = "";
	public string RelativePath { get; init; } = "";
	public string Name { get; init; } = "";
	public string CardKey { get; init; } = "";
	public string PngSha256 { get; init; } = "";
	public string BuildId { get; init; } = "";
	public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
	public bool Active { get; init; } = true;
	public ushort? OverFrameArtId { get; init; }
}

internal sealed class ModRecoveryManifest
{
	public int FormatVersion { get; init; } = 1;
	public string GameRoot { get; init; } = "";
	public string LocalDataRoot { get; init; } = "";
	public List<RecoverableTextureMod> Textures { get; init; } = new();
	public HashSet<ushort> DisabledLegacyCards { get; init; } = new();
}

// Deliberately independent of the EXE/release directory and first-original backups.
// A backup is not the desired Mod. Only successful edits or explicit snapshots enter here.
internal sealed class ModRecoveryStore
{
	private static readonly object Gate = new();
	private readonly string _cacheRoot;
	internal ModRecoveryStore(string? cacheRoot = null) => _cacheRoot = cacheRoot ?? Path.Combine(AppSettingsStore.AppDataRoot, "mod-recovery-v1");

	internal string Folder(string gameRoot)
	{
		string local = IndexService.FindLocalRoot(gameRoot) ?? throw new DirectoryNotFoundException("未找到活动 LocalData 账号。");
		string identity = Path.GetFullPath(gameRoot).TrimEnd('\\', '/').ToUpperInvariant() + "\0" + Path.GetFullPath(local).TrimEnd('\\', '/').ToUpperInvariant();
		return Path.Combine(_cacheRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24]);
	}

	internal ModRecoveryManifest Load(string gameRoot)
	{
		lock (Gate)
		{
			string path = Path.Combine(Folder(gameRoot), "manifest.json");
			if (!File.Exists(path)) return new ModRecoveryManifest { GameRoot = Path.GetFullPath(gameRoot), LocalDataRoot = IndexService.FindLocalRoot(gameRoot)! };
			var manifest = JsonSerializer.Deserialize<ModRecoveryManifest>(File.ReadAllText(path)) ?? throw new InvalidDataException("Mod 恢复库清单为空。");
			if (manifest.FormatVersion != 1 || !Path.GetFullPath(manifest.GameRoot).Equals(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase)
				|| !Path.GetFullPath(manifest.LocalDataRoot).Equals(Path.GetFullPath(IndexService.FindLocalRoot(gameRoot)!), StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Mod 恢复库版本或账号不匹配；未进行写入。");
			return manifest;
		}
	}

	internal void RecordTexture(string gameRoot, TexRef texture, byte[] png, ushort? overFrameArtId = null)
	{
		if (BuiltInCardFrameCatalog.IsPackagedFrame(texture)) return;
		var info = Image.Identify(png) ?? throw new InvalidDataException("恢复素材不是有效图片。");
		if ((long)info.Width * info.Height > 67108864) throw new InvalidDataException("恢复素材超出 8192² 像素预算。");
		string local = IndexService.FindLocalRoot(gameRoot)!;
		string storage = IsUnder(texture.BundlePath, local) ? "LocalData" : IsUnder(texture.BundlePath, IndexService.StreamingRoot(gameRoot)) ? "StreamingAssets" : "GameRoot";
		string root = storage == "LocalData" ? local : storage == "StreamingAssets" ? IndexService.StreamingRoot(gameRoot) : gameRoot;
		string relative = Path.GetRelativePath(root, texture.BundlePath).Replace('\\', '/');
		DirectModArchive.ValidatePath(relative);
		string hash = Convert.ToHexString(SHA256.HashData(png));
		string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storage + "\0" + relative.ToUpperInvariant() + "\0" + texture.Name)));
		lock (Gate)
		{
			var manifest = Load(gameRoot);
			string folder = Folder(gameRoot);
			Directory.CreateDirectory(Path.Combine(folder, "images"));
			AtomicWrite(Path.Combine(folder, "images", hash + ".png"), png);
			// Keep TCG and OCG edits independent. Only the same logical illustration
			// replaces an old intent if its physical resource location changed.
			string variant = IllustrationVariant(relative, texture.CardKey);
			manifest.Textures.RemoveAll(x => x.Id == id || (variant.Length > 0 && CardTextureRules.HasCardIdentity(texture)
				&& x.CardKey == texture.CardKey && x.SourceKind == texture.SourceKind && IllustrationVariant(x.RelativePath, x.CardKey) == variant));
			manifest.Textures.Add(new RecoverableTextureMod { Id = id, StorageKind = storage, SourceKind = texture.SourceKind,
				RelativePath = relative, Name = texture.Name, CardKey = CardTextureRules.HasCardIdentity(texture) ? texture.CardKey : "", PngSha256 = hash,
				BuildId = PortableIndexService.GetGameBuildId(gameRoot), OverFrameArtId = overFrameArtId });
			if (ushort.TryParse(texture.CardKey, out ushort card)) manifest.DisabledLegacyCards.Remove(card);
			Save(gameRoot, manifest);
		}
	}

	internal byte[] ReadPng(string gameRoot, RecoverableTextureMod entry)
	{
		if (entry.PngSha256.Length != 64 || !entry.PngSha256.All(char.IsAsciiHexDigit)) throw new InvalidDataException("恢复素材校验值无效。");
		string path = Path.Combine(Folder(gameRoot), "images", entry.PngSha256 + ".png");
		var data = File.ReadAllBytes(path);
		if (!Convert.ToHexString(SHA256.HashData(data)).Equals(entry.PngSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("恢复素材已损坏：" + entry.CardKey + " " + entry.Name);
		return data;
	}

	internal void DeactivateBundle(string gameRoot, TexRef texture)
		=> DeactivateBundles(gameRoot, new[] { texture });

	internal void DeactivateBundles(string gameRoot, IReadOnlyList<TexRef> textures)
	{
		lock (Gate)
		{
			var manifest = Load(gameRoot);
			for (int i = 0; i < manifest.Textures.Count; i++)
			{
				var item = manifest.Textures[i];
				string root = item.StorageKind == "LocalData" ? manifest.LocalDataRoot : item.StorageKind == "StreamingAssets" ? IndexService.StreamingRoot(gameRoot) : gameRoot;
				DirectModArchive.ValidatePath(item.RelativePath);
				if (textures.Any(texture => Path.GetFullPath(Path.Combine(root, item.RelativePath)).Equals(Path.GetFullPath(texture.BundlePath), StringComparison.OrdinalIgnoreCase)
					|| (texture.CardKey.Length > 0 && item.CardKey == texture.CardKey))) manifest.Textures[i] = item with { Active = false };
			}
			foreach (var texture in textures)
				if (ushort.TryParse(texture.CardKey, out ushort card)) manifest.DisabledLegacyCards.Add(card);
			Save(gameRoot, manifest);
		}
	}

	internal void MarkReapplied(string gameRoot, IEnumerable<string> ids)
	{
		lock (Gate)
		{
			var manifest = Load(gameRoot);
			var applied = ids.ToHashSet(StringComparer.Ordinal);
			for (int i = 0; i < manifest.Textures.Count; i++)
				if (applied.Contains(manifest.Textures[i].Id)) manifest.Textures[i] = manifest.Textures[i] with { BuildId = PortableIndexService.GetGameBuildId(gameRoot) };
			Save(gameRoot, manifest);
		}
	}

	internal void SetOverFrameIntent(string gameRoot, ushort card, ushort? art)
	{
		lock (Gate)
		{
			var manifest = Load(gameRoot);
			for (int i = 0; i < manifest.Textures.Count; i++)
			{
				var entry = manifest.Textures[i];
				if (entry.CardKey == card.ToString() && (entry.OverFrameArtId.HasValue || art.HasValue))
					manifest.Textures[i] = entry with { Active = art.HasValue, OverFrameArtId = art };
			}
			if (art.HasValue) manifest.DisabledLegacyCards.Remove(card); else manifest.DisabledLegacyCards.Add(card);
			Save(gameRoot, manifest);
		}
	}

	private void Save(string root, ModRecoveryManifest manifest) => AtomicWrite(Path.Combine(Folder(root), "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));
	private static string IllustrationVariant(string relative, string card)
	{
		if (!ushort.TryParse(card, out ushort id) || id == 0) return "";
		foreach (string variant in new[] { "tcg", "ocg" })
			if (Path.GetFileName(relative).Equals(Path.GetFileName(IndexService.CardIllustrationRelativePath(card, variant)), StringComparison.OrdinalIgnoreCase)) return variant;
		return "";
	}
	internal static bool IsUnder(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	internal static void AtomicWrite(string path, byte[] data)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try { File.WriteAllBytes(temp, data); File.Move(temp, path, true); }
		finally { if (File.Exists(temp)) File.Delete(temp); }
	}
}
