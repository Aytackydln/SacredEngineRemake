using System.Collections.ObjectModel;
using System.Globalization;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x492880 region initialization. Its sector/world inputs are
/// independent of active-hero lookup. The +350 lifecycle map is separate from
/// singleton region flags, which are set before callback delivery.</summary>
public sealed class WorldScriptRegionInitialization
{
    private readonly Dictionary<uint, (uint Value, uint Tag)> _records;
    private ushort[]? _pending;
    private int _cursor;
    public WorldScriptRegionInitialization(IReadOnlyDictionary<uint, (uint Value, uint Tag)>? initial = null)
    {
        _records = initial?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
        Records = new ReadOnlyDictionary<uint, (uint Value, uint Tag)>(_records);
    }
    public IReadOnlyDictionary<uint, (uint Value, uint Tag)> Records { get; }
    /// <summary>492C90's record+5 byte, in the high byte of the existing tag.
    /// This is distinct from the world-region singleton's initialized flag.</summary>
    internal void MarkEntered(uint region)
    {
        var row=_records[region];
        _records[region]=(row.Value,(row.Tag & ~0xff00u)|0x100u);
    }
    /// <summary>Native current script-region global 0x94E084. Nested callbacks
    /// may change it before the final lifecycle insertion; early exits retain it.</summary>
    public uint CurrentRegion { get; set; } = uint.MaxValue;
    public bool HasPendingCallback => _pending is not null;
    public IReadOnlyList<ushort> PendingFunctions => _pending is null ? Array.Empty<ushort>() : _pending.Skip(_cursor).ToArray();

    /// <returns>Native boolean, or null while callback service delivery is pending.</returns>
    public bool? TryInitialize(uint sector, bool worldPresent, bool multiplayer, WorldScriptRegionInitializationServices services)
    {
        if (HasPendingCallback) throw new InvalidOperationException("Continue the pending region callback before another initialization.");
        if (sector == 0 || !worldPresent) return false;
        var raw = services.ReadSectorRegion(sector);
        CurrentRegion = services.MapScriptRegion(raw);
        var key = services.MapScriptRegion(raw); // original performs both lookups
        var present = _records.ContainsKey(key);
        if ((services.ReadFlags(raw) & 1) != 0 && (multiplayer || present))
        {
            if (multiplayer && !present) _records[CurrentRegion] = (CurrentRegion, 1);
            return true;
        }
        services.SetFlags(raw, 1);
        if (present) { CurrentRegion = uint.MaxValue; return false; }
        var name = string.Create(CultureInfo.InvariantCulture, $"Region{CurrentRegion}Init");
        uint packed;
        if (services.CachedFunction(name) is { } cached) packed = cached;
        else
        {
            var index = services.FindFunction(name);
            if (index == 0) { CurrentRegion = uint.MaxValue; return false; }
            packed = unchecked((uint)index);
        }
        _pending = (packed & 0xffff0000) != 0 ? [(ushort)(packed >> 16), (ushort)packed] : [(ushort)packed];
        _cursor = 0;
        return Continue(services);
    }

    public bool? Continue(WorldScriptRegionInitializationServices services)
    {
        if (_pending is null) throw new InvalidOperationException("No region callback is pending.");
        while (_cursor < _pending.Length)
        {
            if (!services.TryInvoke(_pending[_cursor])) return null;
            _cursor++;
        }
        _records[CurrentRegion] = (CurrentRegion, 1);
        _pending = null;
        CurrentRegion = uint.MaxValue;
        return true;
    }
}
