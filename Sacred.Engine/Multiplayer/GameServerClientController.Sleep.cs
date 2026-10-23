using Sacred.GameplayHost.World;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    private WorldOwnedCreatureSleep? _ownedSleep;

    public void AdvanceGameplay(float deltaSeconds)
    {
        AdvanceCreatureControlCallbacks();
        _ownedSleep ??= new(WorldReplicas, this, EngineLog.WriteLine,
            (actor, state) => WorldReplicas.CreatureControls.QueueOwnedState(actor, state, "sleep wake"));
        _ownedSleep.Advance(deltaSeconds);
        AdvanceCreatureControlCallbacks();
    }
}
