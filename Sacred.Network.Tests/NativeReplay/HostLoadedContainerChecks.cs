using System.Buffers.Binary;
using System.Text.Json;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.World;

internal static class HostLoadedContainerChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        foreach (var name in new[] { "native-host-loaded-container.json", "native-host-elevated-container.json" })
        {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name)));
        var source = proof.RootElement;
        var surfaces = name.Contains("elevated") ? ReadSurfaces(source) : null;
        foreach (var row in source.GetProperty("cases").EnumerateArray())
        {
            var native = Bytes(row, "runtime_hex");
            var positionBytes = Bytes(row, "position_hex");
            var position = new GoldObjectPosition(Int(positionBytes, 4), Int(positionBytes, 8), positionBytes[12], Short(positionBytes, 0));
            var state = new GoldObjectBaseState(row.GetProperty("reference").GetUInt32(), source.GetProperty("type").GetUInt32(),
                0x10002, 0, 0, 0, 0, 0, 0, row.GetProperty("counter_after").GetByte(), row.GetProperty("owner").GetByte(), 0);
            var value = new WorldScriptCreatedObject(source.GetProperty("source_offset").GetInt32(), null!, state, Bytes(source, "initial_hex"));
            var activation = new WorldObjectActivationQueue();
            activation.ActivateInitial(value.Node, 1);
            var tileBytes = Bytes(row.TryGetProperty("tile_hex", out _) ? row : source, "tile_hex");
            BinaryPrimitives.WriteUInt32LittleEndian(tileBytes.AsSpan(8), row.GetProperty("head_before").GetUInt32());
            var tile = WldxTileRecord.FromBytes(tileBytes);
            uint committed = 0;
            var floor = surfaces?.Child(surfaces.FindParent(source.GetProperty("tile")[0].GetInt32(), source.GetProperty("tile")[1].GetInt32()), checked((ushort)position.Surface)) ?? 0;
            var trigger = surfaces?.GetStatic(surfaces.FindParent(source.GetProperty("tile")[0].GetInt32(), source.GetProperty("tile")[1].GetInt32()))?.TriggerId ?? 0;
            WorldScriptContainerPlacement.PlaceOnFloor(value, new(position, floor, trigger, tile, reference => committed = reference));
            var expected = new GoldObjectBaseState(Word(native, 12), Word(native, 16), Word(native, 20), Short(native, 24),
                Int(native, 28), Int(native, 32), native[36], Word(native, 40), Word(native, 52), native[56], native[57], Word(native, 60));
            Check(value.BaseState == expected, "full actual factory base state");
            Check(value.StaticSurfaceReference == Word(native, 44), "actual original parent/child static floor");
            Check(committed == row.GetProperty("head_after").GetUInt32() && value.NextOnTile == Word(native, 48), "original dynamic head and retained successor");
            Check(value.Node.Flags == native[8] && value.Node.QueueKind == native[9] && value.Node.PeriodMilliseconds == Word(native, 4), "actual Container scheduler header remains active");
            var record = Bytes(row, "record_hex");
            Check(BitConverter.DoubleToUInt64Bits(value.Node.StartedAt) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) &&
                BitConverter.DoubleToUInt64Bits(value.Node.NextDue) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)), "actual scheduler insertion and due time");
            Check(value.CurrentValues.Span.SequenceEqual(native.AsSpan(0x118, 130)) && !value.PacketReady, "actual own values without upload readiness");
            Check(row.GetProperty("members").GetArrayLength() == 0 && activation.Nodes.Count == 1, "Container is not a creature member or inactive-sector NPC");
        }
        }
        using (var pending = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-pending-ground-container.json"))))
        foreach (var row in pending.RootElement.GetProperty("cases").EnumerateArray())
        {
            var actor = row.GetProperty("actors")[0];
            var before = Bytes(actor, "before_hex"); var after = Bytes(actor, "after_hex");
            var basis = new GoldObjectBaseState(Word(before, 12), Word(before, 16), Word(before, 20), Short(before, 24),
                Int(before, 28), Int(before, 32), before[36], Word(before, 40), Word(before, 52), before[56], before[57], Word(before, 60));
            var tile = WldxTileRecord.FromBytes(new byte[WldxTileRecord.Size]);
            var next = WorldScriptGroundObjectSectorReload.Next(basis, tile, 0);
            var actual = before.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(actual.AsSpan(48), next);
            Check(actual.AsSpan().SequenceEqual(after), "complete native ground reload preserves pending publication bit and all other object bytes");
        }
        return checks;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("Host loaded Container: " + reason); checks++; }
    }
    private static WorldScriptIndoorSurfaces ReadSurfaces(JsonElement source)
    {
        var entry = KeyxSectorRecord.FromBytes(Bytes(source, "keyx_hex"));
        using var stream = new ZLibStream(new MemoryStream(Bytes(source, "compressed_hex")), CompressionMode.Decompress);
        using var output = new MemoryStream(); stream.CopyTo(output); var original = output.ToArray();
        var outdoor = original.AsSpan(entry.TilesRelativeOffset, 131072).ToArray();
        var groups = new List<WldxIndoorGroupPayload>();
        for (var i = 1; i < entry.TileGridDescriptorCount; i++)
        {
            var descriptor = WldxTileGridDescriptorLayout.FromBytes(original.AsSpan((int)entry.TileGridDescriptorsRelativeOffset + i * 36));
            groups.Add(new(descriptor.WorldX, descriptor.WorldY, descriptor.Width, descriptor.Height, descriptor.Kind,
                original.AsSpan((int)descriptor.TilesOffset, (int)descriptor.TilesSize).ToArray()));
        }
        var statics = source.GetProperty("statics").EnumerateArray().ToDictionary(row => row.GetProperty("reference").GetUInt32(),
            row => MemoryMarshal.Read<StaticObjectRecord>(Bytes(row, "hex")));
        var payload = new WldxSectorPayload(outdoor, groups);
        var ox = SacredSectorCoordinates.ToTileOrigin(entry.RawX); var oy = SacredSectorCoordinates.ToTileOrigin(entry.RawY);
        return new((x, y) => (uint)(x - ox) < 64 && (uint)(y - oy) < 64
                ? WldxTileRecord.FromBytes(outdoor.AsSpan(((y - oy) * 64 + x - ox) * 32)) : null,
            _ => payload, id => statics.GetValueOrDefault(id));
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
}
