using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class CombatStartupChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-combat-startup.json")));
        foreach (var row in fixture.RootElement.GetProperty("difficulties").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.Session.SeedAdvertisedGameFlags(row.GetProperty("flags").GetUInt32());
            Check(world.Session.Difficulty == row.GetProperty("difficulty").GetUInt32(), "Selected session difficulty differs from native startup.");
        }
        foreach (var row in fixture.RootElement.GetProperty("publications").EnumerateArray())
        {
            var damage = row.GetProperty("damage").GetUInt32();
            var packets = row.GetProperty("packets");
            Check(packets.GetArrayLength() == (damage == 0 ? 0 : 1), "Native zero damage must not be mistaken for a published miss packet.");
            if (damage == 0) continue;
            var words = packets[0].EnumerateArray().Select(v => v.GetUInt32()).ToArray();
            Check(words.SequenceEqual(new uint[] { 500, damage, 1712, 600, 0, 0 }), "Native positive damage publication operands differ.");
        }
        var creation = fixture.RootElement.GetProperty("miss_creation_arguments").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
        Check(creation.Length == 5 && creation[0] == 0x305 && creation.Skip(2).All(value => value == 0),
            "Native miss FX allocation disables general FX publication; it is not a missing 16F consumer.");
        foreach (var row in fixture.RootElement.GetProperty("fx_publication_guards").EnumerateArray())
            Check(row.GetProperty("publication_entered").GetBoolean() == (row.GetProperty("publish_operand").GetUInt32() != 0),
                "Native FX publication guard differs from the captured miss allocation operand.");
        return checks;
    }
}
