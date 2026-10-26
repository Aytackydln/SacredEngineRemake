using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class EquipmentFactoryChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var data = EquipmentGenerationFixture.Read(root, fixture.RootElement) with { CategoryRules = ItemCategorySelectionChecks.ReadRules(root) };
        var factory = new WorldEquipmentOrdinaryGeneration(data);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var type = row.GetProperty("item_type").GetUInt32();
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var result = factory.Generate(type, row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), data.Templates.GetInstance(type), random);
            if (!result.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)))
                throw new Exception($"Complete ordinary native item values differ: {row}; actual {Convert.ToHexString(result)}, RNG {random.State}");
            if (random.State != row.GetProperty("final_seed").GetUInt32()) throw new Exception($"Complete ordinary native item RNG differs: {row}; actual {random.State}");
            checks += 2;
        }
        var selection = new WorldEquipmentSelection(data);
        foreach (var row in fixture.RootElement.GetProperty("rolls").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var selected = selection.Select((SacredItemCategory)row.GetProperty("category").GetByte(),
                new SacredEquipmentSelectionCriteria(row.GetProperty("level").GetUInt16(), 15, row.GetProperty("hero_class").GetUInt32(),
                    row.GetProperty("flags").GetByte(), row.GetProperty("exclude_sets").GetBoolean()), random);
            if ((selected ?? 0) != row.GetProperty("selected").GetUInt32() || random.State != row.GetProperty("selected_seed").GetUInt32())
                throw new Exception($"Composed native selection differs: {row}; actual {selected}, RNG {random.State}");
            checks += 2;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var type in data.Templates.Equipment.Keys) hash.AppendData(selection.Shared.Copy(type));
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            if (!digest.Equals(row.GetProperty("shared_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Persistent native shared values differ: {row}; actual hash {digest}");
            checks++;
            if (selected.HasValue)
            {
                var result = factory.Generate(selected.Value, row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), selection.Shared.Copy(selected.Value), random);
                if (!result.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)))
                    throw new Exception($"Composed native ordinary values differ: {row}; actual {Convert.ToHexString(result)}");
            }
            else if (row.GetProperty("result_hex").ValueKind != JsonValueKind.Null) throw new Exception("Native roll result missing.");
            checks++;
            if (random.State != row.GetProperty("final_seed").GetUInt32()) throw new Exception($"Composed native final RNG differs: {row}");
            checks++;
        }
        var simulator = new WorldEquipmentDropSimulator(data);
        foreach (var row in fixture.RootElement.GetProperty("fresh_rolls").EnumerateArray()
            .Concat(fixture.RootElement.GetProperty("category_rolls").EnumerateArray()))
        {
            var category = row.GetProperty("requested_category");
            var result = simulator.Roll(new(row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), row.GetProperty("seed").GetUInt32(),
                category.ValueKind == JsonValueKind.Null ? null : (SacredItemCategory)category.GetByte(), row.GetProperty("hero_class").GetUInt32(),
                row.TryGetProperty("category_mode", out var mode) ? Enum.Parse<SacredDropCategoryMode>(mode.GetString()!) : SacredDropCategoryMode.Uniform));
            if ((result.SelectedType ?? 0) != row.GetProperty("selected").GetUInt32() || (byte)result.Category != row.GetProperty("category").GetByte())
                throw new Exception($"Independent native simulator selection differs: {row}");
            checks += 2;
            var expected = row.GetProperty("result_hex").GetString();
            if (!result.Values.Span.SequenceEqual(expected is null ? [] : Convert.FromHexString(expected)) || result.FinalRandomState != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Independent native simulator values/RNG differ: {row}");
            checks += 2;
        }
        return checks;
    }
}
