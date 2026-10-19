using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MdCardModTool;

internal static class AnimationResourceAudit
{
	public static void Run(string gameRoot, string output)
	{
		Directory.CreateDirectory(output);
		IReadOnlyList<string> ids = MonsterAnimationIndexService.FindInstalledCardIds(gameRoot);
		object[] rows = new object[ids.Count];
		Parallel.For(0, ids.Count, new ParallelOptions
		{
			MaxDegreeOfParallelism = 3
		}, delegate(int i)
		{
			string id = ids[i];
			try
			{
				var set = MonsterAnimationIndexService.Find(gameRoot, id);
				List<MonsterAnimationAssetRef> list = set.Assets;
				IReadOnlyList<MonsterAnimationAssetTriplet> source = MonsterAnimationAssetPairing.FindComplete(set);
				bool flag = source.Any((MonsterAnimationAssetTriplet p) => p.Tier == "SD") && source.Any((MonsterAnimationAssetTriplet p) => p.Tier == "HighEnd_HD");
				bool flag2 = source.Any((MonsterAnimationAssetTriplet p) => p.Atlas.Name != "P" + id + ".atlas");
				ModEngine engine = new ModEngine();
				int num = source.Select((MonsterAnimationAssetTriplet p) => Encoding.UTF8.GetString(engine.ReadTextAsset(p.Atlas).Data).Split('\n').Count((string l) => l.Trim().EndsWith(".png", StringComparison.OrdinalIgnoreCase))).DefaultIfEmpty(0).Max();
				string[] missingPages = source.SelectMany((MonsterAnimationAssetTriplet p) => from l in Encoding.UTF8.GetString(engine.ReadTextAsset(p.Atlas).Data).Split('\n')
					select l.Trim() into page
					where page.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
					where !p.Textures.Any((MonsterAnimationAssetRef t) => (t.Name + ".png").Equals(page, StringComparison.OrdinalIgnoreCase))
					select page).Distinct().ToArray();
				rows[i] = new
				{
					CardId = id,
					Availability = set.Availability.ToString(),
					OfficialPrefab = set.HasOfficialPrefab,
					Bundles = set.ResourceBundles.Select(bundle => new { bundle.RelativeBundlePath, bundle.StorageKind, bundle.Containers }).ToArray(),
					Diagnostics = set.Diagnostics,
					Complete = flag,
					Alias = flag2,
					Pages = num,
					MissingPages = missingPages,
					Assets = list.Count,
					Names = list.Select((MonsterAnimationAssetRef a) => a.Name).Distinct().ToArray(),
					Error = ""
				};
				if (!flag || flag2 || num > 1)
				{
					Console.WriteLine($"card={id}; complete={flag}; alias={flag2}; pages={num}; assets={list.Count}");
				}
			}
			catch (Exception ex)
			{
				rows[i] = new
				{
					CardId = id,
					Complete = false,
					Error = ex.Message
				};
				Console.WriteLine("card=" + id + "; ERROR=" + ex.Message);
			}
		});
		File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		Console.WriteLine($"audited={ids.Count}; gameWrites=False");
	}
}
