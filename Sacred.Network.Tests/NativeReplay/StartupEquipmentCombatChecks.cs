using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;

internal static class StartupEquipmentCombatChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-startup-equipment-combat.json")));
        using var ordinary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement);
        var failures = new List<string>();
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        foreach (var output in row.GetProperty("outputs").EnumerateArray())
        {
            var type = row.GetProperty("type").GetUInt32();
            var basis = type;
            for (var link = 0; link < data.Classes.MaximumParentLinks && data.Classes.ParentResources.Any(range => range.Contains(basis)); link++)
                basis = data.ParentResource(basis) ?? 0;
            var modifier = data.Modifiers.Find(basis) ?? throw new Exception("Native ordinary modifier missing.");
            var category = data.Templates.GetItem(type).ModelDesc.Category;
            var original = data.Templates.GetInstance(type);
            var values = new byte[SacredEquipmentInstanceLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(values, basis);
            var flags = category is SacredItemCategory.Weapon or SacredItemCategory.Shield or SacredItemCategory.Ring
                ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(4)) & 0xc0003f;
            flags |= (unchecked((uint)modifier.Definition.WeaponType) & 255) << 8;
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), flags);
            var mode = output.GetProperty("precision").GetInt32() == 24 ? SacredX87Precision.Single : SacredX87Precision.Extended;
            var level = data.Tuning.Levels.ResolveLevel(row.GetProperty("level").GetUInt32(), modifier.Definition.MinimumLevel, mode);
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            SacredEquipmentCombatGeneration.Apply(values, modifier.Definition, level.CombatFactor, row.GetProperty("difficulty").GetUInt32(), random, mode);
            var expected = Convert.FromHexString(output.GetProperty("values_hex").GetString()!);
            if (!values.AsSpan().SequenceEqual(expected) || random.State != output.GetProperty("rng_after").GetUInt32())
                failures.Add($"type {type}, PC={output.GetProperty("precision")}: bytes [{string.Join(',', Enumerable.Range(0, values.Length).Where(at => values[at] != expected[at]))}], RNG {random.State}/{output.GetProperty("rng_after")}");
            checks += 2;
        }
        if (failures.Count != 0) throw new Exception($"{failures.Count} native startup combat differences:\n" + string.Join('\n', failures.Take(12)));
        return checks;
    }
}
