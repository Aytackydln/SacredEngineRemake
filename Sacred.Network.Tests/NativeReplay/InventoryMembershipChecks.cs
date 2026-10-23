using System.Text.Json;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Gameplay;

internal static class InventoryMembershipChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-inventory-membership.json")));
        var sources = new List<SacredSavedInventory>();
        foreach (var sample in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = Convert.FromHexString(sample.GetProperty("inventory_hex").GetString()!);
            var source = SacredSavedInventory.FromPayload(bytes);
            sources.Add(source);
            var mapping = GoldSavedInventoryMembership.Create(source);
            foreach (var pair in sample.GetProperty("native_membership").EnumerateObject())
            {
                mapping.TryGetValue(uint.Parse(pair.Name), out var id);
                Check(id == pair.Value.GetUInt16(), "original grid traversal and item property store parity");
            }
            Check(source.ToPayload().SequenceEqual(bytes), "membership leaves inventory source unchanged");
            Check(mapping.Count == sample.GetProperty("native_bindings").GetArrayLength(), "outside-grid and bar-only refs omitted by native binder");
        }
        var joined = GoldSavedInventoryMembership.Create(sources[0], sources[1]);
        Check(joined.Count == 3 && joined.Values.All(id => id == 17), "native later shared inventory overwrites prior membership");
        var reversed = GoldSavedInventoryMembership.Create(sources[1], sources[0]);
        Check(reversed[17] == 2 && reversed[18] == 2 && reversed[19] == 17, "binding order affects only visited grid objects");
        Check(GoldSavedInventoryMembership.Create().Count == 0, "empty inventory set");
        Console.WriteLine($"PASS: {checks} native inventory membership checks");
        return checks;
    }
}
