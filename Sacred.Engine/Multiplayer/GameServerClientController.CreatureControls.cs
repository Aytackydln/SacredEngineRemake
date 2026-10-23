using Sacred.GameplayHost.World;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    private WorldCreatureControlCallbacks? _creatureControls;

    private void AdvanceCreatureControlCallbacks()
    {
        if (_creatureControls is null && WorldReplicas.CreatureControls.PendingReplies == 0) return;
        _creatureControls ??= new(WorldReplicas, this, EngineLog.WriteLine);
        _creatureControls.Advance();
    }
}
