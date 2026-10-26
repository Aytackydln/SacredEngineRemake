using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class EquipmentMagicChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-magic.json")));
        using var ordinary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var image = new byte[fixture.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in fixture.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement) with
        { SingleMagicBonusItem = SacredEquipmentMagicDefinitions.Read(image), CategoryRules = ItemCategorySelectionChecks.ReadRules(root) };
        var generator = new WorldEquipmentOrdinaryGeneration(data);
        var checks = 0;
        var failures = new List<string>();
        foreach (var row in fixture.RootElement.GetProperty("rarity_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var quality = SacredEquipmentRaritySelection.Roll(data.Tuning.Magic!.RarityWeights, random);
            if (quality != row.GetProperty("quality").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native base rarity roll differs: {row}; actual {quality}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in fixture.RootElement.GetProperty("bonus_calls").EnumerateArray())
        {
            if (row.GetProperty("kind").GetString() == "afford")
            {
                var result = SacredEquipmentMagicBonusValue.CanAfford(row.GetProperty("code").GetUInt32(), row.GetProperty("budget").GetUInt32(),
                    row.GetProperty("jewelry").GetBoolean(), (byte)row.GetProperty("level").GetUInt32(), data.Tuning.Bonuses);
                if (result != (row.GetProperty("result").GetUInt32() != 0)) throw new Exception($"Native magic affordability differs: {row}; actual {result}");
            }
            else
            {
                var values = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
                var category = data.Templates.GetItem(BinaryPrimitives.ReadUInt32LittleEndian(values)).ModelDesc.Category;
                var cost = SacredEquipmentMagicBonusValue.Add(values, row.GetProperty("slot").GetInt32(), row.GetProperty("budget").GetInt32(), category,
                    row.GetProperty("difficulty").GetUInt32(), data.Tuning.Bonuses, data.Tuning.Normalization);
                if (unchecked((uint)cost) != row.GetProperty("result").GetUInt32() || !values.AsSpan().SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)))
                    throw new Exception($"Native magic cost differs: {row}; actual {cost}, values {Convert.ToHexString(values)}");
            }
            checks++;
        }
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var type = row.GetProperty("item_type").GetUInt32();
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var values = generator.Generate(type, row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(),
                data.Templates.GetInstance(type), random, row.GetProperty("quality").GetUInt32());
            var expected = Convert.FromHexString(row.GetProperty("result_hex").GetString()!);
            if (!values.AsSpan().SequenceEqual(expected) ||
                random.State != row.GetProperty("final_seed").GetUInt32())
                failures.Add($"{type} L{row.GetProperty("level")} D{row.GetProperty("difficulty")} Q{row.GetProperty("quality")} seed {row.GetProperty("seed")}: offsets {string.Join(',', Enumerable.Range(0, values.Length).Where(i => values[i] != expected[i]))}; expected {Convert.ToHexString(expected)}, actual {Convert.ToHexString(values)}, RNG {random.State}/{row.GetProperty("final_seed")}");
            checks += 2;
        }
        if (failures.Count != 0) throw new Exception($"{failures.Count} native magic differences:\n" + string.Join("\n", failures.Take(12)));
        var simulator = new WorldEquipmentDropSimulator(data);
        foreach (var row in fixture.RootElement.GetProperty("rolls").EnumerateArray())
        {
            var quality = row.GetProperty("requested_quality");
            var result = simulator.Roll(new(row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), row.GetProperty("seed").GetUInt32(),
                HeroClass: row.GetProperty("hero_class").GetUInt32(), CategoryMode: Enum.Parse<SacredDropCategoryMode>(row.GetProperty("category_mode").GetString()!),
                Quality: quality.ValueKind == JsonValueKind.Null ? null : quality.GetUInt32()));
            if (result.SelectedType != row.GetProperty("selected").GetUInt32() || result.RolledQuality != row.GetProperty("quality").GetUInt32() ||
                result.FinalRandomState != row.GetProperty("final_seed").GetUInt32() || !result.Values.Span.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)))
                throw new Exception($"Native category/rarity/item roll differs: {row}; actual {Convert.ToHexString(result.Values.Span)}, RNG {result.FinalRandomState}");
            checks += 4;
        }
        return checks;
    }
}
