using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Native named trigger/resource identities and resource-to-object associations.
/// Importing names does not construct actors or execute their scripts.</summary>
public sealed class GoldScriptResourceReplica
{
    private readonly List<GoldScriptNameRecord> _names = [];
    private readonly Dictionary<uint, uint> _bindings = [];
    public GoldScriptResourceReplica()
    {
        Names = _names.AsReadOnly();
        ObjectBindings = new ReadOnlyDictionary<uint, uint>(_bindings);
    }
    public IReadOnlyList<GoldScriptNameRecord> Names { get; }
    public IReadOnlyDictionary<uint, uint> ObjectBindings { get; }
    public long Revision { get; private set; }
    public GoldScriptNameRecord? FindName(string name) => _names.FirstOrDefault(row =>
        row.Name.Length != 0 && GoldScriptNames.Equal(row.Name, name));

    /// <summary>Gold 0x464760(name, requestedTrigger, 0, false), after received creation.
    /// Resource zero retains an existing resource without copying it into the new trigger.</summary>
    public void RegisterTrigger(string name, uint reference)
    {
        Revision++;
        var index = _names.FindIndex(row => row.Name.Length != 0 && GoldScriptNames.Equal(row.Name, name));
        if (index < 0) _names.Add(GoldScriptNameRecord.Create(name, 0, reference));
        else
        {
            var previous = _names[index];
            var trigger = reference != uint.MaxValue && previous.TriggerReference is 0 or uint.MaxValue
                ? reference : previous.TriggerReference;
            _names[index] = previous.WithTrigger(name, trigger);
        }
    }

    /// <summary>Read-only portion of Gold 0x465070. An unknown name requires native
    /// ID reservation; hero/NON_UNIQUE deliberately have no named resource.</summary>
    public uint? FindLocalResource(string name) => GoldScriptNames.Equal(name, "hero") || GoldScriptNames.Equal(name, "NON_UNIQUE")
        ? GoldScriptResourceLayout.UnnamedResource : FindName(GoldScriptResourceLayout.LocalResourcePrefix + name)?.ResourceId;

    public void Apply(GoldWorldBankSnapshot bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (bank.Opcode == GoldWorldBankLayout.Bank2Opcode)
        {
            var rows = bank.Sections[GoldScriptResourceLayout.NameBankSection].Records;
            // Native 0x7E602C skips resizing/copying when the received count is zero.
            if (rows.Count == 0) return;
            Revision++;
            var names = rows.Select(row => GoldScriptNameRecord.FromPayload(row.Data)).ToArray();
            _names.Clear(); _names.AddRange(names);
        }
        else if (bank.Opcode == GoldWorldBankLayout.Bank3Opcode)
        {
            Revision++;
            // Native 0x7E6FE1 inserts/assigns each pair without clearing older keys.
            foreach (var row in bank.Sections[GoldScriptResourceLayout.BindingBankSection].Records)
                _bindings[BinaryPrimitives.ReadUInt32LittleEndian(row.Data)] = BinaryPrimitives.ReadUInt32LittleEndian(row.Data[4..]);
        }
    }

    /// <returns>A native trigger referent update, when an existing named trigger is linked.</returns>
    public (uint Trigger, uint Resource)? Apply(GoldScriptResourceEvent update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Revision++;
        var index = _names.FindIndex(row => row.Name.Length != 0 && GoldScriptNames.Equal(row.Name, update.Name));
        if (update.ResourceId is not { } resource)
        {
            // Gold 0x464DE0 swaps in the last record, preserving native search order.
            if (_names.Count > 65000) _names.Clear();
            else if (index >= 0) { _names[index] = _names[^1]; _names.RemoveAt(_names.Count - 1); }
            return null;
        }
        // Client 0x806480 and server 0x496CD0 ignore zero IDs and empty names.
        if (resource == 0 || update.Name.Length == 0) return null;
        if (index < 0) _names.Add(GoldScriptNameRecord.Create(update.Name, resource));
        else
        {
            var previous = _names[index];
            _names[index] = previous.WithResource(update.Name, resource);
            return (previous.TriggerReference, resource);
        }
        return null;
    }

    /// <summary>Gold 0x464BD0: direct slots 0..16, then explicit mapping, then ascending
    /// object scan, including inactive records. A nonzero mapping wins before object lookup.</summary>
    public uint? FindObject(uint resource, IReadOnlyDictionary<uint, GoldReplicatedObject> objects,
        Func<uint, uint> templateResource)
    {
        if (resource <= 16) return resource;
        if (_bindings.TryGetValue(resource, out var mapped) && mapped != 0) return mapped;
        foreach (var pair in objects.OrderBy(pair => pair.Key))
        {
            if (pair.Key == 0) continue;
            var state = pair.Value.BaseState;
            var effective = state.ScriptResourceId != 0 ? state.ScriptResourceId : templateResource(state.ObjectType);
            if (effective == resource) return pair.Key;
        }
        return null;
    }
    public void Clear() { _names.Clear(); _bindings.Clear(); Revision++; }
}
