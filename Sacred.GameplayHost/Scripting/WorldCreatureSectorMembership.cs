namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FEE50/0x5FEFF0 ordinary creature sector vectors. Removal
/// swaps the final reference into the removed slot; registration skips sector zero.
/// Callers apply world/ownership gates before moving an actor between sectors.</summary>
public sealed class WorldCreatureSectorMembership
{
    private readonly Dictionary<ushort, List<uint>> _sectors = [];
    public IReadOnlyList<uint> Get(ushort sector) => _sectors.TryGetValue(sector, out var list) ? list.AsReadOnly() : Array.Empty<uint>();

    /// <summary>Gold 0x5FCBE3/0x5FCC5D: retired actors on unentered sectors stay on
    /// tile chains but are omitted from the creature sector vector. Entered sectors differ.</summary>
    public void RegisterUnenteredReload(WorldCreatureInitialActor actor)
    {
        if ((actor.Base.Flags & 0x10000000) == 0) Move(actor.Base.ObjectReference,0,actor.Base.Sector);
    }

    public void Move(uint reference, ushort oldSector, ushort newSector)
    {
        if (reference <= 16) throw new ArgumentOutOfRangeException(nameof(reference));
        if (oldSector == newSector) return;
        if (_sectors.TryGetValue(oldSector, out var old))
        {
            var index = old.IndexOf(reference);
            if (index >= 0) { old[index] = old[^1]; old.RemoveAt(old.Count - 1); }
        }
        if (newSector == 0) return;
        if (!_sectors.TryGetValue(newSector, out var current)) _sectors.Add(newSector, current = []);
        if (!current.Contains(reference)) current.Add(reference);
    }
}
