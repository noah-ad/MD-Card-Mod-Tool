using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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
			return string.Join("；", AssetTypes.OrderBy<KeyValuePair<string, int>, string>(delegate(KeyValuePair<string, int> x)
			{
				KeyValuePair<string, int> keyValuePair = x;
				return keyValuePair.Key;
			}).Select(delegate(KeyValuePair<string, int> x)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 2);
				KeyValuePair<string, int> keyValuePair = x;
				defaultInterpolatedStringHandler.AppendFormatted(keyValuePair.Key);
				defaultInterpolatedStringHandler.AppendLiteral(" × ");
				keyValuePair = x;
				defaultInterpolatedStringHandler.AppendFormatted(keyValuePair.Value);
				return defaultInterpolatedStringHandler.ToStringAndClear();
			}));
		}
		return "无 Serialized Asset（通常是资源数据或依赖 Bundle）";
	}
}
