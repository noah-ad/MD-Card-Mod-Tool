namespace MdCardModTool;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--build-index")
        {
            IndexService.BuildAndSave(args[1], (done, total, found) => Console.WriteLine($"{done}/{total}; textures={found}"));
            return;
        }
        if (args.Length == 2 && args[0] == "--find-card-frame")
        {
            var game = args[1];
            var targets = new[]
            {
                Path.Combine(game, "masterduel_Data", "data.unity3d"),
                IndexService.StreamingRoot(game)
            };
            foreach (var target in targets)
            {
                var files = File.Exists(target) ? new[] { target } : Directory.Exists(target) ? Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories) : [];
                foreach (var file in files)
                {
                    try
                    {
                        foreach (var x in new ModEngine().ListTextures(file, game, "游戏内图片").Where(x => x.Name.Contains("card", StringComparison.OrdinalIgnoreCase) && x.Name.Contains("frame", StringComparison.OrdinalIgnoreCase)))
                            Console.WriteLine($"{x.Name}\t{x.Width}x{x.Height}\t{x.RelativeBundlePath}\tPathID={x.PathId}");
                    }
                    catch { }
                }
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--add-card-frames")
        {
            IndexService.AddCardFramesAndSave(args[1]);
            return;
        }
        ApplicationConfiguration.Initialize();
        var promoOutput = args.Length >= 2 && args[0] == "--capture-promo" ? args[1] : null;
        var promoScene = args.Length >= 3 && args[0] == "--capture-promo" ? args[2] : null;
        Application.Run(new MainForm(promoOutput, promoScene));
    }
}
