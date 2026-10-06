using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

internal static class CardBundleIdentity
{
	private static readonly object Gate = new object();

	private static Dictionary<string, string>? ids;

	internal static string Find(string path)
	{
		string fileName = Path.GetFileName(path);
		if (fileName.Length != 8 || !fileName.All(char.IsAsciiHexDigit))
		{
			return "";
		}
		lock (Gate)
		{
			if (ids == null)
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				// CARD_Prop and of_card_asset encode IDs as unsigned 16-bit values.
				// Cover the format's domain, not the last known release/catalog.
				// No cards are fabricated: only installed, verified Bundles are indexed.
				for (int cardId = 1; cardId <= ushort.MaxValue; cardId++)
				{
					string cardKey = cardId.ToString(System.Globalization.CultureInfo.InvariantCulture);
					string[] array = new string[2] { "tcg", "ocg" };
					foreach (string illustrationType in array)
					{
						string fileName2 = Path.GetFileName(IndexService.CardIllustrationRelativePath(cardKey, illustrationType));
						string text2 = cardKey;
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
