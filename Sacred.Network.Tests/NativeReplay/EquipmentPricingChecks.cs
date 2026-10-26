using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentPricingChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-pricing.json")));
        var file = Convert.FromHexString(fixture.RootElement.GetProperty("balance_prefix_hex").GetString()!);
        var pricing = EquipmentPricingBalanceBinReader.Read(file);
        var generation = EquipmentGenerationBalanceBinReader.Read(file);
        var factors = EquipmentNormalizationBalanceBinReader.Read(file);
        var curves = EquipmentGenerationBalanceBinReader.ReadBonusCurves(file);
        var sockets = EquipmentSocketBalanceBinReader.Read(file);
        var rules = fixture.RootElement.GetProperty("rules_hex").EnumerateArray()
            .Select(row => MemoryMarshal.Read<SacredWeaponModifierLayout>(Convert.FromHexString(row.GetString()!))).ToArray();
        var overrideCode = Convert.FromHexString(fixture.RootElement.GetProperty("override_code_hex").GetString()!);
        var definitions = SacredEquipmentPriceDefinitions.Read(overrideCode);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var initial = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            var ruleIndex = row.GetProperty("rule").GetInt32();
            var rule = ruleIndex >= 0 ? rules[ruleIndex] : default;
            var overrides = definitions.Overrides(BinaryPrimitives.ReadUInt32LittleEndian(initial));
            var context = new SacredEquipmentPriceContext((SacredItemCategory)row.GetProperty("category").GetByte(), rule.ItemId0 != 0,
                rule.Modifier, row.GetProperty("set_type").GetByte(), overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
            var actual = SacredEquipmentPricing.CalculateOrdinary(initial, context, pricing, generation, curves, factors, sockets);
            Check(actual == row.GetProperty("price").GetUInt32(), $"Native item price differs: got {actual}; fixture {row}");
            Check(initial.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)),
                $"Native price side effects differ: got {Convert.ToHexString(initial)}; fixture {row}");
        }
        var altered = overrideCode.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(altered.AsSpan(1), 123456);
        BinaryPrimitives.WriteUInt32LittleEndian(altered.AsSpan(12), 234567);
        BinaryPrimitives.WriteUInt32LittleEndian(altered.AsSpan(19), 345678);
        var alteredDefinitions = SacredEquipmentPriceDefinitions.Read(altered);
        Check(alteredDefinitions.Overrides(123456).ClearClassRestrictions && alteredDefinitions.Overrides(234567).ClearLevelAndSkillRequirements &&
            alteredDefinitions.Overrides(345678).ClearLevelAndSkillRequirements, "Resource overrides are extracted, not hardcoded.");
        try { SacredEquipmentPriceDefinitions.Read(overrideCode.Concat(overrideCode).ToArray()); throw new Exception("Ambiguous price overrides accepted."); }
        catch (InvalidDataException) { checks++; }
        foreach (var length in new[] { 0, 3, 4 + 0x11EC })
        {
            var truncated = file.AsSpan(0, length).ToArray();
            if (length >= 4) BinaryPrimitives.WriteUInt32LittleEndian(truncated, (uint)length - 4);
            try { EquipmentPricingBalanceBinReader.Read(truncated); throw new Exception("Truncated pricing factors accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }
}
