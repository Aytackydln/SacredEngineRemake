using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Confusion evaluates parameter zero at the supplied level and duration multiplier.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public struct SacredConfusionFxEventArgumentsLayout
{
    [FieldOffset(0x40)] public ushort CombatArtLevel;
    [FieldOffset(0x70)] public float DurationMultiplier;
    [FieldOffset(0x74)] public uint CombatArtParameters;
}
