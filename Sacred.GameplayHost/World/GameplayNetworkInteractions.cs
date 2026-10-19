using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.GameplayHost.World;

/// <summary>One ordered interaction send and its update-thread local completion; shared by graphical/headless clients.</summary>
internal sealed class GameplayNetworkInteractions(SacredNetworkWorldBinding world, IGameplayNetworkTransport? transport,
    Action<string> log) : IDisposable
{
    private Task? _send;
    private GoldGameEvent? _pending;
    private GoldReplicatedObject? _source;
    public string Status { get; private set; } = "ready";

    public bool Request(GameplayWorldObject target, GameplayInteractionContext context)
    {
        Update();
        if (!world.IsCurrentSession || _send is not null || !context.InReach) return false;
        try
        {
            if (transport is not { IsParticipantWorldApproved: true } || transport.PlayerSlot != world.PlayerSlot)
                throw new InvalidOperationException("Interaction requires the current approved participant connection.");
            if (!world.Replicas.Objects.TryGetValue(target.Reference, out var current) || current.InstanceVersion != target.InstanceVersion ||
                current.BaseState.ObjectType != target.Placement.TypeId || current.Physical is null)
                throw new InvalidOperationException("Interaction target changed before the request.");
            var slot = world.PlayerSlot;
            GoldGameEvent pending;
            if (target.Drop is { IsPickable: true })
            {
                var hero = world.LocalHero;
                if (hero.BaseState.Owner != slot || (hero.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 ||
                    current.Item?.InventoryId is not 0 || !world.Replicas.Sectors.ContainsKey(current.BaseState.Sector) ||
                    (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 || hero.BaseState.Surface != current.BaseState.Surface)
                    throw new InvalidOperationException("Pickup requires an active ground item on the participant's surface.");
                pending = GoldItemPickupCodec.Decode(GoldItemPickupCodec.Create(slot, target.Reference,
                    hero.FacingBits ?? hero.BaseState.Word28));
            }
            else if (target.Definition.ModelDesc.Category == SacredItemCategory.Container)
                pending = GoldContainerInteractionRequest.Create(world.Replicas, slot, target.Reference);
            else if (target.Definition.ModelDesc.Category == SacredItemCategory.Door)
            {
                var hero = world.LocalHero.BaseState;
                if (hero.Owner != slot || (hero.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
                    throw new InvalidOperationException("Interaction requires the active owned participant hero.");
                var basis = current.BaseState;
                if ((basis.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0 || !world.Replicas.Sectors.ContainsKey(basis.Sector) || hero.Surface != basis.Surface)
                    throw new InvalidOperationException("Door must be active on confirmed terrain on the participant's surface.");
                if (!world.Replicas.WorldTriggers.Records.TryGetValue(basis.TriggerReference, out var trigger) || (trigger.State & 0xc000) != 0)
                    throw new InvalidOperationException("Door has an unavailable or locked trigger.");
                pending = GoldDoorCodec.Decode(GoldDoorCodec.Create(target.Reference, (trigger.State & 1) == 0, slot, slot));
            }
            else throw new InvalidOperationException($"{target.Definition.ModelDesc.Category} interaction is not implemented.");
            _source = world.Replicas.Objects[target.Reference];
            _send = transport.SendGameplayAsync(pending.Message);
            _pending = pending;
            var action = pending is GoldItemPickupEvent ? "pick up" : pending is GoldDoorEvent door ? door.Open ? "open" : "close" : "activate container";
            Status = $"sending {action} for object {target.Reference}";
            log($"Network interaction send: participant {slot}, object {target.Reference}, {action}, trigger {_source.BaseState.TriggerReference}.");
        }
        catch (Exception error) { _pending = null; _source = null; Status = error.Message; log("Network interaction: " + Status); }
        return true;
    }

    public void Update()
    {
        if (_send is not { IsCompleted: true } completed) return;
        var pending = _pending; var source = _source;
        _send = null; _pending = null; _source = null;
        try
        {
            completed.GetAwaiter().GetResult();
            if (pending is GoldItemPickupEvent pickup)
            {
                Status = $"pickup request sent for object {pickup.ObjectReference}; waiting for authoritative item/inventory updates";
                log("Network interaction: " + Status);
                return;
            }
            if (!world.IsCurrentSession || source is null || pending is null ||
                !world.Replicas.Objects.TryGetValue(source.BaseState.ObjectReference, out var current) ||
                current.InstanceVersion != source.InstanceVersion || current.PositionVersion != source.PositionVersion ||
                !ReferenceEquals(current.Physical, source.Physical) || (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
            { Status = "send completed; target changed before local activation"; return; }
            if (pending is GoldContainerActivationEvent && (current.BaseState.Flags & GoldContainerActivationCodec.ActivatedFlag) != 0) return;
            world.Replicas.Apply(pending);
            Status = $"sent object {current.BaseState.ObjectReference}; local gameplay activation applied";
            log("Network interaction: " + Status);
        }
        catch (Exception error) { Status = "send failed: " + error.Message; log("Network interaction " + Status); }
    }
    public void Dispose()
    {
        if (_send is { } send) _ = send.ContinueWith(task => { _ = task.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _send = null; _pending = null; _source = null;
    }
}
