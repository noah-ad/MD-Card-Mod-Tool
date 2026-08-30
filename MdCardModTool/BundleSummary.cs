using System.Collections.Generic;
using System.Linq;

namespace MdCardModTool;

public sealed class BundleSummary
{
	public required string RelativePath { get; init; }

	public int SerializedFiles { get; init; }

	public Dictionary<string, int> AssetTypes { get; init; } = new Dictionary<string, int>();

	public string Describe()
	{
		if (AssetTypes.Count != 0)
		{
			return string.Join("；", from x in AssetTypes
				orderby x.Key
				select $"{x.Key} × {x.Value}");
		}
		return "无 Serialized Asset（通常是资源数据或依赖 Bundle）";
	}
}
