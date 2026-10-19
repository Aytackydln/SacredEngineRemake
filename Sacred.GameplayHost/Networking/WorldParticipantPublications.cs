using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

/// <summary>Ordered participant world sends. Required sector work waits for the
/// entire chain and observes transport failures before it can complete.</summary>
public sealed class WorldParticipantPublications(IGameplayNetworkTransport transport, GoldWorldReplica replicas)
{
    private Task _completion = Task.CompletedTask;
    private readonly long _session = replicas.SessionVersion;
    public int PublishedObjects { get; private set; }
    public bool IsComplete
    {
        get
        {
            RequireSession();
            if (!_completion.IsCompleted) return false;
            _completion.GetAwaiter().GetResult();
            return true;
        }
    }
    public void Send(GoldMessage message, bool applyLocally = false)
    {
        RequireSession();
        if (message.Sender != replicas.LocalPlayerSlot || message.Origin != replicas.LocalPlayerSlot)
            throw new InvalidOperationException("World publication requires this participant's identity.");
        var retained = message with { Payload = message.Payload.ToArray() };
        if (applyLocally) replicas.Apply(GoldGameEventDecoder.Decode(retained));
        _completion = SendAfter(_completion, retained);
        // A disconnect can retire this producer before the next update polls it.
        // Keep the original failure on the chain while observing it in that case too.
        _ = _completion.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (applyLocally) PublishedObjects++;
    }
    private async Task SendAfter(Task previous, GoldMessage message)
    {
        await previous.ConfigureAwait(false);
        if (_session != replicas.SessionVersion) throw new InvalidOperationException("World publication session ended.");
        await transport.SendGameplayAsync(message).ConfigureAwait(false);
    }
    private void RequireSession()
    {
        if (_session != replicas.SessionVersion || replicas.LocalPlayerSlot == 0 || !transport.IsParticipantWorldApproved)
            throw new InvalidOperationException("World publication belongs to a retired participant session.");
    }
}
