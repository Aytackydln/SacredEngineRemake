using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ObjectCallbackCleanupChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why)
        {
            if (!value) throw new Exception("Object callback cleanup: " + why);
            checks++;
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-object-callback-cleanup.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            var initial = Convert.FromHexString(row.GetProperty("initial_hex").GetString()!);
            var repeat = row.GetProperty("initial_repeat").GetInt32();
            for (var offset = 0; offset < initial.Length; offset += 68)
            {
                var record = GoldDeathScriptRegistration.FromPayload(initial.AsSpan(offset, 68));
                for (var i = 0; i < repeat; i++) world.DeathScripts.Register(record);
            }
            var beforeCount = world.DeathScripts.Registrations.Count;
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var owned = wire.ToArray();
            var entry = (GoldObjectCallbackCleanupEvent)GoldGameEventDecoder.Decode(new(0x1c1, 0, 3, 7, wire));
            Array.Fill(wire, (byte)0xcc); world.Apply(entry);
            var after = world.DeathScripts.Registrations.SelectMany(record => record.ToPayload()).ToArray();
            Check(world.DeathScripts.Registrations.Count == row.GetProperty("after_count").GetInt32(), "native remaining registration count");
            Check(Convert.ToHexString(SHA256.HashData(after)).Equals(row.GetProperty("after_sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                "native filtering, retained bytes and swap-removal order");
            if (row.GetProperty("after_hex").ValueKind == JsonValueKind.String)
                Check(after.SequenceEqual(Convert.FromHexString(row.GetProperty("after_hex").GetString()!)), "native complete resulting vector");
            Check(entry.ObjectReference == row.GetProperty("reference").GetUInt32() && entry.Message.Payload.SequenceEqual(owned), "owned reference and ignored suffix");
            Check(world.DeathScripts.CleanupNotificationCount == 1 && world.DeathScripts.RemovedRegistrationCount == beforeCount - after.Length / 68,
                "cleanup counters, including no matches");
            Check(world.DeathScripts.OversizedResetCount == (beforeCount > 60000 ? 1 : 0), "native oversized table reset guard");
            Check(world.Revision == 1 && world.Objects.Count == 0, "no runtime-object lookup required");
            Check(world.CreatureControls.PendingReplies == 0 && world.WorldBanks.Functions.PendingCount == 0, "no invented script invocation or owner reply");
            world.Clear();
            Check(world.DeathScripts.Registrations.Count == 0 && world.DeathScripts.CleanupNotificationCount == 0 &&
                world.DeathScripts.RemovedRegistrationCount == 0 && world.DeathScripts.OversizedResetCount == 0, "disconnect resets state and counters");
        }
        foreach (var row in fixture.RootElement.GetProperty("relays").EnumerateArray())
        {
            var call = row.GetProperty("helper_calls")[0];
            Check(call[0].GetUInt32() == row.GetProperty("reference").GetUInt32() && call[1].GetInt32() == 0 && call[2].GetInt32() == 1,
                "server calls cleanup with publish=false, death=true");
            Check(row.GetProperty("relay_count").GetInt32() == 1, "native server forwards each request");
        }
        return checks;
    }
}
