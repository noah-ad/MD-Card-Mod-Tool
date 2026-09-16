using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class CardBundleIdentity
{
	private static readonly object Gate = new object();

	private static string stamp = "";

	private static Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	internal static string Find(string path)
	{
		string fileName = Path.GetFileName(path);
		if (fileName.Length != 8 || !fileName.All(char.IsAsciiHexDigit))
		{
			return "";
		}
		lock (Gate)
		{
			string text = File.GetLastWriteTimeUtc(CardCatalogService.BundledPath).Ticks + ":" + File.GetLastWriteTimeUtc(GameCardCatalogUpdater.ExtraCatalogPath).Ticks;
			if (stamp != text)
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (CardCatalogEntry entry in CardCatalogService.LoadBestAvailable().Entries)
				{
					string[] array = new string[2] { "tcg", "ocg" };
					foreach (string illustrationType in array)
					{
						string fileName2 = Path.GetFileName(IndexService.CardIllustrationRelativePath(entry.CardId.ToString(), illustrationType));
						string text2 = entry.CardId.ToString();
						if (dictionary.TryGetValue(fileName2, out var value) && value != text2)
						{
							dictionary[fileName2] = "";
						}
						else
						{
							dictionary.TryAdd(fileName2, text2);
						}
					}
				}
				ids = dictionary;
				stamp = text;
			}
			return ids.GetValueOrDefault(fileName, "");
		}
	}

	internal static void Refresh(TexRef texture, IReadOnlyList<TexRef> scanned)
	{
		TexRef texRef = scanned.FirstOrDefault((TexRef x) => x.PathId == texture.PathId && x.AssetFileName == texture.AssetFileName) ?? scanned.FirstOrDefault((TexRef x) => x.Name == texture.Name) ?? ((scanned.Count == 1) ? scanned[0] : null);
		if (texRef != null)
		{
			texture.PathId = texRef.PathId;
			texture.AssetFileName = texRef.AssetFileName;
			texture.Width = texRef.Width;
			texture.Height = texRef.Height;
			texture.OverrideBundlePath = null;
			if (texture.CardKey.Length == 0)
			{
				texture.Category = texRef.Category;
			}
			IndexService.NormalizeLocalCardCategory(texture);
		}
	}
}
