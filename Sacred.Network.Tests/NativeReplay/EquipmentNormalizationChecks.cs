using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentNormalizationChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-normalization.json")));
        var file = Convert.FromHexString(fixture.RootElement.GetProperty("balance_prefix_hex").GetString()!);
        var factors = EquipmentNormalizationBalanceBinReader.Read(file);
        var curves = EquipmentGenerationBalanceBinReader.ReadBonusCurves(file);
        var sockets = EquipmentSocketBalanceBinReader.Read(file);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var initial = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            var actual = SacredEquipmentBonusNormalization.Apply(initial, (SacredItemCategory)row.GetProperty("category").GetByte(), curves, factors, sockets);
            var expected = Convert.FromHexString(row.GetProperty("result_hex").GetString()!);
            Check(initial.SequenceEqual(expected), $"Native normalization bytes differ: {row}; got {Convert.ToHexString(initial)}");
            Check(actual.Rarity == row.GetProperty("rarity").GetUInt32(), "Native normalization rarity return.");
            Check((actual.Cost ?? 0x12345678) == row.GetProperty("cost").GetInt32(), "Native normalization cost output / untouched unique output.");
        }
        foreach (var length in new[] { 0, 3, 4 + 0x9D8 })
        {
            var truncated = file.AsSpan(0, length).ToArray();
            if (length >= 4) BinaryPrimitives.WriteUInt32LittleEndian(truncated, (uint)length - 4);
            try { EquipmentNormalizationBalanceBinReader.Read(truncated); throw new Exception("Truncated normalization factors accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }
}
