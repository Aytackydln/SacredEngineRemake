using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Terror Sprinter's 16-byte saved controller at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredTerrorSprinterFxStateLayout
{
    public const int SerializedSize = 0x10;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public float Lifetime;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09)] public byte Mode;
    [FieldOffset(0x0A)] public byte Phase;
    [FieldOffset(0x0B), BinaryUnknown] public byte Unknown00B;
    [FieldOffset(0x0C)] public float Weight;
}
