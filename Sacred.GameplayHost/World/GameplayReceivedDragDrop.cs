using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

public sealed record GameplayDragDropObject(ushort Kind, ushort Category, uint Reference, uint Slot);

/// <summary>Gold 13D's kind-1 descriptor has slot/count zero. cMouse::setDragDropObject (65565A)
/// frees that descriptor and clears the current drag object; it does not create an overhead icon.</summary>
public sealed class GameplayReceivedDragDrop
{
    private readonly GoldWorldReplica _world;
    private readonly GoldConsumerCursor _cursor;
    public GameplayReceivedDragDrop(GoldWorldReplica world)
    {
        _world = world;
        _cursor = world.Consumers.CreateCursor("drag-drop", work => work.Value is GoldCreatureReceiveWork { Event: GoldCreatureMarkerEvent });
    }
    public GameplayDragDropObject? Current { get; set; }
    public long Cleared { get; private set; }
    public void Update()
    {
        if (_cursor.Session != _world.Consumers.Session) { Current = null; Cleared = 0; }
        while (_world.Consumers.TryPeek(_cursor, out var work))
        {
            Current = null; Cleared++;
            _world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "native kind-1 zero-slot drag object cleared");
        }
    }
}
