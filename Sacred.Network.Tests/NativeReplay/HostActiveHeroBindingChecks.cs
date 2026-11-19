using System.Text.Json;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostActiveHeroBindingChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-host-active-hero-binding.json")));
        var data = proof.RootElement;
        var actor = GoldCreatureState.FromPayload(Convert.FromHexString(data.GetProperty("hero_payload_hex").GetString()!));
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            // Fixture slots preserve the actual native manager's logical length.
            // Production uses its existing factory, never this replay slot array.
            var slots = new GoldCreatureState?[data.GetProperty("logical_slots").GetInt32()];
            if (row.GetProperty("slot_present").GetBoolean()) slots[1] = actor;
            var resolved = WorldActiveHeroLookup.Resolve<GoldCreatureState, GoldCreatureState>(
                row.GetProperty("controller_reference").GetUInt32(), slots,
                value => value.BaseState.ObjectReference,
                (_, _) => throw new Exception("The current factory actor must not need reference repair."),
                (_, _) => throw new Exception("The current factory actor must not report an identity mismatch."),
                value => value);
            var native = row.GetProperty("resolved_reference");
            uint? expected = native.ValueKind == JsonValueKind.Null ? null : native.GetUInt32();
            if (resolved?.BaseState.ObjectReference != expected)
                throw new Exception("Current host active-hero lookup mismatch: " + row.GetProperty("label").GetString());
            checks++;
        }
        return checks;
    }
}
