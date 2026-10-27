using System.Numerics;

namespace Sacred.UI.Inventory;

/// <summary>Transient item hover target. A console override allows pointer-free rendering checks.</summary>
public sealed class ItemHoverState
{
    public int? PointerSlot { get; private set; }
    public int? DebugSlot { get; private set; }
    public int? Slot => DebugSlot ?? PointerSlot;
    public Vector2 Anchor { get; private set; }

    public void Update(int? slot, Vector2 anchor) { PointerSlot = slot; Anchor = anchor; }
    public void Override(int? slot) { DebugSlot = slot; PointerSlot = null; }
    public void Clear() { PointerSlot = DebugSlot = null; Anchor = default; }
}
