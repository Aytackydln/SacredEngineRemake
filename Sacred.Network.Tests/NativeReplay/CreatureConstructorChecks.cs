using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class CreatureConstructorChecks
{
    public static int Run(string root) => Run(root, "native-creature-constructor");
    public static int RunHorse(string root) => Run(root, "native-horse-constructor");

    private static int Run(string root, string fixtureName)
    {
        JsonDocument Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var fixture = Fixture(fixtureName);
        using var basis = Fixture("native-creature-base-results");
        using var initialization = Fixture("native-creature-result-initialization");
        using var finalization = Fixture("native-creature-result-finalization");
        using var precision = Fixture("native-startup-floating-point");
        using var passive = Fixture("native-creature-passive-skills");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision, fixture })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var assets = WorldCreatureScalarResultAssets.Read(image, fixtureName == "native-horse-constructor" ? Bytes(fixture.RootElement, "balance_hex") : Bytes(passive.RootElement, "balance_prefix_hex"));
        var definitions = SacredCreatureConstructionDefinitions.Read(image);
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
            Bytes(row, "template_hex").CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
            var template = CreaturePakArchive.Parse(archive).Entries[1];
            var random = new SacredRandomSequence(U(row, "rng_seed"));
            var observations = new List<WorldCreatureConstructionObservation>();
            var actor = WorldCreatureConstructor.Create(template, assets, definitions, random,
                new(U(row, "argument"), U(row, "clock"), 1, 1), observations.Add);
            var native = Bytes(row, "actor_hex");
            uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(at));
            ushort Short(int at) => BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(at));
            void Equal<T>(T actual, T expected, string field) where T : IEquatable<T>
            {
                if (!actual.Equals(expected)) throw new Exception($"Native constructor {template.ItemType}/{U(row, "fill")}: {field}: {actual} != {expected}");
                checks++;
            }
            void Block(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string field)
            {
                if (!actual.SequenceEqual(expected)) throw new Exception($"Native constructor {template.ItemType}/{U(row, "fill")}: {field}: {Convert.ToHexString(actual)} != {Convert.ToHexString(expected)}");
                checks += actual.Length;
            }
            void Component(WorldCreatureConstructionObservation observed, JsonElement expected)
            {
                Block(observed.Values, Bytes(expected, "values_hex"), observed.Step + " values");
                Block(observed.Health, Bytes(expected, "health_hex"), observed.Step + " TP");
                Equal(observed.CombatProfile, expected.GetProperty("profile").GetByte(), "profile");
                Equal(observed.ResultValid, expected.GetProperty("valid").GetByte(), "valid");
            }
            Block(actor.Component.Values, Bytes(row, "values_hex"), "final values");
            Block(actor.Component.Health, Bytes(row, "health_hex"), "final TP");
            Equal(actor.Component.CombatProfile, row.GetProperty("profile").GetByte(), "final profile");
            Equal(actor.Component.ResultValid, row.GetProperty("valid").GetByte(), "final valid");
            Equal(random.State, U(row, "rng_after"), "RNG state");
            Equal(actor.Base.ObjectReference, Word(12), "reference");
            Equal(actor.Base.ObjectType, Word(16), "resource");
            Equal(actor.Base.Flags, Word(20), "object flags");
            Equal(actor.Base.Owner, native[0x39], "owner");
            Equal(actor.RelationClass, Word(0x1f0), "class");
            Equal(actor.BehaviorFlags, Word(0x1f4), "behavior");
            Equal(actor.RuntimeFlags, Word(0x200), "runtime flags");
            Equal(actor.EquipmentSelector, Short(0x25f), "equipment selector");
            Equal(actor.RandomTail, Short(0x574), "random tail");
            Equal(BitConverter.SingleToUInt32Bits(actor.ThinkInterval), Word(0x204), "think interval");
            Equal(BitConverter.SingleToUInt32Bits(actor.ThinkElapsed), Word(0x208), "think elapsed");
            Equal(BitConverter.SingleToUInt32Bits(actor.AnimationSpeedMultiplier), Word(0x2c9), "animation speed");
            Equal(actor.Word210, Word(0x210), "+210"); Equal(actor.Word564, Word(0x564), "+564");
            Equal(actor.LastUpdateSecond, Word(0x56c), "last second");
            Equal(actor.PaletteMask, Word(0x5c), "palette");
            Equal(actor.KiMode, Short(0xfc), "KI mode"); Equal(actor.EiMode, Short(0x150), "EI mode");
            Equal((uint)actor.Scratch.Length, Word(0x4f4), "scratch size");
            if (actor.Scratch.Length != 0) Block(actor.Scratch, Bytes(row, "scratch_hex"), "scratch");
            Block(actor.KiState, native.AsSpan(0xfc, 84), "KI bank");
            Block(actor.EiState, native.AsSpan(0x150, 84), "EI bank");
            Block(actor.ActionState, native.AsSpan(0x2ed, 173), "action bank");
            for (var slot = 0; slot < 19; slot++) Equal(actor.EquipmentReferences[slot], Word(0x1a4 + slot * 4), "equipment slot");
            Equal(observations.Count, 6, "construction order extent");
            var events = row.GetProperty("events").EnumerateArray().ToArray();
            Equal(events.Length, 2, "real event handler extent");
            Equal(events[0].GetProperty("handler").GetString()!, "0x44bcd0", "physical event handler");
            Equal(events[1].GetProperty("handler").GetString()!, "0x52e590", "creature event handler");
            var stages = row.GetProperty("stages").EnumerateArray().ToArray();
            // Earlier results may read untouched native heap bytes (actor flags /
            // combat profile). Both real event handlers ignore them here. Only
            // zero-backed intermediate results are compared; all later results
            // and the final state must agree for BOTH backing patterns.
            if (U(row, "fill") == 0)
            {
                Component(observations[0], events[0]);
                Component(observations[1], stages[0]);
            }
            Component(observations[2], events[1]);
            for (var at = 1; at < stages.Length; at++)
            {
                Component(observations[at + 2], stages[at]);
                Equal(observations[at + 2].Behavior, U(stages[at], "behavior"), "early behavior ordering");
            }
        }
        if (WorldCreatureConstructionEvents.LevelChanged(false, 9, 123) != 123)
            throw new Exception("Physical handler must ignore canonical family 7 regardless of KI/palette.");
        foreach (var state in new[] { (Mode: (ushort)9, Palette: 0u), (Mode: (ushort)0, Palette: 1u) })
        {
            try { WorldCreatureConstructionEvents.LevelChanged(true, state.Mode, state.Palette); throw new Exception("Unsupported event state accepted."); }
            catch (NotSupportedException) { checks++; }
        }
        return checks;
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint U(JsonElement row, string key) => row.GetProperty(key).GetUInt32();
}
