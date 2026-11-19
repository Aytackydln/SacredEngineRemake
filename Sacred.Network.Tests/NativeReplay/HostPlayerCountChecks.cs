using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class HostPlayerCountChecks
{
    public static int Run(string root)
    {
        using var proof = Read(root);
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var replicas = new GoldWorldReplica();
            if (replicas.Session.Lobby.Control.Roster.NativePlayerCount is not null)
                throw new Exception("Unknown roster must not become an absent-player profile.");
            var message = new GoldMessage(0x1b, 0, 0, 1, Convert.FromHexString(row.GetProperty("roster_hex").GetString()!));
            replicas.Apply(GoldGameEventDecoder.Decode(message));
            var count = row.GetProperty("connection_present").GetBoolean() && row.GetProperty("connection_active").GetBoolean()
                ? replicas.Session.Lobby.Control.Roster.NativePlayerCount : 0;
            if (count != row.GetProperty("count").GetUInt32()) throw new Exception("Native roster-address player count mismatch.");
            replicas.Clear();
            if (replicas.Session.Lobby.Control.Roster.NativePlayerCount is not null) throw new Exception("Cleared roster retained a player count.");
            checks += 3;
        }
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var proof = Read(root);
        return Convert.FromHexString(proof.RootElement.GetProperty("cases")[2].GetProperty("roster_hex").GetString()!);
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-player-count.json")));
}
