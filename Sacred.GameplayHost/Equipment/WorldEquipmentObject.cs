using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Mutable construction state; physical and item casts are independent
/// native probes. The attachment link is absent from the 41-byte packet prefix.</summary>
public sealed class WorldEquipmentObject(GoldObjectBaseState state, bool physical, bool item)
{
    public GoldObjectBaseState Base { get; set; } = state;
    public bool Physical { get; } = physical;
    public bool Item { get; } = item;
    public uint PhysicalFlags { get; set; }
    public uint Link { get; private set; }
    public uint AttachmentParent => (Base.Flags & 0x400) != 0 ? Link : 0;
    public uint WorldLink => (Base.Flags & 0x400) == 0 ? Link : 0;

    /// <summary>Gold 0x4498F0 changes the multiplexed link and only flag 0x400.</summary>
    public void SetLink(uint reference, bool attached)
    {
        Link = reference;
        Base = Base with { Flags = attached ? Base.Flags | 0x400 : Base.Flags & ~0x400u };
    }
}
