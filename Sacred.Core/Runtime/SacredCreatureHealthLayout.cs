using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Runtime;

/// <summary>Gold 24-byte health block at creature +0x4D0, immediately after
/// the 238-byte value block. GetCV/SetCV 0x549460/0x549480 select its first three
/// words. Script SetCV restores current health (slot 2) against maximum (slot 1).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCreatureHealthLayout
{
    public const int Size = 24;
    public const int MaximumHitPointsOffset = 4;
    public const int CurrentHitPointsOffset = 8;
    public const int RegenerationOffset = 16;
    [FieldOffset(0), BinaryUnknown] public readonly int NativeSlot0;
    [FieldOffset(MaximumHitPointsOffset)] public readonly int MaximumHitPoints;
    [FieldOffset(CurrentHitPointsOffset)] public readonly int CurrentHitPoints;
    [FieldOffset(12), BinaryUnknown] public readonly uint NativeSlot3;
    [FieldOffset(RegenerationOffset)] public readonly uint Regeneration;
    [FieldOffset(20), BinaryUnknown] public readonly uint NativeSlot5;
}
