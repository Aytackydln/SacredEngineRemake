using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pax.Hero;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Admission;
using Sacred.Network.Connection;
using Sacred.Network.Gameplay;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;
/// <summary>Isolated first-client diagnostic shared by graphical and terminal consumers.
/// Connects, obtains real reservations, executes available startup effects, then disconnects.
/// It never uploads a partial world or sends host finish.</summary>
public static class SacredNetworkHostStartupProbe
{
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
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        await using var session = WorldHostInitializationSession.Start(new(), host, port, contact, source.Header.TriggerCount,
            (state, endpoint, token) => WorldHostStartupDiscovery.SeedAsync(installation, endpoint, contact.Version, state, log, token), log, limit.Token);
        var startup = await session.InitializeAsync(installation, campaign, limit.Token).ConfigureAwait(false);
        var result = new SacredNetworkHostStartupProbeResult(session.Admission.PlayerSlot, session.References.Issued,
            session.References.Available, startup.Execution, startup.Triggers, startup.Objects)
            { PendingCreature = startup.PendingCreature, CreaturePreparation = startup.Objects.Creatures!.Pending };
        // The diagnostic intentionally disposes the persistent owner on return.
        // Its execution is invalidated; a production caller retains the session.
        log("Host startup probe: disconnecting diagnostic session; reservations and generated state expire.");
        return result;
    }
}
