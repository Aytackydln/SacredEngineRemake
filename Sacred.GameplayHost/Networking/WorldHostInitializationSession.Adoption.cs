using System.Net;
using Sacred.Assets.World;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

public sealed partial class WorldHostInitializationSession
{
    private Action<IReadOnlyList<GoldMessage>>? _observeHostBatch;
    private GoldMessagesReceived? _consumedHostMessages;

    /// <summary>Take the event reader from the graphical join controller after first-host assignment.</summary>
    public static WorldHostInitializationSession AdoptFirstHost(WorldHostSavedCharacterSource source,
        GoldGameConnection connection, GoldFirstContact contact, IReadOnlyList<GoldMessage> consumed,
        Action<IReadOnlyList<GoldMessage>> observeBatch, Action<string> log)
    {
        if (!source.Records.Profile.SequenceEqual(contact.Profile) ||
            connection.Admission is not { Phase: GoldAdmissionPhase.SlotAssigned, WorldStartup.AssignedClientHost: true })
            throw new InvalidOperationException("Host handoff requires the assigned connection and its selected character.");
        var installation = source.Installation;
        var bound = SacredWorldTriggerArchive.Load(Path.Combine(installation, "World", "Triggers.PAK")).Header.TriggerCount;
        Task Seed(GoldSessionState state, IPEndPoint remote, CancellationToken token) =>
            WorldHostStartupDiscovery.SeedAsync(installation, remote, contact.Version, state, log, token);
        var owner = new WorldHostInitializationSession(connection, bound, Seed, log, default)
        {
            SelectedCharacter = source,
            StartupRequest = WorldHostStartupRequest.FreshWorld,
            NetworkSpeedSettings = contact.ConnectionSpeed,
            _observeHostBatch = observeBatch,
            _consumedHostMessages = new(consumed.ToArray())
        };
        try
        {
            owner.Completion = owner.ReceiveAsync();
            return owner;
        }
        catch { owner._stop.Dispose(); throw; }
    }

    private async IAsyncEnumerable<GoldConnectionEvent> ReadHostEventsAsync()
    {
        if (_consumedHostMessages is { } consumed) yield return consumed;
        await foreach (var entry in _connection.Events.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            yield return entry;
    }

    private void ApplyHostMessages(IReadOnlyList<GoldMessage> messages)
    {
        GoldGameDispatcher.Dispatch(messages, message =>
        {
            WorldSession.Apply(message);
            if (message is GoldAdmissionEvent) _admissionObserved = true;
            if (message is GoldReferenceBatchEvent batch)
            {
                References.Apply(batch.Batch);
                _log($"Host initialization: received {batch.Batch.Count} object IDs; issued {References.Issued}, available {References.Available}.");
            }
        });
    }
}
