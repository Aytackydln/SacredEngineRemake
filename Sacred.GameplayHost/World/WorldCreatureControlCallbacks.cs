using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Ordered state publications from dialog setters and owner clocks. Receipts retain their state even if
/// subsequent packets change it before the transport's send gate becomes available.</summary>
public sealed class WorldCreatureControlCallbacks(GoldWorldReplica world, IGameplayNetworkTransport transport, Action<string> log)
{
    private readonly List<Task> _retired = [];
    private Task? _sending;
    private long _session = -1;
    private bool _failed;

    public void Advance()
    {
        if (_session != world.SessionVersion)
        {
            if (_sending is { } old) _retired.Add(old);
            _sending = null; _failed = false; _session = world.SessionVersion;
        }
        for (var index = _retired.Count - 1; index >= 0; index--)
        {
            if (!_retired[index].IsCompleted) continue;
            try { _retired[index].GetAwaiter().GetResult(); }
            catch (Exception error) { log($"Network creature state: retired callback failed: {error.Message}"); }
            _retired.RemoveAt(index);
        }
        if (_sending is { IsCompleted: false }) return;
        if (!ObserveSend()) { world.CreatureControls.DiscardReplies(); return; }
        if (!transport.IsParticipantWorldApproved || transport.PlayerSlot == 0)
        {
            // Preserve callbacks received between upload completion and approval.
            // Disconnect clears the replica queue; no readiness state is fabricated here.
            return;
        }
        if (world.LocalPlayerSlot != transport.PlayerSlot) return;
        while (world.CreatureControls.TakeReply() is { } reply)
        {
            if (reply.Owner != transport.PlayerSlot || !world.Objects.TryGetValue(reply.ObjectReference, out var actor) ||
                actor.InstanceVersion != reply.InstanceVersion || actor.BaseState.Owner != transport.PlayerSlot) continue;
            try
            {
                _sending = transport.SendGameplayAsync(GoldCreatureUpdateCodec.CreateState(transport.PlayerSlot,
                    reply.ObjectReference, reply.ObjectType, reply.State));
                log($"Network creature state: owned object {reply.ObjectReference}, dialog {unchecked((int)reply.State.Word245)}; publishing {reply.Reason} callback 0x11A.");
            }
            catch (Exception error) { Failed(error); return; }
            if (!_sending.IsCompleted || !ObserveSend()) return;
        }
    }

    private bool ObserveSend()
    {
        if (_sending is null) return !_failed;
        var sending = _sending; _sending = null;
        try { sending.GetAwaiter().GetResult(); return true; }
        catch (Exception error) { Failed(error); return false; }
    }

    private void Failed(Exception error)
    {
        _failed = true;
        world.CreatureControls.DiscardReplies();
        log($"Network creature state: callback failed; reconnect required: {error.Message}");
    }
}
