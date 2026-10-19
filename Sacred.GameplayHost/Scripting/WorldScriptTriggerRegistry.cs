using System.Collections.ObjectModel;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>File-derived trigger state and SetTrigger registrations. Sparse unwritten IDs remain unknown;
/// they must receive proven native initialization before a complete bank can be produced.</summary>
public sealed class WorldScriptTriggerRegistry : IWorldScriptTriggerRegistry
{
    private readonly Dictionary<uint, GoldWorldTriggerState> _records = [];
    private readonly Dictionary<string, uint> _names = new(WorldScriptNameComparer.Instance);
    private readonly GoldTriggerReferencePool _grants;
    private readonly SacredSectorGrid? _sectors;
    private readonly Dictionary<uint, uint> _patches = [];
    private readonly List<(ushort Sector, SacredScriptPosition Position)> _pendingPositions = [];
    public WorldScriptTriggerRegistry(SacredWorldTriggerArchive source, GoldTriggerReferencePool grants, SacredSectorGrid? sectors = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _sectors = sectors;
        AuthoredTableBound = source.Header.TriggerCount;
        Count = AuthoredTableBound;
        for (uint i = 0; i < source.Records.Count; i++)
        {
            var row = source.Records[(int)i];
            _records.Add(i, new(row.Reference, row.Flags, row.State, row.Referent, row.Parameter));
        }
        Records = new ReadOnlyDictionary<uint, GoldWorldTriggerState>(_records);
        Patches = new ReadOnlyDictionary<uint, uint>(_patches);
        PendingPositions = _pendingPositions.AsReadOnly();
    }
    public uint AuthoredTableBound { get; }
    public uint Count { get; private set; }
    public uint UnknownSlotCount => Count - (uint)_records.Count;
    public IReadOnlyDictionary<uint, GoldWorldTriggerState> Records { get; }
    public IReadOnlyDictionary<uint, uint> Patches { get; }
    public IReadOnlyList<(ushort Sector, SacredScriptPosition Position)> PendingPositions { get; }
    public uint? Find(string name) => _names.TryGetValue(name, out var reference) ? reference : null;

    /// <summary>Gold CreateObj links the named trigger to the new object. It keeps the trigger
    /// name/reference and replaces its referent; repeated attachments therefore target the latest object.</summary>
    internal uint AttachObject(string suffix, uint objectReference)
    {
        var name = SacredScriptTriggerOpcodes.NamePrefix + suffix;
        if (!_names.TryGetValue(name, out var reference) || !_records.TryGetValue(reference, out var state)) return 0;
        _records[reference] = state with { Referent = objectReference };
        return reference;
    }

    internal bool PrepareContainerAnimation(uint reference)
    {
        if (!_records.TryGetValue(reference, out var state) || (state.State & 0x8000) == 0) return false;
        // CreateObj with no explicit on/off operands sends animation event 0xAB.
        // Native 0x415F00 ORs the supplied mask, so bit 0 stays set when already present.
        return true;
    }

    public void Create(SacredScriptTriggerCreation creation)
    {
        if (creation.Name.Length >= 64) throw new InvalidDataException("Trigger name exceeds the native 64-byte registration storage.");
        if (!_grants.TryAllocate(out var reference))
            throw new InvalidDataException($"Trigger '{creation.Name}' needs a 0x154 grant using original Triggers.PAK bound {AuthoredTableBound}.");
        if (reference == uint.MaxValue) throw new InvalidDataException("Trigger reference cannot grow the native vector.");
        _records[reference] = new(reference, creation.NativeFlags, creation.State, 0, 0);
        Count = Math.Max(Count, reference + 1);
        // Native 0x464760 keeps an existing nonzero reference even though SetTrigger allocated another trigger.
        if (!_names.TryGetValue(creation.Name, out var current) || current == 0) _names[creation.Name] = reference;
    }

    /// <summary>Gold startup 0x463F50: replace the packed cell mapping and queue tile positions.
    /// Duplicate mapping or queued XYZ ends the entire current command, including its remaining cells.</summary>
    public void Patch(SacredScriptTriggerPatch patch)
    {
        if (_sectors is null) throw new InvalidDataException("TriggerPatch requires the authored KEYX sector lookup.");
        if (!_names.TryGetValue(patch.Name, out var reference))
            throw new InvalidDataException($"Unresolved trigger '{patch.Name}'.");
        foreach (var cell in patch.Cells)
        {
            var surface = cell.Z < 2 ? 0u : unchecked((uint)cell.Z);
            var key = unchecked(((uint)cell.X << 14 | (uint)cell.Y) << 4 | surface);
            if (_patches.TryGetValue(key, out var current) && current == reference) return;
            _patches[key] = reference;
            if (_pendingPositions.Any(position => position.Position.X == cell.X && position.Position.Y == cell.Y &&
                unchecked((byte)position.Position.Z) == unchecked((byte)cell.Z))) return;
            _pendingPositions.Add((unchecked((ushort)_sectors.GetSector(cell.X, cell.Y)), cell with { Z = unchecked((byte)cell.Z) }));
        }
    }
}
