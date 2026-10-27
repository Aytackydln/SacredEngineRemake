using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Core.GameRes;
using Sacred.Core.GameBin.Sets;
using Sacred.Core.Pak.Items;
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

    [TestMethod]
    [DataRow(false, "Generated ordinary name")]
    [DataRow(true, "Authored set piece")]
    public void CatalogueMembershipOverridesGeneratedNativeInstanceName(bool catalogueMember, string expectedName)
    {
        var definition = default(SacredEquipment) with { IdemId = 321, Name = "Authored set piece" };
        var values = new byte[SacredEquipmentInstanceLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(values, definition.IdemId);
        var item = SacredItemInstance.FromNativeRecord(definition, values);
        item.SetResolvedName(new(0x80012345, "Generated ordinary name", "Gen_test"));
        var resources = new GameResStore(new Dictionary<uint, string>
            { [0x12345] = "Generated ordinary name" }.ToFrozenDictionary());
        SacredSetEntry[] sets = catalogueMember ? [new(2, 0, 0, 0, [321])] : [];
        var builder = new ItemDescriptionContentBuilder(resources, sets);
        Assert.AreEqual(expectedName, builder.GetName(item));
        Assert.AreEqual(expectedName, builder.Create(item)[0].Text);
    }

    [TestMethod]
    [DataRow(0xF0000u, (byte)0, false, 0u)]
    [DataRow(0xF0000u, (byte)0, false, 0x80023456u)]
    [DataRow(0u, (byte)2, false, 0u)]
    [DataRow(0u, (byte)2, false, 0x80023456u)]
    [DataRow(0u, (byte)0, true, 0u)]
    [DataRow(0u, (byte)0, true, 0x80023456u)]
    public void UniqueAndSetHeadingsUseAuthoredResourceEvenWithStaleGeneratedNames(uint flags, byte setType, bool catalogueMember, uint generatedReference)
    {
        const uint id = 321;
        const uint resourceHash = 0x80012345;
        var descriptorBytes = new byte[ItemsPakEntryModelDescLayout.SerializedSize];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptorBytes.AsSpan(32), resourceHash);
        var descriptor = MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(descriptorBytes);
        var definition = default(SacredEquipment) with
        {
            IdemId = id, Name = "Wrong template text", SetType = setType,
            Item = new(default, descriptor, "")
        };
        var values = new byte[SacredEquipmentInstanceLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(values, id);
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), flags);
        var item = SacredItemInstance.FromNativeRecord(definition, values);
        item.SetResolvedName(new(generatedReference, "Wrong generated name", "Gen_wrong"));
        SacredSetEntry[] sets = catalogueMember ? [new(2, 0, 0, 0, [id])] : [];
        var resources = new GameResStore(new Dictionary<uint, string>
        { [resourceHash & 0x7FFFFFFF] = "Authored piece name" }.ToFrozenDictionary());
        var builder = new ItemDescriptionContentBuilder(resources, sets);
        Assert.AreEqual("Authored piece name", builder.GetName(item));
        Assert.AreEqual("Authored piece name", builder.Create(item)[0].Text);
    }

    [TestMethod]
    public void AuthoredCataloguePieceIgnoresStaleGeneratedReferenceAndKeepsSeparateSetTitle()
    {
        var definition = default(SacredEquipment) with { IdemId = 321, Name = "Authored set piece" };
        var item = SacredItemInstance.FromDefinition(definition);
        item.SetResolvedName(new(0x80023456, "Wrong generated name", "Gen_wrong"));
        var resources = new GameResStore(new Dictionary<uint, string>
        { [SacredResourceHash.Compute("SetName2")] = "Set title" }.ToFrozenDictionary());

        var lines = new ItemDescriptionContentBuilder(resources, [new(2, 0, 0, 0, [321])]).Create(item);

        Assert.AreEqual("Authored set piece", lines[0].Text);
        Assert.AreEqual("Set title", lines[1].Text);
    }
}
