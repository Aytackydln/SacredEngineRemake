using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Installed original files + controlled TCP grants, not native-server
/// acceptance. No world data or native process inputs are sent to the installation.</summary>
[TestClass, TestCategory("Installed")]
public sealed class HostInitializationInstalledTests
{
    [TestMethod]
    public async Task GeneratedStartupRetainsItsAdmissionLifetimeUntilExplicitDisposal()
    {
        var installation = Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
        if (!Directory.Exists(installation)) Assert.Inconclusive("Installed Gold files needed; set SACRED_GAME_DIRECTORY.");
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0); peer.AllowLongPreparation();
        var owner = peer.HostSession!;
        await peer.SendAsync(new(0x11, 0, 0, 1, [1, 1, 0, 0, 0, 0]), Words(0x1e, [0x100]),
            Words(0x153, Enumerable.Range(17, 3000).Select(value => (uint)value).ToArray()), Words(0x154, [2269, 2000]));
        await owner.WaitForReservationsAsync(peer.Token);
        var state = await owner.InitializeAsync(installation, SacredCampaignFiles.Resolve(installation), peer.Token);
        Console.WriteLine($"Installed persistent startup: {state.Execution.ExecutedCommands} commands, complete {state.Execution.StartupCompleted}, phase {owner.Phase}, IDs {owner.References.Available}; failure {state.Execution.Failure?.Message}");
        Assert.AreSame(state, owner.Startup); Assert.IsTrue(owner.IsCurrent);
        Assert.IsTrue(owner.AdmissionReceipt!.IsCurrent); Assert.IsFalse(owner.AdmissionReceipt.DeliveryComplete);
        Assert.IsTrue(state.Execution.ExecutedCommands > 14, "Actual received grants must advance beyond the isolated grant guard.");
        Assert.IsTrue(owner.References.Available < 3000, "World producers must consume the actual owner pool.");
        if (state.Execution.StartupCompleted)
        {
            var sources = await owner.PrepareBankSourcesAsync(peer.Token);
            Assert.AreEqual(128, sources.Single(source => source.Name == "startup parameters").Bytes.Length);
            Assert.AreEqual(8600, sources.Single(source => source.Name == "regional replacement block").Bytes.Length);
            var questSource = sources.Single(source => source.Name == "script quest definitions");
            Assert.AreEqual(603u, questSource.Count); Assert.AreEqual(176076, questSource.Bytes.Length);
            Assert.IsNull(questSource.Blocker);
            var triggers = sources.Single(source => source.Name == "runtime triggers");
            Assert.AreEqual(state.Triggers.Count, triggers.Count);
            Assert.IsNotNull(triggers.Blocker, "The installed one-slot gap cannot become a fabricated bank row.");
            Assert.IsTrue(triggers.Bytes.IsEmpty);
            Assert.IsNotNull(sources.Single(source => source.Name == "region singleton final flags").Blocker);
            Assert.IsFalse(owner.AdmissionReceipt.DeliveryComplete, "Preparing private source bytes grants no admission credit.");
            var loadedSector = state.Objects.Creatures!.Prepared.Select(prepared => prepared.Actor!.Base.Sector)
                .First(sector => sector != 0 && state.Objects.Creatures.Terrain!.Tiles.IsLoaded(sector));
            var beforeCommands = state.Execution.ExecutedCommands;
            var beforeCreatures = state.Objects.Creatures.Prepared.Count;
            var beforeReferences = owner.References.Available;
            var regionResult = await owner.InitializeRegionAsync(installation, loadedSector, peer.Token);
            var region = owner.RegionInitialization!;
            Console.WriteLine($"Installed region: sector {loadedSector}, result {regionResult?.ToString() ?? "pending"}, flags {region.Publications.Count}, {region.Blocker}");
            Assert.AreEqual(1, region.Publications.Count, "Native initialization sets the private region flag before callback delivery.");
            if (loadedSector == 2217)
            {
                Assert.AreEqual(true, regionResult, "The installed Region21Init wrapper/body must finish.");
                Assert.AreEqual(beforeCommands + 17, state.Execution.ExecutedCommands);
                Assert.AreEqual(beforeCreatures + 3, state.Objects.Creatures.Prepared.Count);
                Assert.AreEqual(beforeReferences - 3, owner.References.Available);
                Assert.AreEqual(2, state.Execution.Quests!.ActivationPublications.Count);
                Assert.IsTrue(state.Execution.Quests.IsActive(2101)); Assert.IsTrue(state.Execution.Quests.IsActive(2102));
                Assert.AreEqual(1, region.Lifecycle.Records.Count);
                Assert.AreEqual(true, await owner.InitializeRegionAsync(installation, loadedSector, peer.Token));
                Assert.AreEqual(beforeCommands + 17, state.Execution.ExecutedCommands, "Already initialized region cannot replay callbacks.");
                Assert.AreEqual(beforeCreatures + 3, state.Objects.Creatures.Prepared.Count);
                Assert.AreEqual(beforeReferences - 3, owner.References.Available);
                Assert.AreEqual(2, state.Execution.Quests.ActivationPublications.Count);
                Assert.AreEqual(1, region.Publications.Count);
                Assert.IsFalse(state.Execution.IsExecutingStartup); Assert.IsFalse(state.Execution.IsInitializingRegion);
                var updatedQuestSource = (await owner.PrepareBankSourcesAsync(peer.Token)).Single(source => source.Name == "script quest definitions");
                Assert.IsFalse(questSource.Bytes.Span.SequenceEqual(updatedQuestSource.Bytes.Span), "Region quest activation must reach the existing bank source.");
                Assert.IsFalse(owner.AdmissionReceipt.DeliveryComplete);
            }
            if (regionResult is null)
            {
                var count = region.Publications.Count;
                Assert.IsNull(await owner.InitializeRegionAsync(installation, loadedSector, peer.Token));
                Assert.AreEqual(count, region.Publications.Count, "Retrying a pending callback must not repeat flag publication.");
                Assert.AreEqual(0, region.Lifecycle.Records.Count, "No completion record while a callback remains pending.");
            }
            Assert.IsTrue(owner.IsCurrent); Assert.IsFalse(state.Execution.IsInvalidated);
        }
        Assert.AreEqual(state.Execution.StartupCompleted ? WorldHostInitializationPhase.StartupComplete : WorldHostInitializationPhase.StartupBlocked, owner.Phase);
        var phase = owner.Phase; var invalidated = state.Execution.IsInvalidated;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.InitializeAsync(installation, SacredCampaignFiles.Resolve(installation), peer.Token));
        Assert.AreSame(state, owner.Startup);
        Assert.AreEqual(phase, owner.Phase); Assert.AreEqual(invalidated, state.Execution.IsInvalidated);
        await owner.DisposeAsync();
        Assert.IsTrue(state.Execution.IsInvalidated); Assert.IsFalse(owner.IsCurrent);
        Assert.IsNull(owner.AdmissionReceipt); Assert.AreEqual(0, owner.References.Available);
        Assert.IsNull(owner.RegionInitialization);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.PrepareBankSourcesAsync());
    }
    private static GoldMessage Words(ushort opcode, uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        return new(opcode, 0, 0, 1, bytes);
    }
}
