using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Current requested neighborhood and native confirmations. Departed terrain does not delete replicated objects.</summary>
public sealed class GoldSectorInterestState
{
    private readonly HashSet<ushort> _requested = [];
    private readonly Dictionary<ushort, GoldSectorEntrySlot> _confirmed = [];
    public GoldSectorInterestState() => Confirmed = new ReadOnlyDictionary<ushort, GoldSectorEntrySlot>(_confirmed);
    public IReadOnlyDictionary<ushort, GoldSectorEntrySlot> Confirmed { get; }
    public IReadOnlySet<ushort> Requested { get; private set; } = FrozenSet<ushort>.Empty;
    public ushort Center { get; private set; }
    public long Revision { get; private set; }
    public bool HasRequest { get; private set; }
    public bool IsConfirmed => HasRequest && _requested.All(_confirmed.ContainsKey);

    /// <summary>Call when an owned interest send is scheduled, before draining its reply. New sectors still require native confirmation.</summary>
    public void Begin(GoldSectorInterestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CurrentSector == 0 || request.PreviousSector != Center)
            throw new InvalidOperationException("Sector interest must continue the current request chain.");
        var requested = new HashSet<ushort>(_requested);
        for (var i = 0; i < GoldSectorInterestLayout.SlotCount; i++)
        {
            requested.Remove(request.GetLeftSector(i));
            if (request.GetEnteredSector(i) is var sector && sector != 0) requested.Add(sector);
        }
        if (!requested.Contains(request.CurrentSector))
            throw new InvalidOperationException("Sector interest does not retain its center.");
        _requested.Clear(); _requested.UnionWith(requested);
        foreach (var sector in _confirmed.Keys.Where(id => !_requested.Contains(id)).ToArray()) _confirmed.Remove(sector);
        Requested = _requested.ToFrozenSet();
        Center = request.CurrentSector;
        HasRequest = true;
        Revision++;
    }

    public void Confirm(GoldSectorEntry reply)
    {
        var changed = false;
        for (var i = 0; i < reply.Count; i++)
        {
            var slot = reply.GetSlot(i);
            // A delayed reply cannot resurrect a sector which the current interest has retired.
            if (!slot.IsPresent || HasRequest && !_requested.Contains(slot.Sector)) continue;
            if (!_confirmed.TryGetValue(slot.Sector, out var previous) || previous != slot)
            {
                _confirmed[slot.Sector] = slot;
                changed = true;
            }
        }
        if (changed) Revision++;
    }

    public void Clear()
    {
        _requested.Clear(); _confirmed.Clear(); Requested = FrozenSet<ushort>.Empty;
        Center = 0; HasRequest = false; Revision++;
    }
}
