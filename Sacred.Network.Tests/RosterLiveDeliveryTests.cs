using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class RosterLiveDeliveryTests
{
    public static IEnumerable<object[]> Boundaries() =>
        from stage in new[] { "ui", "modal", "resources", "identifier", "tooltip", "profile", "refresh", "notice" }
        from mutation in new[] { "reset", "replace", "fail", "swallow", "reenter" }
        select new object[] { stage, mutation };

    [TestMethod, DynamicData(nameof(Boundaries), DynamicDataSourceType.Method)]
    public void NestedRosterPreservesLifetimeAndRejectsPartialFailure(string stage, string mutation)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-roster-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("mode").GetString() == "profiles" && value.GetProperty("mutation").GetString() == "none" &&
            value.GetProperty("old_mask").GetUInt32() == 0 && value.GetProperty("local").GetUInt32() == 1 &&
            value.GetProperty("flag28").GetBoolean() && value.GetProperty("multiplayer").GetBoolean());
        var fixture = new RosterLiveFixture(row);
        var incoming = GoldPlayerRoster.FromPayload(Convert.FromHexString(native.RootElement.GetProperty("tables").GetProperty("profiles").GetString()!));
        var owner = new SacredGameplayReplicaSession();
        var admission = GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [1, 1, 0, 0, 0, 0]));
        owner.Apply(admission); var receipt = owner.Admission.Current!;
        var original = fixture.Services;
        WorldRosterLiveServices services = null!;
        var reached = false; var stopped = false; var callbacksAfterStop = 0; var laterQuery = 0;
        void Boundary(string at, Action action)
        {
            if (stopped) callbacksAfterStop++;
            action();
            if (reached || stage != at) return;
            reached = true;
            Assert.IsTrue(receipt.LiveRosterStarted);
            Assert.IsFalse(receipt.LiveRosterComplete);
            Assert.IsFalse(receipt.DeliveryComplete);
            if (mutation == "reenter")
            {
                Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLiveRoster(receipt, incoming, services));
                return;
            }
            stopped = true;
            if (mutation == "reset") owner.Clear();
            else if (mutation == "replace") owner.Apply(admission);
            else throw new InvalidDataException("Partial nested roster failure.");
        }
        services = original with
        {
            Ui = request => Boundary("ui", () => original.Ui(request)),
            PrepareModal = () => Boundary("modal", original.PrepareModal),
            ResolveResources = () =>
            {
                WorldRosterResources resource = null!;
                Boundary("resources", () => resource = original.ResolveResources());
                return resource with { Localize = key =>
                {
                    string result = null!;
                    Boundary("tooltip", () => result = resource.Localize(key));
                    return result;
                } };
            },
            Identifier = name =>
            {
                string result = null!;
                Boundary("identifier", () => result = original.Identifier(name));
                return result;
            },
            Profile = request => Boundary("profile", () => original.Profile(request)),
            Refresh = () => Boundary("refresh", original.Refresh),
            JoinNotice = slot => Boundary("notice", () => original.JoinNotice(slot))
        };
        var outer = new WorldAdmissionLiveServices(fixture.Controller, () => fixture.Controller, () => { },
            () => { laterQuery++; return false; }, _ =>
            {
                try { owner.Admission.DeliverLiveRoster(receipt, incoming, services); }
                catch (InvalidDataException) when (mutation == "swallow") { }
            }, _ => { }, () => { }, () => { }, _ => { });
        // The nested consumer cannot run outside the accepted service window.
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLiveRoster(receipt, incoming, services));
        Assert.IsFalse(receipt.LiveRosterStarted);
        if (mutation == "reenter") owner.Admission.DeliverLive(receipt, outer);
        else if (mutation == "fail") Assert.ThrowsException<InvalidDataException>(() => owner.Admission.DeliverLive(receipt, outer));
        else Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLive(receipt, outer));
        Assert.IsTrue(reached);
        Assert.AreEqual(0, callbacksAfterStop);
        Assert.AreEqual(mutation == "reenter" ? 2 : 1, laterQuery);
        Assert.AreEqual(mutation == "reenter", receipt.LiveRosterComplete);
        Assert.AreEqual(mutation == "reenter", receipt.DeliveryComplete);
        Assert.AreEqual(mutation is "fail" or "swallow", receipt.DeliveryRejected);
        Assert.IsFalse(receipt.LiveInProgress);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
        Assert.ThrowsException<InvalidOperationException>(() => owner.Admission.DeliverLiveRoster(receipt, incoming, services));
        if (mutation == "replace")
        {
            Assert.IsNotNull(owner.Admission.Current);
            Assert.IsFalse(owner.Admission.Current.LiveRosterStarted);
            Assert.IsFalse(owner.Admission.Current.LiveRosterComplete);
        }
    }

    [TestMethod]
    public void ProfileArgumentsRemainOwnedWhenCallbackChangesCurrentStorage()
    {
        using var native = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-roster-live.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("mode").GetString() == "profiles" && value.GetProperty("mutation").GetString() == "none");
        var fixture = new RosterLiveFixture(row);
        var incoming = GoldPlayerRoster.FromPayload(Convert.FromHexString(native.RootElement.GetProperty("tables").GetProperty("profiles").GetString()!));
        var owner = new SacredGameplayReplicaSession();
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [1, 1, 0, 0, 0, 0])));
        var receipt = owner.Admission.Current!; var profiles = 0;
        var services = fixture.Services with { Profile = request =>
        {
            profiles++;
            var expected = request.Profile.ToArray(); var name = request.Name;
            fixture.Controller.WriteByte(0x54u + (uint)(request.Slot * GoldPlayerDataLayout.Size) + GoldPlayerDataLayout.ProfileOffset, 0x7f);
            fixture.Names[request.Slot] = "Reentered";
            CollectionAssert.AreEqual(expected, request.Profile.ToArray());
            Assert.AreEqual(name, request.Name);
        } };
        owner.Admission.DeliverLive(receipt, new(fixture.Controller, () => fixture.Controller,
            () => { }, () => false, _ => owner.Admission.DeliverLiveRoster(receipt, incoming, services),
            _ => { }, () => { }, () => { }, _ => { }));
        Assert.AreEqual(4, profiles);
        Assert.IsTrue(receipt.LiveRosterComplete);
    }
}
