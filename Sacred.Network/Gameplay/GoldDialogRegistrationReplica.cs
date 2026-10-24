using System.Globalization;

namespace Sacred.Network.Gameplay;

public sealed record GoldDialogRegistration(GoldDialogRegistrationEvent Event, GoldReplicatedObject Object,
    string Key, string CallbackName, GoldScriptFunctionSnapshot Functions)
{
    public bool TryResolveFunction(out uint index)
    {
        if (!Functions.TryGetDefinitions(out var definitions)) { index = uint.MaxValue; return false; }
        for (var i = 0; i < definitions.Count; i++)
            if (GoldScriptNames.Equal(CallbackName, definitions[i].Name)) { index = (uint)i; return true; }
        index = uint.MaxValue; return true; // Native registry retains -1 for a valid absent function.
    }
}

public sealed class GoldDialogCallbackGroup
{
    private readonly List<GoldDialogRegistration> _callbacks = [];
    internal GoldDialogCallbackGroup(string key) { Key = key; Callbacks = _callbacks.AsReadOnly(); }
    public string Key { get; }
    public IReadOnlyList<GoldDialogRegistration> Callbacks { get; }
    internal void Add(GoldDialogRegistration registration) => _callbacks.Add(registration);
}

/// <summary>1CD's resource resolution, dialog binding and callback registration. Registered callbacks are not executed here.</summary>
public sealed class GoldDialogRegistrationReplica
{
    private readonly List<GoldDialogRegistration> _registrations = [];
    private readonly List<GoldDialogCallbackGroup> _groups = [];
    private readonly Queue<(uint Previous, uint Next, uint Reference, long Instance)> _attachments = [];
    public GoldDialogRegistrationReplica() { Registrations = _registrations.AsReadOnly(); Groups = _groups.AsReadOnly(); }
    public IReadOnlyList<GoldDialogRegistration> Registrations { get; }
    public IReadOnlyList<GoldDialogCallbackGroup> Groups { get; }
    public int PendingAttachmentCount => _attachments.Count;
    internal bool Apply(GoldDialogRegistrationEvent entry, GoldWorldReplica world, Dictionary<uint, GoldReplicatedObject> objects)
    {
        if (world.FindScriptObject(entry.Resource) is not { } reference || !objects.TryGetValue(reference, out var actor) ||
            actor.Creature is null) return false; // Native 463240 has no inactive-object guard.
        var key = string.Create(CultureInfo.InvariantCulture, $"Talk_{entry.Name}_Dlg_{unchecked((int)entry.DialogIndex)}");
        var callback = string.Create(CultureInfo.InvariantCulture, $"SelfTriggerQuest{unchecked((int)entry.QuestWord)}");
        _registrations.Add(new(entry, actor, key, callback, world.WorldBanks.Functions.Capture()));
        var group = _groups.FirstOrDefault(g => GoldScriptNames.Equal(g.Key, key));
        if (group is null) { group = new(key); _groups.Add(group); }
        group.Add(_registrations[^1]);
        _attachments.Enqueue((actor.Creature.DialogIndex, entry.DialogIndex, reference, actor.InstanceVersion));
        ApplyAttachments(world.ScriptDialogs, objects);
        var creature = actor.Creature.WithControlWord(GoldCreatureStateLayout.Word245Offset, entry.DialogIndex);
        var setterFlags = unchecked((int)entry.DialogIndex) <= 0 ? actor.BaseState.Flags & ~0x80000u : actor.BaseState.Flags;
        // 5498F0(1) requests an owned 11A before the final interpreter dirty flag is set.
        world.CreatureControls.QueueOwnedState(actor with { BaseState = actor.BaseState with { Flags = setterFlags } },
            GoldCreatureUpdateState.FromCreature(creature), "dialog registration");
        objects[reference] = actor with { Creature = creature, BaseState = actor.BaseState with { Flags = setterFlags | 0x80000 } };
        return true;
    }
    internal void ApplyAttachments(GoldScriptDialogReplica dialogs, IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        if (!dialogs.IsBaselineKnown) return;
        while (_attachments.TryDequeue(out var work))
        {
            if (!objects.TryGetValue(work.Reference, out var actor) || actor.InstanceVersion != work.Instance) continue;
            // Native signed-positive and unsigned-count guards exclude zero, negatives and out-of-range slots.
            if (unchecked((int)work.Previous) > 0) dialogs.SetAttachedObject(work.Previous, 0);
            if (unchecked((int)work.Next) > 0) dialogs.SetAttachedObject(work.Next, work.Reference);
        }
    }
    internal void Clear() { _registrations.Clear(); _groups.Clear(); _attachments.Clear(); }
}
