using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.UI.Character;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Renders the standalone native statistics artwork with no bound values yet.</summary>
internal static class Dx12PlayerStatsPanel
{
    public static void Build(PlayerStatsLayout layout, ImDrawListPtr draw,
        IReadOnlyDictionary<string, int> textures, Vector2 origin, float scale)
    {
        var artwork = new Dx12UiArtwork(draw, textures, origin, scale);
        draw.PushClipRect(origin, origin + new Vector2(PlayerStatsLayout.Width, PlayerStatsLayout.Height) * scale, true);
        foreach (var background in layout.Backgrounds)
            artwork.Draw(background.Art, background.Position, 0x9FFFFFFF, background.Size);
        draw.PopClipRect();
        foreach (var decoration in layout.Decorations) artwork.Draw(decoration.Art, decoration.Position);
    }
}
