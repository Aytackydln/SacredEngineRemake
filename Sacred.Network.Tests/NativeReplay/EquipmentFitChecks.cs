using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentFitChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-fit.json")));
        using var selection = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-live-selection.json")));
        var json = fixture.RootElement;
        var rules = SacredEquipmentClassFitDefinitions.Read(Image(json));
        var preferences = SacredEquipmentPreferenceDefinitions.Read(Image(selection.RootElement));
        var checks = 0;
        foreach (var variant in json.GetProperty("definition_variants").EnumerateArray())
        {
            var actual = SacredEquipmentClassFitDefinitions.Read(Image(variant));
            if (actual.MinimumVariantAlias != rules.MinimumVariantAlias || !actual.ForcedUnusableResources.SetEquals(rules.ForcedUnusableResources))
                throw new Exception("Installed class-fit operands differ.");
            checks += actual.ForcedUnusableResources.Count + 1;
        }
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var flags = row.GetProperty("flags").GetUInt32();
            var skillCalls = 0;
            var variantCalls = 0;
            var fit = input.GetProperty("has_item").GetBoolean() ? SacredEquipmentClassFit.Evaluate(input.GetProperty("resource").GetUInt32(),
                input.GetProperty("hero_resource").GetUInt32(), input.GetProperty("hero_class").GetBoolean(),
                (SacredItemCategory)input.GetProperty("category").GetByte(), input.GetProperty("usage").ValueKind == JsonValueKind.Null ? null : input.GetProperty("usage").GetByte(),
                flags, rules, preferences, () =>
                {
                    variantCalls++;
                    var value = input.GetProperty("variant").GetUInt32();
                    return new(value != 0x7E60, value == 0x7E60 ? 0 : value);
                }) : new SacredEquipmentClassFitResult(false, flags);
            var bytes = new byte[SacredEquipmentInstanceLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), fit.Flags);
            var requirements = input.GetProperty("requirements").EnumerateArray().Select(value => value.GetByte()).ToArray();
            requirements.CopyTo(bytes, 20);
            var values = MemoryMarshal.Read<SacredEquipmentInstanceLayout>(bytes);
            var stats = input.GetProperty("stats").EnumerateArray().Select(value => value.GetUInt16()).ToArray();
            var allowed = SacredEquipmentFit.Requirements(fit.Allowed, input.GetProperty("class_only").GetBoolean(),
                input.GetProperty("has_values").GetBoolean() ? values : null, new(stats[2], stats[4], stats[1], stats[0], stats[3]),
                skill =>
                {
                    if (skill != requirements[6]) throw new Exception("Unexpected skill lookup during fit.");
                    skillCalls++;
                    return unchecked((byte)input.GetProperty("skill_rank").GetInt32());
                });
            if (allowed != row.GetProperty("allowed").GetBoolean() || fit.Flags != row.GetProperty("final_flags").GetUInt32() ||
                skillCalls != row.GetProperty("skill_calls").GetInt32() || variantCalls != row.GetProperty("variant_calls").GetInt32())
                throw new Exception($"Native equipment fit differs: {row}; actual allowed {allowed}, flags {fit.Flags:X8}, skill calls {skillCalls}, variant calls {variantCalls}");
            checks += 4;
        }
        return checks;
    }

    private static byte[] Image(JsonElement json)
    {
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
}
