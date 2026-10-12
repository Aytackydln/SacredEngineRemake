using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Inventory.Actors;
using Sacred.UI.Character;
using Sacred.UI.Hud;
using Sacred.UI.Layout;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>Player stats above the original armor/accessory controls. Hands belong to the taskbar.</summary>
public sealed class InventoryPanelLayout
{
    public IReadOnlyList<InventoryPanelSlot> Slots { get; }
    public EquipmentPanelLayout Equipment { get; }
    public static VerticalPanelLayout Panel { get; } = new(new Vector2[]
    {
        new(PlayerStatsLayout.Width, PlayerStatsLayout.Height),
        new(EquipmentSlotsLayout.Width, EquipmentSlotsLayout.Height)
    });
    public const float Height = PlayerStatsLayout.Height + EquipmentSlotsLayout.Height;
    public const float Width = 280;

    public InventoryPanelLayout(SacredGameActor actor, EquipmentPanelAssets assets)
    {
        Equipment = new(actor, assets.Definitions);
        Slots = Equipment.Slots.Select(slot => slot with { Position = slot.Position + EquipmentOrigin }).ToArray();
    }

    public static Vector2 StatsOrigin => Panel.Children[0].Position;
    public static Vector2 EquipmentOrigin => Panel.Children[1].Position;

    public static Vector2 Origin(Vector2 viewport) =>
        new(viewport.X - (EquipmentSlotsLayout.Width + ActorPortraitLayout.ColumnWidth + 20) * BottomHudLayout.GetScale(viewport), 0);
    public static HudRectangle Bounds(Vector2 viewport) =>
        new(Origin(viewport) - new Vector2(8, 24) * BottomHudLayout.GetScale(viewport),
            new Vector2(Width + 16, Height + 24) * BottomHudLayout.GetScale(viewport));

    public int? HitTest(Vector2 point, Vector2 viewport)
    {
        var local = (point - Origin(viewport)) / BottomHudLayout.GetScale(viewport);
        foreach (var slot in Slots)
            if (new HudRectangle(slot.Position, slot.Art.Size).Contains(local)) return slot.Index;
        return null;
    }
}

public sealed record InventoryPanelSlot(int Index, UiTextureRegion Art, Vector2 Position);
