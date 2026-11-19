using System.Runtime.InteropServices;

namespace Sacred.Core.CombatArts;

/// <summary>Gold's 0x78-byte definition, resolved by 4D1AF0. 4D1C00 returns
/// base + rank * per-level for the acquired-art regeneration timer.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSpellDefinitionLayout
{
    public const int Size = 0x78;
    [FieldOffset(0)] public readonly ushort Code;
    [FieldOffset(0x40)] public readonly float RegenerationBase;
    [FieldOffset(0x44)] public readonly float RegenerationPerLevel;
}
