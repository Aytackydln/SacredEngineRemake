using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupEquippedResultChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Read("native-startup-equipped-results");
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var passive = Read("native-creature-passive-skills");
        using var selection = Read("native-equipment-slot-selection");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var results = WorldCreatureScalarResultAssets.Read(image, Bytes(passive.RootElement, "balance_prefix_hex"));
        var slots = SacredEquipmentSlotDefinitions.Read(Bytes(selection.RootElement, "override_code_hex"));
        var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
        Bytes(fixture.RootElement, "template_hex").CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
        var template = CreaturePakArchive.Parse(archive).Entries[1];
        var data = EquipmentGenerationFixture.ReadStartup(root);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("armor_penalties").EnumerateArray())
        {
            var factor = SacredCreatureArmorLevelPenalty.Calculate((SacredX87Precision)row.GetProperty("precision").GetInt32(),
                row.GetProperty("level").GetUInt16(), row.GetProperty("rank").GetByte(), row.GetProperty("item_level").GetByte(), row.GetProperty("modifier").GetInt32());
            Check(BitConverter.GetBytes(factor).AsSpan().SequenceEqual(Bytes(row, "factor_hex")), $"original armor factor {row}: {factor}");
        }
        foreach (var query in fixture.RootElement.GetProperty("queries").EnumerateArray())
        {
            var actual = SacredCreatureHandSkills.Resolve(Hand(query.GetProperty("left").GetUInt32()), Hand(query.GetProperty("right").GetUInt32()));
            var predicates = query.GetProperty("predicates").EnumerateArray().Select(value => value.GetBoolean()).ToArray();
            var expected = new SacredCreatureSkillContext(predicates[0], predicates[1], predicates[2], predicates[3], predicates[4], predicates[5],
                query.GetProperty("usage").GetUInt16(), query.GetProperty("offhand").GetBoolean(), query.GetProperty("offhand_usage10").GetBoolean());
            Check(actual == expected, $"actual hand predicates for {query}: {actual} != {expected}");

            SacredCreatureHandEquipment? Hand(uint type)
            {
                if (type == 0) return null;
                var shared = data.Templates.Equipment[type];
                return new(data.Templates.GetItem(type).ModelDesc.Category, shared.UsageIdentifier,
                    shared.InstanceValues.EquipmentTypeCode, query.GetProperty("current_type").GetByte());
            }
        }
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row, "before_hex");
            var actor = Actor(before);
            var references = new GoldObjectReferencePool();
            references.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(27u).Concat(BitConverter.GetBytes(28u)).ToArray()));
            var factory = new WorldEquipmentInitialFactory(references, new(10), new(), 1);
            var empty = actor with { EquipmentReferences = new uint[19] };
            foreach (var itemRow in fixture.RootElement.GetProperty("items").EnumerateArray())
            {
                var type = itemRow.GetProperty("type").GetUInt32();
                var item = factory.Create(data.Templates.GetItem(type), data.Templates.GetInstance(type), 1, networking: true);
                WorldScriptEquipmentGeneration.Apply(item, itemRow.GetProperty("generation_level").GetUInt16(), 1, data, new(itemRow.GetProperty("generation_seed").GetUInt32()), SacredX87Precision.Single);
                empty = WorldCreatureInitialEquipmentInstallation.Apply(empty, item, type, data, slots, factory.Items, networking: true);
            }
            var originalValues = (byte[])actor.Component.Values.Clone();
            var originalHealth = (byte[])actor.Component.Health.Clone();
            var actual = WorldCreatureEquippedResults.Apply(actor, template, results, data, factory.Items,
                row.GetProperty("difficulty").GetUInt32(), 1, networking: true, players: 0);
            var expected = Bytes(row, "after_hex");
            Check(actual.Component.Values.AsSpan().SequenceEqual(expected.AsSpan(0x3b0, 238)),
                $"complete equipped values: {row.GetProperty("hands")}/{row.GetProperty("level")}/{row.GetProperty("difficulty")}; actual {Convert.ToHexString(actual.Component.Values)} expected {Convert.ToHexString(expected.AsSpan(0x3b0, 238))}");
            Check(actual.Component.Health.AsSpan().SequenceEqual(expected.AsSpan(0x4d0, 24)), "native equipped health without invented refill");
            Check(actual.Component.CombatProfile == expected[0x4ce] && actual.Component.ResultValid == expected[0x4cf], "native caller result validity");
            Check(actual.RuntimeFlags == Word(expected, 0x200), "native equipment flag additions");
            Check(actual.Base == actor.Base && actual.BehaviorFlags == actor.BehaviorFlags && actual.EquipmentReferences.SequenceEqual(actor.EquipmentReferences), "calculation retains physical/control/slot state");
            Check(ReferenceEquals(actor.Node, actual.Node) && actor.Component.Values.AsSpan().SequenceEqual(originalValues) && actor.Component.Health.AsSpan().SequenceEqual(originalHealth), "private calculation preserves prior actor snapshot and scheduler lifetime");
        }
        return checks;
        void Check(bool value, string message) { if (!value) throw new Exception("Startup equipped results: " + message); checks++; }
    }

    private static WorldCreatureInitialActor Actor(byte[] bytes) => new(
        new(Word(bytes, 12), Word(bytes, 16), Word(bytes, 20), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32)), bytes[36],
            Word(bytes, 40), Word(bytes, 52), bytes[56], bytes[57], Word(bytes, 60)),
        new(bytes.AsSpan(0x3b0, 238).ToArray(), bytes.AsSpan(0x4d0, 24).ToArray(), bytes[0x4ce], bytes[0x4cf]),
        Word(bytes, 0x1f0), Word(bytes, 0x1f4), BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x25f)), 0, 0, 0, [])
    {
        RuntimeFlags = Word(bytes, 0x200), Flags2B7 = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0x2b7)),
        EquipmentReferences = Enumerable.Range(0, 19).Select(index => Word(bytes, 0x1a4 + index * 4)).ToArray()
    };
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
}
