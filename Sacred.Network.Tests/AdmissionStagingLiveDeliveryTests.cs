using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class AdmissionStagingLiveDeliveryTests
{
    public static IEnumerable<object[]> Cases() =>
        from stage in new[] { "accepted", "host", "window", "identifier", "alternate" }
        from mutation in new[] { "reset", "replace", "fail", "swallow", "reenter" }
        select new object[] { stage, mutation };

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void StagingServicesPreserveReceiptLifetime(string stage, string mutation)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-admission-staging-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("scenario").GetString() == "none" && value.GetProperty("flags").GetInt32() == 9 && value.GetProperty("slot").GetInt32() == 1);
        var fixture = new AdmissionLiveFixture(row, Convert.FromHexString(native.RootElement.GetProperty("templates").GetProperty(row.GetProperty("profile_present").GetInt32().ToString()).GetString()!));
        var owner = new SacredGameplayReplicaSession();
        var admission = GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, Convert.FromHexString(row.GetProperty("wire_hex").GetString()!)));
        owner.Apply(admission); var receipt = owner.Admission.Current!;
        var names = Enumerable.Repeat("", 17).ToArray();
        WorldAdmissionStagingServices staging = null!;
        var reached = false; var stopped = false; var afterStop = 0; var queries = 0;
        void Boundary(string at)
        {
            if (stopped) afterStop++;
            if (reached || at != stage) return;
            reached = true;
            Assert.IsFalse(receipt.LiveStagingComplete);
            if (mutation == "reenter")
            { Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLiveStaging(receipt, staging)); return; }
            stopped = true;
            if (mutation == "reset") owner.Clear();
            else if (mutation == "replace") owner.Apply(admission);
            else throw new InvalidDataException("Partial staging failure.");
        }
        var roster = new WorldRosterLiveServices(fixture.Controller, new(slot => names[slot], (slot, name) => names[slot] = name, (_, _) => { }),
            name => { Boundary("identifier"); return name; }, () => true, _ => { }, _ => { }, () => { }, () => new(_ => "K"), () => { }, _ => { });
        staging = new(roster, () => fixture.Connection, () => Encoding.Unicode.GetBytes("Hero"), () => Encoding.Unicode.GetBytes("Saved"),
            () => 0x76543210, () => { Boundary("alternate"); return 3; }, Boundary);
        var outer = fixture.Services with
        {
            QueryConnection = () => { queries++; return false; },
            AcceptedRoster = _ =>
            {
                try { owner.Admission.DeliverLiveStaging(receipt, staging); }
                catch (InvalidDataException) when (mutation == "swallow") { }
            }
        };
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLiveStaging(receipt, staging));
        if (mutation == "reenter") owner.Admission.DeliverLive(receipt, outer);
        else if (mutation == "fail") Assert.ThrowsException<InvalidDataException>(() => owner.Admission.DeliverLive(receipt, outer));
        else Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLive(receipt, outer));
        Assert.IsTrue(reached); Assert.AreEqual(0, afterStop);
        Assert.AreEqual(mutation == "reenter" ? 2 : 1, queries);
        Assert.AreEqual(mutation == "reenter", receipt.LiveStagingComplete);
        Assert.AreEqual(mutation == "reenter", receipt.DeliveryComplete);
        Assert.AreEqual(mutation is "fail" or "swallow", receipt.DeliveryRejected);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
    }
}
