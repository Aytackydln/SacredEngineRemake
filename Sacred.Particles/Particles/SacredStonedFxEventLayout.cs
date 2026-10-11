using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Inputs read only when the object's default-duration byte is zero.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public struct SacredStonedFxEventLayout
{
    [FieldOffset(0x40)] public ushort CombatArtSelector;
    [FieldOffset(0x70)] public float DurationMultiplier;
    [FieldOffset(0x74)] public uint CombatArtHandle;
}
