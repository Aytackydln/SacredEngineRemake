using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class ItemCategorySelectionChecks
{
    public static SacredItemCategoryRules ReadRules(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-item-category-selection.json")));
        return SacredItemCategoryDefinitions.Read(ReadImage(fixture.RootElement));
    }

    private static byte[] ReadImage(JsonElement source)
    {
        var image = new byte[source.GetProperty("image_size").GetInt32()];
        foreach (var chunk in source.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }

    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-item-category-selection.json")));
        var image = ReadImage(fixture.RootElement);
        var rules = SacredItemCategoryDefinitions.Read(image);
        using var factory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with { CategoryRules = rules };
        var selector = new WorldDropCategorySelection(data);
        var nativeMasks = fixture.RootElement.GetProperty("availability").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
        var checks = 0;
        foreach (var (category, mask) in selector.Availability)
        {
            if (mask != nativeMasks[(byte)category]) throw new Exception($"Initial native category availability differs: {category}, actual {mask:X}, expected {nativeMasks[(byte)category]:X}");
            checks++;
        }
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var mode = Enum.Parse<SacredDropCategoryMode>(row.GetProperty("mode").GetString()!);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var hero = row.GetProperty("hero_class").GetUInt32();
            SacredItemCategory actual;
            if (mode == SacredDropCategoryMode.NativeWeighted)
            {
                var scenario = row.GetProperty("scenario").GetString();
                var masks = rules.Weights.ToDictionary(entry => entry.Category, entry => scenario switch
                {
                    "installed" => nativeMasks[(byte)entry.Category],
                    "all" => 0xC0003FU,
                    "single" when entry.Category == SacredItemCategory.Ring => 1U,
                    _ => 0U
                });
                actual = SacredItemCategorySelection.Weighted(rules, masks, hero, random);
            }
            else actual = selector.Select(mode, hero, random);
            if ((byte)actual != row.GetProperty("category").GetByte() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native category choice/RNG differs: {row}; actual {actual}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in fixture.RootElement.GetProperty("equipment_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var category = selector.SelectEquipment(Enum.Parse<SacredDropCategoryMode>(row.GetProperty("mode").GetString()!),
                row.GetProperty("hero_class").GetUInt32(), random);
            if ((byte)category != row.GetProperty("category").GetByte() || random.State != row.GetProperty("final_seed").GetUInt32() ||
                !WorldEquipmentDropSimulator.Categories.Contains(category))
                throw new Exception($"Equipment-only category scope differs: {row}; actual {category}, RNG {random.State}");
            checks += 3;
        }
        // Changing original data must change the extracted weights, rather than
        // silently falling back to a hardcoded distribution.
        var weightChunk = fixture.RootElement.GetProperty("definition_chunks")[2];
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(weightChunk.GetProperty("offset").GetInt32() + 4), 137);
        if (SacredItemCategoryDefinitions.Read(image).Weights[0].Weight != 137) throw new Exception("Category weights were not read from original data.");
        checks++;
        return checks;
    }
}
