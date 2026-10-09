using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.UI;
using Sacred.UI.Textures;

namespace Sacred.UI.Hud;

/// <summary>The native starting hand/combat-art frames and their ornamental taskbar wings.</summary>
public sealed class BottomHudQuickSlotLayout
{
    public BottomHudDecoration HandSlot { get; }
    public BottomHudDecoration HandBackground { get; }
    public IReadOnlyList<BottomHudDecoration> CombatArtSlots { get; }
    public IReadOnlyList<BottomHudDecoration> CombatArtBackgrounds { get; }
    public IReadOnlyList<BottomHudDecoration> Wings { get; }

    public BottomHudQuickSlotLayout(IReadOnlyList<SacredExecutableUiTextureDefinitionLayout> table)
    {
        // refreshSlotGfx uses selector 103 for the active slot, even when it is empty.
        var activeFrame = new UiTextureRegion(table[103]);
        HandSlot = new(activeFrame, new(328, 691));
        CombatArtSlots = new BottomHudDecoration[] { new(activeFrame, new(640, 691)) };
        HandBackground = new(BottomHudSlotBackgrounds.Hand(table), HandSlot.Position);
        CombatArtBackgrounds = new BottomHudDecoration[]
        {
            new(BottomHudSlotBackgrounds.CombatArt(table), CombatArtSlots[0].Position)
        };

        // setSlotCount(1) tiles from x=362-66 and x=560+66 toward the center.
        // Follow-on ornaments are chosen once, as in the native rand() & 1 path.
        var leftEnd = new UiTextureRegion(table[5]);
        var leftContinuation = new UiTextureRegion(table[6 + Random.Shared.Next(2)]);
        var rightEnd = new UiTextureRegion(table[8]);
        var rightContinuation = new UiTextureRegion(table[9 + Random.Shared.Next(2)]);
        Wings = new BottomHudDecoration[]
        {
            Wing(leftEnd, 296),
            Wing(leftContinuation, 296 + leftEnd.Size.X),
            Wing(rightEnd, 626),
            Wing(rightContinuation, 626 - rightEnd.Size.X)
        };
    }

    public bool ContainsPointer(Vector2 point, Vector2 viewport)
    {
        if (GetBounds(HandSlot, viewport).Contains(point)) return true;
        foreach (var slot in CombatArtSlots)
            if (GetBounds(slot, viewport).Contains(point)) return true;
        foreach (var wing in Wings)
            if (GetBounds(wing, viewport).Contains(point)) return true;
        return false;
    }

    private static BottomHudDecoration Wing(UiTextureRegion region, float x) =>
        new(region, new(x, 756 - region.Anchor.Y));

    public static HudRectangle GetBounds(BottomHudDecoration piece, Vector2 viewport) =>
        new(BottomHudLayout.ToScreen(piece.Position, viewport),
            piece.Region.Size * BottomHudLayout.GetScale(viewport));
}
