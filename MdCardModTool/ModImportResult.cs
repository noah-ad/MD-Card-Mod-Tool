using System.Collections.Generic;

namespace MdCardModTool;

public sealed record ModImportResult(int BundleCount, IReadOnlyList<string> ChangedBundlePaths);
