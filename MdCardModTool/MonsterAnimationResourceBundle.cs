using System;
using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public sealed record MonsterAnimationResourceBundle(
    string BundlePath,
    string RelativeBundlePath,
    string StorageKind,
    IReadOnlyList<string> Containers)
{
    public bool IsPrefab => Containers.Any(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
    public bool IsTimeline => Containers.Any(path => path.EndsWith(".playable", StringComparison.OrdinalIgnoreCase));
    public bool HasSpineData => Containers.Any(path => path.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
}

public enum MonsterAnimationAvailability
{
    Unresolved,
    IncompleteSpine,
    Spine,
    NativeTimeline
}
