using System.Text.Json;
using System.Buffers.Binary;
using Sacred.GameplayHost.Networking;

internal static class Controller117Checks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-controller-event117.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var fixture = new Controller117Fixture(row);
            WorldController117.Dispatch(fixture.Services);
            var after = row.GetProperty("after");
            var captured = WorldController117.Capture(fixture.Services);
            var header = Convert.FromHexString(after.GetProperty("header").GetString()!);
            var connectionHeader = Convert.FromHexString(after.GetProperty("connection_header").GetString()!);
            Check(captured.Identity.LocalReference == BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x14)) &&
                captured.Identity.HostedServerWindow == BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0x24)) &&
                captured.Identity.ConnectionLocalReference == (fixture.ConnectionPresent ? BinaryPrimitives.ReadUInt32LittleEndian(connectionHeader.AsSpan(0x48)) : (uint?)null) &&
                captured.Gates.Flag9 == header[9] && captured.Gates.Flag28 == header[0x28] && captured.HostedProcessActive1C == header[0x1c] &&
                captured.HostByte1 == header[1] && captured.PendingByte3 == header[3],
                "post-service identities and absent connection are distinct from the earlier admission projection");
            Check(fixture.Events.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetString()!)),
                "whole original local handler/connection clear/process/backend/UI service order");
            Check(fixture.Header.AsSpan().SequenceEqual(Convert.FromHexString(after.GetProperty("header").GetString()!)), "complete mapped controller header and callback mutations");
            Check(fixture.Tail.AsSpan().SequenceEqual(Convert.FromHexString(after.GetProperty("tail").GetString()!)), "current service-role reread after backend query");
            Check(fixture.ConnectionHeader.AsSpan().SequenceEqual(Convert.FromHexString(after.GetProperty("connection_header").GetString()!)), "connection identity, conditional session release and byte clears");
            Check(after.GetProperty("words").EnumerateArray().All(word => fixture.Word(word[0].GetUInt32()) == word[1].GetUInt32()), "all extended control/string words and retained backing");
            Check(fixture.TextFirst == after.GetProperty("text_first").GetString() && fixture.PendingCount == after.GetProperty("pending_count").GetUInt32(), "retained text terminator and controller vector extent");
            Check(fixture.ConnectionPresent == after.GetProperty("connection_present").GetBoolean(), "current connection disappearance after query");
            Check(fixture.FirstPayloads.SequenceEqual(after.GetProperty("first_payloads").EnumerateArray().Select(value => value.GetUInt32())) &&
                fixture.SecondPayloads.SequenceEqual(after.GetProperty("second_payloads").EnumerateArray().Select(value => value.GetUInt32())), "node-only frees, mutable next links and pump repopulation");
            var ui = row.GetProperty("ui").EnumerateArray().ToArray();
            Check(fixture.Ui.Count == ui.Length, "native immediate UI count");
            for (var i = 0; i < ui.Length; i++)
            {
                var actual = fixture.Ui[i]; var expected = ui[i];
                Check(actual.EventId == expected.GetProperty("event").GetUInt32() && actual.Target == expected.GetProperty("target").GetString() &&
                    actual.Mode == expected.GetProperty("mode").GetUInt32() && actual.Action == expected.GetProperty("word_10").GetUInt32() &&
                    actual.NativeWord0C == expected.GetProperty("word_c").GetUInt32(), "independent UI +0x0C/+0x10 operands retained through typed delivery");
            }
        }
        return checks;
        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Local controller 117: " + why); checks++; }
    }

    public static byte[] AdmissionWireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-admission-controller.json")));
        var row = native.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("flags").GetByte() == 0 &&
            value.GetProperty("slot").GetByte() == 1 && value.GetProperty("token").GetUInt32() == 0);
        return Convert.FromHexString(row.GetProperty("wire_hex").GetString()!);
    }
}
