using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Particles.Particles;

/// <summary>Teleport's interpretation of the native 0x40-byte cParticle.
/// OldPosition and Velocity are reused as orbit state while Energy equals seven.
/// After ejection Velocity is an ordinary velocity vector; Energy also controls size.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x40)]
public struct SacredTeleportParticleLayout
{
    [FieldOffset(0x00)] public Vector3 Position;
    [FieldOffset(0x0C)] public float VerticalPhase;
    [FieldOffset(0x10)] public float OrbitAngle;
    [FieldOffset(0x14)] public float OrbitRadius;
    [FieldOffset(0x18)] public Vector3 Velocity;
    [FieldOffset(0x18)] public float PhaseSpeed;
    [FieldOffset(0x1C)] public float OrbitSpeed;
    [FieldOffset(0x24), BinaryUnknown] public float UnusedMass;
    [FieldOffset(0x28)] public float HalfSize;
    [FieldOffset(0x2C)] public float Energy;
    [FieldOffset(0x30)] public float Rotation;
    [FieldOffset(0x34), BinaryUnknown] public uint UnusedColor;
    [FieldOffset(0x38), BinaryUnknown] public float UnusedMoment;
    [FieldOffset(0x3C)] public byte Frame;
    [FieldOffset(0x3D)] public byte ParameterSetId;
    [FieldOffset(0x3E), BinaryUnknown] public ushort Padding;
}
