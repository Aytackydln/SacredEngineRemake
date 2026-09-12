namespace Sacred.Core.GameBin.Scripts;

/// <summary>Literal coordinates; units depend on the operand tag.</summary>
public readonly record struct SacredScriptPosition(int X, int Y, int Z);

/// <summary>Recovered literal arguments of opcode 8. This describes a script instruction,
/// not an active world object: control flow and execution state are not evaluated.
/// Missing optional operands remain null rather than inventing runtime defaults.</summary>
public sealed record SacredScriptCreateObject(
    string? Name,
    uint TypeId,
    SacredScriptPosition? TilePosition,
    SacredScriptPosition? WorldPosition,
    short? HeightOffset,
    string? SymbolicTilePosition = null);
