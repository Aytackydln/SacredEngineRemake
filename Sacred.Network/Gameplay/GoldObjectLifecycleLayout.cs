namespace Sacred.Network.Gameplay;

/// <summary>Gold client 0x7DD573 / 0x7DD67A and server 0x4CE3B0 / 0x4D6950.</summary>
public static class GoldObjectLifecycleLayout
{
    public const ushort RemoveOpcode = 0x166;
    public const ushort ImmediateRemoveOpcode = 0x167;
    public const ushort OwnerOpcode = 0x171;
    public const int RemoveSize = 4;
    public const int OwnerSize = 8;
    public const int ReferenceOffset = 0;
    public const int OwnerWordOffset = 4;
    public const uint RemovalReferenceMask = 0x7FFFFFFF;
    public const uint InactiveFlag = 0x10;
}
