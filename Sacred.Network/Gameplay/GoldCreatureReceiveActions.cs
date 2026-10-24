using System.Buffers.Binary;
using Sacred.Core.Pak.Items;

namespace Sacred.Network.Gameplay;

/// <summary>Original virtual-call work, bound to the received object lifetime. Executing model/UI/script effects remains separate.</summary>
public sealed record GoldCreatureReceiveWork(GoldCreatureReceiveActionEvent Event, GoldReplicatedObject Object,
    byte? MarkerCategory = null)
{
    public ushort? MarkerKind => Event is GoldCreatureMarkerEvent ? (ushort)1 : null;
    public float MarkerScaleX => 1;
    public float MarkerScaleY => 1;
    public int MarkerOffsetX => -32;
    public int MarkerOffsetY => -32;
    /// <summary>The native busy-interpreter path queues command ten and the new type, in a 68-byte runtime record.</summary>
    public uint? DeferredScriptCommand => Event is GoldCreatureTypeChangeEvent ? 10u : null;
}

public sealed class GoldCreatureReceiveActions
{
    private readonly List<GoldCreatureReceiveWork> _requests = [];
    public GoldCreatureReceiveActions() => Requests = _requests.AsReadOnly();
    public IReadOnlyList<GoldCreatureReceiveWork> Requests { get; }
    public long MissingOrInactiveCount { get; private set; }
    public long TimedControlCount { get; private set; }
    internal bool Apply(GoldCreatureReceiveActionEvent incoming, Dictionary<uint, GoldReplicatedObject> objects,
        IReadOnlyDictionary<uint, SacredItemCategory> categories)
    {
        var entry = (GoldCreatureReceiveActionEvent)GoldReceiveActionCodec.Decode(incoming.Message);
        if (!objects.TryGetValue(entry.ObjectReference, out var obj) || obj.Creature is null ||
            (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
        { MissingOrInactiveCount++; return false; }
        byte? category = null;
        if (entry is GoldCreatureMarkerEvent)
        {
            var type = obj.BaseState.ObjectType;
            category = type is 0 or >= 0x7E60 ? (byte)0 : categories.TryGetValue(type, out var value) ? (byte)value : null;
        }
        _requests.Add(new(entry, obj, category));
        if (entry is GoldCreatureTimedControlEvent control)
        {
            var bytes = obj.Creature.ToPayload();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word24DOffset), control.ControlWord);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word259Offset), control.ClockWord);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), obj.Creature.Word200 | GoldReceiveActionCodec.TimedControlFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.MovementPathIndexOffset), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.MovementPathCountOffset), 0);
            // Receipt always enters the network branch: no local action-0x50 guard or outgoing 178 echo.
            // The conditional model action reset, omitted AI elapsed clock and linked-recipient/FX calls
            // are retained as work rather than manufacturing a successful animation or callback.
            objects[entry.ObjectReference] = obj with { Creature = GoldCreatureState.FromPayload(bytes), TimedControl = control,
                Movement = null, MovementVersion = obj.MovementVersion + 1 };
            TimedControlCount++;
        }
        return true;
    }
    internal void Clear() { _requests.Clear(); MissingOrInactiveCount = TimedControlCount = 0; }
}
