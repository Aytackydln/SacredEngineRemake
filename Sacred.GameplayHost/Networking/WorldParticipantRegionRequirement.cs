namespace Sacred.GameplayHost.Networking;

/// <summary>Required native work retained without marking initialization complete or invalidating terrain.</summary>
public sealed record WorldParticipantRegionRequirement(ushort Sector, byte RawRegion, ushort? ScriptRegion)
{
    public string Callback => ScriptRegion is { } region ? $"Region{region}Init" : "unresolved script-region callback";
    public override string ToString() => $"sector {Sector}, raw region {RawRegion}, {Callback}: native region/NPC initialization pending";
}
