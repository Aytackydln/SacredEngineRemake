using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>Gold cUI_Window2::adjustFrame (pureHD 0x727620), used by item tooltips.</summary>
public sealed class ItemDescriptionFrame(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> definitions)
{
    public const uint BackgroundArgb = 0x9F000000;
    public const int LeftExtent = 13, TopExtent = 11, RightExtent = 14, BottomExtent = 10;

    public IEnumerable<(string TextureName, UiFrameQuad Quad)> GetQuads(float width, float height)
    {
        // Native UI selectors, not texture archive IDs. Edges stretch; they do not tile.
        var top = definitions[21];
        var side = definitions[34];
        var corner = definitions[55];
        var topSize = new Vector2(top.U1 + 1 - top.U0, top.V1 + 1 - top.V0);
        var sideSize = new Vector2(side.U1 + 1 - side.U0, side.V1 + 1 - side.V0);
        var cornerSize = new Vector2(corner.U1 + 1 - corner.U0, corner.V1 + 1 - corner.V0);
        var topUv = new Vector2(top.U0, top.V0) / 256;
        var topEnd = new Vector2(top.U1, top.V1 + 1) / 256;
        var sideUv = new Vector2(side.U0, side.V0) / 256;
        var sideEnd = new Vector2(side.U1 + 1, side.V1) / 256;
        var cornerUv = new Vector2(corner.U0, corner.V0) / 256;
        var cornerEnd = new Vector2(corner.U1 + 1, corner.V1 + 1) / 256;
        var reflectedStart = new Vector2(corner.U1, corner.V0) / 256;
        var reflectedEnd = new Vector2(corner.U0 - 1, corner.V1 + 1) / 256;
        var topName = Sacred.Assets.Executable.SacredUiTextureTable.GetTextureName(top);
        var sideName = Sacred.Assets.Executable.SacredUiTextureTable.GetTextureName(side);
        var cornerName = Sacred.Assets.Executable.SacredUiTextureTable.GetTextureName(corner);
        yield return (topName, new(new(topSize, topUv, topEnd), new(4, -4), new(width - 4, topSize.Y)));
        yield return (topName, new(new(topSize, new(topUv.X, topEnd.Y), new(topEnd.X, topUv.Y)), new(4, height - 3), new(width - 4, topSize.Y)));
        yield return (sideName, new(new(sideSize, sideUv, sideEnd), new(-4, 0), new(sideSize.X, height)));
        yield return (sideName, new(new(sideSize, sideUv, sideEnd), new(width, 0), new(sideSize.X, height)));
        yield return (cornerName, new(new(cornerSize, cornerUv, cornerEnd), new(width - 24, -11), cornerSize));
        yield return (cornerName, new(new(cornerSize, reflectedStart, reflectedEnd), new(-13, -11), cornerSize));
        yield return (cornerName, new(new(cornerSize, new(cornerUv.X, cornerEnd.Y), new(cornerEnd.X, cornerUv.Y)), new(width - 24, height - 8), cornerSize));
        yield return (cornerName, new(new(cornerSize, new(reflectedStart.X, reflectedEnd.Y), new(reflectedEnd.X, reflectedStart.Y)), new(-13, height - 8), cornerSize));
    }
}
