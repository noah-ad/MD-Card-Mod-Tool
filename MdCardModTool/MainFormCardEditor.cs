using System;
using System.IO;
using System.Threading.Tasks;

namespace MdCardModTool;

public sealed partial class MainForm
{
    // Shared by both editor entrances and the frame preview. Decode before
    // qualifying the canvas: old indexes can have zero or obsolete dimensions.
    internal async Task PrepareCardArtAsync(TexRef texture)
    {
        string gameRoot = _gameRoot ?? throw new InvalidOperationException(Localizer.T("cards.editor.choose"));
        var index = _index;
        if (!CardTextureRules.HasCardIdentity(texture))
            throw new InvalidDataException(Localizer.F("cards.editor.identity", texture.Name, texture.SourceKind, texture.CardKey));

        await DecodeWithReferenceRepairAsync(texture);
        if (IsDisposed || !string.Equals(_gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase)
            || !ReferenceEquals(index, _index) || !IndexWorkspaceGuard.Belongs(gameRoot, texture))
            throw new OperationCanceledException(Localizer.T("cards.editor.workspaceChanged"));

        if (!CardTextureRules.IsCardArt(texture))
            throw new InvalidDataException(Localizer.F("cards.editor.canvas", texture.CardKey, texture.Width, texture.Height));
    }
}
