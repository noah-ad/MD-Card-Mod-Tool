using System;
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
        var ids = MonsterAnimationIndexService.FindInstalledCardIds(gameRoot);
        var rows = new object[ids.Count];
        Parallel.For(0, ids.Count, new ParallelOptions { MaxDegreeOfParallelism = 3 }, i =>
        {
            string id = ids[i];
            try
            {
                var assets = MonsterAnimationIndexService.FindPrefabDependencies(gameRoot, id);
                assets.AddRange(MonsterAnimationIndexService.FindCompanionPaths(gameRoot, id, assets));
                assets = assets.DistinctBy(a => Path.GetFullPath(a.BundlePath) + "|" + a.PathId).ToList();
                var set = new MonsterAnimationSet { CardId = id, Assets = assets };
                var pairs = MonsterAnimationAssetPairing.FindComplete(set);
                bool complete = pairs.Any(p => p.Tier == "SD") && pairs.Any(p => p.Tier == "HighEnd_HD");
                bool alias = pairs.Any(p => p.Atlas.Name != "P" + id + ".atlas");
                var engine = new ModEngine();
                int pages = pairs.Select(p => Encoding.UTF8.GetString(engine.ReadTextAsset(p.Atlas).Data)
                    .Split('\n').Count(l => l.Trim().EndsWith(".png", StringComparison.OrdinalIgnoreCase))).DefaultIfEmpty(0).Max();
                var missingPages = pairs.SelectMany(p => Encoding.UTF8.GetString(engine.ReadTextAsset(p.Atlas).Data).Split('\n')
                    .Select(l => l.Trim()).Where(l => l.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .Where(page => !p.Textures.Any(t => (t.Name + ".png").Equals(page, StringComparison.OrdinalIgnoreCase)))).Distinct().ToArray();
                rows[i] = new { CardId = id, Complete = complete, Alias = alias, Pages = pages, MissingPages = missingPages, Assets = assets.Count,
                    Names = assets.Select(a => a.Name).Distinct().ToArray(), Error = "" };
                if (!complete || alias || pages > 1) Console.WriteLine($"card={id}; complete={complete}; alias={alias}; pages={pages}; assets={assets.Count}");
            }
            catch (Exception ex) { rows[i] = new { CardId = id, Complete = false, Error = ex.Message }; Console.WriteLine($"card={id}; ERROR={ex.Message}"); }
        });
        File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"audited={ids.Count}; gameWrites=False");
    }
}
