using System;

namespace MdCardModTool;

public sealed record ModPackageInfo(string Name, DateTimeOffset CreatedAt, int BundleCount, long TotalSize);
