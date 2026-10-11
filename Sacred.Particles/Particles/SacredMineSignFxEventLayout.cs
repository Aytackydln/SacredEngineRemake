using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x78)]
public readonly struct SacredMineSignFxEventLayout
{
    [FieldOffset(0x40)] public readonly ushort CombatArtLevel;
    [FieldOffset(0x70)] public readonly float CountMultiplier;
    [FieldOffset(0x74)] public readonly uint CombatArtRecord;
}
