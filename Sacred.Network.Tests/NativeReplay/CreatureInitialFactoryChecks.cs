using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.GameBin;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;

internal static class CreatureInitialFactoryChecks
{
    public static int Run(string root) => Run(root, false);
    public static int RunHorse(string root) => Run(root, true);
    private static int Run(string root, bool horse)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Read(horse ? "native-horse-initial-factory" : "native-creature-initial-factory");
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var constructor = Read(horse ? "native-horse-constructor" : "native-creature-constructor");
        using var passive = Read("native-creature-passive-skills");
        using var levelEvidence = Read("native-creature-level-selection");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision, constructor })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var results = WorldCreatureScalarResultAssets.Read(image, horse ? Bytes(constructor.RootElement, "balance_hex") : Bytes(passive.RootElement, "balance_prefix_hex"));
        var definitions = SacredCreatureConstructionDefinitions.Read(image);
        var levelBalance = CreatureLevelBalanceBinReader.Read(Bytes(levelEvidence.RootElement, "balance_prefix_hex"));
        var progression = CreatureBalanceBinReader.Read(Bytes(levelEvidence.RootElement, "balance_prefix_hex"));
        var levelDefinitions = SacredCreatureLevelDefinitions.Read(Bytes(levelEvidence.RootElement, "cap_override_code_hex"));
        var checks = 0;
        WorldCreatureInitialActor? firstActor = null;
        SacredCreatureTemplate? firstTemplate = null;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
            Bytes(row, "template_hex").CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
            var template = CreaturePakArchive.Parse(archive).Entries[1];
            var descriptor = new ItemsPakEntry(new((ushort)template.ItemType, 0, 128, 0),
                MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var references = new GoldObjectReferencePool();
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(U(row, "reference"))));
            var counter = new WorldObjectAllocationCounter(row.GetProperty("counter_before").GetByte());
            var random = new SacredRandomSequence(U(row, "rng_seed"));
            var factory = new WorldCreatureInitialFactory(results, definitions, references, counter, random,
                row.GetProperty("owner").GetByte(), 1);
            var actor = factory.Create(template, descriptor, U(row, "clock"), row.GetProperty("scheduler_clock").GetDouble(),
                row.GetProperty("networking").GetBoolean(), row.GetProperty("owned").GetBoolean());
            var native = Bytes(row, "actor_hex");
            firstActor ??= actor;
            firstTemplate ??= template;
            void Equal<T>(T actual, T expected, string field) where T : IEquatable<T>
            {
                if (!actual.Equals(expected)) throw new Exception($"Original initial factory {template.ItemType}: {field}: {actual} != {expected}");
                checks++;
            }
            void Block(byte[] actual, byte[] expected, string field)
            {
                if (!actual.AsSpan().SequenceEqual(expected)) throw new Exception($"Original initial factory {template.ItemType}: {field} differs.");
                checks += actual.Length;
            }
            Equal(actor.Base.ObjectReference, Word(native, 12), "reference");
            Equal(actor.Base.ObjectType, Word(native, 16), "resource");
            Equal(actor.Base.Flags, Word(native, 20), "flags");
            Equal(actor.Base.Byte38, native[0x38], "instance counter");
            Equal(actor.Base.Owner, native[0x39], "owner");
            Equal(counter.Value, row.GetProperty("counter").GetByte(), "shared counter");
            Equal(actor.Node.Flags, native[8], "node active");
            Equal(actor.Base.Word28, Word(native, 40), "cached angle");
            Equal(BitConverter.SingleToUInt32Bits(actor.Direction.X), Word(native, 0x70), "direction X");
            Equal(BitConverter.SingleToUInt32Bits(actor.Direction.Y), Word(native, 0x74), "direction Y");
            Equal(BitConverter.SingleToUInt32Bits(actor.Direction.Z), Word(native, 0x78), "direction Z");
            Block(actor.Component.Values, Bytes(row, "values_hex"), "values");
            Block(actor.Component.Health, Bytes(row, "health_hex"), "health");
            Equal(actor.Component.CombatProfile, row.GetProperty("profile").GetByte(), "profile");
            Equal(actor.Component.ResultValid, row.GetProperty("valid").GetByte(), "result valid");
            Equal(actor.BehaviorFlags, Word(native, 0x1f4), "behavior");
            Equal(actor.RelationClass, Word(native, 0x1f0), "relation");
            Equal(random.State, U(row, "rng_after"), "RNG state");
            Equal(actor.Scratch.Length, (int)Word(native, 0x4f4), "scratch size uses reserved reference");
            Equal(actor.RandomTail, BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(0x574)), "random tail");
            Equal(BitConverter.SingleToUInt32Bits(actor.ThinkElapsed), Word(native, 0x208), "elapsed");
            Block(actor.KiState, native.AsSpan(0xfc, 84).ToArray(), "KI bank");
            Block(actor.EiState, native.AsSpan(0x150, 84).ToArray(), "EI bank");
            Block(actor.ActionState, native.AsSpan(0x2ed, GoldCreatureStateLayout.Block2EDSize).ToArray(), "action bank");
            var record = Bytes(row, "record_hex");
            Equal(BitConverter.DoubleToUInt64Bits(actor.Node.StartedAt), BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)), "activation clock");
            Equal(BitConverter.DoubleToUInt64Bits(actor.Node.NextDue), BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(16)), "activation due");
            if (!ReferenceEquals(factory.Actors[actor.Base.ObjectReference], actor) ||
                !ReferenceEquals(factory.Activation.Nodes.Single(), actor.Node) || references.Available != 0 ||
                !factory.GraphicObjects.SequenceEqual(row.GetProperty("graphic_objects").EnumerateArray().Select(value => value.GetUInt32())))
                throw new Exception("Composed initial factory retained stale registry/queue/reference/graphic state.");
            checks += 4;
            var position = row.GetProperty("position");
            var posed = WorldCreatureScriptPlacement.PlaceDormant(actor, new(position.GetProperty("x").GetInt32(),
                position.GetProperty("y").GetInt32(), U(position, "surface"), U(position, "sector")), factory.Activation);
            posed = WorldCreatureScriptPose.Apply(posed, row.GetProperty("script_facing").GetInt32());
            var nativePose = Bytes(row, "pose_actor_hex");
            Equal(posed.Node.Flags, nativePose[8], "dormant node");
            Equal((uint)posed.ScriptInitialPosition.X, Word(nativePose, 0x2bd), "script position X");
            Equal((uint)posed.ScriptInitialPosition.Y, Word(nativePose, 0x2c1), "script position Y");
            Equal(posed.ScriptInitialPosition.Sector, (uint)BinaryPrimitives.ReadUInt16LittleEndian(nativePose.AsSpan(0x2b9)), "script sector");
            Equal(posed.ScriptInitialPosition.Surface, (uint)nativePose[0x2c5], "script surface");
            Equal(BitConverter.SingleToUInt32Bits(posed.Direction.X), Word(nativePose, 0x70), "script heading X");
            Equal(BitConverter.SingleToUInt32Bits(posed.Direction.Y), Word(nativePose, 0x74), "script heading Y");
            var assigned = WorldCreatureScriptLevel.Apply(posed, template, row.GetProperty("level_input").GetInt32(),
                results, levelBalance, progression, levelDefinitions, 1, actor.Base.Owner,
                row.GetProperty("networking").GetBoolean(), row.GetProperty("sector_maximum").GetUInt16(), 0);
            Block(assigned.Component.Values, Bytes(row, "assigned_values_hex"), "assigned values");
            Block(assigned.Component.Health, Bytes(row, "assigned_health_hex"), "assigned health");
            Equal(assigned.Component.ResultValid, row.GetProperty("assigned_valid").GetByte(), "assigned result valid");
            Equal(assigned.BehaviorFlags, Word(Bytes(row, "assigned_actor_hex"), 0x1f4), "assigned behavior");
            factory.Update(assigned);
            Equal(ReferenceEquals(factory.Actors[actor.Base.ObjectReference], assigned), true, "assigned registry update");
            var controlled = WorldCreatureScriptControl.Apply(assigned with { RuntimeFlags = U(row, "runtime_flags_before") },
                U(row, "script_behavior"), row.GetProperty("aggression").GetInt32(), row.GetProperty("flag_input").GetInt32());
            var nativeControl = Bytes(row, "control_actor_hex");
            Equal(controlled.RelationClass, Word(nativeControl, 0x1f0), "script relation class");
            Equal(controlled.BehaviorFlags, Word(nativeControl, 0x1f4), "script behavior");
            Equal(controlled.RuntimeFlags, Word(nativeControl, 0x200), "script runtime flags");
            Equal(controlled.Flags2B7, BinaryPrimitives.ReadUInt16LittleEndian(nativeControl.AsSpan(0x2b7)), "script status flags");
        }
        foreach (var row in fixture.RootElement.GetProperty("level_cases").EnumerateArray())
        {
            var nativeValues = Bytes(row, "values_hex");
            if (BinaryPrimitives.ReadUInt16LittleEndian(nativeValues.AsSpan(78)) > 101)
            {
                try
                {
                    WorldCreatureScriptLevel.Apply(firstActor!, firstTemplate!, row.GetProperty("input").GetInt32(),
                        results, levelBalance, progression, levelDefinitions, U(row, "difficulty"), 1, true, 0, 0);
                    throw new Exception("Uncompared startup XP power accepted.");
                }
                catch (NotSupportedException) { checks++; continue; }
            }
            var actor = WorldCreatureScriptLevel.Apply(firstActor!, firstTemplate!, row.GetProperty("input").GetInt32(),
                results, levelBalance, progression, levelDefinitions, U(row, "difficulty"), 1, true, 0, 0);
            if (!actor.Component.Values.AsSpan().SequenceEqual(Bytes(row, "values_hex")) ||
                !actor.Component.Health.AsSpan().SequenceEqual(Bytes(row, "health_hex")) ||
                actor.Component.ResultValid != row.GetProperty("valid").GetByte() || actor.BehaviorFlags != U(row, "behavior"))
                throw new Exception($"Original script level {row.GetProperty("input")}, difficulty {row.GetProperty("difficulty")} differs; XP {BinaryPrimitives.ReadUInt32LittleEndian(actor.Component.Values.AsSpan(4))}.");
            checks += SacredActorValuesLayout.Size + GoldCreatureValuesLayout.Block128Size + 2;
        }
        if (horse) return checks;
        // Execute the original first CreateNPC command through the production
        // script dispatcher. Synthetic KEYX location/equipment descriptors are
        // boundaries; equipment and final results execute before the expected
        // publication preparation. No whole-campaign upload/readiness is granted.
        using var operands = Read("native-creature-creation-operands");
        var original = operands.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("name").GetString() == "installed_first_creature");
        var code = Bytes(original, "command_hex");
        var templateBytes = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(templateBytes, 0x00464943);
        BinaryPrimitives.WriteUInt32LittleEndian(templateBytes.AsSpan(4), 2);
        var sample = fixture.RootElement.GetProperty("cases").EnumerateArray().First(row => row.GetProperty("type").GetUInt32() == original.GetProperty("result").GetProperty("type").GetUInt32() && row.GetProperty("level_input").GetInt32() == 5);
        Bytes(sample, "template_hex").CopyTo(templateBytes, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
        var creatures = CreaturePakArchive.Parse(templateBytes);
        var type = creatures.Entries[1].ItemType;
        var items = new Dictionary<uint, ItemsPakEntry> { [type] = new(new((ushort)type, 0, 128, 0),
            MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(sample, "descriptor_hex")), "") };
        using var equipmentFixture = Read("native-equipment-initial-factory");
        var authoredEquipment = original.GetProperty("result").GetProperty("equipment").EnumerateArray().ToArray();
        var weaponHeader = new byte[256 + authoredEquipment.Length * 258]; BinaryPrimitives.WriteUInt32LittleEndian(weaponHeader, 0x084e5057);
        BinaryPrimitives.WriteInt32LittleEndian(weaponHeader.AsSpan(4), authoredEquipment.Length);
        for (var index = 0; index < authoredEquipment.Length; index++)
        {
            var resource = authoredEquipment[index].GetUInt32();
            var nativeItem = equipmentFixture.RootElement.GetProperty("cases").EnumerateArray().First(row => U(row, "type") == resource);
            items[resource] = new(new((ushort)resource, 0, 128, 0), MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(nativeItem, "descriptor_hex")), "");
            Bytes(nativeItem, "initial_values_hex").CopyTo(weaponHeader, 256 + index * 258 + 128);
            weaponHeader[256 + index * 258 + 30] = nativeItem.GetProperty("usage").GetByte();
        }
        var objects = SacredObjectTemplateArchive.FromPayload(items, weaponHeader);
        var keyx = new byte[256 + 768]; BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c), SacredWorldCoordinates.TileCenterToWorld(52 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40), SacredWorldCoordinates.TileCenterToWorld(39 * 64) - 25);
        var sectors = SacredSectorIndex.FromPayload(keyx);
        using var slotEvidence = Read("native-equipment-slot-selection");
        var assets = new WorldScriptCreatureAssets(objects, creatures, results, definitions, levelBalance, progression, levelDefinitions, sectors)
            { Equipment = EquipmentGenerationFixture.ReadStartup(root), EquipmentSlots = SacredEquipmentSlotDefinitions.Read(Bytes(slotEvidence.RootElement, "override_code_hex")) };
        var sharedReferences = new GoldObjectReferencePool(); sharedReferences.Apply(GoldReferenceBatch.FromPayload(ScriptGroupChecks.Words(26, 27, 28)));
        var sharedCounter = new WorldObjectAllocationCounter(9);
        var sharedRandom = new SacredRandomSequence(2004);
        var world = new WorldScriptCreatureWorld(assets, sharedReferences, sharedCounter, new(1, 1, false, 0, () => 1000), sharedRandom, _ => { });
        var scriptDefinitions = new WorldScriptDefinitions(); scriptDefinitions.DefinePosition("wache1_rg1", 3388, 2524);
        var context = new WorldScriptExecutionContext(scriptDefinitions, [], ReadOnlyMemory<byte>.Empty, true, sharedRandom.Next) { CreatureWorld = world };
        new WorldScriptExecutor(context).ExecuteStartup(code);
        if (context.IsInvalidated || context.ExecutedCommands != 1 || !context.StartupCompleted || world.Pending is not null)
            throw new Exception("Compared initial NPC command did not complete its private construction/publication effects.");
        var pending = world.Prepared.Single();
        using var equipped = Read("native-startup-equipped-results");
        var nativeEquipped = Bytes(equipped.RootElement.GetProperty("cases").EnumerateArray().First(row =>
            U(row, "level") == 6 && U(row, "difficulty") == 1 && U(row, "hands") == 3), "after_hex");
        if (pending.Actor is not { } prepared || pending.Stage != "prepared" || !pending.PacketReady || pending.CreatureState is null || pending.Publications.Count != 3 ||
            prepared.Base.ObjectReference != 26 || sharedCounter.Value != 12 || sharedReferences.Available != 0 || prepared.Node.Active ||
            !ReferenceEquals(world.Factory.Actors[26], prepared) || pending.FacingDegrees != original.GetProperty("result").GetProperty("facing").GetUInt16() ||
            !prepared.Component.Values.AsSpan().SequenceEqual(nativeEquipped.AsSpan(0x3b0, 238)) ||
            !prepared.Component.Health.AsSpan().SequenceEqual(nativeEquipped.AsSpan(0x4d0, 24)) || prepared.Component.ResultValid != nativeEquipped[0x4cf])
            throw new Exception("First script creature's composed preparation differs from native scalar/level state or registry lifetime.");
        if (prepared.ScriptGroup != 1 || context.Groups.Groups.Count != 1 || !context.Groups.Groups[1].SequenceEqual(new uint[] { 26 }))
            throw new Exception("First script creature did not retain its native startup group without publication.");
        if (pending.Equipment.Count != 2 || pending.PendingEquipment is not { } equipmentItem || equipmentItem.Physical.Base.ObjectReference != 28 ||
            equipmentItem.Physical.Base.Byte38 != 12 || !equipmentItem.Node.Active || equipmentItem.Node.PeriodMilliseconds != 200000 ||
            world.Factory.Activation.ActivationCount != 3 || world.Factory.Activation.Records.Count != 3 ||
            !ReferenceEquals(world.EquipmentFactory.Activation, world.Factory.Activation) ||
            !ReferenceEquals(world.Factory.Activation.Records[2].Node, equipmentItem.Node) || prepared.EquipmentReferences[12] != 28 || prepared.EquipmentReferences[13] != 27 ||
            pending.Equipment.Any(item => item.Physical.AttachmentParent != 26))
            throw new Exception("Initial equipment did not share the original reservation/counter/scheduler order and installed attachment graph.");
        var expectedRandom = new SacredRandomSequence(2004);
        for (var draw = 0; draw < 3; draw++) expectedRandom.Next();
        using var generation = Read("native-startup-equipment-generation");
        var firstGeneration = generation.RootElement.GetProperty("cases").EnumerateArray().First(row => U(row, "seed") == expectedRandom.State &&
            U(row, "type") == pending.Equipment[0].Physical.Base.ObjectType).GetProperty("outputs")[0];
        expectedRandom = new(U(firstGeneration, "rng_after"));
        var nativeGeneration = generation.RootElement.GetProperty("cases").EnumerateArray().First(row => U(row, "seed") == expectedRandom.State &&
            U(row, "type") == equipmentItem.Physical.Base.ObjectType).GetProperty("outputs")[0];
        if (sharedRandom.State != U(nativeGeneration, "rng_after") || !equipmentItem.Generated ||
            !equipmentItem.Values.AsSpan().SequenceEqual(Bytes(nativeGeneration, "result_hex")) ||
            equipmentItem.Name?.NativeReference != U(nativeGeneration, "name_reference") ||
            equipmentItem.Physical.Base.Word3C != U(nativeGeneration, "name_reference") ||
            equipmentItem.Quality != U(nativeGeneration, "quality") || equipmentItem.LegacyGenerationSelector != U(nativeGeneration, "legacy_selector"))
            throw new Exception("Initial script equipment generation/naming differs from original PC=24 caller, including pre-decode/constructor RNG order.");
        if (!pending.Publications.Select(message => GoldObjectBaseState.FromPayload(message.Payload.AsSpan(message.Opcode == 0x16f ? 24 : 8)).ObjectReference).SequenceEqual(new uint[] { 26, 28, 27 }) ||
            !pending.Publications.Select(message => message.Opcode).SequenceEqual(new ushort[] { 0x16f, 0x16e, 0x16e }) ||
            (prepared.Base.Flags & 0x10000) != 0 || pending.Equipment.Any(item => (item.Physical.Base.Flags & 0x10000) != 0))
            throw new Exception("Compared initial publication order, opcodes or pending-bit clearing differ from native.");
        checks += 22;
        return checks;
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint U(JsonElement row, string key) => row.GetProperty(key).GetUInt32();
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
