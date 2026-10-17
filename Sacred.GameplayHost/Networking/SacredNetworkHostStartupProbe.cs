using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pax.Hero;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;
/// <summary>Isolated first-client diagnostic shared by graphical and terminal consumers.
/// Connects, obtains real reservations, executes available startup effects, then disconnects.
/// It never uploads a partial world or sends host finish.</summary>
public static class SacredNetworkHostStartupProbe
{
    // Both original first-host captures request 2,000 IDs using the authored trigger-table bound.
    private const uint InitialTriggerGrantCount = 2000;
    public static Task<SacredNetworkHostStartupProbeResult> RunSavedCharacterAsync(string installation,
        SacredCampaignFiles campaign, int selection, string host, int port, Action<string> log, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hero = SacredHeroPaxProfile.ReadSelected(installation, selection);
        var contact = GoldFirstContact.Create(hero.Profile, new(hero.Name, checked((byte)selection), 1, 1, 1, 0), GoldLanCompatibility.Read(installation));
        return RunAsync(installation, campaign, contact, host, port, log, cancellationToken);
    }

    public static async Task<SacredNetworkHostStartupProbeResult> RunAsync(string installation,
        SacredCampaignFiles campaign, GoldFirstContact contact, string host, int port, Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(contact);
        cancellationToken.ThrowIfCancellationRequested();
        var source = SacredWorldTriggerArchive.Load(Path.Combine(installation, "World", "Triggers.PAK"));
        var replicas = new GoldWorldReplica();
        var references = new GoldObjectReferencePool();
        await using var connection = new GoldGameConnection();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        var requested = false;
        log($"Host startup probe: connecting to {host}:{port}, script set {campaign.Name}.");
        cancellationToken.ThrowIfCancellationRequested();
        connection.Start(host, port, contact);
        try
        {
            await foreach (var entry in connection.Events.ReadAllAsync(limit.Token).ConfigureAwait(false))
            {
                if (entry is GoldConnectionClosed closed) throw new IOException($"Host startup probe closed: {closed.Reason}");
                if (entry is GoldMessagesReceived received)
                    GoldGameDispatcher.Dispatch(received.Messages, message =>
                    {
                        replicas.Apply(message);
                        if (message is GoldReferenceBatchEvent batch)
                        {
                            references.Apply(batch.Batch);
                            log($"Host startup probe: received {batch.Batch.Count} object IDs; total {references.Issued}.");
                        }
                    });
                var admission = connection.Admission;
                if (admission.Phase == GoldAdmissionPhase.Rejected)
                    throw new InvalidDataException($"Host startup probe rejected: reason {admission.RejectionReason}, mismatch 0x{admission.DataMismatchMask:X8}.");
                if (admission.Phase != GoldAdmissionPhase.SlotAssigned) continue;
                if (!admission.WorldStartup.AssignedClientHost || admission.WorldStartup.HostWorldAvailable || admission.WorldStartup.ParticipantStartApproved)
                    throw new InvalidOperationException("Host startup probe requires a first-client host assignment on an empty server.");
                if (!requested)
                {
                    log($"Host startup probe: slot {admission.PlayerSlot}, flags 0x{admission.Flags:X2}; requesting separate trigger/object IDs.");
                    await connection.SendAsync(GoldReferenceCodec.CreateTriggerRangeRequest(source.Header.TriggerCount, InitialTriggerGrantCount, admission.PlayerSlot), limit.Token).ConfigureAwait(false);
                    await connection.SendAsync(GoldReferenceCodec.CreateRequest(64, admission.PlayerSlot), limit.Token).ConfigureAwait(false);
                    requested = true;
                }
                if (!replicas.TriggerReferences.HasGrant || references.Available == 0) continue;
                var sectors = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx")).CreateGrid();
                var triggers = new WorldScriptTriggerRegistry(source, replicas.TriggerReferences, sectors);
                var templates = SacredObjectTemplateArchive.Load(installation);
                var objects = new WorldScriptObjectWorld(templates, references,
                    sectors, triggers, admission.PlayerSlot, 0, hasActiveHero: false);
                var scripts = WorldCampaignScripts.Load(campaign);
                var state = WorldScriptStartupInspection.Run(scripts, log, limit.Token, triggers, objects);
                var pendingCreature = WorldScriptStartupDependencies.InspectCreature(installation, scripts, state, templates, log);
                // The returned state is diagnostic only; its reservations expire on disconnect.
                state.IsInvalidated = true;
                foreach (var obj in objects.Objects)
                {
                    var row = obj.BaseState;
                    log($"Host startup object: ref {row.ObjectReference}, type {row.ObjectType}, sector {row.Sector}, position {row.X},{row.Y},{row.Surface}, trigger {row.TriggerReference}.");
                }
                return new(admission.PlayerSlot, references.Issued, references.Available, state, triggers, objects)
                    { PendingCreature = pendingCreature };
            }
            throw new IOException("Host startup connection ended before reservation preparation completed.");
        }
        finally
        {
            connection.Stop();
            await connection.Completion.ConfigureAwait(false);
            replicas.Clear(); references.Clear();
            log("Host startup probe: disconnected and cleared reservation/session state.");
        }
    }
}
