using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7C0680/7C0730 transfer 0x18C bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredAnducarFxStateLayout
{
    public const int SerializedSize = 0x18C;
    public const int MaximumCount = 10;
    [FieldOffset(0x000)] public SacredAnducarValuesLayout ElapsedTimes;
    [FieldOffset(0x028)] public SacredAnducarValuesLayout HoldTimes;
    [FieldOffset(0x050)] public SacredAnducarValuesLayout Delays;
    [FieldOffset(0x078)] public SacredAnducarBytesLayout Alive;
    [FieldOffset(0x082)] public byte Active;
    [FieldOffset(0x083), BinaryUnknown] public byte Unknown083;
    [FieldOffset(0x084)] public SacredAnducarValuesLayout Scales;
    [FieldOffset(0x0AC)] public SacredAnducarValuesLayout Angles;
    [FieldOffset(0x0D4)] public SacredAnducarOffsetsLayout Offsets;
    /// <summary>0 delayed; 1 grow; 2 hold; 3 shrink. Stage 3 persists after completion.</summary>
    [FieldOffset(0x14C)] public SacredAnducarBytesLayout Stages;
    [FieldOffset(0x156)] public SacredAnducarBytesLayout ModelSelectors;
    [FieldOffset(0x160)] public byte Count;
    [FieldOffset(0x161), BinaryUnknown] public byte Unknown161;
    [FieldOffset(0x162), BinaryUnknown] public ushort Unknown162;
    [FieldOffset(0x164)] public SacredAnducarValuesLayout Envelopes;
}
