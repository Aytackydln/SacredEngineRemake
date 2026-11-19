using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostSavedItemChecks
{
    public static int Run(string root)
    {
        using var fixture = Read("native-host-saved-items");
        using var pricing = Read("native-equipment-pricing");
        var balance = Bytes(pricing.RootElement, "balance_prefix_hex");
        var tuning = new SacredEquipmentGenerationTuning(EquipmentGenerationBalanceBinReader.Read(balance),
            EquipmentGenerationBalanceBinReader.ReadBonusCurves(balance), EquipmentNormalizationBalanceBinReader.Read(balance),
            EquipmentSocketBalanceBinReader.Read(balance), EquipmentPricingBalanceBinReader.Read(balance));
        var prices = SacredEquipmentPriceDefinitions.Read(Bytes(pricing.RootElement, "override_code_hex"));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var reference = row.GetProperty("reference").GetUInt32();
            var descriptor = new ItemsPakEntry(new((ushort)row.GetProperty("type").GetUInt32(), 0, 128, 0),
                MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var source = SacredSavedItem.FromPayload(Bytes(row, "source_hex"), fixture.RootElement.GetProperty("format_version").GetByte(),
                descriptor.ModelDesc.Category == SacredItemCategory.Weapon);
            var modifier = MemoryMarshal.Read<SacredWeaponModifierLayout>(Bytes(row, "price_rule_hex"));
            var overrides = prices.Overrides(source.ItemResource);
            var context = new SacredEquipmentPriceContext(descriptor.ModelDesc.Category, modifier.ItemId0 != 0, modifier.Modifier,
                Bytes(row, "shared_hex")[35], overrides.ClearClassRestrictions, overrides.ClearLevelAndSkillRequirements);
            var restored = WorldEquipmentSavedImport.Prepare(source, descriptor, context, tuning);
            var native = Bytes(row, "item_hex");
            var expected = Base(native);
            Check(restored.FileBase == expected, "file base and physical flag normalization");
            Check(restored.PhysicalFlags == Word(native, 0xb0), "physical state flags");
            Check(restored.CopyValues().AsSpan().SequenceEqual(native.AsSpan(0x118, 130)),
                $"all native item price/requirement side effects, type {descriptor.ItemIndex}");
            Check(restored.QuestNumber == unchecked((short)Short(native, 0x94)) && restored.QuestEffect == Short(native, 0x96), "quest words");
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(reference)));
            var counter = new WorldObjectAllocationCounter(row.GetProperty("counter_before").GetByte());
            var activation = new WorldObjectActivationQueue();
            var factory = new WorldEquipmentInitialFactory(references, counter, activation, row.GetProperty("owner").GetByte());
            var initial = Bytes(row, "initial_values_hex");
            var clock = row.GetProperty("scheduler_clock").GetDouble();
            var fresh = factory.Create(descriptor, initial, clock, true);
            var nativeFresh = Bytes(row, "factory_hex");
            Check(fresh.Physical.Base == Base(nativeFresh) && fresh.Values.AsSpan().SequenceEqual(nativeFresh.AsSpan(0x118, 130)), "actual class constructor");
            Check(fresh.Node.Flags == nativeFresh[8] && fresh.Node.PeriodMilliseconds == Word(nativeFresh, 4), "class scheduler header");
            Check(fresh.Physical.Direction == Vector(nativeFresh, 0x70), "factory heading");
            Check(references.IsAllocated(reference), "actual received and consumed reference");
            // Independent saved-path control models the already-consumed plan.
            // The installed host test verifies shared pool/counter ownership.
            var consumed = new GoldObjectReferencePool(); consumed.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(reference)));
            consumed.Consume([reference]);
            var savedFactory = new WorldEquipmentInitialFactory(consumed, new(row.GetProperty("counter_before").GetByte()), new(), 1);
            var item = savedFactory.RestoreAllocated(descriptor, initial, restored, clock, reference);
            var flags = expected.Flags & ~0x200010u; if ((flags & 2) != 0) flags &= ~0x30u;
            Check(item.Physical.Base == (expected with { ObjectReference = reference, Owner = 1, Flags = flags }), "outer actual identity and cleanup");
            Check(item.Physical.Direction == Vector(native, 0x70) && item.Physical.PhysicalFlags == Word(native, 0xb0), "saved physical direction and flags");
            Check(item.Physical.Link == Word(native, 0x2c) && item.NextOnTile == 0, "saved link and cleared next-on-tile");
            Check(item.Node.Flags == native[8] && item.Node.PeriodMilliseconds == Word(native, 4) && item.Node.NextDue == fresh.Node.NextDue,
                "saved node metadata is consumed without replacing the active constructor schedule");
            Check(ReferenceEquals(item.SavedState, restored) && !item.PacketReady && !item.Generated, "runtime reader state does not imply upload or generated readiness");
            Check(consumed.Available == 0 && savedFactory.Items.Count == 1, "no second allocation");
            var count = counter.Value;
            try { savedFactory.RestoreAllocated(descriptor, initial, restored, clock, reference); throw new Exception("Duplicate saved item accepted"); }
            catch (InvalidOperationException) { checks++; }
            Check(counter.Value == count && savedFactory.Items.Count == 1, "duplicate rejection before mutation");
            Check(source.ToPayload().AsSpan().SequenceEqual(Bytes(row, "source_hex")), "source bytes stay unchanged");
        }
        return checks;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Host saved items: " + why); checks++; }
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence", name + ".json")));
    }

    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static GoldPhysicalVector Vector(byte[] bytes, int at) => new(BitConverter.UInt32BitsToSingle(Word(bytes, at)),
        BitConverter.UInt32BitsToSingle(Word(bytes, at + 4)), BitConverter.UInt32BitsToSingle(Word(bytes, at + 8)));
    private static GoldObjectBaseState Base(byte[] bytes) => new(Word(bytes, 12), Word(bytes, 16), Word(bytes, 20), Short(bytes, 24),
        unchecked((int)Word(bytes, 28)), unchecked((int)Word(bytes, 32)), bytes[36], Word(bytes, 40), Word(bytes, 52), bytes[56], bytes[57], Word(bytes, 60));
}
