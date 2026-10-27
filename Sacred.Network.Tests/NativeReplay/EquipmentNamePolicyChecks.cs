using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Equipment;
using Sacred.Core.GameBin.Sets;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class EquipmentNamePolicyChecks
{
    public static int Run(SacredEquipmentGenerationData data, JsonElement source)
    {
        // Preserve original catalogue membership, including pieces with SetType zero.
        var sets = source.GetProperty("authored").EnumerateArray().Where(row => row.GetProperty("is_set").GetBoolean())
            .GroupBy(row => row.GetProperty("set_index").GetInt32())
            .Select(group => new SacredSetEntry(group.Key, 0, 0, 0,
                group.Select(row => row.GetProperty("item_type").GetUInt32()).ToArray())).ToArray();
        var names = new WorldEquipmentNames(data with { Sets = sets });
        var checks = 0;
        foreach (var row in source.GetProperty("authored").EnumerateArray())
        {
            var type = row.GetProperty("item_type").GetUInt32();
            var values = data.Templates.GetInstance(type);
            var before = values.ToArray();
            var random = new SacredRandomSequence(2004);
            var actual = names.Generate(values, type, random);
            if (actual.Text != row.GetProperty("text").GetString() || actual.GeneratedKey is not null ||
                random.State != 2004 || !values.AsSpan().SequenceEqual(before))
                throw new Exception($"Authored item {type} was renamed or consumed naming RNG: {actual}");
            checks += 4;
        }

        var sword = data.Templates.Equipment.Keys.First(type =>
            data.Modifiers.Find(type)?.Definition.WeaponType == (int)SacredEquipmentType.Sword &&
            !sets.Any(set => set.ItemIds.Contains(type)) && data.Templates.Equipment[type].SetType == 0);
        var fire = OrdinaryValues(sword, 802, 40);
        var magic = OrdinaryValues(sword, 803, 40);
        var fireName = names.Generate(fire, sword, new(2004));
        var magicName = names.Generate(magic, sword, new(2004));
        if (fireName.GeneratedKey is null || magicName.GeneratedKey is null || fireName.Text == magicName.Text)
            throw new Exception($"Changing only the elemental modifier did not change the name: {fireName}; {magicName}");
        checks++;

        var weak = System.Runtime.InteropServices.MemoryMarshal.Read<SacredEquipmentInstanceLayout>(OrdinaryValues(sword, 0x10000 | 802, 1));
        var strong = System.Runtime.InteropServices.MemoryMarshal.Read<SacredEquipmentInstanceLayout>(OrdinaryValues(sword, 0x10000 | 802, 30000));
        if (SacredEquipmentNameBonuses.Rank(strong, false, data.Tuning.Bonuses) <=
            SacredEquipmentNameBonuses.Rank(weak, false, data.Tuning.Bonuses))
            throw new Exception("Stronger modifiers did not increase the generated-name variant rank.");
        checks++;
        return checks;
    }

    private static byte[] OrdinaryValues(uint type, uint bonus, short amount)
    {
        var values = new byte[SacredEquipmentInstanceLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(values, type);
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), 8 << 16);
        BinaryPrimitives.WriteInt16LittleEndian(values.AsSpan(28), 10);
        BinaryPrimitives.WriteInt16LittleEndian(values.AsSpan(36), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(74), bonus);
        BinaryPrimitives.WriteInt16LittleEndian(values.AsSpan(106), amount);
        return values;
    }
}
