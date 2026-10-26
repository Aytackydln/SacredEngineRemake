using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.EquipmentVariants;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentVariantChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-variants.json")));
        using var classFixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-class-inference.json")));
        var patterns = classFixture.RootElement.GetProperty("patterns").EnumerateArray().ToDictionary(row => row[0].GetUInt32(), row => row[1].GetString()!);
        var rules = SacredEquipmentClassDefinitions.ReadCode(Convert.FromHexString(classFixture.RootElement.GetProperty("code_hex").GetString()!),
            classFixture.RootElement.GetProperty("address").GetUInt32(), type => patterns[type]);
        var bytes = Convert.FromHexString(fixture.RootElement.GetProperty("rust_hex").GetString()!);
        var profiles = EquipmentVariantsBinArchive.Read(bytes);
        var gate = SacredEquipmentSelectionClassGateDefinitions.ReadCode(Convert.FromHexString(fixture.RootElement.GetProperty("gate_code_hex").GetString()!),
            fixture.RootElement.GetProperty("replacement_source_minimum").GetUInt32(), fixture.RootElement.GetProperty("marker").GetString()!);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var descriptorBytes = new byte[ItemsPakEntryModelDescLayout.SerializedSize];
            descriptorBytes[46] = row.GetProperty("category").GetByte();
            BinaryPrimitives.WriteUInt16LittleEndian(descriptorBytes.AsSpan(116), row.GetProperty("profile").GetUInt16());
            var descriptor = MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(descriptorBytes);
            var result = SacredEquipmentVariantLookup.ResolveCanonicalSource(1000, row.GetProperty("canonical").GetUInt32(),
                gate.MinimumReplacementSource, rules, profiles, _ => null, _ => descriptor);
            if (result.Supported != row.GetProperty("supported").GetBoolean() || result.ReplacementType != row.GetProperty("replacement").GetUInt32())
                throw new Exception($"Native equipment variant differs: {row}; actual {result}");
            checks += 2;
        }
        foreach (var damaged in new[] { bytes[..3], bytes[..^1], bytes.Concat(new byte[1]).ToArray() })
        {
            try { EquipmentVariantsBinArchive.Read(damaged); throw new Exception("Invalid Rust.bin accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }
}
