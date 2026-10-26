using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

internal static class EquipmentZeroQualityChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-zero-quality.json")));
        var fixture = source.RootElement;
        var bytes = Convert.FromHexString(fixture.GetProperty("balance_prefix_hex").GetString()!);
        var tuning = EquipmentSocketBalanceBinReader.Read(bytes);
        Check(tuning.QualityBudgets.OrderBy(pair => pair.Key).SequenceEqual(fixture.GetProperty("quality_budgets").EnumerateObject()
            .Select(pair => new KeyValuePair<uint, uint>(uint.Parse(pair.Name), pair.Value.GetUInt32())).OrderBy(pair => pair.Key)), "Third Balance map quality budgets.");
        Check(tuning.Budget(0) == 0, "Scripted zero quality reaches the native zero-budget branch.");
        Check(tuning.MaximumSockets.Count == 33 && tuning.MaximumSockets.Values.All(value => value is >= 0 and <= 4), "Original file socket pool defaults.");
        foreach (var row in fixture.GetProperty("cases").EnumerateArray())
        {
            var category = (SacredItemCategory)row.GetProperty("category").GetByte();
            var chance = row.GetProperty("chance");
            var balance = chance.ValueKind == JsonValueKind.Null ? tuning : new SacredEquipmentSocketBalance(
                chance.GetUInt32(), chance.GetUInt32(), tuning.Costs, tuning.QualityBudgets);
            var initial = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            SacredEquipmentSocketGeneration.ApplyZeroQuality(initial, category, row.GetProperty("maximum").GetInt32(), 0,
                row.GetProperty("difficulty").GetUInt32(), balance, random);
            Check(initial.SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)), "Native zero-quality item bytes.");
            Check(random.State == row.GetProperty("state").GetUInt32(), "Native zero-quality RNG consumption.");
        }
        var untouched = Enumerable.Repeat((byte)0x5a, 130).ToArray();
        var before = untouched.ToArray();
        var rng = new SacredRandomSequence(2004);
        try { SacredEquipmentSocketGeneration.ApplyZeroQuality(untouched, SacredItemCategory.Weapon, 4, 1, 0, tuning, rng); throw new Exception("Nonzero magic budget accepted."); }
        catch (InvalidOperationException) { checks++; }
        Check(rng.State == 2004 && untouched.SequenceEqual(before), "Unsupported quality rejected before RNG/mutation.");
        Check(tuning.Budget(uint.MaxValue) == 0 && tuning.SocketMaximum(uint.MaxValue) == 0, "Native missing table keys default to zero.");
        foreach (var length in new[] { 0, 3, bytes.Length - 1 })
        {
            try { EquipmentSocketBalanceBinReader.Read(bytes.AsSpan(0, length)); throw new Exception("Truncated socket balance accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        // A hostile vector count must fail before record multiplication/iteration.
        var badCount = bytes.ToArray();
        var vectorsOffset = 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(badCount);
        BinaryPrimitives.WriteUInt32LittleEndian(badCount.AsSpan(vectorsOffset), uint.MaxValue);
        try { EquipmentSocketBalanceBinReader.Read(badCount); throw new Exception("Unbounded socket pool count accepted."); }
        catch (InvalidDataException) { checks++; }
        return checks;
    }
}
