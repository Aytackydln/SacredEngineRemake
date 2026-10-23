namespace Sacred.Network.Gameplay;

/// <summary>Native append-only spatial script tables. World bank 4 replaces the map-icon table when nonempty.</summary>
public sealed class GoldScriptSpatialReplica
{
    private readonly List<GoldMapIconState> _mapIcons = [];
    private readonly List<GoldAttractorState> _attractors = [];
    public GoldScriptSpatialReplica()
    {
        MapIcons = _mapIcons.AsReadOnly();
        Attractors = _attractors.AsReadOnly();
    }
    public IReadOnlyList<GoldMapIconState> MapIcons { get; }
    public IReadOnlyList<GoldAttractorState> Attractors { get; }
    public bool HasMapState { get; private set; }
    public long MapRevision { get; private set; }
    internal void Apply(GoldScriptSpatialEvent entry)
    {
        // Re-decode the retained wire record; callers cannot substitute inconsistent parsed fields.
        switch (GoldScriptSpatialCodec.Decode(entry.Message))
        {
            case GoldMapIconEvent icon:
                _mapIcons.Add(icon.Icon); HasMapState = true; MapRevision++;
                break;
            case GoldAttractorEvent attractor:
                _attractors.Add(attractor.Attractor);
                break;
        }
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank4Opcode) return;
        var records = bank.Sections[GoldScriptSpatialLayout.MapIconBankSection].Records;
        // 0x7E65BF skips resize/import for count zero, preserving the native current table.
        if (records.Count == 0) return;
        var next = records.Select(record => GoldMapIconState.FromPayload(record.Data)).ToArray();
        _mapIcons.Clear(); _mapIcons.AddRange(next); HasMapState = true; MapRevision++;
    }
    internal void Clear()
    {
        _mapIcons.Clear(); _attractors.Clear(); HasMapState = false; MapRevision++;
    }
}
