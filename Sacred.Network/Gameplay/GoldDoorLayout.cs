using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Gold client 0x7DF534/0x7DF5FD and GameServer 0x4C7758/0x4C7881.</summary>
public static class GoldDoorLayout
{
    public const ushort CloseOpcode = 0x111;
    public const ushort OpenOpcode = 0x112;
    public const int PrefixSize = 4;
    public const int ObjectReferenceOffset = 0;
    public const ushort OpenState = SacredWorldTriggerLayout.OpenStateMask;
    public const uint ActivationAction = 0x21;
}
