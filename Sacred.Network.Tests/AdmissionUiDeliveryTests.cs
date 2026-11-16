using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class AdmissionUiDeliveryTests
{
    private static (SacredGameplayReplicaSession Owner, WorldAdmissionReceipt Receipt, WorldAdmissionWork Work) Start()
    {
        var owner = new SacredGameplayReplicaSession();
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 1, [1, 2, 0, 0, 0, 0])));
        var receipt = owner.Admission.Current!;
        owner.Admission.Prepare(receipt, new(0, 0, 0, 0, 0), GoldPlayerRoster.FromPayload(new byte[GoldPlayerRosterLayout.Size]),
            new(false, true, false, false, false, 0, new(0, 0, 0, 0, 0, new byte[] { 72, 0 }, ReadOnlyMemory<byte>.Empty))
            { Multiplayer = true }, name => name);
        while (owner.Admission.TryPeek(receipt, out var work))
        {
            if (work.Effect is WorldAdmissionEffect.Ui) return (owner, receipt, work);
            owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "supplied fixture service");
        }
        throw new Exception("fixture has no UI effect");
    }

    [TestMethod]
    public void NativeUiServiceIsSynchronousAndAcknowledgedAfterListenerReturns()
    {
        var (owner, receipt, work) = Start();
        var completed = receipt.CompletedEffects;
        GoldLobbyUiRequest? delivered = null;
        WorldAdmissionUiDispatcher? dispatcher = null;
        dispatcher = new(() => 1, _ => request =>
        {
            Assert.AreEqual(completed, receipt.CompletedEffects);
            delivered = request;
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "too early"));
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverUi(work, dispatcher!));
        });
        owner.Admission.DeliverUi(work, dispatcher);
        Assert.AreSame(((WorldAdmissionEffect.Ui)work.Effect).Request, delivered);
        Assert.AreEqual(completed + 1, receipt.CompletedEffects);
        Assert.AreEqual(1u, dispatcher.DirectCalls);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverUi(work, dispatcher));
        Assert.IsFalse(receipt.DeliveryComplete, "Later effects are still pending.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeEmptyOrNullListenerCompletesTheServiceWithoutInventingAnActor(bool hasSlot)
    {
        var (owner, receipt, work) = Start();
        var before = receipt.CompletedEffects;
        var dispatcher = new WorldAdmissionUiDispatcher(() => hasSlot ? 1 : 0, _ =>
        {
            if (!hasSlot) throw new Exception("Empty native listener vector must not be read.");
            return null;
        });
        owner.Admission.DeliverUi(work, dispatcher);
        Assert.AreEqual(before + 1, receipt.CompletedEffects);
        Assert.AreEqual(1u, dispatcher.DirectCalls);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        Assert.IsTrue(receipt.Plan!.RequiresActorBinding);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ListenerReentryInvalidatesOldCompletion(bool replace)
    {
        var (owner, receipt, work) = Start();
        var completed = receipt.CompletedEffects;
        var dispatcher = new WorldAdmissionUiDispatcher(() => 1, _ => _ =>
        {
            if (replace) owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 2, [1, 3, 0, 0, 0, 0])));
            else owner.Clear();
        });
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverUi(work, dispatcher));
        Assert.IsFalse(receipt.IsCurrent);
        Assert.AreEqual(completed, receipt.CompletedEffects);
        if (replace) Assert.AreEqual((byte)3, owner.Admission.Current!.PlayerSlot);
    }

    [TestMethod]
    public void FailedListenerStopsReceiptWithoutAutomaticReplay()
    {
        var (owner, receipt, work) = Start();
        var calls = 0;
        var dispatcher = new WorldAdmissionUiDispatcher(() => 1, _ => _ => { calls++; throw new IOException("listener failed"); });
        Assert.ThrowsException<IOException>(() => owner.Admission.DeliverUi(work, dispatcher));
        Assert.IsTrue(receipt.DeliveryRejected);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverUi(work, dispatcher));
        Assert.AreEqual(1, calls);
    }
}
