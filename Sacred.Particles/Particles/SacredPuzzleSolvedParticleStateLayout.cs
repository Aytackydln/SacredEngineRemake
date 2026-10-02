using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold PuzzleSolved payload at object +0x20A0, serialized by 0x76CBA0.
/// Texture handles follow the 0xA0-byte payload at +0x2140/+0x2144.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredPuzzleSolvedParticleStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0x00)] public readonly float ElapsedTime;
    [FieldOffset(0x04)] public readonly byte Alive;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown005;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown006;
    [FieldOffset(0x08)] public readonly float Speed;
    [FieldOffset(0x0C)] public readonly uint Color0;
    [FieldOffset(0x10)] public readonly uint Color1;
    [FieldOffset(0x14)] public readonly uint Color2;
    [FieldOffset(0x18)] public readonly uint Color3;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x3C)] public readonly SacredParticleEmissionLayout Emission;
}
