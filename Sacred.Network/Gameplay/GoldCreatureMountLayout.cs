namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x15C applies each reference to the other at 0x4C6BCC, through 0x43C0D0.</summary>
public static class GoldCreatureMountLayout
{
    public const ushort Opcode = 0x15C;
    public const int Size = 8;
    public const int FirstReferenceOffset = 0;
    public const int SecondReferenceOffset = 4;
    public const uint DetachSetMask = 0x10000000;
    public const uint PlayerLinkClearMask = 0x90000000;
    /// <summary>Original 0x4021C0 resource table guard, independent of packet reference limits.</summary>
    public const uint ResourceLimit = 0x7E60;
}
