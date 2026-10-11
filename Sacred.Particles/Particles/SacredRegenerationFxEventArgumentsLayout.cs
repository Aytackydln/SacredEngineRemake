using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Mapped regeneration event prefix. The +44 field is only tested for zero by Mana Regen.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public readonly struct SacredRegenerationFxEventArgumentsLayout
{
    /// <summary>Explicit timer input scaled by the native 0.01 operand; not assumed to be milliseconds.</summary>
    [FieldOffset(0x3C)] public readonly uint DurationUnits;
    [FieldOffset(0x40)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x44)] public readonly uint CombatArtPresence;
    [FieldOffset(0x70)] public readonly float DurationMultiplier;
    [FieldOffset(0x74)] public readonly uint CombatArtParametersAddress;
}
