using System.Linq;

namespace MdCardModTool;

public sealed record MonsterAnimationAssetProfile(string Tier, string Region, string Scale)
{
	public string DisplayName
	{
		get
		{
			if (Scale.Length != 0)
			{
				return $"{Tier} · {Region.ToUpperInvariant()} · {Scale}";
			}
			return Tier + " · " + Region.ToUpperInvariant();
		}
	}

	public string FilePrefix => string.Join('-', new string[3] { Tier, Region, Scale }.Where((string x) => x.Length > 0));
}
