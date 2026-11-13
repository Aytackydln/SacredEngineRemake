namespace Sacred.Core.GameBin.Scripts;

/// <summary>Literal coordinates; units depend on the operand tag. Tile-position
/// Z selects the surface byte, not particle height. An unloaded elevated Effect
/// can retain that surface while its initial particle origin remains at height zero.</summary>
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
    string? SymbolicTilePosition = null,
    ushort? FacingDegrees = null,
    string? TriggerReference = null)
{
    /// <summary>Later tag-1 operand, separate from Name. Gold 0x48611C stores
    /// its decoded text/resource in secondary slots; later operands replace it.
    /// Resolving the associated-creature lifecycle belongs to the runtime caller.</summary>
    public string? SecondaryName { get; init; }
    public string? TakeScript { get; init; }
    public string? UseScript { get; init; }
}
