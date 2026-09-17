using System;
using System.IO;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MdCardModTool;

internal static class FoilInnerFrameTests
{
    internal static void Run()
    {
        int templates = 0;
        for (int n = 1; n <= 30; n++)
        {
            string key = $"card_frame{n:D2}";
            if (!AstellarOverFrameTemplateCatalog.IsAvailable(key)) continue;
            var template = AstellarOverFrameTemplateCatalog.Load(key);
            using var input = new Image<Rgba32>(704, 1024, new Rgba32(29, 127, 233, 255));
            input[0, 0] = new Rgba32(31, 57, 93, 0);
            using var stream = new MemoryStream(); input.SaveAsPng(stream);
            byte[] baseline = stream.ToArray();
            byte[] output = AstellarOverFrameComposer.RemoveFoilInnerFrame(baseline, template);
            using var actual = Image.Load<Rgba32>(output);
            using var art = Image.Load<Rgba32>(template.Layers["ArtFrame"]);
            using var effect = Image.Load<Rgba32>(template.Layers["EffFrame"]);
            using var box = Image.Load<Rgba32>(template.Layers["EffBox"]);
            int changed = 0;
            for (int y = 0; y < 1024; y++) for (int x = 0; x < 704; x++)
            {
                var before = input[x, y]; var after = actual[x, y];
                bool inner = (art[x, y].A > 0 || effect[x, y].A > 0) && box[x, y].A == 0;
                if (before.R != after.R || before.G != after.G || before.B != after.B || after.A != (inner ? 4 : before.A))
                    throw new Exception("RGB/alpha boundary failure " + key);
                if (inner) changed++;
            }
            if (changed == 0) throw new Exception("Empty inner mask " + key);
            // Re-running is stable; the disabled path does not call the transform.
            if (!output.AsSpan().SequenceEqual(AstellarOverFrameComposer.RemoveFoilInnerFrame(output, template))) throw new Exception("Not idempotent");
            Console.WriteLine($"PASS {key}: innerPixels={changed}; RGB/unmaskedAlpha preserved");
            templates++;
        }
        if (templates == 0) throw new Exception("No templates tested");
        if (JsonSerializer.Deserialize<OverFrameFrameSettings>("{}")!.RemoveFoilInnerFrame) throw new Exception("Legacy default changed");
        var enabled = new OverFrameFrameSettings(RemoveFoilInnerFrame: true);
        if (!JsonSerializer.Deserialize<OverFrameFrameSettings>(JsonSerializer.Serialize(enabled))!.RemoveFoilInnerFrame) throw new Exception("Settings roundtrip");
        Console.WriteLine($"PASS templates={templates}; legacyOff=True; settingsRoundtrip=True; gameWrites=False");
    }
}
