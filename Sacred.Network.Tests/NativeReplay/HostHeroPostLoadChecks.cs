using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;
using System.Numerics;

internal static class HostHeroPostLoadChecks
{
    public static int Run(string root)
    {
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-hero-values.json")));
        var data = evidence.RootElement;
        using var basis = Read("native-creature-base-results");
        using var initialization = Read("native-creature-result-initialization");
        using var finalization = Read("native-creature-result-finalization");
        using var precision = Read("native-startup-floating-point");
        using var passive = Read("native-creature-passive-skills");
        var image = new byte[basis.RootElement.GetProperty("image_size").GetInt32()];
        foreach (var definition in new[] { basis, initialization, finalization, precision, evidence })
            foreach (var chunk in definition.RootElement.GetProperty("definition_chunks").EnumerateArray())
                Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var balance = Convert.FromHexString(passive.RootElement.GetProperty("balance_prefix_hex").GetString()!);
        var templateBytes = Convert.FromHexString(data.GetProperty("template_hex").GetString()!);
        var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943); BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 1);
        templateBytes.CopyTo(archive, CreaturePakHeaderLayout.Size);
        var template = CreaturePakArchive.Parse(archive).Entries[0];
        using var factoryProof = Read("native-host-hero-factory");
        var factoryRow = factoryProof.RootElement.GetProperty("cases").EnumerateArray().First(row => row.GetProperty("type").GetUInt32() == template.ItemType && row.GetProperty("owner").GetByte() == 1);
        var initialBytes = Convert.FromHexString(factoryRow.GetProperty("actor_hex").GetString()!);
        var assets = WorldCreatureScalarResultAssets.Read(image, balance);
        var profiles = SacredSavedCreatureProfiles.Read(image);
        var timers = SacredCombatArtTimerDefinitions.Read(image);
        using var post = Read("native-host-hero-post-load");
        var postData = post.RootElement;
        var rules = SacredHostHeroPostLoadDefinitions.Read(Convert.FromHexString(postData.GetProperty("class_code_hex").GetString()!));
        var source = SacredSavedCreature.FromPayload(Convert.FromHexString(postData.GetProperty("source_hex").GetString()!), postData.GetProperty("format_version").GetByte());
        var artInputs = new SacredCreatureArtTimerInputs(Enumerable.Range(0, source.Values.Count22).Select(index => source.Values.GetRecord22(index).ToArray()),
            timers, SacredCombatArtSkillTimers.Read(image, balance, source.Values.Block08.Slice(28, 8).ToArray().ToHashSet()));
        var preArts = data.GetProperty("cases")[0].GetProperty("arts_hex").GetString()!;
        var preArtBytes = Convert.FromHexString(preArts);
        var records = Enumerable.Range(0, preArtBytes.Length / 22).Select(index => preArtBytes.AsSpan(index * 22, 22).ToArray()).ToArray();
        var checks = 0;
        foreach (var row in postData.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var expected = Convert.FromHexString(row.GetProperty("actor_hex").GetString()!);
            var actor = new WorldCreatureInitialActor(new(Word(before, 12), Word(before, 16), Word(before, 20), Short(before, 24),
                unchecked((int)Word(before, 28)), unchecked((int)Word(before, 32)), before[36], Word(before, 40), Word(before, 52), before[56], before[57], Word(before, 60)),
                new(before.AsSpan(0x3b0, 238).ToArray(), before.AsSpan(0x4d0, 24).ToArray(), before[0x4ce], before[0x4cf]),
                Word(before, 0x1f0), Word(before, 0x1f4), Short(before, 0x25f), Short(before, 0x574), Float(before, 0x208), 0, [])
            {
                RestoredCreature = new(source, records),
                RestoredPhysical = new(0, 0, 0, 0, 0, 0, default, default, default, default, default, 0, 0, Word(before, 0xb0), default, default, default, null, 0, 0),
                KiState = before.AsSpan(0xfc, 84).ToArray(), EiState = before.AsSpan(0x150, 84).ToArray(),
                KiMode = Short(before, 0xfc), EiMode = Short(before, 0x150), LocomotionAction = Word(before, 0x1f8)
            };
            var queue = new WorldObjectActivationQueue(); queue.ActivateInitial(actor.Node, 1);
            var result = WorldCreatureHeroPostLoad.Apply(actor, template, assets, artInputs, rules, 1);
            Check(result.HeroPostLoadCompleted, "all supported C0 actions completed");
            Check(result.Base.Flags == Word(expected, 20) && result.Base.ObjectReference == Word(expected, 12) && result.Base.Owner == expected[57], "base identity and cleanup");
            Check(result.KiState.AsSpan().SequenceEqual(expected.AsSpan(0xfc, 84)) && result.EiState.AsSpan().SequenceEqual(expected.AsSpan(0x150, 84)), "KI/EI reset first four bytes only");
            Check(result.KiMode == 0 && result.EiMode == 0 && result.LocomotionAction == Word(expected, 0x1f8), "selectors and locomotion");
            Check(result.RestoredPhysical!.Flags == Word(expected, 0xb0), "physical flag mask");
            Check(result.Component.Values.AsSpan().SequenceEqual(expected.AsSpan(0x3b0, 238)), "actual C0 scalar recalculation");
            Check(result.Component.Health.AsSpan().SequenceEqual(expected.AsSpan(0x4d0, 24)), "health retained; actual regeneration");
            Check(result.RestoredCreature!.CombatArts.SelectMany(value => value).SequenceEqual(Convert.FromHexString(row.GetProperty("arts_hex").GetString()!)), "actual C0 art recalculation");
            Check(result.Component.CombatProfile == expected[0x4ce] && result.Component.ResultValid == expected[0x4cf], "component flags");
            Check(ReferenceEquals(result.Node, actor.Node) && queue.ActivationCount == 1 && queue.Nodes.Count == 1, "no second activation or actor node");
            Check(actor.KiState.AsSpan().SequenceEqual(before.AsSpan(0xfc, 84)) && actor.EiState.AsSpan().SequenceEqual(before.AsSpan(0x150, 84)), "input actor is unchanged");
            Check(ReferenceEquals(result, WorldCreatureHeroPostLoad.Apply(result, template, assets, artInputs, rules, 1)), "idempotent C0 stage");
            foreach (var unsupported in new[] { actor with { PartnerReference = 1 }, actor with { Base = actor.Base with { Flags = actor.Base.Flags | 2 } },
                actor with { Base = actor.Base with { Owner = 2 } }, actor with { RestoredPhysical = null } })
            {
                try { WorldCreatureHeroPostLoad.Apply(unsupported, template, assets, artInputs, rules, 1); throw new Exception("Unsupported C0 context accepted"); }
                catch (NotSupportedException) { checks++; }
            }
        }
        return checks;
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        void Check(bool pass, string why) { if (!pass) throw new InvalidDataException("HostHeroPostLoad: " + why); checks++; }
    }
    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ushort Short(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static float Float(byte[] bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
}
