using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>BFG transfers 0x14 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredBfgFxStateLayout
{
    public const int SerializedSize = 0x14;
    [FieldOffset(0)] public byte Active;
    [FieldOffset(1), BinaryUnknown] public byte Unknown001;
    [FieldOffset(2), BinaryUnknown] public ushort Unknown002;
    [FieldOffset(4)] public float ElapsedTime;
    [FieldOffset(8)] public uint SineColor;
    [FieldOffset(12)] public uint ComplementColor;
    [FieldOffset(16)] public uint CosineColor;
}
