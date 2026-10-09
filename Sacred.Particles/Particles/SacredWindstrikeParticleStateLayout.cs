using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Gold Windstrike serializer 0x789FC0 transfers 0xA0 bytes from +0x20A0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredWindstrikeParticleStateLayout
{
    public const int SerializedSize = 0xA0;
    [FieldOffset(0x00)] public readonly uint EmissionStopped;
    [FieldOffset(0x04)] public readonly byte Active;
    [FieldOffset(0x05), BinaryUnknown] public readonly byte Unknown05;
    [FieldOffset(0x06), BinaryUnknown] public readonly ushort Unknown06;
    [FieldOffset(0x08)] public readonly float ElapsedTime;
    [FieldOffset(0x0C)] public readonly uint CornerColor0;
    [FieldOffset(0x10)] public readonly uint CornerColor1;
    [FieldOffset(0x14)] public readonly uint CornerColor2;
    [FieldOffset(0x18)] public readonly uint CornerColor3;
    [FieldOffset(0x1C)] public readonly SacredParticleMotionLayout Motion;
    [FieldOffset(0x3C)] public readonly SacredParticleEmissionLayout Emission;
}
