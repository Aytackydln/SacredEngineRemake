using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Both Trap02 visual constructors transfer 8 bytes from object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredTrap02FxStateLayout
{
    public const int SerializedSize = 8;
    [FieldOffset(0)] public float ElapsedTime;
    [FieldOffset(4)] public byte Active;
    [FieldOffset(5), BinaryUnknown] public byte Unknown005;
    [FieldOffset(6), BinaryUnknown] public ushort Unknown006;
}
