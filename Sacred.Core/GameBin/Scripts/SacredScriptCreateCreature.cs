namespace Sacred.Core.GameBin.Scripts;

/// <summary>CreateNPC (opcode 1) operands, before runtime factory/value initialization.
/// The first nonzero TypeId chooses the creature; subsequent TypeIds are equipment resources,
/// in original order. They are never object-instance references.</summary>
public sealed record SacredScriptCreateCreature(string? Name, uint TypeId, IReadOnlyList<uint> EquipmentTypes,
    SacredScriptPosition? TilePosition, ushort? FacingDegrees, int Group, int LevelInput, uint Behavior,
    int Aggression, bool HasExplicitBehavior)
{
    public const ushort Opcode = 1;
    /// <summary>Operand 0x6B; default -1. Zero additionally enables the native regional
    /// replacement pass before construction. Flag application requires the constructed actor.</summary>
    public int Flag2B7Mask08Input { get; init; } = -1;
    /// <summary>Gold 0x483A08, applied after construction. The omitted negative sentinel
    /// preserves the actor's constructor flags; encoded UInt16 values clear/set mask 0x0008.</summary>
    public ushort ApplyFlag2B7Mask08(ushort flags) => Flag2B7Mask08Input == 0 ? (ushort)(flags & ~8)
        : Flag2B7Mask08Input > 0 ? (ushort)(flags | 8) : flags;
    /// <summary>Last authored world operand, when it follows the last tile operand.
    /// Its Z is retained for file inspection but does not select the native surface.</summary>
    public SacredScriptPosition? WorldPosition { get; init; }
    /// <summary>Gold CreateNPC scratch words after all placement operands: defaults (-1,0,-1).
    /// A tile operand replaces all three words; a world operand negates X/Y and preserves Z.
    /// The runtime selects world units only when both encoded X and Y are negative.</summary>
    public SacredScriptPosition PlacementInput { get; init; } = TilePosition ?? new(-1, 0, -1);
}
