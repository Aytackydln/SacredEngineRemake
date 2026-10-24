using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class DialogRegistrationTests
{
    private static GoldGameEvent Registration(uint resource, uint index, uint quest = 7, string name = "Npc") =>
        Event(0x1cd, [..Words(resource, index, quest), ..Encoding.Latin1.GetBytes(name), 0]);

    [TestMethod]
    public void DialogBindingClearsOldSlotAndPreservesCaseFoldedRegistryOrder()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500)); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Dialogs()));
        world.WorldBanks.Functions.Configure(ReceiveActionPackets.Functions("unused", "SelfTriggerQuest7", "SELFTRIGGERQUEST7"));
        var original = world.ScriptDialogs.Records[1]; world.Apply(Registration(900, 2));
        Assert.AreEqual(0u, world.ScriptDialogs.Records[1].AttachedObjectReference); Assert.AreEqual(500u, world.ScriptDialogs.Records[2].AttachedObjectReference);
        Assert.AreEqual(101u, original.AttachedObjectReference); Assert.AreEqual(2u, world.Objects[500].Creature!.DialogIndex);
        Assert.AreEqual(0x80000u, world.Objects[500].BaseState.Flags);
        var reply = world.CreatureControls.TakeReply(); Assert.IsNotNull(reply); Assert.AreEqual(2u, reply.State.Word245);
        world.Apply(Registration(900, 2, name: "nPC")); var group = world.CreatureCommands.Dialogs.Groups.Single();
        Assert.AreEqual("Talk_Npc_Dlg_2", group.Key); Assert.AreEqual(2, group.Callbacks.Count);
        Assert.IsTrue(group.Callbacks[0].TryResolveFunction(out var index)); Assert.AreEqual(1u, index);
        Assert.AreEqual(1u, group.Callbacks[0].Object.Creature!.DialogIndex, "The request retains the pre-setter actor snapshot.");
    }

    [TestMethod]
    public void DeferredBindingsUseBaselineBoundsAndCapturedFunctionGeneration()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500));
        world.Apply(Registration(900, 2)); var registration = world.CreatureCommands.Dialogs.Registrations.Single();
        Assert.AreEqual(1, world.CreatureCommands.Dialogs.PendingAttachmentCount); Assert.IsFalse(registration.TryResolveFunction(out _));
        var renamed = ReceiveActionPackets.Functions("Renamed")[4..]; world.Apply(Event(0x1c4, [..Words(1), ..renamed]));
        world.WorldBanks.Functions.Configure(ReceiveActionPackets.Functions("unused", "SelfTriggerQuest7"));
        Assert.IsTrue(registration.TryResolveFunction(out var function)); Assert.AreEqual(1u, function);
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Dialogs()));
        Assert.AreEqual(0, world.CreatureCommands.Dialogs.PendingAttachmentCount); Assert.AreEqual(500u, world.ScriptDialogs.Records[2].AttachedObjectReference);
        world.Apply(Registration(900, uint.MaxValue, 999));
        Assert.AreEqual(uint.MaxValue, world.Objects[500].Creature!.DialogIndex); Assert.AreEqual(0u, world.ScriptDialogs.Records[2].AttachedObjectReference);
        Assert.AreEqual(0x80000u, world.Objects[500].BaseState.Flags); Assert.AreEqual(4, world.ScriptDialogs.Records.Count);
        Assert.IsTrue(world.CreatureCommands.Dialogs.Registrations[^1].TryResolveFunction(out function)); Assert.AreEqual(uint.MaxValue, function);
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b4))); Assert.IsTrue(world.ScriptDialogs.IsBaselineKnown);
        Assert.AreEqual(4, world.ScriptDialogs.Records.Count, "An empty bank does not clear the native dialog vector.");
    }

    [TestMethod]
    public void ResourceBindingsAndInactiveGuardMatchNativeWithoutRetargetingReusedObjects()
    {
        var world = new GoldWorldReplica(); world.Apply(CreatureCommandPackets.Actor(500, flags: 0x10));
        world.Apply(Registration(900, 2)); Assert.AreEqual(1, world.CreatureCommands.Dialogs.Registrations.Count, "1CD permits an inactive creature.");
        var previous = world.Objects[500]; world.Apply(CreatureCommandPackets.Actor(500));
        Assert.AreNotEqual(previous.InstanceVersion, world.Objects[500].InstanceVersion);
        world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Dialogs()));
        Assert.AreEqual(101u, world.ScriptDialogs.Records[1].AttachedObjectReference);
        Assert.AreEqual(102u, world.ScriptDialogs.Records[2].AttachedObjectReference, "An old pending attachment cannot bind the replacement lifetime.");
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b3, (GoldScriptResourceLayout.BindingBankSection, new[] { Words(951, 500) }))));
        world.Apply(Registration(951, 3)); Assert.AreEqual(500u, world.ScriptDialogs.Records[3].AttachedObjectReference);
        world.Clear(); Assert.AreEqual(0, world.CreatureCommands.Dialogs.PendingAttachmentCount); Assert.IsFalse(world.ScriptDialogs.IsBaselineKnown);
    }
}
