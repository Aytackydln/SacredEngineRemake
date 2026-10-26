using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.WeaponModifiers;
using Sacred.Core.GameBin.WeaponModifiers;

internal static class EquipmentModifierLookupChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-modifier-lookup.json")));
        var code = Convert.FromHexString(fixture.RootElement.GetProperty("selection_code_hex").GetString()!);
        var definitions = SacredEquipmentSelectionDefinitions.Read(code);
        var modifiers = WeaponModifiersBinArchive.Parse(Convert.FromHexString(fixture.RootElement.GetProperty("wpmod_hex").GetString()!));
        var bases = fixture.RootElement.GetProperty("base_items").EnumerateArray().ToDictionary(row => row[0].GetUInt32(), row => row[1].GetUInt32());
        var resolver = new SacredEquipmentModifierResolver(modifiers, bases, definitions.ModifierRedirects);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var actual = resolver.Find(row.GetProperty("item_type").GetUInt32());
            var expected = row.GetProperty("record");
            Check(actual?.RecordIndex == (expected.ValueKind == JsonValueKind.Null ? (int?)null : expected.GetInt32()),
                $"Native equipment modifier association differs: {row}; actual {actual?.RecordIndex}");
        }
        // Resource ranges come from original instructions, including changed data.
        var altered = code.ToArray();
        for (var i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(altered.AsSpan(32 + i * 8 + 2), (uint)(100 + i * 100));
        var changed = SacredEquipmentSelectionDefinitions.Read(altered);
        Check(changed.ModifierRedirects.RedirectSource(101) && !changed.ModifierRedirects.RedirectSource(100) &&
            changed.ModifierRedirects.RejectTarget(100) && changed.ModifierRedirects.RejectTarget(200), "Modifier bounds must be extracted with original strict/inclusive comparisons.");
        try { SacredEquipmentSelectionDefinitions.Read(code.Concat(code).ToArray()); throw new Exception("Ambiguous selector signatures accepted."); }
        catch (InvalidDataException) { checks++; }
        return checks;
    }
}
