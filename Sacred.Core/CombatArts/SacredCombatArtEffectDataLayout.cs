using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Core.CombatArts;

/// <summary>Gold sEventFXData, copied by cEventFX 0x41E1D0 and cObjectFX 0x5A3840.
/// Embedded unchanged in the FX save/network streams. Handles are native process
/// addresses and must be resolved against the combat-art definition in the receiving runtime.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCombatArtEffectDataLayout
{
    public const int Size = 56;
    [FieldOffset(0)] public readonly ushort CombatArtLevel;
    [FieldOffset(2)] public readonly ushort Padding;
    [FieldOffset(4)] public readonly uint Parameter;
    [FieldOffset(8)] public readonly Vector3 Vector1;
    [FieldOffset(20)] public readonly Vector3 Vector2;
    [FieldOffset(32)] public readonly float MagicDamageBonus;
    [FieldOffset(36)] public readonly float PoisonDamageBonus;
    [FieldOffset(40)] public readonly float FireDamageBonus;
    [FieldOffset(44)] public readonly float PhysicalDamageBonus;
    /// <summary>Generic buff command 6 scales parameter-zero duration by this value,
    /// then truncates to integer seconds. Some particle initializers retain a float duration.</summary>
    [FieldOffset(48)] public readonly float DurationMultiplier;
    [FieldOffset(52)] public readonly uint CombatArtDefinitionHandle;
}
