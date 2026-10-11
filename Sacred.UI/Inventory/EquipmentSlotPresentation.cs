using System.Numerics;
using Sacred.Inventory.Actors;
using Sacred.UI.Hud;
using Sacred.UI.Textures;

namespace Sacred.UI.Inventory;

/// <summary>A native equipment control, identified independently of the actor's list order.</summary>
public sealed record EquipmentSlotPresentation(
    EquipmentSlotType Type, int Occurrence, int NativeIndex, UiTextureRegion Art, Vector2 Position)
{
    public HudRectangle Bounds => new(Position, Art.Size);
}
