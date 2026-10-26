using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Core.GameRes;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class EquipmentNameChecks
{
    public static SacredEquipmentNamingData ReadNaming(JsonElement source)
    {
        var image = new byte[source.GetProperty("image_size").GetInt32()];
        foreach (var chunk in source.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var strings = source.GetProperty("resources").EnumerateObject().ToFrozenDictionary(row => uint.Parse(row.Name), row => row.Value.GetString()!);
        return new(SacredEquipmentNameDefinitions.Read(image), new GameResStore(strings));
    }

    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-names.json")));
        using var ordinary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement) with { Naming = ReadNaming(fixture.RootElement) };
        var names = new WorldEquipmentNames(data);
        var checks = 0;
        var failures = new List<string>();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var name = names.Generate(bytes, row.GetProperty("visual").GetUInt32(), random);
            if (name.NativeReference != row.GetProperty("native_reference").GetUInt32() || name.Text != row.GetProperty("text").GetString() ||
                random.State != row.GetProperty("final_seed").GetUInt32() || !bytes.AsSpan().SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)))
                failures.Add($"Native item name differs: {row}; actual {name}, RNG {random.State}, values {Convert.ToHexString(bytes)}");
            checks += 4;
        }
        foreach (var row in fixture.RootElement.GetProperty("authored").EnumerateArray())
        {
            var actual = names.Existing(row.GetProperty("item_type").GetUInt32());
            if (actual.Text != row.GetProperty("text").GetString()) failures.Add($"Native authored name differs: {row}; actual {actual}");
            checks++;
        }
        using var magic = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-magic.json")));
        var image = new byte[magic.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var chunk in magic.RootElement.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var simulator = new WorldEquipmentDropSimulator(data with
            { SingleMagicBonusItem = SacredEquipmentMagicDefinitions.Read(image), CategoryRules = ItemCategorySelectionChecks.ReadRules(root) });
        foreach (var row in fixture.RootElement.GetProperty("composed").EnumerateArray())
        {
            var category = row.GetProperty("requested_category");
            var quality = row.TryGetProperty("requested_quality", out var requestedQuality) ? requestedQuality : default;
            var request = new SacredEquipmentDropRequest(row.GetProperty("level").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), row.GetProperty("seed").GetUInt32(),
                Category: category.ValueKind == JsonValueKind.Null ? null : (SacredItemCategory)category.GetByte(),
                HeroClass: row.TryGetProperty("hero_class", out var hero) ? hero.GetUInt32() : 0,
                CategoryMode: row.TryGetProperty("category_mode", out var mode) ? Enum.Parse<SacredDropCategoryMode>(mode.GetString()!) : SacredDropCategoryMode.Uniform,
                Quality: quality.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : quality.GetUInt32());
            if (row.TryGetProperty("profile", out var profile)) request = request with
            {
                RarityContext = new(Enum.Parse<SacredLootRarityProfile>(profile.GetString()!), row.GetProperty("equipment_bonus").GetSingle(),
                    row.GetProperty("survival_seconds").GetUInt32(), row.GetProperty("source_level").GetUInt16(), row.GetProperty("creature_bonus").GetUInt16())
            };
            var actual = simulator.Roll(request);
            var selected = row.GetProperty("selected");
            uint? expectedType = selected.ValueKind == JsonValueKind.Null || selected.GetUInt32() == 0 ? null : selected.GetUInt32();
            var expectedBytes = row.GetProperty("result_hex").ValueKind == JsonValueKind.Null ? [] : Convert.FromHexString(row.GetProperty("result_hex").GetString()!);
            if (actual.SelectedType != expectedType || !actual.Values.Span.SequenceEqual(expectedBytes) || actual.FinalRandomState != row.GetProperty("final_seed").GetUInt32() ||
                expectedType.HasValue && (actual.Name?.NativeReference != row.GetProperty("native_reference").GetUInt32() || actual.Name?.Text != row.GetProperty("text").GetString()))
                failures.Add($"Native named roll differs: {row}; actual {actual}, name {actual.Name}, values {Convert.ToHexString(actual.Values.Span)}");
            checks += 5;
        }
        if (failures.Count != 0) throw new Exception($"{failures.Count} native item-name differences:\n" + string.Join("\n", failures.Take(8)));
        return checks;
    }
}
