using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Production transport/lifetime integration. Native wire policy and
/// startup calculations have independent replay/capture evidence.</summary>
[TestClass, TestCategory("Packet")]
public sealed class HostInitializationSessionTests
{
    [TestMethod]
    public async Task EarlyAdmissionBehindTransportNoticesRetainsItsActualReceipt()
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await using var peer = await GoldLoopbackPeer.StartHostAsync();
            await peer.SendAsync(Admission(), Words(0x1e, 0x100), Words(0x153, 500, 501), Words(0x154, 2269, 2000));
            await peer.HostSession!.WaitForReservationsAsync(peer.Token);
            Assert.IsTrue(peer.HostSession.AdmissionReceipt!.IsCurrent);
            Assert.IsTrue(peer.HostSession.IsCurrent);
        }
    }

    private static GoldMessage Admission(byte flags = 1) => new(0x11, 0, 0, 1, [flags, 1, 0, 0, 0, 0]);
    private static GoldMessage Words(ushort opcode, params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        return new(opcode, 0, 0, 1, bytes);
    }
    private static Task Admit(GoldLoopbackPeer peer, byte flags = 1) => peer.SendAsync(Admission(flags), Words(0x1e, 0x100));

    [TestMethod, DataRow(false), DataRow(true)]
    public async Task RealTransportReservationsRemainOwnedUntilDisposal(bool triggerFirst)
    {
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0);
        var owner = peer.HostSession!;
        await Admit(peer);
        var sent = new List<GoldMessage>();
        while (!sent.Any(message => message.Opcode == 0x154)) sent.AddRange(await peer.ReadClientBatchAsync());
        var requests = sent.Where(message => message.Opcode is 0x153 or 0x154).ToArray();
        CollectionAssert.AreEqual(new ushort[] { 0x154 }, requests.Select(message => message.Opcode).ToArray());
        CollectionAssert.AreEqual(GoldReferenceCodec.CreateTriggerRangeRequest(2268, 2000, 1).Payload, requests[0].Payload);
        Assert.IsTrue(requests.All(message => message.Sender == 1 && message.Origin == 1));
        var ready = owner.WaitForReservationsAsync(peer.Token);
        var objects = Words(0x153, 500, 501); var triggers = Words(0x154, 2269, 2000);
        await peer.SendAsync(triggerFirst ? triggers : objects);
        Assert.IsFalse(ready.IsCompleted, "One grant kind cannot satisfy the two independent pools.");
        await peer.SendAsync(triggerFirst ? objects : triggers);
        await ready;
        Assert.IsTrue(owner.IsCurrent); Assert.IsFalse(owner.Completion.IsCompleted);
        var receipt = owner.AdmissionReceipt!;
        Assert.IsTrue(receipt.IsCurrent); Assert.IsFalse(receipt.DeliveryComplete);
        Assert.IsNull(receipt.Plan, "Wire role/grants cannot fabricate admission service completion.");
        Assert.AreEqual(WorldHostInitializationPhase.ReservationsAvailable, owner.Phase);
        Assert.AreEqual(1u, owner.Replicas.Session.Difficulty);
        Assert.IsTrue(owner.References.TryAllocate(out var reference)); Assert.AreEqual(500u, reference);
        Assert.AreEqual(2000u, owner.Replicas.TriggerReferences.Available);
        // A later received mutation proves the owner continues to consume events.
        await peer.SendAsync(Words(0x1e, 0x200), Words(0x153, 502, 503));
        while (owner.References.Issued < 4 && !owner.Completion.IsCompleted) await Task.Delay(5, peer.Token);
        Assert.AreEqual(2u, owner.Replicas.Session.Difficulty);
        await owner.DisposeAsync(); await owner.DisposeAsync();
        Assert.IsFalse(owner.IsCurrent); Assert.AreEqual(0, owner.References.Issued);
        Assert.IsFalse(receipt.IsCurrent); Assert.IsNull(owner.AdmissionReceipt);
        Assert.IsFalse(owner.Replicas.TriggerReferences.HasGrant);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.WaitForReservationsAsync());
    }

    [TestMethod, DataRow((byte)0), DataRow((byte)5)]
    public async Task ExistingWorldOrParticipantAssignmentCannotStartFirstHost(byte flags)
    {
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0);
        await Admit(peer, flags);
        await peer.HostSession!.Completion.WaitAsync(peer.Token);
        Assert.AreEqual(WorldHostInitializationPhase.Disconnected, peer.HostSession.Phase);
        StringAssert.Contains(peer.HostSession.Failure!, "empty server");
        await Assert.ThrowsExceptionAsync<IOException>(() => peer.HostSession.WaitForReservationsAsync());
    }

    [TestMethod]
    public async Task UnsolicitedObjectGrantIsRetainedWithoutAnotherObjectRequest()
    {
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0);
        var owner = peer.HostSession!;
        await peer.SendAsync(Admission(), Words(0x1e, 0x100), Words(0x153, 500, 501), Words(0x154, 2269, 2000));
        await owner.WaitForReservationsAsync(peer.Token);
        var sent = new List<GoldMessage>();
        while (!sent.Any(message => message.Opcode == 0x154)) sent.AddRange(await peer.ReadClientBatchAsync());
        Assert.IsFalse(sent.Any(message => message.Opcode == 0x153));
        Assert.AreEqual(2, owner.References.Available);
        owner.WorldSession.Clear();
        Assert.IsFalse(owner.IsCurrent, "Cleared admission ownership cannot remain a usable host lifetime.");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.WaitForReservationsAsync());
    }

    [TestMethod]
    public async Task RepeatedReservationInvalidatesAndClearsTheOwnedLifetime()
    {
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0);
        var owner = peer.HostSession!;
        await Admit(peer); await peer.SendAsync(Words(0x153, 500, 501), Words(0x154, 2269, 2000));
        await owner.WaitForReservationsAsync(peer.Token);
        await peer.SendAsync(Words(0x153, 501, 502));
        await owner.Completion.WaitAsync(peer.Token);
        StringAssert.Contains(owner.Failure!, "Repeated object reservation");
        Assert.AreEqual(0, owner.References.Issued); Assert.IsFalse(owner.IsCurrent);
    }

    [TestMethod]
    public async Task CancellationBeforeGrantsReleasesWaitersAndClosesTransport()
    {
        using var cancel = new CancellationTokenSource();
        await using var peer = await GoldLoopbackPeer.StartHostAsync(token: cancel.Token, seed: null);
        var owner = peer.HostSession!;
        var ready = owner.WaitForReservationsAsync();
        cancel.Cancel();
        await owner.Completion.WaitAsync(peer.Token);
        await Assert.ThrowsExceptionAsync<IOException>(() => ready);
        Assert.IsFalse(owner.IsCurrent); Assert.AreEqual(WorldHostInitializationPhase.Disconnected, owner.Phase);
    }

    [TestMethod]
    public async Task MissingSessionFactsFailBeforeReservationRequests()
    {
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source: null, connectionSetting: 0, seed: (_, _, _) => Task.CompletedTask);
        await peer.SendAsync(Admission());
        await peer.HostSession!.Completion.WaitAsync(peer.Token);
        StringAssert.Contains(peer.HostSession.Failure!, "session flags");
        Assert.AreEqual(0, peer.HostSession.References.Issued);
    }

    [TestMethod]
    public async Task CancellationInterruptsPendingDiscoveryWithoutWaitingForAGrant()
    {
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var peer = await GoldLoopbackPeer.StartHostAsync(null, 0, cancel.Token, async (_, _, token) =>
            { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        await peer.SendAsync(Admission()); await entered.Task.WaitAsync(peer.Token);
        cancel.Cancel(); await peer.HostSession!.Completion.WaitAsync(peer.Token);
        Assert.IsFalse(peer.HostSession.IsCurrent);
    }
}
