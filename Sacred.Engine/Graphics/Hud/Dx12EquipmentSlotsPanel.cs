using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Inventory.Actors;
using Sacred.UI.Inventory;

namespace Sacred.Engine.Graphics.Hud;

/// <summary>Renders equipment-local slots without owning a stats area or parent panel.</summary>
internal static class Dx12EquipmentSlotsPanel
{
    public static void Build(EquipmentPanelAssets assets, EquipmentPanelLayout layout, SacredGameActor actor,
        int? selectedSlot, Dx12EquipmentPreviewPass previews, ImDrawListPtr draw,
        IReadOnlyDictionary<string, int> textures, Vector2 origin, float scale)
    {
        var artwork = new Dx12UiArtwork(draw, textures, origin, scale);
        artwork.Draw(assets.Background, Vector2.Zero, 0x9FFFFFFF);
        foreach (var piece in assets.Frame.Pieces) artwork.Draw(piece.Art, piece.Position);
        foreach (var slot in layout.Slots)
        {
            artwork.Draw(slot.Art, slot.Position, 0xAFFFFFFF);
            if (actor.EquipmentSlots[slot.Index].Equipment is { } item)
                previews.Draw(draw, item, origin + slot.Position * scale, scale, false);
            if (selectedSlot == slot.Index)
            {
                var minimum = origin + slot.Position * scale;
                draw.AddRect(minimum, minimum + slot.Art.Size * scale, 0xFF32B9DA, 0, ImDrawFlags.None, scale);
            }
        }
    }
}
