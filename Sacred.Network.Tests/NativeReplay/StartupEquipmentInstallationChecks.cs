using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupEquipmentInstallationChecks
{
    public static int Run(string root) => Run(root, false);
    public static int RunArmor(string root) => Run(root, true);

    private static int Run(string root, bool armor)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, armor ? "docs/game-server/evidence/native-startup-armor-installation.json" : "docs/game-server/evidence/native-startup-equipment-installation.json")));
        using var selection = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-slot-selection.json")));
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(selection.RootElement, "override_code_hex"));
        var data = EquipmentGenerationFixture.ReadStartup(root);
        using var commandFixture = armor ? JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-equipment-command-generation.json"))) : null;
        var checks = 0;
        WorldEquipmentInitialFactory? firstFactory = null;
        WorldCreatureInitialActor? firstActor = null;
        GoldObjectReferencePool? firstReferences = null;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row, "actor_before_hex");
            var actor = new WorldCreatureInitialActor(Base(before), new(before.AsSpan(0x3b0, 238).ToArray(), before.AsSpan(0x4d0, 24).ToArray(), before[0x4ce], before[0x4cf]),
                Word(before, 0x1f0), Word(before, 0x1f4), 0, 0, 0, 0, [])
            {
                Direction = Vector(before, 0x70), RuntimeFlags = Word(before, 0x200),
                EquipmentReferences = Enumerable.Range(0, 19).Select(index => Word(before, 0x1a4 + index * 4)).ToArray()
            };
            var itemBefore = Bytes(row, "item_before_hex");
            var consecutive = row.GetProperty("consecutive").GetBoolean();
            var references = consecutive ? firstReferences! : new GoldObjectReferencePool();
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(Word(itemBefore, 12))));
            var factory = consecutive ? firstFactory! : new WorldEquipmentInitialFactory(references, new WorldObjectAllocationCounter((byte)(itemBefore[56] - 1)), new(), itemBefore[57]);
            if (consecutive)
            {
                actor = firstActor!;
                // The existing factory retains its first registry/node lifetime.
            }
            var original = actor;
            var type = row.GetProperty("type").GetUInt32();
            var item = factory.Create(data.Templates.GetItem(type), data.Templates.GetInstance(type), 1, networking: true);
            if (armor)
                WorldScriptEquipmentCommandGeneration.Apply(item, row.GetProperty("generation_level").GetUInt16(), 1, Sacred.Assets.GameBin.Balance.CreatureBalanceBinReader.Read(Bytes(commandFixture!.RootElement, "balance_prefix_hex")), data, new(row.GetProperty("generation_seed").GetUInt32()));
            else WorldScriptEquipmentGeneration.Apply(item, row.GetProperty("generation_level").GetUInt16(), 1, data, new(row.GetProperty("generation_seed").GetUInt32()), SacredX87Precision.Single);
            Check(item.Values.AsSpan().SequenceEqual(itemBefore.AsSpan(0x118, 130)), "actual supplied initial generated values");
            actor = WorldCreatureInitialEquipmentInstallation.Apply(actor, item, type, data, slots, factory.Items, networking: true);
            var expectedActor = Bytes(row, "actor_after_hex");
            var expectedItem = Bytes(row, "item_after_hex");
            if (armor)
            {
                var slot = data.Templates.GetItem(type).ModelDesc.Category == Sacred.Core.Pak.Items.SacredItemCategory.Helmet ? 0 : 1;
                Check(actor.EquipmentReferences[slot] == item.Physical.Base.ObjectReference, "Armor occupies its actual head/body slot");
            }
            Check(actor.Base == Base(expectedActor), "native actor base state");
            Check(actor.EquipmentReferences.SequenceEqual(Enumerable.Range(0, 19).Select(index => Word(expectedActor, 0x1a4 + index * 4))), "native installed slot references");
            Check(original.EquipmentReferences.SequenceEqual(Enumerable.Range(0, 19).Select(index => Word(before, 0x1a4 + index * 4))), "initial actor slot snapshot is not mutated");
            Check(ReferenceEquals(actor.Component, original.Component) && ReferenceEquals(actor.Node, original.Node) &&
                actor.Component.Values.AsSpan().SequenceEqual(expectedActor.AsSpan(0x3b0, 238)) && actor.Component.Health.AsSpan().SequenceEqual(expectedActor.AsSpan(0x4d0, 24)),
                "event 107 does not invent recalculation or replace actor lifetime");
            Check(item.Physical.Base == Base(expectedItem), "native item base flags/name/resource");
            Check(item.Physical.Link == Word(expectedItem, 0x2c) && item.Physical.AttachmentParent == actor.Base.ObjectReference, "native attachment parent/link");
            Check(item.Physical.Direction == Vector(expectedItem, 0x70) && item.Physical.Vector7C == Vector(expectedItem, 0x7c) &&
                item.Physical.Vector88 == Vector(expectedItem, 0x88) && item.Physical.PhysicalFlags == Word(expectedItem, 0xb0), "native unloaded preparation and attachment");
            Check(item.Values.AsSpan().SequenceEqual(expectedItem.AsSpan(0x118, 130)), "native normalized own item values");
            Check(item.Node.Active && !item.PacketReady, "attachment preserves initial item activation without granting upload readiness");
            if (type == fixture.RootElement.GetProperty("cases")[0].GetProperty("type").GetUInt32() && !consecutive)
                (firstFactory, firstActor, firstReferences) = (factory, actor, references);
        }
        foreach (var row in fixture.RootElement.GetProperty("bounds").EnumerateArray())
        {
            var values = Bytes(row, "before_hex");SacredEquipmentDamageBounds.Normalize(values);
            Check(values.AsSpan().SequenceEqual(Bytes(row, "after_hex")), "original signed own-value damage ordering");
        }
        foreach (var row in fixture.RootElement.GetProperty("position_calls").EnumerateArray())
            Check(row.GetProperty("result").GetInt32() == 6 && row.GetProperty("input_hex").GetString() == row.GetProperty("after_hex").GetString(),
                "original DLL ignores zero sequence handles without mutating the position vector");
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Initial native equipment installation: " + why);checks++; }
    }

    private static GoldObjectBaseState Base(byte[] bytes) => new(Word(bytes, 12), Word(bytes, 16), Word(bytes, 20),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)),
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32)), bytes[36], Word(bytes, 40), Word(bytes, 52), bytes[56], bytes[57], Word(bytes, 60));
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static GoldPhysicalVector Vector(byte[] bytes, int at) => new(BitConverter.UInt32BitsToSingle(Word(bytes, at)),
        BitConverter.UInt32BitsToSingle(Word(bytes, at + 4)), BitConverter.UInt32BitsToSingle(Word(bytes, at + 8)));
}
