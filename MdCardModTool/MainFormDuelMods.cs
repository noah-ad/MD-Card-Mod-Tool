using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MdCardModTool;

public sealed partial class MainForm
{
	private readonly List<TexRef> _duelModRows = new();
	private readonly HashSet<TexRef> _duelPreviewOnlyRows = new();
	private bool CoveredByDuelMod(TexRef texture) => _duelPreviewOnlyRows.Contains(texture)
		|| _duelModRows.Any(row => row.ManagedDuel!.Slots.Any(slot => slot.BundlePath.Equals(texture.BundlePath, StringComparison.OrdinalIgnoreCase)));
	private IEnumerable<TexRef> ManagedModRows() => _textures.Where(x => !CoveredByDuelMod(x)).Concat(_duelModRows);
	private TexRef[] ResolveManagedSelection(IEnumerable<TexRef> selection) => selection.Select(x =>
		x.ManagedDuel != null || !CoveredByDuelMod(x) ? x : _duelModRows.FirstOrDefault(row => row.ManagedDuel!.Contains(x)) ?? x).Distinct().ToArray();
	private void ApplyDuelModRows(string root, IReadOnlyList<DuelManagedMod> mods)
	{
		_duelModRows.Clear(); _duelModRows.AddRange(mods.Select(x => x.Row(root)));
		_duelPreviewOnlyRows.Clear();
		foreach (var texture in _textures)
		{
			var row = _duelModRows.FirstOrDefault(x => x.ManagedDuel!.Contains(texture));
			if (row != null)
			{
				if (!texture.IsModded) _duelPreviewOnlyRows.Add(texture);
				texture.IsModded = true;
				texture.ModModifiedAtUtc ??= row.ModModifiedAtUtc;
			}
		}
	}
	private bool ShowDuelModSelection(TexRef texture)
	{
		if (texture.ManagedDuel is not { } mod) return false;
		_previewHint.Text = mod.Kind + " MOD\n\n" + mod.Name + "\n\n可多选备份／批量还原；试听及替换请打开“场地与音乐”。";
		_previewHint.Visible = true;
		_previewHint.BringToFront();
		_info.Text = mod.Name + Environment.NewLine + string.Join(Environment.NewLine, mod.Slots.Select(x => x.LogicalPath))
			+ Environment.NewLine + (mod.Verified ? "首次原版备份及校验记录完整。" : "备份或当前文件校验异常，自动还原／导出已受保护。")
			+ Environment.NewLine + "备份请使用 MOD 管理的“导出选中／导出全部”或“备份当前 Mod”。";
		return true;
	}
}
