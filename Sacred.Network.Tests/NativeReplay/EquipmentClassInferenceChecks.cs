using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentClassInferenceChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-class-inference.json")));
        var patterns = fixture.RootElement.GetProperty("patterns").EnumerateArray().ToDictionary(row => row[0].GetUInt32(), row => row[1].GetString()!);
        var code = Convert.FromHexString(fixture.RootElement.GetProperty("code_hex").GetString()!);
        var rules = SacredEquipmentClassDefinitions.ReadCode(code, fixture.RootElement.GetProperty("address").GetUInt32(), type => patterns[type]);
        var bases = fixture.RootElement.GetProperty("base_items").EnumerateArray().ToDictionary(row => row[0].GetUInt32(), row => row[1].GetUInt32());
        using var namesFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-modifier-lookup.json")));
        var names = SacredItemResourceNames.Read(Convert.FromHexString(namesFixture.RootElement.GetProperty("resource_names_hex").GetString()!));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = new byte[SacredEquipmentInstanceLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, row.GetProperty("item_type").GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), row.GetProperty("flags").GetUInt32());
            var parents = row.GetProperty("parents").EnumerateArray().ToDictionary(pair => pair[0].GetUInt32(), pair => pair[1].GetUInt32());
            var customName = row.GetProperty("name").GetString();
            var result = SacredEquipmentClassInference.Apply(bytes, rules,
                type => parents.TryGetValue(type, out var parent) ? parent : bases.TryGetValue(type, out parent) ? parent : null,
                type => customName ?? names.Find(type));
            if (result != row.GetProperty("native_return").GetBoolean() || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != row.GetProperty("result_flags").GetUInt32())
                throw new Exception($"Native class inference differs: {row}; actual {Convert.ToHexString(bytes.AsSpan(0, 8))}, {result}");
            checks += 2;
        }
        return checks;
    }
}
