using Sacred.Core.GameBin.Scripts;

namespace Sacred.World.Objects;

/// <summary>One script trigger shared by all linked door models. No quest callbacks are executed.</summary>
public sealed record WorldDoorDefinition(
    uint Id, string? ObjectName, string? TriggerName, bool InitiallyOpen, bool Locked,
    IReadOnlyList<SacredScriptPosition> BlockingCells);
