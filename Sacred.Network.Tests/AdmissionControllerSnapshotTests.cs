using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class AdmissionControllerSnapshotTests
{
    private static SacredGameplayReplicaSession Start()
    {
        var owner = new SacredGameplayReplicaSession();
        owner.Admission.ConfigureIdentifier(new SacredTextMarkup([]));
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [1, 2, 0, 0, 0, 0])));
        return owner;
    }
    private static WorldAdmissionRosterInputs RosterInputs() => new(0, 0, 0, 0, 0, new byte[] { 72, 0 }, ReadOnlyMemory<byte>.Empty);

    [TestMethod]
    public void CurrentSnapshotAndEstablishedMultiplayerRemainSeparateDependencies()
    {
        var owner = Start(); var receipt = owner.Admission.Current!;
        Assert.ThrowsException<NotSupportedException>(() => owner.Admission.Prepare(receipt, false, true, RosterInputs()));
        var fresh = WorldAdmissionControllerSetup.Construct(0);
        owner.Admission.CaptureController(receipt, fresh);
        Assert.ThrowsException<NotSupportedException>(() => owner.Admission.Prepare(receipt, false, true, RosterInputs()));
        Assert.IsNull(receipt.Plan);
        var initialized = WorldAdmissionControllerSetup.Initialize(fresh, 1, 1, 0);
        owner.Admission.CaptureController(receipt, initialized);
        var plan = owner.Admission.Prepare(receipt, false, true, RosterInputs());
        Assert.IsTrue(plan.RequiresActorBinding);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.CaptureController(receipt, initialized));
    }

    [TestMethod]
    public void ReplacementCannotInheritAnEarlierReceiptSnapshot()
    {
        var owner = Start(); var receipt = owner.Admission.Current!;
        owner.Admission.CaptureController(receipt, WorldAdmissionControllerSetup.Initialize(WorldAdmissionControllerSetup.Construct(0), 1, 1, 0));
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 1, [1, 3, 0, 0, 0, 0])));
        var replacement = owner.Admission.Current!;
        Assert.IsNull(replacement.ControllerSnapshot);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.CaptureController(receipt, receipt.ControllerSnapshot!));
        Assert.ThrowsException<NotSupportedException>(() => owner.Admission.Prepare(replacement, false, true, RosterInputs()));
    }

    [TestMethod]
    public void ResetUsesOwnedServiceAndCreditsOnlyItsCompletedEffect()
    {
        var owner = Start(); var receipt = owner.Admission.Current!;
        owner.Admission.CaptureController(receipt, WorldAdmissionControllerSetup.Initialize(WorldAdmissionControllerSetup.Construct(0), 1, 1, 0));
        owner.Admission.Prepare(receipt, false, true, RosterInputs());
        owner.Admission.TryPeek(receipt, out var reset);
        var calls = 0;
        var service = new WorldAdmissionResetService(() => { calls++; return new(1); }, _ => { });
        owner.Admission.DeliverResetService(reset, service);
        Assert.AreEqual((byte)0, service.Current!.EntryFlag0);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1, receipt.CompletedEffects);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverResetService(reset, service));
    }

    [TestMethod]
    public void ResetFactoryReentryCannotCreditOldAdmission()
    {
        var owner = Start(); var receipt = owner.Admission.Current!;
        owner.Admission.CaptureController(receipt, WorldAdmissionControllerSetup.Initialize(WorldAdmissionControllerSetup.Construct(0), 1, 1, 0));
        owner.Admission.Prepare(receipt, false, true, RosterInputs());
        owner.Admission.TryPeek(receipt, out var reset);
        var service = new WorldAdmissionResetService(() =>
        {
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(reset, GoldConsumerOutcome.Consumed, "before construction returns"));
            owner.Clear(); return new(1);
        }, _ => { });
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverResetService(reset, service));
        Assert.IsFalse(receipt.IsCurrent);
        Assert.AreEqual(0, receipt.CompletedEffects);
        Assert.AreEqual((byte)0, service.Current!.EntryFlag0);
    }
}
