using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Zero subtype uses the fixed eight-second large ring; any nonzero subtype uses combat duration.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public struct SacredTerrorSprinterFxEventLayout
{
    [FieldOffset(0x08)] public uint OwnerIdentity;
    [FieldOffset(0x38)] public uint Subtype;
    [FieldOffset(0x40)] public ushort CombatArtLevel;
    [FieldOffset(0x70)] public float DurationMultiplier;
    [FieldOffset(0x74)] public uint CombatArtDefinition;
}
