using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.UI.Textures;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Draws an original atlas crop in a component's own coordinate system.</summary>
internal readonly struct Dx12UiArtwork(ImDrawListPtr draw, IReadOnlyDictionary<string, int> textures,
    Vector2 origin, float scale)
{
    public void Draw(UiTextureRegion art, Vector2 position, uint color = 0xFFFFFFFF, Vector2? size = null)
    {
        var minimum = origin + position * scale;
        draw.AddImage(textures[art.TextureName], minimum, minimum + (size ?? art.Size) * scale,
            art.UvMinimum, art.UvMaximum, color);
    }
}
