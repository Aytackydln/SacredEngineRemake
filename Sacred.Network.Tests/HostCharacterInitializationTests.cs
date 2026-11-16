using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Original selected file plus controlled TCP grants, not actor/host approval.</summary>
[TestClass, TestCategory("Installed")]
public sealed class HostCharacterInitializationTests
{
    private static readonly string Installation = Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
    private static readonly Lazy<WorldHostSavedCharacterSource> Source = new(() => WorldHostSavedCharacterSource.ReadSelected(Installation, 1));

    [TestMethod]
    public async Task CharacterConsumesSharedReservationsOnceAndCannotBypassNativeActorEntry()
    {
        var source = ReadSource();
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, default, null);
        peer.AllowLongPreparation();
        await Admit(peer, source.ObjectCount + 4);
        var owner = peer.HostSession!;
        var character = await owner.PrepareCharacterAsync(peer.Token);
        Assert.AreEqual(WorldHostInitializationPhase.WaitingForActorBinding, owner.Phase);
        Assert.AreEqual(4, owner.References.Available);
        Assert.AreEqual(owner.Admission.PlayerSlot, character.ReferencePlan.PlayerSlot);
        Assert.AreEqual((uint)owner.Admission.PlayerSlot, character.ReferencePlan.References[source.Records.Hero.Creature.BaseState.ObjectReference]);
        var allocated = character.ReferencePlan.References.Values.Where(reference => reference > 16).Order().ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(17, source.ObjectCount).Select(value => (uint)value).ToArray(), allocated);
        Assert.AreEqual(source.ObjectCount + 3, character.Packets.MessageCount);
        Assert.IsTrue(character.IsCurrent);
        Assert.AreEqual(source.Records.Hero.Creature.BaseState.Resource, character.HeroConstruction.Template.ItemType);
        Assert.AreEqual((uint)owner.Admission.PlayerSlot, character.HeroConstruction.Actor.Base.ObjectReference);
        Assert.AreEqual(owner.Admission.PlayerSlot, character.HeroConstruction.Actor.Base.Owner);
        Assert.IsTrue(character.HeroConstruction.Actor.Node.Active);
        Assert.AreEqual((byte)1, character.HeroConstruction.Counter.Value);
        Assert.AreEqual(0x80040u, character.HeroConstruction.Actor.RuntimeFlags);
        var model = character.HeroConstruction.ModelResources;
        Assert.IsNotNull(model);
        Assert.IsNotNull(model.Body.Mesh); Assert.IsNotNull(model.Body.Skin);
        Assert.IsNull(model.Body.DefaultAnimation, "Saved numeric selection cannot be replaced with a default animation.");
        Assert.IsTrue(model.HasRoot); Assert.IsFalse(model.IsInvalidated);
        Assert.AreEqual(System.Numerics.Vector3.Zero, model.RootLocalOffset);
        Assert.IsFalse(string.IsNullOrEmpty(model.SavedAnimation.Name));
        Assert.AreEqual(0u, character.HeroConstruction.Actor.Base.Flags & 0x4000000u,
            "CPU model resources alone cannot claim native creature activation.");
        Assert.AreSame(character.HeroConstruction, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction);
        Assert.AreSame(model, (await owner.PrepareCharacterAsync(peer.Token)).HeroConstruction.ModelResources);
        var expectedEntry = source.StartPositions.Select(owner.Admission.PlayerSlot, true, 0);
        Assert.AreEqual(expectedEntry.X, character.EntryPosition.X);
        Assert.AreEqual(expectedEntry.Y, character.EntryPosition.Y);
        Assert.AreSame(character, await owner.PrepareCharacterAsync(peer.Token));
        Assert.AreEqual(4, owner.References.Available, "Retry cannot allocate the character twice.");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.InitializeAsync(Installation, SacredCampaignFiles.Resolve(Installation), peer.Token));
        Assert.IsNull(owner.Startup, "Native pre-script actor phase cannot be replaced with absent-hero execution.");
        Assert.AreEqual(4, owner.References.Available);
        Assert.IsNull(owner.RegionInitialization);
        Assert.AreEqual(0, owner.Replicas.Objects.Count, "Packet preparation must not invent a runtime actor registry entry.");
        Assert.AreEqual((byte)0, owner.Replicas.LocalPlayerSlot);
        Assert.IsFalse(owner.AdmissionReceipt!.DeliveryComplete);
        await owner.DisposeAsync();
        Assert.IsFalse(character.IsCurrent); Assert.IsNull(owner.Character);
        Assert.IsTrue(character.HeroConstruction.IsInvalidated);
        Assert.IsTrue(model.IsInvalidated);
        Assert.IsFalse(character.HeroConstruction.Actor.Node.Active);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => owner.PrepareCharacterAsync());
    }

    [TestMethod]
    public async Task ShortGrantDoesNotPartlyDrainReferencesAndCanResumeAfterActualReplenishment()
    {
        var source = ReadSource(); Assert.IsTrue(source.ObjectCount > 1);
        await using var peer = await GoldLoopbackPeer.StartHostAsync(source, default, null);
        await Admit(peer, source.ObjectCount - 1);
        var owner = peer.HostSession!;
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => owner.PrepareCharacterAsync(peer.Token));
        Assert.IsNull(owner.Character); Assert.IsNull(owner.Startup);
        Assert.AreEqual(source.ObjectCount - 1, owner.References.Available);
        await peer.SendAsync(Words(0x153, [(uint)(17 + source.ObjectCount - 1)]));
        while (owner.References.Available < source.ObjectCount) await Task.Delay(5, peer.Token);
        var prepared = await owner.PrepareCharacterAsync(peer.Token);
        Assert.IsTrue(prepared.IsCurrent); Assert.AreEqual(0, owner.References.Available);
        Assert.IsTrue(owner.IsCurrent); Assert.IsFalse(owner.AdmissionReceipt!.DeliveryComplete);
    }

    private static WorldHostSavedCharacterSource ReadSource()
    {
        if (!Directory.Exists(Installation)) Assert.Inconclusive("Installed original Gold files are required.");
        return Source.Value;
    }
    private static async Task Admit(GoldLoopbackPeer peer, int references)
    {
        await peer.SendAsync(new(0x11, 0, 0, 1, [1, 1, 0, 0, 0, 0]), Words(0x1e, [0x110]),
            Words(0x153, Enumerable.Range(17, references).Select(value => (uint)value).ToArray()), Words(0x154, [2269, 2000]));
        await peer.HostSession!.WaitForReservationsAsync(peer.Token);
    }
    private static GoldMessage Words(ushort opcode, uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        return new(opcode, 0, 0, 1, bytes);
    }
}
