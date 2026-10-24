using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Tests.Infrastructure;

internal static class QuestDeltaPackets
{
    public static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    public static GoldGameEvent Event(ushort opcode, byte[] bytes, byte sender = 0) => GoldGameEventDecoder.Decode(new(opcode, sender, 2, 0, bytes));
    public static byte[] Words(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }
    public static byte[] Record(JsonElement row)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Bytes(row, "prefix_hex"));
        var vectors = row.GetProperty("vectors_hex");
        for (var i = 0; i < vectors.GetArrayLength(); i++)
        {
            var bytes = Convert.FromHexString(vectors[i].GetString()!);
            writer.Write(bytes.Length / (vectors.GetArrayLength() == 2 && i == 0 ? 44 : 4)); writer.Write(bytes);
        }
        if (vectors.GetArrayLength() == 3)
        {
            var children = row.GetProperty("children"); writer.Write(children.GetArrayLength());
            foreach (var child in children.EnumerateArray())
            {
                var bytes = Bytes(child, "words_hex"); writer.Write(bytes.Length / 4); writer.Write(bytes); writer.Write(Bytes(child, "fields_hex"));
            }
        }
        return stream.ToArray();
    }
    public static GoldMessage Bank(ushort opcode, params (int Section, byte[][] Records)[] sections)
    {
        var header = new byte[40]; var body = new List<byte>();
        if (opcode == 0x1b1) body.AddRange(new byte[GoldWorldBankGlobals.Size]);
        foreach (var section in sections.OrderBy(s => s.Section))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(section.Section * 4), (uint)section.Records.Length);
            foreach (var record in section.Records) body.AddRange(record);
        }
        return new(opcode, 1, 2, 0, [..header, ..body]);
    }
    public static void Seed(GoldWorldReplica world, JsonElement initial)
    {
        byte[][] Rows(string name) => initial.GetProperty(name).EnumerateArray().Select(r => Convert.FromHexString(r.GetString()!)).ToArray();
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b1, (0, Rows("journal")))));
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b3, (1, Rows("entries")))));
        world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b4,
            (1, initial.GetProperty("headers").EnumerateArray().Select(Record).ToArray()),
            (5, initial.GetProperty("database").EnumerateArray().Select(Record).ToArray()), (6, Rows("indexed")))));
    }
    public static GoldGameEvent Actor(uint flags = 0, bool container = false, bool physicalOnly = false)
    {
        var basis = new GoldObjectBaseState(500, 1, flags, 2581, 300, 400, 0, 0, 0, 0, 2, 0);
        var physical = new byte[121]; basis.ToPayload().CopyTo(physical, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(physical.AsSpan(41), GoldPhysicalStateLayout.Marker);
        BinaryPrimitives.WriteInt16LittleEndian(physical.AsSpan(GoldPhysicalStateLayout.QuestNumberOffset), -1);
        byte[] body;
        if (container) body = GoldContainerState.Create(GoldItemState.Create(GoldPhysicalState.FromPayload(physical), [], new byte[130], 0), []).ToPayload();
        else if (physicalOnly) body = physical;
        else
        {
            body = new byte[445]; physical.CopyTo(body, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(121), GoldCreatureStateLayout.Marker);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(GoldCreatureStateLayout.Word200Offset), 0xa0000000);
        }
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(container ? (ushort)0x16c : (ushort)0x16b, 1, body));
    }
}
