using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostSectorActivationChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-sector-activation");
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = HostSectorInitializationChecks.Bytes(row, "actor_before_hex");
            var actor = new WorldCreatureInitialActor(Base(before), new([], [], 0, 0), Word(before, 0x1f0), Word(before, 0x1f4), 0, 0, 0, 0, [])
            { EquipmentReferences = Enumerable.Range(0, 19).Select(index => Word(before, 0x1a4 + index * 4)).ToArray() };
            actor.Node.Flags = before[8];
            var equipped = row.GetProperty("equipped").GetBoolean();
            var scheduler = new WorldObjectActivationQueue(equipped ? 18u : 19u);
            var items = new Dictionary<uint, WorldEquipmentInitialItem>();
            GoldObjectBaseState? originalItem = null;
            if (equipped)
            {
                var data = HostSectorInitializationChecks.Bytes(row, "item_before_hex");
                var physical = new WorldEquipmentObject(Base(data), true, true);
                physical.SetLink(Word(data, 44), attached: true); originalItem = physical.Base;
                var item = new WorldEquipmentInitialItem(physical, data.AsSpan(0x118, 130).ToArray())
                    { Generated = true, ResourceCategory = SacredItemCategory.Weapon };
                items.Add(item.Physical.Base.ObjectReference, item);
                scheduler.ActivateInitial(item.Node, .5);
            }
            var originalActor = actor.Base;
            Check(WorldCreatureSectorReactivation.Activate(actor, scheduler, items, row.GetProperty("clock").GetDouble()), "actual dormant actor activation");
            var after = HostSectorInitializationChecks.Bytes(row, "actor_after_hex");
            Check(actor.Base == originalActor && actor.Node.Flags == after[8], "native actor bytes change only node active flag");
            Check(scheduler.ActivationCount == row.GetProperty("activation_after").GetUInt32(), "native shared activation count increment");
            var records = HostSectorInitializationChecks.Bytes(row, "record_hex");
            Check(scheduler.Records.Count * 32 == records.Length && scheduler.Nodes.Count == 1, "native equipment tombstone and actor insertion");
            for (var index = 0; index < scheduler.Records.Count; index++)
            {
                var record = scheduler.Records[index]; var at = index * 32;
                Check(record.Flags == records[at + 4] && (record.Node is null) == (Word(records, at) == 0), "native record/node lifetime");
                Check(BitConverter.DoubleToUInt64Bits(record.StartedAt) == BinaryPrimitives.ReadUInt64LittleEndian(records.AsSpan(at + 8)) &&
                    BitConverter.DoubleToUInt64Bits(record.NextDue) == BinaryPrimitives.ReadUInt64LittleEndian(records.AsSpan(at + 16)), "exact original scheduler clock and due time");
            }
            if (equipped)
            {
                var item = items.Values.Single();
                Check(item.Physical.Base == originalItem && item.Physical.AttachmentParent == actor.Base.ObjectReference &&
                    item.Node.Flags == HostSectorInitializationChecks.Bytes(row, "item_after_hex")[8], "native active attached Weapon deactivation preserves physical/attachment state");
            }
            var count = scheduler.ActivationCount;
            Check(!WorldCreatureSectorReactivation.Activate(actor, scheduler, items, double.NaN) && scheduler.ActivationCount == count, "already-active retry has no clock/callback/record effects");
        }
        return checks;
        void Check(bool value, string reason) { if (!value) throw new Exception("Host sector activation: " + reason); checks++; }
    }
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static GoldObjectBaseState Base(byte[] data) => new(Word(data, 12), Word(data, 16), Word(data, 20),
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(24)), unchecked((int)Word(data, 28)), unchecked((int)Word(data, 32)), data[36],
        Word(data, 40), Word(data, 52), data[56], data[57], Word(data, 60));
}
