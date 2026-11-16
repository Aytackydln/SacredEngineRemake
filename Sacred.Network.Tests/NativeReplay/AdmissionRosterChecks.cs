using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class AdmissionRosterChecks
{
    public static int Run(string root)
    {
        using var native = Read(root);
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var template = Convert.FromHexString(data.GetProperty("templates").GetProperty(row.GetProperty("fill").GetInt32().ToString()).GetString()!);
            var names = data.GetProperty("names").GetProperty(row.GetProperty("length").GetInt32().ToString());
            var admission = (GoldAdmissionEvent)GoldGameEventDecoder.Decode(new GoldMessage(0x11, 0, 0, 0, Bytes(row, "wire_hex")));
            var prefix = WorldAdmissionControllerPrefix.Apply(new(0, 0, 0, 0, 0), admission, true, false);
            var stage = WorldAdmissionRosterBuilder.Build(prefix, admission, GoldPlayerData.FromPayload(template),
                new(0x76543210, 7, 0x4b, row.GetProperty("connection_flags").GetUInt32(),
                    unchecked((int)row.GetProperty("lobby_id").GetUInt32()), Bytes(names, "local"), Bytes(names, "replacement")));
            var payload = stage.Roster.ToPayload();
            Check(Convert.ToHexString(SHA256.HashData(payload)).Equals(row.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "complete native submitted table hash");
            Check(stage.Roster.GetEntry(0).ToPayload().SequenceEqual(template), "staging record zero preserved");
            Check(stage.Roster.GetEntry(admission.PlayerSlot).ToPayload().SequenceEqual(Bytes(row, "assigned_hex")), "assigned row including unbounded override/profile overlap");
            Check(stage.Flag48 == row.GetProperty("flag48").GetByte() && stage.Flag3819 == row.GetProperty("flag3819").GetByte(), "native runtime flags");
            Check(stage.RequiresAcceptedContinuation, "roster stage is not complete admission");
            var copies = row.GetProperty("copies");
            Check(copies.GetArrayLength() == 2 && copies[0][0].GetUInt32() == 0x384c + admission.PlayerSlot * 12 &&
                copies[0][1].GetUInt32() == 0x3834 && copies[1][0].GetUInt32() == 0x3918 + admission.PlayerSlot * 12,
                "compared string-array assignment destinations and first source");
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Admission roster: " + why);
            checks++;
        }
    }
    public static byte[] WireExample(string root)
    {
        using var native = Read(root);
        var data = native.RootElement;
        var row = data.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("flags").GetByte() == 9 &&
            value.GetProperty("slot").GetByte() == 16 && value.GetProperty("length").GetInt32() == 258 && value.GetProperty("lobby_id").GetUInt32() == uint.MaxValue);
        var bytes = new byte[GoldPlayerRosterLayout.Size];
        Convert.FromHexString(data.GetProperty("templates").GetProperty(row.GetProperty("fill").GetInt32().ToString()).GetString()!).CopyTo(bytes, 0);
        Bytes(row, "assigned_hex").CopyTo(bytes, 16 * GoldPlayerDataLayout.Size);
        if (!data.GetProperty("other_rows_zero").GetBoolean() || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(row.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Uncompared native roster reconstruction");
        return bytes;
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-roster.json")));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
