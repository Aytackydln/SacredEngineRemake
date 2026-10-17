using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>SetTrigger's tagged byte at Gold 0x472BC0/0x496520.
/// Value 1 selects native trigger flags 2; other byte values select flags 1.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptTriggerScopeLayout
{
    public const int SerializedSize = 2;
    [FieldOffset(0)] public readonly byte Tag; // 0x28
    [FieldOffset(1)] public readonly byte Scope;
}

public static class SacredScriptTriggerOpcodes
{
    public const ushort SetTrigger = 48;
    public const ushort TriggerPatch = 50;
    /// <summary>Gold handlers prepend this namespace before the authored name.</summary>
    public const string NamePrefix = "Trg";
    /// <summary>Gold 0x496F20 caps its collected cell count at 99.</summary>
    public const int MaximumPatchCells = 99;
}

public sealed record SacredScriptTriggerPatch(string Name, IReadOnlyList<SacredScriptPosition> Cells, int SourceOffset);

/// <summary>Completely decoded SetTrigger allocation inputs. Does not allocate the world reference.</summary>
public readonly record struct SacredScriptTriggerCreation(string Name, byte Scope, ushort State, int SourceOffset)
{
    public ushort NativeFlags => (ushort)((Scope == 1 ? 2 : 1) | 0x80);
}
