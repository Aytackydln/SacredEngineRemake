using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Last bank per transport identity and opcode; observation does not confer host startup approval.</summary>
public sealed class GoldWorldBankReplica
{
    private readonly Dictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot> _states = [];
    private readonly Dictionary<ushort, GoldWorldBankSnapshot> _current = [];
    public GoldWorldBankReplica()
    {
        States = new ReadOnlyDictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot>(_states);
        Current = new ReadOnlyDictionary<ushort, GoldWorldBankSnapshot>(_current);
    }
    public IReadOnlyDictionary<(byte Sender, byte Origin, ushort Opcode), GoldWorldBankSnapshot> States { get; }
    /// <summary>Native bank import replaces each global bank in arrival order. States additionally
    /// retains routing identities for diagnostics; an older origin must not win current-state lookup.</summary>
    public IReadOnlyDictionary<ushort, GoldWorldBankSnapshot> Current { get; }
    public bool IsComplete => _current.Count == 4;
    public GoldScriptSpatialReplica Spatial { get; } = new();
    public GoldActualQuestsReplica ActualQuests { get; } = new();
    public GoldScriptRegistrationReplica Registrations { get; } = new();
    public GoldScriptTableReplica ScriptTables { get; } = new();
    public GoldScriptFunctionReplica Functions { get; } = new();
    public GoldScriptDefinitionReplica Definitions { get; } = new();
    public GoldMovementCallbackReplica MovementCallbacks { get; } = new();
    public GoldScriptRegionReplica ScriptRegions { get; } = new();
    public GoldTriggerPatchReplica TriggerPatches { get; } = new();
    public GoldScriptGroupReplica ScriptGroups { get; } = new();
    public GoldQuestReplica Quests { get; } = new();
    public GoldIndexedRecordReplica IndexedRecords { get; } = new();
    public GoldCounterReplica Counters { get; } = new();
    public GoldScriptInvocationReplica Invocations { get; } = new();
    internal void Apply(GoldScriptWorldEvent entry) => Apply(entry, null);
    internal void Apply(GoldScriptWorldEvent entry, GoldSectorEntryRuntime? runtime)
    {
        switch (entry)
        {
            case GoldIndexedRecordEvent indexed: IndexedRecords.Apply(indexed); break;
            case GoldScriptGroupEvent group: ScriptGroups.Apply(group); break;
            case GoldTriggerPatchEvent patch: TriggerPatches.Apply(patch); break;
            case GoldScriptRegionEvent region: ScriptRegions.Apply(region); break;
            case GoldMovementCallbackEvent callback: MovementCallbacks.Apply(callback); break;
            case GoldScriptDefinitionEvent definition: Definitions.Apply(definition); break;
            case GoldScriptFunctionEvent function: Functions.Apply(function); break;
            case GoldScriptSpatialEvent spatial: Spatial.Apply(spatial); break;
            case GoldActualQuestsEvent quests: ActualQuests.Apply(quests); break;
            case GoldScriptRegistrationEvent registration: Registrations.Apply(registration, runtime); break;
            case GoldScriptTableEvent table: ScriptTables.Apply(table); break;
        }
    }
    internal void Apply(GoldWorldBankEvent entry)
    {
        _states[(entry.Message.Sender, entry.Message.Origin, entry.Message.Opcode)] = entry.Snapshot;
        _current[entry.Message.Opcode] = entry.Snapshot;
        Quests.Apply(entry.Snapshot, Functions);
        IndexedRecords.Apply(entry.Snapshot);
        Counters.Apply(entry.Snapshot);
        Spatial.Apply(entry.Snapshot);
        ActualQuests.Apply(entry.Snapshot);
        Registrations.Apply(entry.Snapshot);
        Definitions.Apply(entry.Snapshot);
        MovementCallbacks.Apply(entry.Snapshot, Functions);
        ScriptRegions.Apply(entry.Snapshot);
        TriggerPatches.Apply(entry.Snapshot);
        ScriptGroups.Apply(entry.Snapshot);
    }
    internal void Clear() { _states.Clear(); _current.Clear(); Spatial.Clear(); ActualQuests.Clear(); Registrations.Clear(); ScriptTables.Clear(); Functions.Clear(); Definitions.Clear(); MovementCallbacks.Clear(); ScriptRegions.Clear(); TriggerPatches.Clear(); ScriptGroups.Clear(); Quests.Clear(); IndexedRecords.Clear(); Counters.Clear(); Invocations.Clear(); }
}
