using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostHeroFactoryChecks
{
    public static int Run(string root)
    {
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        using var proof = Read("native-host-hero-factory");
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var passive = Read("native-creature-passive-skills");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var source in new[] { basis, initialization, finalization, precision, proof })
            foreach (var chunk in source.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Bytes(chunk, "hex").CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var results = WorldCreatureScalarResultAssets.Read(image, Bytes(passive.RootElement, "balance_prefix_hex"));
        var definitions = SacredCreatureConstructionDefinitions.Read(image);
        var defaults = SacredEquipmentDefaultDefinitions.Read(image);
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var archive = new byte[CreaturePakHeaderLayout.Size + 2 * CreatureGoldRecordLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
            Bytes(row, "template_hex").CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
            var template = CreaturePakArchive.Parse(archive).Entries[1];
            var descriptor = new ItemsPakEntry(new((ushort)template.ItemType, 0, 128, 0),
                MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(Bytes(row, "descriptor_hex")), "");
            var grants = new GoldObjectReferencePool();
            grants.Apply(GoldReferenceBatch.FromPayload(BitConverter.GetBytes(17u)));
            var counter = new WorldObjectAllocationCounter();
            var random = new SacredRandomSequence(U(row, "rng_seed"));
            var owner = row.GetProperty("owner").GetByte();
            var factory = new WorldCreatureInitialFactory(results, definitions, grants, counter, random, owner, 1);
            if (Enumerable.Range(0, 19).Any(slot => defaults.Resource(template.ItemType, (ushort)slot) != 0))
            {
                Check(row.GetProperty("factory_attempts").GetArrayLength() > 1, "native requires a default-item construction attempt");
                try { factory.CreateHero(template, descriptor, 0, 0, defaults); throw new Exception("Missing default-item factory accepted"); }
                catch (NotSupportedException) { Check(counter.Value == 0 && grants.Available == 1 && factory.Actors.Count == 0, "unsupported default-item path rejects atomically"); }
                continue;
            }
            var actor = factory.CreateHero(template, descriptor, U(row, "clock"), row.GetProperty("scheduler_clock").GetDouble(), defaults);
            var native = Bytes(row, "actor_hex");
            uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(at));
            ushort Short(int at) => BinaryPrimitives.ReadUInt16LittleEndian(native.AsSpan(at));
            Check(U(row, "seeded_reference") == owner && row.GetProperty("seed_cleanups")[0].GetUInt32() == owner, "Engine queues actual local slot before factory");
            Check(actor.Base.ObjectReference == Word(12) && actor.Base.ObjectType == Word(16) && actor.Base.Flags == Word(20), "native assigned base identity/flags");
            Check(actor.Base.Owner == native[57] && actor.Base.Byte38 == native[56] && actor.Base.ScriptResourceId == Word(60), $"owner {actor.Base.Owner}/{native[57]}, allocation byte {actor.Base.Byte38}/{native[56]} and native script resource {actor.Base.ScriptResourceId}/{Word(60)}");
            Check(actor.Base.Sector == Short(24) && actor.Base.X == Word(28) && actor.Base.Y == Word(32) && actor.Base.Surface == native[36], "unplaced native root");
            Check(actor.RelationClass == Word(0x1f0) && actor.BehaviorFlags == Word(0x1f4) && actor.RuntimeFlags == Word(0x200), "hero class and constructor flags");
            Check(actor.ThinkInterval == BitConverter.UInt32BitsToSingle(Word(0x204)) && actor.ThinkElapsed == BitConverter.UInt32BitsToSingle(Word(0x208)), "hero tick interval and randomized elapsed");
            Check(BitConverter.SingleToUInt32Bits(actor.Direction.X) == Word(0x70) && BitConverter.SingleToUInt32Bits(actor.Direction.Y) == Word(0x74) && BitConverter.SingleToUInt32Bits(actor.Direction.Z) == Word(0x78), "native initial direction event");
            Check(actor.RandomTail == Short(0x574) && actor.EquipmentSelector == Short(0x25f), "constructor tail and selector");
            Block(actor.Component.Values, Bytes(row, "values_hex"), "constructor values");
            Block(actor.Component.Health, Bytes(row, "health_hex"), "constructor health");
            Block(actor.KiState, native.AsSpan(0xfc, 84), "KI bank");
            Block(actor.EiState, native.AsSpan(0x150, 84), "EI bank");
            Block(actor.ActionState, native.AsSpan(0x2ed, 173), "action bank");
            Check(actor.Node.Active && actor.Node.Flags == native[8] && actor.Node.PeriodMilliseconds == Word(4), "actual initial node activation");
            Check(counter.Value == row.GetProperty("counter").GetByte() && random.State == U(row, "rng_after"), "shared counter and RNG");
            Check(factory.Actors.Count == 1 && ReferenceEquals(factory.Actors[owner], actor), "existing factory registry owns hero");
            Check(grants.Available == 1, "local identity does not consume an ordinary 153 grant");
            Check(factory.Activation.Records.Count == 1, "single shared scheduler record");
            try { factory.CreateHero(template, descriptor, 0, 0, defaults); throw new Exception("Repeated hero creation accepted"); }
            catch (InvalidOperationException) { Check(counter.Value == 1 && grants.Available == 1, "repeat rejects before allocator mutation"); }
        }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostHeroFactory: " + reason); checks++; }
        void Block(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string reason) { Check(actual.SequenceEqual(expected), reason); checks += actual.Length; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint U(JsonElement row, string key) => row.GetProperty(key).GetUInt32();
}
