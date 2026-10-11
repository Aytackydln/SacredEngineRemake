using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold 0x779750 saves 0x1C bytes beginning at object +20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredPlongFxStateLayout
{
    public const int SerializedSize = 0x1C;
    [FieldOffset(0x00)] public uint ParticleCount;
    [FieldOffset(0x04)] public float ElapsedTime;
    [FieldOffset(0x08)] public byte Active;
    [FieldOffset(0x09), BinaryUnknown] public byte Unknown009;
    [FieldOffset(0x0A), BinaryUnknown] public ushort Unknown00A;
    [FieldOffset(0x0C)] public uint CornerColor0;
    [FieldOffset(0x10)] public uint CornerColor1;
    [FieldOffset(0x14)] public uint CornerColor2;
    [FieldOffset(0x18)] public uint CornerColor3;
}
