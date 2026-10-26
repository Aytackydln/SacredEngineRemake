using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Scripting;

internal static class EquipmentSlotSelectionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-slot-selection.json")));
        var code = Convert.FromHexString(fixture.RootElement.GetProperty("override_code_hex").GetString()!);
        var definitions = SacredEquipmentSlotDefinitions.Read(code);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var occupied = row.GetProperty("occupied").GetUInt32();
            var equipment = Enumerable.Range(0, 19).Select(i => (occupied & (1u << i)) != 0 ? (uint)(100 + i) : 0).ToArray();
            var available = row.GetProperty("available").GetUInt32();
            var category = (SacredItemCategory)row.GetProperty("category").GetByte();
            var usage = row.GetProperty("usage");
            byte? usageByte = usage.ValueKind == JsonValueKind.Null ? null : usage.GetByte();
            var forceLeft = definitions.ForcesLeftHand(row.GetProperty("type").GetUInt32());
            var reference = row.GetProperty("reference").GetUInt32();
            bool Available(int slot) => (available & (1u << slot)) != 0;
            var actual = WorldCreatureEquipmentSelection.Resolve(category, usageByte, forceLeft, reference, equipment, Available);
            Check((int)actual == row.GetProperty("selected").GetInt32(), $"Native preferred equipment slot differs: {row}");
            var installed = WorldCreatureEquipmentSelection.FindInstallationSlot(category, usageByte, forceLeft,
                row.GetProperty("item_reference").GetUInt32(), reference, equipment, Available);
            Check((int)installed == row.GetProperty("installed").GetInt32(), $"Native installation dispatch slot differs: {row}");
            Check(equipment.SequenceEqual(Enumerable.Range(0, 19).Select(i => (occupied & (1u << i)) != 0 ? (uint)(100 + i) : 0)),
                "Slot selection does not credit installation or mutate equipment references.");
        }
        var changed = code.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(2), 123456);
        BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(14), 234567);
        var changedDefinitions = SacredEquipmentSlotDefinitions.Read(changed);
        Check(changedDefinitions.ForcesLeftHand(123456) && changedDefinitions.ForcesLeftHand(234567), "Slot override IDs come from instructions.");
        try { SacredEquipmentSlotDefinitions.Read(code.Concat(code).ToArray()); throw new Exception("Ambiguous slot override accepted."); }
        catch (InvalidDataException) { checks++; }
        return checks;
    }
}
