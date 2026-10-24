using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.Inventory;

/// <summary>Lower-left backpack/art pages, independent of the right-hand character column.</summary>
public static class InventoryTabsLayout
{
    public const float Width = 640;
    public const float ContentHeight = 256;
    public const float TabHeight = 32;
    public const float Height = TabHeight + ContentHeight;
    public static Vector2 ContentOrigin => new(0, TabHeight);

    public static Vector2 Origin(Vector2 viewport) => new(0,
        viewport.Y - (768 - 400) * BottomHudLayout.GetScale(viewport));
    public static HudRectangle Bounds(Vector2 viewport) =>
        new(Origin(viewport), new Vector2(Width, Height) * BottomHudLayout.GetScale(viewport));
    public static HudRectangle TabBounds(InventoryTab tab) =>
        new(new Vector2((int)tab * 176, 0), new(172, TabHeight - 2));
    public static Vector2 ToLocal(Vector2 point, Vector2 viewport) =>
        (point - Origin(viewport)) / BottomHudLayout.GetScale(viewport);

    public static InventoryTab? HitTestTab(Vector2 point, Vector2 viewport)
    {
        var local = ToLocal(point, viewport);
        if (TabBounds(InventoryTab.Inventory).Contains(local)) return InventoryTab.Inventory;
        if (TabBounds(InventoryTab.CombatArts).Contains(local)) return InventoryTab.CombatArts;
        return null;
    }
}
