using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class EquipmentInitialFactoryChecks
{
    public static int Run(string root) => Run(root, "native-equipment-initial-factory.json");
    public static int RunWings(string root) => Run(root, "native-wings-initial-factory.json");
    public static int RunArmor(string root) => Run(root, "native-armor-initial-factory.json");

    public static int RunShoulder(string root) => Run(root, "native-shoulder-initial-factory.json");

    public static int RunAccessories(string root) => Run(root, "native-armor-accessory-initial-factory.json");

    private static int Run(string root, string fixtureName)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", fixtureName)));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var references = new GoldObjectReferencePool();
            var reference = row.GetProperty("reference").GetUInt32();
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(reference)));
            var counter = new WorldObjectAllocationCounter(row.GetProperty("counter_before").GetByte());
            var activation = new WorldObjectActivationQueue();
            var factory = new WorldEquipmentInitialFactory(references, counter, activation, row.GetProperty("owner").GetByte());
            var descriptor = new ItemsPakEntry(new((ushort)row.GetProperty("type").GetUInt32(), 0, 128, 0),
                MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var initial = Bytes(row, "initial_values_hex");
            var item = factory.Create(descriptor, initial, row.GetProperty("scheduler_clock").GetDouble(),
                row.GetProperty("networking").GetBoolean(), row.GetProperty("owned").GetBoolean());
            var native = Bytes(row, "item_hex");
            var state = item.Physical.Base;
            var expected = new GoldObjectBaseState(Word(native, 12), Word(native, 16), Word(native, 20),
                Short(native, 24), Int(native, 28), Int(native, 32), native[36], Word(native, 40), Word(native, 52), native[56], native[57], Word(native, 60));
            Check(state == expected, "base identity, flags, position, counter, owner and script resource");
            Check(counter.Value == row.GetProperty("counter").GetByte() && references.Available == 0, "shared counter and reservation consumption");
            Check(factory.Items.Count == 1 && ReferenceEquals(factory.Items[reference], item), "stable registry lifetime");
            for (var at = 0; at < item.Values.Length; at++)
                Check(item.Values[at] == native[0x118 + at], "original own values");
            Array.Fill(initial, (byte)0xcc);
            Check(item.Values.AsSpan().SequenceEqual(native.AsSpan(0x118, 130)), "item owns a separate copy of shared source values");
            Check(item.Node.Flags == native[8] && item.Node.QueueKind == native[9] && item.Node.PeriodMilliseconds == Word(native, 4),
                "item-specific scheduler header and initial activation");
            var record = Bytes(row, "record_hex");
            Check(activation.ActivationCount == 1 && activation.Nodes.Count == 1 && activation.Records.Count == 1 &&
                ReferenceEquals(activation.Nodes[0], item.Node) && ReferenceEquals(activation.Records[0].Node, item.Node), "registered node lifetime");
            Check(BitConverter.DoubleToUInt64Bits(item.Node.StartedAt) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) &&
                BitConverter.DoubleToUInt64Bits(item.Node.NextDue) == BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)), "exact native scheduler clock and PC=24 due time");
            Check(item.Physical.Direction == Vector(native, 0x70), "actual physical event heading");
            Check(item.Physical.Vector7C == Vector(native, 0x7c) &&
                item.Physical.Vector88 == Vector(native, 0x88) &&
                item.Physical.Link == Word(native, 0x2c) && item.Physical.PhysicalFlags == Word(native, 0xb0), "initial physical vectors/link/flags");
            Check(item.QuestNumber == unchecked((short)Short(native, 0x94)) && item.QuestEffect == Short(native, 0x96) &&
                item.InventoryId == Word(native, 0x1e0) && BitConverter.SingleToUInt32Bits(item.RuntimeFloat1DC) == Word(native, 0x1dc), "initial quest/inventory/item float");
            Check(factory.GraphicObjects.SequenceEqual(row.GetProperty("graphic_objects").EnumerateArray().Select(value => value.GetUInt32())), "native graphic registration");
            Check(!item.PacketReady && !item.Generated && item.Quality is null, "constructor does not grant generated/installed packet readiness");
            var slot = WorldCreatureEquipmentSelection.Resolve(descriptor.ModelDesc.Category, row.GetProperty("usage").GetByte(), false, 26, new uint[19]);
            Check((slot != SacredCreatureEquipmentSlot.Unavailable) == row.GetProperty("eligible").GetBoolean(), "actual CreateNPC eligibility gate");
            Check(!activation.ActivateInitial(item.Node, 42) && activation.ActivationCount == 1, "duplicate initial activation");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Initial native equipment factory: " + why); checks++; }
    }

    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static GoldPhysicalVector Vector(byte[] bytes, int at) => new(BitConverter.UInt32BitsToSingle(Word(bytes, at)),
        BitConverter.UInt32BitsToSingle(Word(bytes, at + 4)), BitConverter.UInt32BitsToSingle(Word(bytes, at + 8)));
}
