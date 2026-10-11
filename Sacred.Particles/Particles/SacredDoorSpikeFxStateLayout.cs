using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7C4850/7C4900 transfer 0xB0 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredDoorSpikeFxStateLayout
{
    public const int SerializedSize = 0xB0;
    public const int StoredCount = 10;
    /// <summary>Updated but untouched by event initialization; retained on native reinitialization.</summary>
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x08)] public float Envelope;
    [FieldOffset(0x0C)] public SacredDoorSpikeScalesLayout Scales;
    /// <summary>0 grow; 1 hold; 2 shrink. Base +74 mask 0x08 releases only while holding.</summary>
    [FieldOffset(0x34)] public byte Phase;
    [FieldOffset(0x35), BinaryUnknown] public byte Unknown035;
    [FieldOffset(0x36), BinaryUnknown] public ushort Unknown036;
    /// <summary>Ten authored XYZ offsets, although the renderer submits only six spikes.</summary>
    [FieldOffset(0x38)] public SacredDoorSpikeOffsetsLayout Offsets;
}
