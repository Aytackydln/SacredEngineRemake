using System;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Completes local activation on the update thread after an actual participant send.</summary>
internal sealed class NetworkDoorInteractionPublisher(GameServerClientController client, SacredNetworkWorldBinding world) : IDisposable
{
    private Task? _send;
    private GoldDoorEvent? _pending;
    private long _positionVersion;
    private GoldPhysicalState? _sourcePhysical;
    public string Status { get; private set; } = "ready";
    public bool Request(uint reference)
    {
        Update();
        if (!world.IsCurrentSession || _send is not null) return true;
        try
        {
            if (!world.Replicas.Objects.TryGetValue(reference, out var target) ||
                !world.Replicas.WorldTriggers.Records.TryGetValue(target.BaseState.TriggerReference, out var trigger))
                throw new InvalidOperationException("The target has no received trigger state.");
            var open = (trigger.State & 1) == 0;
            _send = client.SendDoorInteractionAsync(reference, open);
            _pending = GoldDoorCodec.Decode(GoldDoorCodec.Create(reference, open, world.PlayerSlot, world.PlayerSlot));
            _positionVersion = target.PositionVersion;
            _sourcePhysical = target.Physical;
            Status = $"sending {(open ? "open" : "close")} for object {reference}";
        }
        catch (Exception error) { Status = error.Message; EngineLog.WriteLine("Network interaction: " + Status); }
        return true;
    }
    public void Update()
    {
        if (_send is not { IsCompleted: true } completed) return;
        var pending = _pending;
        _send = null; _pending = null;
        try
        {
            completed.GetAwaiter().GetResult();
            if (!world.IsCurrentSession || pending is null ||
                !world.Replicas.Objects.TryGetValue(pending.ObjectReference, out var current) || current.PositionVersion != _positionVersion ||
                !ReferenceEquals(current.Physical, _sourcePhysical) || (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) return;
            world.Replicas.Apply(pending);
            Status = $"sent object {pending.ObjectReference}; applied {world.Replicas.DoorInteractions.AppliedCount} door activations";
            EngineLog.WriteLine("Network interaction: " + Status);
        }
        catch (Exception error) { Status = "send failed: " + error.Message; EngineLog.WriteLine("Network interaction " + Status); }
    }
    public void Dispose()
    {
        if (_send is { } send) _ = send.ContinueWith(task => { _ = task.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _send = null; _pending = null;
    }
}
