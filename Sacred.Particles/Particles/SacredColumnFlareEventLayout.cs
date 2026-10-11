using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Native parameter-zero combat duration inputs used by both column-flare initializers.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public struct SacredColumnFlareEventLayout
{
    [FieldOffset(0x0C)] public uint OwnerIdentity;
    [FieldOffset(0x40)] public ushort CombatArtLevel;
    [FieldOffset(0x70)] public float DurationScale;
    [FieldOffset(0x74)] public uint CombatArtPointer;
}
