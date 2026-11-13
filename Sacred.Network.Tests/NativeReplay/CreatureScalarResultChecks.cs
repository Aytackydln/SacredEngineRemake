using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class CreatureScalarResultChecks
{
    public static int Run(string root)
    {
        JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Fixture("native-creature-scalar-results");
        using var initialFixture = Fixture("native-creature-result-initialization");
        using var baseFixture = Fixture("native-creature-base-results");
        using var finalFixture = Fixture("native-creature-result-finalization");
        using var passiveFixture = Fixture("native-creature-passive-skills");
        var initialization = SacredCreatureResultDefinitions.Read(Image(initialFixture.RootElement));
        var combat = SacredCreatureCombatDefinitions.Read(Image(baseFixture.RootElement));
        var finalization = SacredCreatureResultFinalizationDefinitions.Read(Image(finalFixture.RootElement));
        var balance = Convert.FromHexString(passiveFixture.RootElement.GetProperty("balance_prefix_hex").GetString()!);
        var baseBalance = CreatureResultBalanceBinReader.Read(balance);
        var skillBalance = CreatureSkillBalanceBinReader.Read(balance);
        var equipmentBalance = CreatureEquipmentLevelBalanceBinReader.Read(balance);
        var record = Convert.FromHexString(fixture.RootElement.GetProperty("template_hex").GetString()!);
        var archive = new byte[CreaturePakHeaderLayout.Size + record.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 1);
        record.CopyTo(archive, CreaturePakHeaderLayout.Size);
        var template = CreaturePakArchive.Parse(archive).Entries[0];
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var predicates = input.GetProperty("predicates").EnumerateArray().Select(value => value.GetBoolean()).ToArray();
            var skills = new SacredCreatureSkillContext(predicates[0], predicates[1], predicates[2], predicates[3], predicates[4], predicates[5],
                input.GetProperty("usage").GetUInt16(), input.GetProperty("offhand").GetBoolean(), false);
            var context = new WorldCreatureScalarResultContext(input.GetProperty("resource").GetUInt32(), 100, input.GetProperty("difficulty").GetUInt32(),
                input.GetProperty("boost").GetBoolean(), 0x12345678, row.GetProperty("actor_flags").GetUInt32(), row.GetProperty("component_flags").GetUInt32(),
                row.GetProperty("profile").GetByte(), 0, 0, false, skills, 1, (SacredX87Precision)row.GetProperty("precision").GetInt32());
            var equipment = row.GetProperty("gear").EnumerateArray().Select(slot => new WorldCreatureScalarEquipment(slot.GetProperty("slot").GetUInt16(),
                slot.GetProperty("records_hex").EnumerateArray().Select(value => new WorldCreatureScalarEquipmentRecord(Convert.FromHexString(value.GetString()!),
                    (SacredItemCategory)slot.GetProperty("category").GetByte(), 0)).ToArray())).ToArray();
            var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var before = (byte[])values.Clone();
            var actual = Calculate(context, equipment);
            var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            if (!actual.Values.SequenceEqual(expected) || actual.Regeneration != row.GetProperty("regeneration").GetUInt32() ||
                actual.EquipmentFlagAdditions != (row.GetProperty("flags_after").GetUInt32() & 0x10000000))
            {
                var at = Enumerable.Range(0, values.Length).FirstOrDefault(i => actual.Values[i] != expected[i], -1);
                throw new Exception($"Composed native scalar results differ at {at}, regeneration {actual.Regeneration}, flags {actual.EquipmentFlagAdditions:X8}: {row}; actual {Convert.ToHexString(actual.Values)}");
            }
            if (!values.SequenceEqual(before)) throw new Exception("Private scalar calculation mutated source actor.");
            checks += values.Length + 3;
            if (checks > 241) continue;
            foreach (var unsupported in new[] { context with { Reference = 16 }, context with { LocalReference = 100 }, context with { Mounted = true },
                context with { LearnedCombatArtCount = 1 }, context with { ActiveEffectCount = 1 }, context with { Precision = SacredX87Precision.Double } })
            {
                try { Calculate(unsupported, []); throw new Exception("Unsupported actor state accepted."); }
                catch (NotSupportedException) { checks++; }
            }
            var item = new byte[130]; item[25] = 255;
            try { Calculate(context, [new(0, [new(item, (SacredItemCategory)5, 0)])]); throw new Exception("Overlevel item accepted."); }
            catch (NotSupportedException) { checks++; }
            item[25] = 10; BinaryPrimitives.WriteUInt16LittleEndian(item.AsSpan(58), 0x2000);
            try { Calculate(context, [new(0, [new(item, (SacredItemCategory)8, 0)])]); throw new Exception("Spell-item threshold accepted."); }
            catch (NotSupportedException) { checks++; }
            if (!values.SequenceEqual(before)) throw new Exception("Failed scalar calculation mutated source actor.");

            WorldCreatureScalarResult Calculate(WorldCreatureScalarResultContext state, IReadOnlyList<WorldCreatureScalarEquipment> gear)
                => WorldCreatureScalarResults.Calculate(values, input.GetProperty("has_template").GetBoolean() ? template : null,
                    state, gear, initialization, combat, finalization, baseBalance, skillBalance, equipmentBalance);
        }
        return checks;
    }

    private static byte[] Image(JsonElement json)
    {
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
}
