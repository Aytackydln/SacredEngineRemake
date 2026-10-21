using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero.Objects;

[InlineArray(4)]
public struct SacredSavedStatusOperands
{
    private ushort _first;
}

/// <summary>Gold 0x5853C1..0x58544F: one record in the values object's third counted list.
/// The runtime record is 32 bytes with a vector of UInt32 operands; saving writes
/// its first four operands as UInt16, padding absent operands with zero.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSavedStatusModifierLayout
{
    public const int Size = 28;
    [FieldOffset(0)] public readonly uint ModifierType;
    /// <summary>Type-dependent integer operand, passed to Gold AddBonus as its second argument.</summary>
    [FieldOffset(4)] public readonly int Parameter;
    [FieldOffset(8)] public readonly SacredSavedStatusOperands Operands;
    [FieldOffset(16)] public readonly float Magnitude;
    /// <summary>Gold 0x562CF8 decrements nonnegative values. Negative values use the
    /// owner-validity path; -1 is tied to the owner's current action family.</summary>
    [FieldOffset(20)] public readonly float RemainingSeconds;
    /// <summary>Gold AddBonus stores the normalized low UInt16 code in this UInt32 field.
    /// Special sentinel values also occur; this is not an object reference.</summary>
    [FieldOffset(24)] public readonly uint SourceCode;
}
