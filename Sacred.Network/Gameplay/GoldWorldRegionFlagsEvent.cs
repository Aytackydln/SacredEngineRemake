using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldWorldRegionFlagsEvent(GoldMessage Message, uint Region, uint Mask, bool Set) : GoldGameEvent(Message)
{
    public byte RegionIndex => unchecked((byte)Region);
}
