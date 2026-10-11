using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7A7A90/7A7B30 serialize 0x18 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredForgeElementFxStateLayout
{
    public const int SerializedSize = 0x18;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float Scroll;
    /// <summary>Growth/fade envelope: model XY scale, second flare size and ring Z scale.</summary>
    [FieldOffset(0x08)] public float GrowthFactor;
    /// <summary>Compression envelope: model XY scale, first flare size and ring XY scale.</summary>
    [FieldOffset(0x0C)] public float CompressionFactor;
    [FieldOffset(0x10)] public float ChildElapsedTime;
    [FieldOffset(0x14)] public byte Active;
    [FieldOffset(0x15)] public byte Element;
    /// <summary>0 grow, 1 hold, 2 compress, 3 fade, 4 finished. Base flags +74 bit 8 requests compression.</summary>
    [FieldOffset(0x16)] public byte Phase;
    [FieldOffset(0x17), BinaryUnknown] public byte Unknown017;
}
