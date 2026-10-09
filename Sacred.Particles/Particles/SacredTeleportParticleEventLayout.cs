using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Fields read by teleport start 0x771E20. The aligned destination matches
/// Gold's event/world-position copy; unknown event fields remain unnamed.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x30)]
public readonly struct SacredTeleportParticleEventLayout
{
    [FieldOffset(0), BinaryUnknown] public readonly uint VTable;
    /// <summary>Kind two supplies ActorId and Destination; other kinds leave owner zero.</summary>
    [FieldOffset(4)] public readonly uint Kind;
    [FieldOffset(8)] public readonly uint ActorId;
    [FieldOffset(0xC), BinaryUnknown] public readonly uint Unknown0C;
    [FieldOffset(0x10)] public readonly ushort DestinationMap;
    [FieldOffset(0x12), BinaryUnknown] public readonly ushort DestinationPadding12;
    [FieldOffset(0x14)] public readonly int DestinationX;
    [FieldOffset(0x18)] public readonly int DestinationY;
    [FieldOffset(0x1C)] public readonly byte DestinationZ;
    [FieldOffset(0x1D), BinaryUnknown] public readonly byte DestinationPadding1D;
    [FieldOffset(0x1E), BinaryUnknown] public readonly ushort DestinationPadding1E;
    [FieldOffset(0x20), BinaryUnknown] public readonly uint Unknown20;
    [FieldOffset(0x24), BinaryUnknown] public readonly uint Unknown24;
    [FieldOffset(0x28), BinaryUnknown] public readonly uint Unknown28;
    [FieldOffset(0x2C)] public readonly float CastSpeed;
}
