using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.WeaponModifiers;

/// <summary>Gold serialized bonus definition; the native runtime vector stores the same 24 bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredWeaponModifierBonusLayout
{
    public const int Size = 24;
    /// <summary>Chance of accepting the bonus during generation, out of 100.</summary>
    [FieldOffset(0)] public readonly ushort Chance;
    /// <summary>Packed BonusT: low byte targets an opponent category (0x80 ranged,
    /// 0x81 mounted); bits 8–11 restrict the hero, and bits 13–15 select the damage family.</summary>
    [FieldOffset(2)] public readonly ushort Type;
    [FieldOffset(4)] public readonly uint Group;
    [FieldOffset(8)] public readonly short Minimum;
    [FieldOffset(10)] public readonly short Maximum;
    /// <summary>Native skill/combat-art pool selector; not a combat-art ID or on-hit cast.</summary>
    [FieldOffset(12)] public readonly uint Pool;
    /// <summary>Rank divisor when the original low group code is below 201; zero defaults to 20.
    /// Native tests the original group, even when a pool selects a skill.</summary>
    [FieldOffset(16)] public readonly int CombatArtLevelDivisor;
    /// <summary>Rank divisor when the original low group code is 600..798; zero defaults to 10.</summary>
    [FieldOffset(20)] public readonly int SkillLevelDivisor;
}
