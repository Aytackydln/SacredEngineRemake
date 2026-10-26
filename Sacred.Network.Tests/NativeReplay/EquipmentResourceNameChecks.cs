using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Executable;

internal static class EquipmentResourceNameChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-modifier-lookup.json")));
        var source = Convert.FromHexString(fixture.RootElement.GetProperty("resource_names_hex").GetString()!);
        var names = SacredItemResourceNames.Read(source);
        foreach (var row in fixture.RootElement.GetProperty("resource_name_cases").EnumerateArray())
        {
            if (names.Find(row.GetProperty("item_type").GetUInt32()) != row.GetProperty("name").GetString())
                throw new Exception($"Original symbolic resource name differs: {row}");
            checks++;
        }
        if (names.Find(uint.MaxValue) != null) throw new Exception("Static names invented a runtime fallback.");
        checks++;
        foreach (var damaged in new[] { source[..^1], source.Concat(source).ToArray(), source[..(SacredExecutableTypeNameLayout.SerializedSize - 1)] })
        {
            try { SacredItemResourceNames.Read(damaged); throw new Exception("Invalid resource name table accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }
}
