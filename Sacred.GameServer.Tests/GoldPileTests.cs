using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;
using Sacred.GameServer.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Tests;

[TestClass]
public class GoldPileTests
{
    [TestMethod]
    public void PileSizeFollowsTheGamesThresholds()
    {
        // Gold 0x4AEC1E: below 50, below 350, below 1000, else.
        foreach (var (amount, resource) in new[] { (1u, 0x140Cu), (49u, 0x140Cu), (50u, 0x140Du), (349u, 0x140Du), (350u, 0x140Eu),
                     (999u, 0x140Eu), (1000u, 0x140Fu), (GoldPile.MaximumAmount, 0x140Fu) })
        {
            Assert.AreEqual(resource, GoldPile.ResourceFor(amount), $"{amount}");
            Assert.IsTrue(GoldPile.IsMoney(resource));
        }
        Assert.IsFalse(GoldPile.IsMoney(0x140B));
        Assert.IsFalse(GoldPile.IsMoney(0x1410));
    }

    [TestMethod]
    public void ValueBlockCarriesTheAmountAsPrice()
    {
        var values = GoldPile.Values(12345);
        Assert.AreEqual(GoldItemStateLayout.ItemDataSize, values.Length);
        var instance = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(values);
        Assert.AreEqual(0x140Fu, instance.ItemType);
        Assert.AreEqual(GoldPile.ValueFlags, instance.Flags);
        Assert.AreEqual(12345u, instance.Price, "value block +8, cItemBase +0x120");
        Assert.IsTrue(values.AsSpan(12).IndexOfAnyExcept((byte)0) < 0, "the rest of the Gold rows is zero");
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => GoldPile.Values(0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => GoldPile.Values(GoldPile.MaximumAmount + 1));
    }

    [TestMethod]
    public void ItemStateWithAPileRoundTrips()
    {
        var basis = new GoldObjectBaseState(0x20, GoldPile.SmallResource, 0, 7, 100, 200, 0, 0, 0, 0, 1, 0);
        var physical = GoldPhysicalConstructionWriter.Write(basis, new GoldPhysicalVector(0, 1, 0));
        var payload = GoldItemState.Create(physical, [], GoldPile.Values(42), 0).ToPayload();
        Assert.AreEqual(GoldItemStateLayout.MinimumSize, payload.Length);
        var state = GoldItemState.FromPayload(payload);
        Assert.AreEqual(GoldPile.SmallResource, state.PhysicalState.BaseState.ObjectType);
        Assert.AreEqual(GoldPile.SmallResource, state.Instance.ItemType);
        Assert.AreEqual(42u, state.Instance.Price);
        Assert.AreEqual(0u, state.InventoryId, "on the ground");
        Assert.AreEqual(GoldItemStateLayout.Marker, state.RepeatedMarker);
        Assert.IsFalse(state.HasWeaponExtension);
        // Price sits at item data +8 right after the 121-byte physical prefix, marker and empty record list.
        Assert.AreEqual(42u, BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(GoldItemStateLayout.RecordsOffset + 8)));
    }

    /// <summary>The template equals the Weapon.pak "Gold" rows' instance values (+128) apart from the price.</summary>
    [TestMethod]
    public void TemplateMatchesTheWeaponPakGoldRows()
    {
        var dir = Environment.GetEnvironmentVariable("SACRED_GAME_DIR") ?? @"B:\Spiele\GOG Games\sacred gold GOG";
        var path = Path.Combine(dir, "PAK", "Weapon.pak");
        if (!File.Exists(path)) { Assert.Inconclusive("no Weapon.pak (SACRED_GAME_DIR)"); return; }
        var pak = File.ReadAllBytes(path);
        const int header = WeaponPakHeaderLayout.SerializedSize, row = 258, instance = 128;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(pak.AsSpan(4));
        var found = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            var values = pak.AsSpan(header + i * row + instance, SacredEquipmentInstanceLayout.Size);
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(values);
            if (!GoldPile.IsMoney(resource)) continue;
            found.Add(resource);
            var expected = GoldPile.Values(1).AsSpan().ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(expected, resource);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(8), BinaryPrimitives.ReadUInt32LittleEndian(values[8..]));
            CollectionAssert.AreEqual(expected, values.ToArray(), $"row {i} (0x{resource:X})");
        }
        CollectionAssert.AreEquivalent(new[] { 0x140Cu, 0x140Du, 0x140Eu, 0x140Fu }, found.ToArray());
    }
}
