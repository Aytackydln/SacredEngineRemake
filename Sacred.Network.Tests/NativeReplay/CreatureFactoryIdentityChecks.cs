using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class CreatureFactoryIdentityChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-factory-identity.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var resource = row.GetProperty("resource").GetUInt16();
            var raw = new byte[ItemsPakEntryModelDescLayout.SerializedSize];
            raw[46] = (byte)SacredItemCategory.Creature;
            BinaryPrimitives.WriteUInt32LittleEndian(raw, row.GetProperty("graphic").GetBoolean() ? 0x1000000u : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(87), U(row, "angle_bits"));
            var descriptor = new ItemsPakEntry(new(resource, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(raw), "");
            var actor = new WorldCreatureInitialActor(new(0, resource, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new(new byte[SacredActorValuesLayout.Size], new byte[GoldCreatureValuesLayout.Block128Size], 6, 0), 7, 0, 0, 0, 0, 0, []);
            new WorldObjectActivationQueue().ActivateInitial(actor, 0);
            var actual = WorldCreatureFactoryIdentity.Assign(actor, descriptor, U(row, "reference"),
                row.GetProperty("owner").GetByte(), row.GetProperty("counter").GetByte(), row.GetProperty("owned").GetBoolean());
            var direction = Convert.FromHexString(row.GetProperty("direction_hex").GetString()!);
            if (actual.Base.ObjectReference != U(row, "reference") || actual.Base.ObjectType != resource ||
                actual.Base.Owner != row.GetProperty("owner").GetByte() || actual.Base.Byte38 != row.GetProperty("counter").GetByte() ||
                actual.Base.Flags != U(row, "flags") || actual.Base.Word28 != U(row, "cached_angle"))
                throw new Exception($"Native factory identity differs: {row}");
            if (BitConverter.SingleToUInt32Bits(actual.Direction.X) != Word(direction, 0) ||
                BitConverter.SingleToUInt32Bits(actual.Direction.Y) != Word(direction, 4) ||
                BitConverter.SingleToUInt32Bits(actual.Direction.Z) != Word(direction, 8))
                throw new Exception($"Native factory direction differs: {row}; {actual.Direction}");
            if (!ReferenceEquals(actual.Node, actor.Node) || !ReferenceEquals(actual.Component, actor.Component) ||
                !ReferenceEquals(actual.EquipmentReferences, actor.EquipmentReferences) || actor.Base.ObjectReference != 0)
                throw new Exception("Identity assignment changed constructor state, queue identity or source actor.");
            checks += 13;
            var registration = new WorldCreatureFactoryRegistration();
            var complete = registration.CompleteUnpublished(actual, descriptor, row.GetProperty("networking").GetBoolean(), row.GetProperty("new_reference").GetBoolean());
            if (complete.Base.Flags != U(row, "completed_flags") ||
                !registration.GraphicObjects.SequenceEqual(row.GetProperty("graphic_objects").EnumerateArray().Select(value => value.GetUInt32())) ||
                complete.Direction != actual.Direction || !ReferenceEquals(complete.Node, actual.Node))
                throw new Exception($"Original unpublished factory continuation differs: {row}");
            checks += 4;
        }
        foreach (var angle in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, 65537f, -65537f })
        {
            try { SacredStartupHeading.ToDirection(angle); throw new Exception("Uncompared heading accepted."); }
            catch (NotSupportedException) { checks++; }
        }
        return checks;
    }
    private static uint U(JsonElement row, string key) => row.GetProperty(key).GetUInt32();
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
