using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class CreaturePlacementChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-flat-placement.json")));
        var checks = 0;
        foreach (var branch in new[] { "cases", "direct" })
        foreach (var row in fixture.RootElement.GetProperty(branch).EnumerateArray())
        {
            var before = Bytes(row, "before_hex");
            var actor = new WorldCreatureInitialActor(new(0, Word(before, 16), 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new(new byte[SacredActorValuesLayout.Size], new byte[GoldCreatureValuesLayout.Block128Size], 6, 0), 7, 0, 0, 0, 0, 0, []);
            var queue = new WorldObjectActivationQueue();
            queue.ActivateInitial(actor, row.GetProperty("clock").GetDouble());
            actor = actor with { Base = actor.Base with { ObjectReference = Word(before, 12), Flags = Word(before, 20),
                Sector = Short(before, 24), Owner = before[0x39] }, NextOnTile = Word(before, 0x30) };
            var position = new GoldObjectPosition(row.GetProperty("x").GetInt32(), row.GetProperty("y").GetInt32(), 0, row.GetProperty("sector").GetUInt32());
            WorldCreatureInitialActor result;
            var native = Bytes(row, "after_hex");
            if (branch == "cases")
            {
                var placement = WorldCreatureGroundPlacement.Place(actor, position, WldxTileRecord.FromBytes(new byte[32]), row.GetProperty("occupant").GetUInt32());
                result = placement.Actor;
                var membership = new WorldCreatureSectorMembership();
                foreach (var member in row.GetProperty("old_members").EnumerateArray())
                    membership.Move(member.GetUInt32(), 0, actor.Base.Sector);
                membership.Move(result.Base.ObjectReference, actor.Base.Sector, result.Base.Sector);
                var expected = row.GetProperty("bucket").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
                if (placement.TileHead != Word(native, 12) || !membership.Get(result.Base.Sector).SequenceEqual(expected) ||
                    !membership.Get(actor.Base.Sector).SequenceEqual(row.GetProperty("old_after").EnumerateArray().Select(value => value.GetUInt32())))
                    throw new Exception("Native tile head / sector vector differs.");
                Compare(result, native);
                if (expected.Length != 0) queue.DeactivateInitial(result);
                native = Bytes(row, "inactive_hex");
                checks += 2;
            }
            else result = WorldCreatureScriptPlacement.PlaceDormant(actor, position, queue);
            Compare(result, native);
            var record = Bytes(row, "record_hex");
            if (queue.Records.Count != 1 || queue.Records[0].Flags != record[4] ||
                (queue.Records[0].Node is null) != (Word(record, 0) == 0) ||
                BitConverter.DoubleToUInt64Bits(queue.Records[0].StartedAt) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) ||
                BitConverter.DoubleToUInt64Bits(queue.Records[0].NextDue) != BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)) || queue.ActivationCount != 1)
                throw new Exception("Native deactivation record/tombstone differs.");
            if (!ReferenceEquals(actor.Component, result.Component) || !ReferenceEquals(actor.Node, result.Node))
                throw new Exception("Placement replaced constructed component or node state.");
            if (!result.Node.Active && queue.DeactivateInitial(result)) throw new Exception("Duplicate deactivation accepted.");
            checks += 9;
        }
        return checks;

        void Compare(WorldCreatureInitialActor actor, byte[] native)
        {
            if (actor.Base.ObjectReference != Word(native, 12) || actor.Base.ObjectType != Word(native, 16) ||
                actor.Base.Flags != Word(native, 20) || actor.Base.Sector != Short(native, 24) || actor.Base.X != (int)Word(native, 28) ||
                actor.Base.Y != (int)Word(native, 32) || actor.Base.Surface != native[36] || actor.NextOnTile != Word(native, 48) || actor.Node.Flags != native[8])
                throw new Exception("Original placement actor state differs.");
            checks += 9;
        }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ushort Short(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
}
