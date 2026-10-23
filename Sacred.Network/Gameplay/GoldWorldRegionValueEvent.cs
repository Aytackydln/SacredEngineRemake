using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWorldRegionValueEvent(GoldMessage Message, uint Region, uint Selector, uint RawValue) : GoldGameEvent(Message)
{
    public byte RegionIndex => unchecked((byte)Region);
    public ushort ValueKind => unchecked((ushort)Selector);
    public bool Add => Message.Opcode == GoldWorldRegionValueLayout.AddOpcode;
}
