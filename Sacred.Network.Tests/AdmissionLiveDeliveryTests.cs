using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class AdmissionLiveDeliveryTests
{
    public static IEnumerable<object[]> Boundaries() =>
        from stage in new[] { "reset", "query1", "roster", "query2", "ui", "finalizer", "117", "chat" }
        from mutation in new[] { "reset-session", "replace-receipt", "fail", "reenter" }
        select new object[] { stage, mutation };

    [TestMethod, DynamicData(nameof(Boundaries), DynamicDataSourceType.Method)]
    public void ServiceBoundaryPreservesReceiptLifetimeAndFailure(string stage, string mutation)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-admission-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("scenario").GetString() == "none" &&
            value.GetProperty("flags").GetInt32() == (stage == "117" ? 0 : 255) &&
            value.GetProperty("flag9").GetBoolean() &&
            value.GetProperty("queries")[0].GetBoolean() && value.GetProperty("queries")[1].GetBoolean());
        var template = native.RootElement.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString());
        var fixture = new AdmissionLiveFixture(row, Convert.FromHexString(template.GetString()!));
        var owner = new SacredGameplayReplicaSession();
        var incoming = GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, Convert.FromHexString(row.GetProperty("wire_hex").GetString()!)));
        owner.Apply(incoming); var receipt = owner.Admission.Current!;
        var original = fixture.Services;
        WorldAdmissionLiveServices services = null!;
        var query = 0; var reached = false; var callbacksAfterInterruption = 0; var interrupted = false;
        void Boundary(string at, Action callback)
        {
            if (interrupted) callbacksAfterInterruption++;
            callback();
            if (at != stage || reached) return;
            reached = true;
            Assert.IsTrue(receipt.LiveInProgress);
            Assert.IsFalse(receipt.DeliveryComplete);
            if (mutation == "reenter")
            {
                Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLive(receipt, services));
                Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.Prepare(receipt,
                    new(0, 0, 0, 0, 0), GoldPlayerRoster.FromPayload(fixture.Roster),
                    new(false, false, false, false, false, 0,
                        new(0, 0, 0, 0, 0, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)) { Multiplayer = true }, name => name));
                return;
            }
            interrupted = true;
            if (mutation == "reset-session") owner.Clear();
            else if (mutation == "replace-receipt") owner.Apply(incoming);
            else throw new InvalidDataException("Service failed after partial effects.");
        }
        services = original with
        {
            ResetResource = () => Boundary("reset", original.ResetResource),
            QueryConnection = () =>
            {
                var result = false;
                Boundary("query" + ++query, () => result = original.QueryConnection());
                return result;
            },
            AcceptedRoster = admission => Boundary("roster", () => original.AcceptedRoster(admission)),
            Ui = request => Boundary("ui", () => original.Ui(request)),
            FinalizeConnection = () => Boundary("finalizer", original.FinalizeConnection),
            DispatchController117 = () => Boundary("117", original.DispatchController117),
            ChatResource = key => Boundary("chat", () => original.ChatResource(key))
        };
        if (mutation == "reenter") owner.Admission.DeliverLive(receipt, services);
        else if (mutation == "fail") Assert.ThrowsException<InvalidDataException>(() => owner.Admission.DeliverLive(receipt, services));
        else Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLive(receipt, services));
        Assert.IsTrue(reached, "Selected native profile must reach the tested service.");
        Assert.AreEqual(0, callbacksAfterInterruption);
        Assert.IsFalse(receipt.LiveInProgress);
        Assert.AreEqual(mutation == "reenter", receipt.DeliveryComplete);
        Assert.AreEqual(mutation == "fail", receipt.DeliveryRejected);
        Assert.AreEqual(mutation == "reenter", receipt.LiveResult is not null);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLive(receipt, services));
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        if (mutation == "replace-receipt")
        {
            Assert.IsNotNull(owner.Admission.Current);
            Assert.IsFalse(owner.Admission.Current.LiveStarted);
            Assert.IsNull(owner.Admission.Current.LiveResult);
        }
    }

    [TestMethod]
    public void FirstRejectionClearsReplacementConnectionAfterController117()
    {
        var (owner, receipt, fixture) = Rejecting(false);
        var connection = fixture.Connection;
        var bytes = new byte[0x50];
        var replacement = new WorldControlMemory(at => bytes[at], (at, value) => bytes[at] = value,
            at => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
            (at, value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
            (at, value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
        replacement.WriteWord(0x48, 93);
        var result = owner.Admission.DeliverLive(receipt, fixture.Services with
        {
            ReadConnection = () => connection,
            DispatchController117 = () =>
            {
                Assert.AreEqual(31u, fixture.Controller.ReadWord(0x14));
                Assert.AreEqual(31u, connection.ReadWord(0x48));
                fixture.Controller.WriteWord(0x14, 91); connection.WriteWord(0x48, 92);
                connection = replacement;
            }
        });
        Assert.IsTrue(result.PrefixRejected);
        Assert.AreEqual(0u, result.Identity.LocalReference);
        Assert.AreEqual(0u, result.Identity.ConnectionLocalReference);
        Assert.AreEqual(0u, replacement.ReadWord(0x48));
        Assert.AreEqual(92u, fixture.Connection.ReadWord(0x48));
        Assert.IsTrue(receipt.DeliveryComplete);
    }

    [TestMethod]
    public void FirstRejectionRequiresConnectionAfterController117()
    {
        var (owner, receipt, fixture) = Rejecting(false);
        var present = true;
        Assert.ThrowsException<NotSupportedException>(() => owner.Admission.DeliverLive(receipt, fixture.Services with
        {
            ReadConnection = () => present ? fixture.Connection : null,
            DispatchController117 = () => present = false
        }));
        Assert.AreEqual(0u, fixture.Controller.ReadWord(0x14));
        Assert.AreEqual(31u, fixture.Connection.ReadWord(0x48));
        Assert.IsTrue(receipt.DeliveryRejected);
        Assert.IsNull(receipt.LiveResult);
    }

    [TestMethod]
    public void LateRejectionPreservesAbsentConnectionInPostServiceSnapshot()
    {
        var (owner, receipt, fixture) = Rejecting(true);
        var present = true;
        var result = owner.Admission.DeliverLive(receipt, fixture.Services with
        {
            ReadConnection = () => present ? fixture.Connection : null,
            DispatchController117 = () => present = false
        });
        Assert.IsTrue(result.LateRejected);
        Assert.IsNull(result.Identity.ConnectionLocalReference);
        Assert.AreEqual((uint)receipt.PlayerSlot, result.Identity.LocalReference);
        Assert.IsTrue(receipt.DeliveryComplete);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
    }

    private static (SacredGameplayReplicaSession Owner, WorldAdmissionReceipt Receipt, AdmissionLiveFixture Fixture) Rejecting(bool late)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-admission-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("scenario").GetString() == "none" && value.GetProperty("flags").GetInt32() == 0 &&
            value.GetProperty("flag9").GetBoolean() && value.GetProperty("queries")[0].GetBoolean() != late &&
            value.GetProperty("queries")[1].GetBoolean());
        var template = native.RootElement.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString());
        var fixture = new AdmissionLiveFixture(row, Convert.FromHexString(template.GetString()!));
        var owner = new SacredGameplayReplicaSession();
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, Convert.FromHexString(row.GetProperty("wire_hex").GetString()!))));
        return (owner, owner.Admission.Current!, fixture);
    }
}
