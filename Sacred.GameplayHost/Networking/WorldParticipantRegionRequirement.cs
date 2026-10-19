namespace Sacred.GameplayHost.Networking;

/// <summary>Required native work retained without marking initialization complete or invalidating terrain.</summary>
public sealed record WorldParticipantRegionRequirement(ushort Sector, byte RawRegion, ushort? ScriptRegion)
{
    public bool IsEntering { get; init; }
    public string Callback => ScriptRegion is { } region ? $"Region{region}{(IsEntering ? "Enter" : "Init")}" : "unresolved script-region callback";
    public override string ToString() => $"sector {Sector}, raw region {RawRegion}, {Callback}: native region/NPC {(IsEntering ? "entry" : "initialization")} pending";
}
