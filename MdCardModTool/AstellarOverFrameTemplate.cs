using System.Collections.Generic;

namespace MdCardModTool;

public sealed record AstellarOverFrameTemplate(string Key, IReadOnlyDictionary<string, byte[]> Layers);
