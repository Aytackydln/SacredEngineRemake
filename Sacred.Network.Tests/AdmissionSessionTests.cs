using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Core.Runtime;

namespace Sacred.Network.Tests;

/// <summary>Host delivery/lifetime contracts; native policy and service order
/// are compared separately by AdmissionComplete in NativeReplay.</summary>
[TestClass, TestCategory("Packet")]
public sealed class AdmissionSessionTests
{
    private static GoldAdmissionEvent Admission(byte flags = 5, byte slot = 16) =>
        (GoldAdmissionEvent)GoldGameEventDecoder.Decode(new(0x11, 0, 0, 42, [flags, slot, 0xef, 0xbe, 0xad, 0xde]));
    private static GoldPlayerRoster Previous() => GoldPlayerRoster.FromPayload(new byte[GoldPlayerRosterLayout.Size]);
    private static WorldAdmissionContinuationInputs Inputs(bool? multiplayer = true) =>
        new(false, true, false, false, false, 0,
            new(0x76543210, 7, 0, 0, 0, new byte[] { 72, 0, 0, 0 }, ReadOnlyMemory<byte>.Empty))
        { Multiplayer = multiplayer };
    private static WorldAdmissionPlanResult Prepare(SacredGameplayReplicaSession owner, WorldAdmissionReceipt receipt,
        WorldAdmissionContinuationInputs? inputs = null, Func<string, string>? identifier = null) =>
        owner.Admission.Prepare(receipt, new(1, 0, 31, 123, 31), Previous(), inputs ?? Inputs(), identifier ?? (name => name));
    private static (SacredGameplayReplicaSession Owner, WorldAdmissionReceipt Receipt) Start()
    {
        var owner = new SacredGameplayReplicaSession();
        owner.Apply(Admission());
        return (owner, owner.Admission.Current!);
    }

    [TestMethod]
    public void ReceiveOwnsWireAndPreparationCannotCreditServiceDelivery()
    {
        var owner = new SacredGameplayReplicaSession();
        var incoming = Admission();
        owner.Apply(incoming with { Flags = 0, PlayerSlot = 1, HostToken = 0 });
        var receipt = owner.Admission.Current!;
        Array.Clear(incoming.Message.Payload);
        Assert.AreEqual((byte)16, receipt.PlayerSlot);
        Assert.AreEqual((byte)16, owner.Replicas.Session.Teams.LocalPlayerSlot);
        Assert.IsNull(receipt.Plan);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        var revision = owner.Replicas.Revision;
        var plan = Prepare(owner, receipt);
        Assert.AreEqual(16u, plan.Prefix.State.LocalReference);
        Assert.AreEqual(0xdeadbeefu, plan.Prefix.State.HostToken);
        Assert.AreEqual(revision, owner.Replicas.Revision, "Preparation does not replay raw roster/session reducers.");
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.AreEqual(0, receipt.CompletedEffects);
        Assert.IsTrue(owner.Admission.TryPeek(receipt, out var head));
        Assert.IsInstanceOfType<WorldAdmissionEffect.ResetService>(head.Effect);
    }

    [DataTestMethod]
    [DataRow((byte)1, (byte)0, false)]
    [DataRow((byte)0, (byte)1, false)]
    [DataRow((byte)0, (byte)0, true)]
    public void InvalidReceiveCannotReplacePendingWork(byte sender, byte origin, bool truncated)
    {
        var (owner, receipt) = Start();
        var incoming = Admission();
        var message = incoming.Message with { Sender = sender, Origin = origin,
            Payload = truncated ? [1] : incoming.Message.Payload };
        var revision = owner.Replicas.Revision;
        Assert.ThrowsException<InvalidDataException>(() => owner.Apply(incoming with { Message = message }));
        Assert.AreSame(receipt, owner.Admission.Current);
        Assert.AreEqual(revision, owner.Replicas.Revision);
    }

    [TestMethod]
    public void UnknownContextStaysPendingAndPreparationCannotRunTwice()
    {
        var (owner, receipt) = Start();
        Assert.ThrowsException<NotSupportedException>(() => Prepare(owner, receipt, Inputs(null)));
        Assert.IsNull(receipt.Plan);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        Prepare(owner, receipt);
        Assert.ThrowsException<InvalidOperationException>(() => Prepare(owner, receipt));
        Assert.AreEqual(0, receipt.CompletedEffects);
    }

