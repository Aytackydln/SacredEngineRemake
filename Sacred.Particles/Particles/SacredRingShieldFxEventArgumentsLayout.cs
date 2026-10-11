using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Both shield event starts read combat parameter zero at the supplied level and multiplier.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public struct SacredRingShieldFxEventArgumentsLayout
{
    public const int SerializedSize = 0x78;
    [FieldOffset(0x0C)] public uint Owner;
    [FieldOffset(0x40)] public ushort CombatArtLevel;
    [FieldOffset(0x70)] public float DurationMultiplier;
    [FieldOffset(0x74)] public uint CombatArtParameters;
}
