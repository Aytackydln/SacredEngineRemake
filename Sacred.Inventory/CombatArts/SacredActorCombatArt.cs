using System;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;

namespace Sacred.Inventory.CombatArts;

/// <summary>One acquired sSpellEntry. Timers are retained explicitly; regeneration
/// formulas and timer advancement belong to the future combat calculation.</summary>
public sealed record SacredActorCombatArt(SacredCombatArtKind Kind, ushort Code,
    byte BaseLevel, byte BonusLevel, ushort Flags = 0,
    float TimerMaximum = 0, float TimerFactor = 0, float Timer = 0)
{
    public ushort TotalLevel => (ushort)(BaseLevel + BonusLevel);
    /// <summary>Original import, retained when the actor rebuilds equipment-derived bonus levels.</summary>
    public SacredCombatArtRuntimeLayout? NativeValues { get; private init; }

    public static SacredActorCombatArt FromNativeRecord(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SacredCombatArtRuntimeLayout.Size)
            throw new ArgumentException("A combat-art entry must contain exactly 22 bytes.", nameof(bytes));
        var source = MemoryMarshal.Read<SacredCombatArtRuntimeLayout>(bytes);
        if (source.Kind is not (SacredCombatArtKind.Spell or SacredCombatArtKind.SpecialMove))
            throw new ArgumentException("Unknown native combat-art kind.", nameof(bytes));
        return new(source.Kind, source.Code, source.BaseLevel, source.BonusLevel,
            source.Flags, source.TimerMaximum, source.TimerFactor, source.Timer) { NativeValues = source };
    }
}
