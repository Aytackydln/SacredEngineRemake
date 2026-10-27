using System.Buffers.Binary;
using System.Collections.Frozen;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Inventory.Items;
using Sacred.UI.Inventory;

namespace AssetViewer.Tests;

[TestClass]
public sealed class ItemDescriptionContentTests
{
    [TestMethod]
    public void ConcreteValuesReplaceTemplateValuesAndOnlyActiveSocketsCountAsOccupied()
    {
        var definition = default(SacredEquipment) with { IdemId = 321, Name = "Template", Price = 100, SpawnLevel = 1 };
        var bytes = new byte[SacredEquipmentInstanceLayout.Size];
        Write32(0, 321); Write32(4, 0xC6003F); Write32(8, 9876);
        bytes[12] = 1; bytes[13] = 2;
        bytes[20] = 31; bytes[21] = 4; bytes[23] = 5; bytes[24] = 6; bytes[25] = 44;
        bytes[26] = 3; bytes[27] = 7;
        Write16(28, 10); Write16(30, 2); Write16(36, 20); Write16(38, 5);
        Write16(44, 12); Write16(46, -3); Write16(48, 999); Write16(50, 8); Write16(54, 9);
        var sockets = new byte[3 * SacredEquipmentInstanceLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(sockets, 444);
        BinaryPrimitives.WriteUInt32LittleEndian(sockets.AsSpan(2 * SacredEquipmentInstanceLayout.Size), 555);
        var item = SacredItemInstance.FromNativeObjectRecords(definition, 321, bytes, sockets);
        item.SetResolvedName(new SacredEquipmentNameResult(0, "Generated"));
        var resources = new GameResStore(new Dictionary<uint, string>
        {
            [SacredResourceHash.Compute("INVENTAR_MIN_FORCE")] = "Kraftminimum",
            [SacredResourceHash.Compute("9402")] = "Test Skill"
        }.ToFrozenDictionary());
        var lines = new ItemDescriptionContentBuilder(resources, []).Create(item);
        var text = lines.Select(line => line.Text).ToArray();

        Assert.AreEqual("Generated [1/2]", text[0]);
        CollectionAssert.Contains(text, "Price: 9876");
        CollectionAssert.Contains(text, "Level: 44");
        CollectionAssert.Contains(text, "Damage: 12-25");
        CollectionAssert.Contains(text, "Physical: 10-20");
        CollectionAssert.Contains(text, "Fire: 2-5");
        CollectionAssert.Contains(text, "Attack: 12");
        CollectionAssert.Contains(text, "Defense: -3");
        CollectionAssert.Contains(text, "Physical Resistance: 8");
        CollectionAssert.Contains(text, "Magic Resistance: 9");
        CollectionAssert.Contains(text, "Minimum Level: 31");
        CollectionAssert.Contains(text, "Kraftminimum: 4");
        CollectionAssert.Contains(text, "Minimum Charisma: 5");
        CollectionAssert.Contains(text, "Minimum Endurance: 6");
        CollectionAssert.Contains(text, "Minimum Test Skill: 7");
        Assert.IsFalse(text.Any(line => line.Contains("999") || line.Contains("Dexterity") || line.Contains("Expected Protection")));
        Assert.AreEqual(ItemDescriptionColors.Modifier(802), lines.Single(line => line.Text == "Fire: 2-5").Color);
        Assert.AreEqual(ItemDescriptionColors.Text, lines.Single(line => line.Text == "Minimum Level: 31").Color);

        void Write32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        void Write16(int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), value);
    }

    [TestMethod]
    public void EmptyValuesDoNotProduceDamageResistanceRequirementsOrSocketPlaceholders()
    {
        var definition = default(SacredEquipment) with { Name = "Plain ring" };
        var item = SacredItemInstance.FromDefinition(definition);
        var lines = new ItemDescriptionContentBuilder(GameResStore.Empty, []).Create(item);
        CollectionAssert.AreEqual(new[] { "Plain ring", "Price: 0", "Level: 0" }, lines.Select(line => line.Text).ToArray());
    }
}