    [TestMethod]
    public void DeferredHeadIsStableAndConsumedEffectsCannotBeAcknowledgedTwice()
    {
        var (owner, receipt) = Start();
        var plan = Prepare(owner, receipt);
        owner.Admission.TryPeek(receipt, out var first);
        owner.Admission.Report(first, GoldConsumerOutcome.Deferred, "controller service unavailable");
        Assert.AreEqual(0, receipt.CompletedEffects);
        Assert.AreEqual(GoldConsumerOutcome.Deferred, receipt.LastReport!.Outcome);
        owner.Admission.TryPeek(receipt, out var retry);
        Assert.AreSame(first, retry);
        owner.Admission.Report(first, GoldConsumerOutcome.Consumed, "controller reset delivered");
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(first, GoldConsumerOutcome.Consumed, "duplicate"));
        while (owner.Admission.TryPeek(receipt, out var work))
            owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "test service delivered");
        Assert.AreEqual(receipt.EffectCount, receipt.CompletedEffects);
        Assert.IsTrue(receipt.DeliveryComplete);
        Assert.IsTrue(plan.RequiresActorBinding, "Delivery does not establish a registered hero or readiness.");
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
    }

    [TestMethod]
    public void RejectedDeliveryStopsAtItsHeadAndCannotBeReprepared()
    {
        var (owner, receipt) = Start();
        Prepare(owner, receipt);
        owner.Admission.TryPeek(receipt, out var work);
        owner.Admission.Report(work, GoldConsumerOutcome.Rejected, "service failed");
        Assert.IsTrue(receipt.DeliveryRejected);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "late success"));
        Assert.ThrowsException<InvalidOperationException>(() => Prepare(owner, receipt));
    }

    [TestMethod]
    public void ForeignWorkAndInvalidOutcomeCannotAdvanceDelivery()
    {
        var (owner, receipt) = Start();
        var (other, otherReceipt) = Start();
        Prepare(owner, receipt);
        Prepare(other, otherReceipt);
        owner.Admission.TryPeek(receipt, out var work);
        Assert.ThrowsException<InvalidOperationException>(() => other.Admission.Report(work, GoldConsumerOutcome.Consumed, "foreign"));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => owner.Admission.Report(work, (GoldConsumerOutcome)99, "invalid"));
        Assert.AreEqual(0, receipt.CompletedEffects);
        Assert.AreEqual(0, otherReceipt.CompletedEffects);
    }

    [DataTestMethod]
    [DataRow("owner-clear")]
    [DataRow("replica-clear")]
    [DataRow("replacement")]
    public void LifetimeChangeInvalidatesPreparationAndOutstandingAcknowledgements(string change)
    {
        var (owner, receipt) = Start();
        Prepare(owner, receipt);
        owner.Admission.TryPeek(receipt, out var work);
        switch (change)
        {
            case "owner-clear": owner.Clear(); break;
            case "replica-clear": owner.Replicas.Clear(); break;
            case "replacement": owner.Apply(Admission(slot: 1)); break;
        }
        Assert.IsFalse(receipt.IsCurrent);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "stale"));
        Assert.ThrowsException<InvalidOperationException>(() => Prepare(owner, receipt));
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.TryPeek(receipt, out _));
        if (change == "replacement") Assert.AreEqual((byte)1, owner.Admission.Current!.PlayerSlot);
        else Assert.IsNull(owner.Admission.Current);
    }

    [DataTestMethod]
    [DataRow((ushort)0x002)]
    [DataRow((ushort)0x008)]
    [DataRow((ushort)0x037)]
    [DataRow((ushort)0x039)]
    [DataRow((ushort)0x03f)]
    [DataRow((ushort)0x157)]
    public void TerminalNativeNoticesInvalidatePendingWork(ushort opcode)
    {
        var (owner, receipt) = Start();
        var payload = opcode == 0x03f ? new byte[8] : opcode is 0x002 or 0x157 ? new byte[4] : [];
        owner.Apply(GoldGameEventDecoder.Decode(new(opcode, 0, 0, 0, payload)));
        Assert.IsFalse(receipt.IsCurrent);
        Assert.IsNull(owner.Admission.Current);
    }

    [TestMethod]
    public void PeerKickAndOrdinaryRosterUpdatesDoNotEndAdmissionLifetime()
    {
        var (owner, receipt) = Start();
        owner.Apply(GoldGameEventDecoder.Decode(new(0x157, 3, 0, 0, new byte[4])));
        owner.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(3, "Remote")));
        Assert.AreSame(receipt, owner.Admission.Current);
        Assert.AreEqual("Remote", owner.Replicas.Session.Lobby.Control.Roster.Find(3)!.Name);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IdentifierReentryCannotPublishOldPreparation(bool replace)
    {
        var (owner, receipt) = Start();
        var calls = 0;
        Assert.ThrowsException<InvalidOperationException>(() => Prepare(owner, receipt, identifier: name =>
        {
            calls++;
            if (receipt.IsCurrent)
            {
                if (replace) owner.Apply(Admission(slot: 1));
                else owner.Clear();
            }
            return name;
        }));
        Assert.AreEqual(1, calls, "Stop calling supplied services immediately after invalidation.");
        Assert.IsNull(receipt.Plan);
        Assert.AreEqual(0, receipt.EffectCount);
        Assert.IsFalse(receipt.IsCurrent);
        if (replace)
        {
            Assert.AreEqual((byte)1, owner.Admission.Current!.PlayerSlot);
            Assert.IsNotNull(Prepare(owner, owner.Admission.Current));
        }
    }

    [TestMethod]
    public void NestedPreparationCannotPublishOrAcknowledgeIncompleteWork()
    {
        var (owner, receipt) = Start();
        var calls = 0;
        Prepare(owner, receipt, identifier: name =>
        {
            calls++;
            Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
            Assert.ThrowsException<InvalidOperationException>(() => Prepare(owner, receipt));
            return name;
        });
        Assert.AreEqual(16, calls);
        Assert.IsNotNull(receipt.Plan);
        Assert.IsFalse(receipt.DeliveryComplete);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SuccessfulFinalizerCannotAcknowledgeWorkAfterPumpReentry(bool replace)
    {
        var (owner, receipt) = Start();
        Prepare(owner, receipt);
        WorldAdmissionWork finalizer;
        while (true)
        {
            Assert.IsTrue(owner.Admission.TryPeek(receipt, out finalizer!));
            if (finalizer.Effect is WorldAdmissionEffect.FinalizeConnection) break;
            owner.Admission.Report(finalizer, GoldConsumerOutcome.Consumed, "supplied fixture service");
        }
        var backend = new object();
        var services = new WorldConnectionBackendPumpServices<object>(() => backend,
            _ => { if (replace) owner.Apply(Admission(slot: 1)); else owner.Clear(); },
            _ => { }, _ => { }, _ => 0, (_, _) => throw new Exception("empty vector"),
            _ => throw new Exception("empty vector must not clear"), () => { }, () => { },
            _ => throw new Exception("empty vector must not append"), _ => true);
        var pending = new List<int> { 1, 2 };
        var reset = false;
        var result = WorldAdmissionFinalizer.Reset(true, pending, () => reset = true, _ => { },
            () => WorldConnectionBackendPump.PumpAndQuery(services));
        Assert.IsTrue(result, "A successful backend query does not restore the old session lifetime.");
        Assert.IsTrue(reset);
        Assert.AreEqual(0, pending.Count);
        Assert.IsFalse(receipt.IsCurrent);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(finalizer, GoldConsumerOutcome.Consumed, "stale success"));
        if (replace) Assert.AreEqual((byte)1, owner.Admission.Current!.PlayerSlot);
    }

    [TestMethod]
    public void MissingExecutableRulesLeaveReceiptPending()
    {
        var (owner, receipt) = Start();
        Assert.ThrowsException<NotSupportedException>(() => owner.Admission.Prepare(receipt, new(1, 0, 0, 0, 0), Previous(), Inputs()));
        Assert.IsNull(receipt.Plan);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.ConfigureIdentifier(new([])));
    }

    [TestMethod]
    public void ConfiguredRulesAreUsedAndSurviveSessionReset()
    {
        var owner = new SacredGameplayReplicaSession();
        owner.Admission.ConfigureIdentifier(new([new("H", 1)]));
        for (var session = 0; session < 2; session++)
        {
            owner.Apply(Admission());
            var receipt = owner.Admission.Current!;
            var plan = owner.Admission.Prepare(receipt, new(1, 0, 0, 0, 0), Previous(), Inputs());
            var row = plan.AppliedRoster!.Rows.Single(value => value.Slot == 16);
            Assert.AreEqual("H", row.Name);
            Assert.AreEqual("", row.Identifier);
            Assert.IsFalse(receipt.DeliveryComplete);
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.ConfigureIdentifier(new([])));
            owner.Clear();
        }
    }
}
