using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class LootSpecialResourceChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-loot-special-resources.json")));
        using var factory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        using var rewards = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-death-rewards.json")));
        var image = new byte[rewards.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in rewards.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with { CreatureDeathRewardRules = SacredCreatureDeathRewardDefinitions.Read(image) };
        var json = fixture.RootElement;
        var original = Convert.FromHexString(factory.RootElement.GetProperty("weapon_hex").GetString()!);
        var checks = 0;
        if (data.Templates.EquipmentRows.Count != BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4))) throw new Exception("Original equipment row count differs.");
        checks++;
        Span<byte> values = stackalloc byte[130];
        foreach (var row in json.GetProperty("shared_cases").EnumerateArray())
        {
            var found = data.Templates.TryGetSharedTemplate(row.GetProperty("resource").GetUInt32(), out var template);
            var expected = row.GetProperty("row");
            if (found != (expected.ValueKind != JsonValueKind.Null)) throw new Exception($"Native shared-template presence differs: {row}");
            if (found)
            {
                var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref template, 1));
                if (!bytes.SequenceEqual(original.AsSpan(256 + expected.GetInt32() * bytes.Length, bytes.Length)))
                    throw new Exception($"Native shared row binding differs: {row}");
                MemoryMarshal.Write(values, in template.InstanceValues);
                if (!values.SequenceEqual(bytes[128..])) throw new Exception("Shared instance overlay differs from original bytes.");
                checks += 2;
            }
            checks++;
        }
        var families = SacredEquipmentModifierFamilies.Build(data.Templates.EquipmentRows.Select(row => row.ItemId), type => data.Modifiers.Find(type)?.Definition.WeaponType);
        var nativeFamilies = json.GetProperty("families");
        if (families.Count != nativeFamilies.GetArrayLength()) throw new Exception("Native modifier family count differs.");
        checks++;
        foreach (var family in nativeFamilies.EnumerateArray())
        {
            var key = family.GetProperty("key").GetInt32();
            if (!families.TryGetValue(key, out var members) || !members.SequenceEqual(family.GetProperty("resources").EnumerateArray().Select(value => value.GetUInt32())))
                throw new Exception($"Native modifier family order differs: {key}");
            checks += members.Count + 1;
        }
        var selector = new WorldEquipmentUniqueVariants(data, new(data));
        foreach (var row in json.GetProperty("unique_cases").EnumerateArray())
        {
            var key = row.GetProperty("key").GetInt32();
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var actual = selector.Select(families[key][0], row.GetProperty("level").GetUInt16(), random);
            var expected = row.GetProperty("resource");
            if (actual != (expected.ValueKind == JsonValueKind.Null ? null : expected.GetUInt32()) || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native unique resource/RNG differs: {row}; actual {actual}, RNG {random.State}");
            checks += 2;
        }
        return checks;
    }
}
