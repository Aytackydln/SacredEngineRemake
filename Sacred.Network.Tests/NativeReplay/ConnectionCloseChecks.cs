using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class ConnectionCloseChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-connection-close.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = Enumerable.Repeat((byte)0xa5, 0x40468).ToArray();
            var memory = new WorldControlMemory(at => bytes[at], (at, value) => bytes[at] = value,
                at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
                (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
                (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
            var backend = row.GetProperty("present").GetBoolean() ? 0x6082000u : 0;
            var mutation = row.GetProperty("mutation").GetString(); var count = row.GetProperty("count").GetUInt32();
            memory.WriteWord(8, 101); memory.WriteWord(0xc, 102); memory.WriteWord(0x10, 103);
            memory.WriteWord(0x14, row.GetProperty("socket").GetUInt32()); memory.WriteWord(0x18, row.GetProperty("ready").GetUInt32());
            memory.WriteWord(0x24, 0x6080000);
            memory.WriteWord(0x4044c, 0x6090000); memory.WriteWord(0x40450, 0x6090000 + count * 52); memory.WriteWord(0x40454, 0x6090000 + count * 52);
            var events = new List<object[]>();
            var services = new WorldConnectionCloseServices(memory, () => backend, target =>
            {
                events.Add(["optional", target, 0]); if (mutation == "optional-replace") backend = 0x6081000;
            }, value => backend = value, () => events.Add(["clear"]), handle =>
            {
                events.Add(["handle", handle]); if (mutation == "handle-next") memory.WriteWord(0xc, 777);
            }, socket =>
            {
                events.Add(["shutdown", socket, 2]); if (mutation == "socket-remove") memory.WriteWord(0x14, uint.MaxValue);
            }, socket => events.Add(["socket-close", socket]), () => events.Add(["cleanup"]), (target, offset) =>
            {
                events.Add(["transport", target, offset]); if (offset == 0x3c && mutation == "transport-replace") memory.WriteWord(0x24, 0x6081000);
            }, () =>
            {
                for (uint index = 1; index <= count; index++) events.Add(["free", 0x6091000 + index * 16]);
            }, () =>
            {
                for (uint index = 1; index <= count; index++) events.Add(["erase", 0x6091100 + index * 16, 1]);
            }, () =>
            {
                if (count == 0) return;
                events.Add(["records"]); for (uint index = 0; index < count; index++) events.Add(["destroy", index]);
                memory.WriteWord(0x40450, 0x6090000);
            }, () =>
            {
                events.Add(["log", memory.ReadByte(0x2a)]); if (mutation == "log-active") memory.WriteByte(0x2a, 0x7f);
            });
            WorldConnectionClose.Close(services);
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "service order/current operands");
            Check(bytes.AsSpan(0, 0x50).SequenceEqual(Convert.FromHexString(row.GetProperty("header").GetString()!)), "header");
            Check(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(4, 0x40410))).Equals(row.GetProperty("reset_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "full bulk reset");
            Check(backend == row.GetProperty("backend").GetUInt32() && (memory.ReadWord(0x40450) - 0x6090000) / 52 == row.GetProperty("pending").GetUInt32(), "global and pending ownership");
            Check(bytes.AsSpan(0x40460, 8).SequenceEqual(Convert.FromHexString(row.GetProperty("tail").GetString()!)), "tail flags");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Connection close {mutation}: {why}"); checks++; }
        }
        return checks;
    }
}
