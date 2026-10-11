using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 7BD320/7BD3C0 serialize twelve bytes starting at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredPongBatterFxStateLayout
{
    public const int SerializedSize = 0x0C;
    [FieldOffset(0x00)] public float ElapsedTime;
    [FieldOffset(0x04)] public byte Active;
    [FieldOffset(0x05), BinaryUnknown] public byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public ushort Unknown006;
    [FieldOffset(0x08)] public float HitFlash;
}
