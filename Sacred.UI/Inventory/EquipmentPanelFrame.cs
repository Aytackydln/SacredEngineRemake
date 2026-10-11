using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>cUI_Equipment::default1024 ornaments, independent of armor controls.</summary>
public sealed class EquipmentPanelFrame
{
    public IReadOnlyList<EquipmentPanelDecoration> Pieces { get; }

    public EquipmentPanelFrame(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        var pieces = new List<EquipmentPanelDecoration>();
        // The original randomizes the right spines and the middle bottom tiles.
        // Retain one of those native combinations for a stable reusable panel.
        var spine = new UiTextureRegion(table[34]);
        for (var y = 10f; y < 10 + 8 * spine.Size.Y; y += spine.Size.Y)
            pieces.Add(new(spine, new(256 - spine.Anchor.X, y)));
        var x = -1f;
        for (var segment = 0; segment < 4; segment++)
        {
            if (segment == 3) x += 3;
            var top = new UiTextureRegion(table[segment == 0 ? 50 : segment == 3 ? 51 : 7]);
            var bottom = new UiTextureRegion(table[segment == 0 ? 60 : segment == 3 ? 61 : 26]);
            pieces.Add(new(top, new(x, -top.Anchor.Y)));
            if (segment == 3) x -= 3;
            pieces.Add(new(bottom, new(x, 256 - bottom.Anchor.Y)));
            x += bottom.Size.X;
        }
        Pieces = pieces;
    }
}

public sealed record EquipmentPanelDecoration(UiTextureRegion Art, Vector2 Position);
