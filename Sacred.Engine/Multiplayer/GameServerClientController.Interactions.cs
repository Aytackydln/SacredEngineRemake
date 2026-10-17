using System;
using System.Threading.Tasks;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    public Task SendDoorInteractionAsync(uint reference, bool open)
    {
        if (!IsParticipantWorldApproved || _connection is null ||
            !WorldReplicas.Objects.TryGetValue(PlayerSlot, out var hero) || hero.BaseState.Owner != PlayerSlot ||
            (hero.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
            throw new InvalidOperationException("Interaction requires the active owned participant hero.");
        if (!WorldReplicas.Objects.TryGetValue(reference, out var target) || target.Physical is null ||
            (target.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
            !WorldReplicas.Sectors.ContainsKey(target.BaseState.Sector) || hero.BaseState.Surface != target.BaseState.Surface)
            throw new InvalidOperationException("Interaction target is not an active physical object on confirmed terrain.");
        if (!WorldReplicas.WorldTriggers.Records.TryGetValue(target.BaseState.TriggerReference, out var trigger) ||
            (trigger.State & 0xc000) != 0)
            throw new InvalidOperationException("Interaction target has an unavailable or locked trigger.");
        EngineLog.WriteLine($"Network interaction send: participant {PlayerSlot}, object {reference}, {(open ? "open" : "close")}, trigger {target.BaseState.TriggerReference}.");
        return _connection.SendAsync(GoldDoorCodec.Create(reference, open, PlayerSlot, PlayerSlot)).AsTask();
    }
}
