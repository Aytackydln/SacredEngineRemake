using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class AdmissionControllerChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-admission-controller.json")));
        var data = native.RootElement;
        var checks = 0;
        Check(data.GetProperty("opcode").GetInt32() == 0x11 && data.GetProperty("selector").GetInt32() == 9 &&
            data.GetProperty("target").GetUInt32() == 0x7e1257, "original two-level dispatch route");
        Check(data.GetProperty("existing_singleton_getter").GetBoolean(), "existing getter resolves the dispatcher receiver supplied by that session");
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row, "before");
            var connection = Bytes(row, "connection_before");
            var admission = (GoldAdmissionEvent)GoldGameEventDecoder.Decode(new GoldMessage(0x11, 0, 0, 0, Bytes(row, "wire_hex")));
            var state = new WorldAdmissionControllerState(before[3], before[1], Word(before, 0x14), Word(before, 0x24), Word(connection, 0x48));
            var result = WorldAdmissionControllerPrefix.Apply(state, admission,
                row.GetProperty("present").GetBoolean() && row.GetProperty("active").GetBoolean(), row.GetProperty("flag9").GetBoolean());
            Check(result.Rejected == row.GetProperty("rejected").GetBoolean(), "native acceptance/rejection gate");
            Check(result.RequiresAcceptedContinuation != result.Rejected, "prefix acceptance retains continuation boundary");
            before[3] = result.State.PendingByte3;
            before[1] = result.State.HostByte1;
            Put(before, 0x14, result.State.LocalReference);
            Put(before, 0x24, result.State.HostToken);
            Put(connection, 0x48, result.State.ConnectionLocalReference);
            Check(before.SequenceEqual(Bytes(row, "after")), "all controller prefix bytes, including old host/token preservation");
            Check(connection.SequenceEqual(Bytes(row, "connection_after")), "connection identity write/clear with untouched bytes");
            var events = row.GetProperty("events").EnumerateArray().Select(value => value.GetString()).ToArray();
            Check(events[0] == "reset-service", "reset service precedes guard");
            Check(events.Contains("backend-query") == row.GetProperty("present").GetBoolean(), "actual wrapper absent-backend short circuit");
            Check(events.Contains("send-117") == result.Rejected && events.Contains("rejection-ui") == result.Rejected,
                "rejection-only UI/send boundary");
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Admission controller: " + why);
            checks++;
        }
    }
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-admission-controller.json")));
        return Bytes(native.RootElement.GetProperty("cases").EnumerateArray().First(row =>
            row.GetProperty("flags").GetByte() == 5 && row.GetProperty("slot").GetByte() == 16 &&
            row.GetProperty("token").GetUInt32() == 0xdeadbeef), "wire_hex");
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] data, int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
    private static void Put(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
}
