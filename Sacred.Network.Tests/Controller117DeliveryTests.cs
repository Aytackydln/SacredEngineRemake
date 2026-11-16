using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class Controller117DeliveryTests
{
    private static Controller117Fixture Fixture()
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-controller-event117.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(row =>
        {
            var input = row.GetProperty("inputs");
            return input.GetProperty("active").GetInt32() == 1 && input.GetProperty("busy").GetInt32() == 0 &&
                input.GetProperty("connection_active").GetInt32() == 1 && input.GetProperty("process_active").GetInt32() == 0 &&
                !input.GetProperty("backend").GetBoolean() && input.GetProperty("mutation").GetString() == "none";
        });
        return new(row);
    }

    private static (SacredGameplayReplicaSession Owner, WorldAdmissionReceipt Receipt, WorldAdmissionWork Work, Controller117Fixture Fixture) Start()
    {
        var fixture = Fixture(); var current = WorldController117.Capture(fixture.Services);
        var owner = new SacredGameplayReplicaSession();
        owner.Admission.ConfigureIdentifier(new SacredTextMarkup([]));
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 1, [0, 3, 0x33, 0x33, 0, 0])));
        var receipt = owner.Admission.Current!;
        owner.Admission.CaptureController(receipt, new(new(0xa5, 0xa5, 3, current.Identity.HostedServerWindow, 42),
            GoldPlayerRoster.FromPayload(new byte[GoldPlayerRosterLayout.Size]), current.Gates, current.ServiceFlags, 1));
        var plan = owner.Admission.Prepare(receipt, false, true, new(0, 0, 0, 0, 0, new byte[] { 72, 0 }, ReadOnlyMemory<byte>.Empty));
        Assert.IsTrue(plan.LateRejected);
        // Preparation is a projection. The fixture's caller explicitly applies
        // its known prefix/staging state before executing the local service.
        var memory = fixture.Services.Controller; var prefix = plan.Prefix.State;
        memory.WriteByte(3, prefix.PendingByte3); memory.WriteByte(1, prefix.HostByte1);
        memory.WriteWord(0x14, prefix.LocalReference); memory.WriteWord(0x24, prefix.HostToken);
        memory.WriteByte(2, plan.Flag2); memory.WriteByte(0x48, plan.StagedRoster!.Flag48);
        memory.WriteByte(0x3819, plan.StagedRoster.Flag3819);
        fixture.Services.ReadConnection()!.Memory.WriteWord(0x48, prefix.ConnectionLocalReference);
        while (owner.Admission.TryPeek(receipt, out var work))
        {
            if (work.Effect is WorldAdmissionEffect.DispatchController117) return (owner, receipt, work, fixture);
            if (work.Effect is WorldAdmissionEffect.ResetService)
                owner.Admission.DeliverResetService(work, new(() => new(1), _ => { }));
            else if (work.Effect is WorldAdmissionEffect.Ui)
                owner.Admission.DeliverUi(work, new(() => 0, _ => throw new Exception("empty UI storage")));
            else owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "supplied roster boundary");
        }
        throw new Exception("fixture did not request local controller dispatch");
    }

    [TestMethod]
    public void LocalDispatchCapturesActualStateAfterThePreparedRejection()
    {
        var (owner, receipt, work, fixture) = Start();
        owner.Admission.DeliverController117(work, fixture.Services);
        Assert.AreEqual(0x3333u, receipt.Plan!.Prefix.State.HostToken);
        Assert.AreEqual(0u, receipt.ControllerAfter117!.Identity.HostedServerWindow);
        Assert.AreEqual(3u, receipt.ControllerAfter117.Identity.LocalReference);
        Assert.AreEqual((uint?)0, receipt.ControllerAfter117.Identity.ConnectionLocalReference);
        Assert.AreEqual((byte)0, receipt.ControllerAfter117.Gates.Flag9);
        Assert.AreEqual((byte)0, receipt.ControllerAfter117.HostByte1);
        Assert.IsFalse(receipt.Plan.RequiresActorBinding);
        Assert.IsTrue(receipt.DeliveryComplete);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverController117(work, fixture.Services));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReentrantListenerCannotCreditOrRecursivelyDispatchOldWork(bool replace)
    {
        var (owner, receipt, work, fixture) = Start(); var originalUi = fixture.Services.Ui;
        WorldController117Services? services = null;
        services = fixture.Services with { Ui = request =>
        {
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Report(work, GoldConsumerOutcome.Consumed, "early"));
            Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverController117(work, services!));
            originalUi(request);
            if (replace) owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 2, [1, 4, 0, 0, 0, 0])));
            else owner.Clear();
        } };
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverController117(work, services));
        Assert.IsFalse(receipt.IsCurrent);
        Assert.IsNull(receipt.ControllerAfter117);
        if (replace) Assert.AreEqual((byte)4, owner.Admission.Current!.PlayerSlot);
    }

    [TestMethod]
    public void PartialServiceFailureStopsAutomaticRetry()
    {
        var (owner, receipt, work, fixture) = Start(); var calls = 0;
        var services = fixture.Services with { Ui = _ => { calls++; throw new IOException("listener failed after connection clear"); } };
        Assert.ThrowsException<IOException>(() => owner.Admission.DeliverController117(work, services));
        Assert.IsTrue(receipt.DeliveryRejected);
        Assert.IsNull(receipt.ControllerAfter117);
        Assert.IsFalse(owner.Admission.TryPeek(receipt, out _));
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverController117(work, services));
        Assert.AreEqual(1, calls);
    }
}
