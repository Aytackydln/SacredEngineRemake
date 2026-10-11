using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>MagLite derives duration and light strength from combat-art parameters zero and one.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public readonly struct SacredMagLiteFxEventArgumentsLayout
{
    [FieldOffset(0x0C)] public readonly uint OwnerIdentity;
    [FieldOffset(0x40)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x70)] public readonly float DurationMultiplier;
    [FieldOffset(0x74)] public readonly uint CombatArtParametersAddress;
}
