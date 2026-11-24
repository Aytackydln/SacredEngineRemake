using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

internal static class HostAmbientFactoryChecks
{
    public static int Run(string root)
    {
        using var proof = Read("native-host-ambient-factory");
        using var entered = Read("native-host-entered-npc-factory");
        using var prefix = Read("native-host-entered-npc-prefix");
        using var automatic = Read("native-host-entered-npc-level");
        using var shieldInstallation = Read("native-host-entered-npc-shield-installation");
        using var enteredPublication = Read("native-host-entered-npc-publication");
        using var slotsEvidence = Read("native-equipment-slot-selection");
        using var tuning = Read("native-script-automatic-level");
        using var levelEvidence = Read("native-creature-level-selection");
        using var constructor = Read("native-creature-constructor"); using var horse = Read("native-horse-constructor");
        using var basis = Read("native-creature-base-results"); using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization"); using var precision = Read("native-startup-floating-point");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { constructor, horse, basis, initialization, finalization, precision, tuning })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var results = WorldCreatureScalarResultAssets.Read(image, Bytes(horse.RootElement, "balance_hex"));
        var definitions = SacredCreatureConstructionDefinitions.Read(image);
        var levels = CreatureLevelBalanceBinReader.Read(Bytes(tuning.RootElement, "balance_hex"));
        var progression = CreatureBalanceBinReader.Read(Bytes(tuning.RootElement, "balance_hex"));
        var levelDefinitions = SacredCreatureLevelDefinitions.Read(Bytes(levelEvidence.RootElement, "cap_override_code_hex"));
        var automaticDefinitions = SacredCreatureAutomaticLevelDefinitions.Read(image);
        var equipmentData = EquipmentGenerationFixture.ReadStartup(root);
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(slotsEvidence.RootElement,"override_code_hex"));
        var checks = 0;
        foreach (var document in new[] { proof, entered, prefix, automatic, shieldInstallation, enteredPublication })
        {
            var keyx = KeyxSectorRecord.FromBytes(Bytes(document.RootElement, "keyx_hex"));
            using var compressed = new MemoryStream(Bytes(document.RootElement, "compressed_hex"));
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress); using var output = new MemoryStream(); inflate.CopyTo(output);
            var terrain = output.ToArray();
            foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                var archive = new byte[256 + CreatureGoldRecordLayout.Size * 2];
                BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943); BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
                Bytes(row, "template_hex").CopyTo(archive, 256 + CreatureGoldRecordLayout.Size);
                var template = CreaturePakArchive.Parse(archive).Entries[1];
                var descriptor = new ItemsPakEntry(new((ushort)template.ItemType, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
                var hasWeapon = ReferenceEquals(document, enteredPublication);
                var hasShield = ReferenceEquals(document, shieldInstallation) || hasWeapon;
                var allocationCount = hasWeapon ? 3 : hasShield ? 2 : 1;
                var references = new GoldObjectReferencePool();
                references.Apply(GoldReferenceBatch.FromPayload(Enumerable.Range(86, allocationCount).SelectMany(value => BitConverter.GetBytes((uint)value)).ToArray()));
                var counter = new WorldObjectAllocationCounter(69); var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
                var hasAutomaticLevel = ReferenceEquals(document, automatic) || hasShield;
                var scriptPrefix = ReferenceEquals(document, prefix) || hasAutomaticLevel;
                var facing = scriptPrefix ? random.Next() % 361 : 0;
                var factory = new WorldCreatureInitialFactory(results, definitions, references, counter, random, 1, 1);
                var actor = factory.Create(template, descriptor, 1000, 1, true, true);
                var bytes = Bytes(row, "position_hex");
                var position = new GoldObjectPosition(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), bytes[12], BinaryPrimitives.ReadUInt16LittleEndian(bytes));
                var x = SacredWorldCoordinates.WorldToTile(position.X) - SacredSectorCoordinates.ToTileOrigin(keyx.RawX);
                var y = SacredWorldCoordinates.WorldToTile(position.Y) - SacredSectorCoordinates.ToTileOrigin(keyx.RawY);
                var tile = WldxTileRecord.FromBytes(terrain.AsSpan(keyx.TilesRelativeOffset + (y * 64 + x) * 32));
                uint head = 0;
                actor = WorldCreatureAmbientPlacement.Place(actor, new(position, 0, 0, tile, reference => head = reference));
                if (scriptPrefix)
                {
                    actor = WorldCreatureScriptPose.Apply(actor, facing);
                    actor = WorldCreatureScriptControl.Apply(actor, 0x4000, -1, -1);
                }
                if (hasAutomaticLevel)
                {
                    var inputs = new WorldCreatureAutomaticLevelInputs(row.GetProperty("hero_level").GetUInt16(), [],
                        row.GetProperty("elapsed").GetUInt32(), row.GetProperty("progress").GetByte(), 0, true, true, 1, 4);
                    var selected = WorldCreatureAutomaticLevel.Select(inputs, 1, true, false, levels, progression,
                        automaticDefinitions.Tuning, results.StartupPrecision, random.Next);
                    actor = WorldCreatureScriptLevel.ApplyAutomaticRequested(actor, template, checked((ushort)selected.RequestedLevel),
                        results, levels, progression, levelDefinitions, 1, 1, true, 4, row.GetProperty("players").GetUInt32());
                }
                IReadOnlyDictionary<uint,WorldEquipmentInitialItem> itemRegistry = new Dictionary<uint,WorldEquipmentInitialItem>();
                if (hasShield)
                {
                    var itemFactory = new WorldEquipmentInitialFactory(references,counter,factory.Activation,1);
                    itemRegistry = itemFactory.Items;
                    var item = itemFactory.Create(equipmentData.Templates.GetItem(1200), Bytes(row,"shield_values_hex"),1,true);
                    WorldScriptCreatureEquipmentValues.Apply(item,BinaryPrimitives.ReadUInt16LittleEndian(actor.Component.Values.AsSpan(78)),
                        1,equipmentData,random,results.StartupPrecision);
                    actor = WorldCreatureInitialEquipmentInstallation.Apply(actor,item,1200,equipmentData,slots,itemFactory.Items,true);
                    var nativeItem = Bytes(row,"shield_hex");
                    var expectedFlags = BinaryPrimitives.ReadUInt32LittleEndian(nativeItem.AsSpan(20)) | (hasWeapon ? 0x10000u : 0);
                    Check(item.Values.AsSpan().SequenceEqual(nativeItem.AsSpan(0x118,130)) &&
                        item.Physical.AttachmentParent == 86 && item.Physical.Base.Flags == expectedFlags &&
                        item.Physical.Base.Word3C == BinaryPrimitives.ReadUInt32LittleEndian(nativeItem.AsSpan(60)) &&
                        item.Node.Flags == nativeItem[8] && factory.Activation.Nodes.Count == 2 && counter.Value == 71,
                        $"Shield seed {row.GetProperty("seed")}: value differences [{string.Join(',',Enumerable.Range(0,130).Where(at => item.Values[at] != nativeItem[0x118+at]))}], parent {item.Physical.AttachmentParent}, flags {item.Physical.Base.Flags:X8}/{BinaryPrimitives.ReadUInt32LittleEndian(nativeItem.AsSpan(20)):X8}, name {item.Physical.Base.Word3C:X8}/{BinaryPrimitives.ReadUInt32LittleEndian(nativeItem.AsSpan(60)):X8}, node {item.Node.Flags}/{nativeItem[8]}, nodes {factory.Activation.Nodes.Count}, counter {counter.Value}/{row.GetProperty("allocation_counter")}");
                    if (hasWeapon)
                    {
                        const uint weaponType = 1729;
                        var weaponDescriptor = equipmentData.Templates.GetItem(weaponType);
                        var weapon = itemFactory.Create(weaponDescriptor, equipmentData.Templates.GetInstance(weaponType), 1, true);
                        WorldScriptCreatureEquipmentValues.Apply(weapon, BinaryPrimitives.ReadUInt16LittleEndian(actor.Component.Values.AsSpan(78)),
                            1, equipmentData, random, results.StartupPrecision);
                        actor = WorldCreatureInitialEquipmentInstallation.Apply(actor, weapon, weaponType, equipmentData, slots, itemFactory.Items, true);
                        var nativeWeapon = Bytes(row, "weapon_hex");
                        Check(weapon.Values.AsSpan().SequenceEqual(nativeWeapon.AsSpan(0x118, 130)) &&
                            weapon.Physical.Base.Word3C == BinaryPrimitives.ReadUInt32LittleEndian(nativeWeapon.AsSpan(60)) &&
                            weapon.Physical.AttachmentParent == 86 && factory.Activation.Nodes.Count == 3,
                            "generated and named Weapon installed after the original Shield on the active graph");
                        actor = actor with { Base = actor.Base with { Flags = actor.Base.Flags | 0x80000000u } };
                        actor = WorldCreatureEquippedResults.Apply(actor, template, results, equipmentData, itemRegistry, 1, 1, true, row.GetProperty("players").GetUInt32());
                    }
                    Check(actor.EquipmentReferences.SequenceEqual(Enumerable.Range(0,19).Select(index =>
                        BinaryPrimitives.ReadUInt32LittleEndian(Bytes(row,"actor_hex").AsSpan(0x1a4+index*4)))),
                        "native parent Shield slot vector after actual installation");
                }
                var state = WorldCreatureInitialSerialization.Write(actor, descriptor, itemRegistry,
                    type => type == descriptor.ItemIndex ? descriptor : equipmentData.Templates.GetItem(type));
                var published = row.GetProperty("publications").GetArrayLength() != 0;
                if (published) actor = actor with { Base = actor.Base with { Flags = actor.Base.Flags & ~0x10000u } };
                factory.Update(actor);
                var native = Bytes(row, "actor_hex");
                if (scriptPrefix)
                    Check(actor.RelationClass == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x1f0)) &&
                        actor.BehaviorFlags == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x1f4)) &&
                        actor.RuntimeFlags == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x200)) &&
                        actor.KiMode == BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(0xfc)) &&
                        actor.Direction.X == BitConverter.ToSingle(native,0x70) && actor.Direction.Y == BitConverter.ToSingle(native,0x74),
                        "actual entered caller pose and control before current automatic level/equipment");
                Check(actor.Component.Values.SequenceEqual(native.AsSpan(0x3b0, 238).ToArray()) && actor.Component.Health.SequenceEqual(native.AsSpan(0x4d0, 24).ToArray()), "all constructor result/health bytes");
                if (hasWeapon)
                {
                    var messages = WorldCreatureInitialPublication.Prepare(actor, state, itemRegistry, equipmentData.Templates.GetItem);
                    var nativePublications = row.GetProperty("publications");
                    Check(messages.Count == nativePublications.GetArrayLength(), "equipped actor sends a creature and both slot items");
                    for (var index = 0; index < messages.Count; index++)
                    {
                        var expected = nativePublications[index];
                        var nativePayload = Bytes(expected, "payload_hex");
                        nativePayload.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
                        var opcode = expected.GetProperty("opcode").GetUInt16();
                        var packet = GoldObjectStateCodec.Create(opcode, 1, nativePayload);
                        var actual = messages[index];
                        var difference = packet.Payload.Length == actual.Payload.Length
                            ? string.Join(',', Enumerable.Range(0, packet.Payload.Length).Where(at => packet.Payload[at] != actual.Payload[at])) : "different lengths";
                        Check(actual.Opcode == opcode && actual.Payload.SequenceEqual(packet.Payload),
                            $"ordered native object publication {index}, opcode {opcode:X}, differences {difference}");
                    }
                }
                else if (published)
                {
                    var managedPayload = state.ToPayload(); var nativePayload = Bytes(row.GetProperty("publications")[0], "payload_hex");
                    // Existing creature consumer proof establishes that Reset3DPosition
                    // discards this failed zero-handle bone cache. No other bytes normalize.
                    nativePayload.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
                    Check(managedPayload.SequenceEqual(nativePayload), "entire native 16F body, with only unused bone-cache normalization");
                }
                else Check((actor.Base.Flags & 0x10000) != 0 && (actor.Base.Flags & 0x80000000) == 0,
                    "actual entered NPC factory suppresses publication and preserves pending state without local published flag");
                Check(actor.Base.Flags == BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(20)) && actor.Node.Flags == native[8] && actor.Node.Active && head == 86, "native owned flags, active scheduler and actual tile head");
                var membership = new WorldCreatureSectorMembership();
                membership.RegisterAmbient(actor, row.GetProperty("interest_flags").GetUInt32());
                Check(membership.Get(actor.Base.Sector).SequenceEqual(row.GetProperty("members").EnumerateArray().Select(value=>value.GetUInt32())) &&
                    row.GetProperty("changed_tiles").GetArrayLength() == 1, "native tile insertion and actual runtime-gated sector membership");
                Check(random.State == row.GetProperty("rng_after").GetUInt32() && references.Available == 0 && counter.Value == 69 + allocationCount && ReferenceEquals(factory.Actors[86], actor), "same factory, reservation, counter and exact RNG");
                var activationCount = factory.Activation.ActivationCount;
                var activationRecords = factory.Activation.Records.Count;
                var plan = WorldCreatureSectorActivation.Prepare(actor.Base.Sector, SacredSectorCoordinates.ToGrid(keyx), factory, membership, null, null);
                WorldCreatureSectorActivation.Apply(factory, plan, null, double.NaN);
                Check(plan.Count == row.GetProperty("members").GetArrayLength() && factory.Activation.ActivationCount == activationCount &&
                    factory.Activation.Records.Count == activationRecords && random.State == row.GetProperty("rng_after").GetUInt32(), "actual native member/factory position checks and already-active completion");
                if (plan.Count != 0 && !hasShield)
                {
                    var flags = actor.Node.Flags; actor.Node.Flags &= 0x7f;
                    try
                    {
                        var dormantPlan = WorldCreatureSectorActivation.Prepare(actor.Base.Sector, SacredSectorCoordinates.ToGrid(keyx), factory, membership, null, null);
                        try { WorldCreatureSectorActivation.Apply(factory, dormantPlan, null, double.NaN); throw new Exception("Missing scheduler clock bypassed."); }
                        catch (NotSupportedException) { checks++; }
                    }
                    finally { actor.Node.Flags = flags; }
                }
            }
        }
        return checks;
        JsonDocument Read(string name) => HostSectorInitializationChecks.Read(root, name);
        void Check(bool value, string reason) { if (!value) throw new Exception("Host ambient factory: " + reason); checks++; }
    }
    internal static byte[] WireExample(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-ambient-factory");
        var body = Bytes(proof.RootElement.GetProperty("cases")[0].GetProperty("publications")[0], "payload_hex");
        body.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
        return GoldObjectStateCodec.Create(0x16f, 1, body).Payload;
    }
    internal static byte[] EnteredNpcWireExample(string root, int index)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-entered-npc-publication");
        var row = proof.RootElement.GetProperty("cases")[0].GetProperty("publications")[index];
        var body = Bytes(row, "payload_hex");
        body.AsSpan(GoldPhysicalStateLayout.SelectedVectorOffset, 12).Clear();
        return GoldObjectStateCodec.Create(row.GetProperty("opcode").GetUInt16(), 1, body).Payload;
    }
    private static byte[] Bytes(JsonElement row, string name) => HostSectorInitializationChecks.Bytes(row, name);
}
