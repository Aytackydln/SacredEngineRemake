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

internal static class HostHeroValueImportChecks
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
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var source = SacredSavedCreature.FromPayload(Convert.FromHexString(row.GetProperty("source_hex").GetString()!), data.GetProperty("format_version").GetByte());
            var native = Convert.FromHexString(row.GetProperty("actor_hex").GetString()!);
            var resource = source.BaseState.Resource;
            var skillIds = source.Values.Block08.Slice(28, 8).ToArray().ToHashSet();
            var artInputs = new SacredCreatureArtTimerInputs(Enumerable.Range(0, source.Values.Count22).Select(index => source.Values.GetRecord22(index).ToArray()),
                timers, SacredCombatArtSkillTimers.Read(image, balance, skillIds));
            var skills = new SacredCreatureSkillContext(false, false, false, false, false, false, 0,
                source.GetEquipmentReference(SacredSavedCreatureLayout.RightHandEquipmentIndex) != 0, false);
            var context = new WorldCreatureScalarResultContext(resource, 1, 1, false, 0, source.BaseState.Flags,
                0, profiles.Profile(resource), source.Values.Count22, 0, false, skills, 1, Sacred.Core.Runtime.SacredX87Precision.Single)
                { SavedHeroArts = artInputs };
            var values = source.Values.Block08.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(78), WorldCreatureSavedValueImport.LevelOnExperience(source.Values.Experience));
            var result = assets.Calculate(values, template, context, []);
            var expected = native.AsSpan(0x3b0, 238);
            if (!result.Values.AsSpan().SequenceEqual(expected))
            {
                var differing = Enumerable.Range(0, 238).Where(i => result.Values[i] != native[0x3b0 + i]).Select(i => $"{i}:{result.Values[i]:X2}/{native[0x3b0+i]:X2}");
                throw new InvalidDataException("HostHeroValueImport scalar differences: " + string.Join(' ', differing));
            }
            var expectedArts = Convert.FromHexString(row.GetProperty("arts_hex").GetString()!);
            var actualArts = result.CombatArts!.SelectMany(row => row).ToArray();
            if (!actualArts.AsSpan().SequenceEqual(expectedArts))
                throw new InvalidDataException("HostHeroValueImport acquired arts differ: " + Convert.ToHexString(actualArts) + " / " + Convert.ToHexString(expectedArts));
            if (result.Regeneration != BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(0x4e0))) throw new InvalidDataException("HostHeroValueImport physical regeneration differs.");
            checks += result.Values.Length + actualArts.Length + 1;
            var initial = Initial(initialBytes);
            var physical = WorldCreatureSavedPhysicalImport.Restore(initial, source.PhysicalData, Vector3.Zero, 1, 1, WorldStartupEffectProjection.ComparedGold);
            var restored = WorldCreatureSavedValueImport.Restore(physical, source, template, assets, profiles, artInputs, 1);
            var state = restored.RestoredCreature!;
            Check(restored.Component.Values.AsSpan().SequenceEqual(expected), "integrated scalar import");
            Check(restored.Component.Health.AsSpan().SequenceEqual(native.AsSpan(0x4d0, 24)), "HP retained without refill; regeneration updated");
            Check(state.CombatArts.SelectMany(bytes => bytes).SequenceEqual(expectedArts), "integrated acquired arts");
            Check(state.Combos.SelectMany(bytes => bytes).SequenceEqual(Convert.FromHexString(row.GetProperty("combos_hex").GetString()!)), "complete combo list replaces constructor state");
            Check(restored.Component.CombatProfile == native[0x4ce] && restored.Component.ResultValid == native[0x4cf], "profile and result-valid byte");
            Check(restored.RelationClass == Word(native, 0x1f0) && restored.BehaviorFlags == Word(native, 0x1f4) && restored.RuntimeFlags == Word(native, 0x200), "creature class/behavior flags");
            Check(restored.KiState.AsSpan().SequenceEqual(native.AsSpan(0xfc, 84)) && restored.EiState.AsSpan().SequenceEqual(native.AsSpan(0x150, 84)) && restored.ActionState.AsSpan().SequenceEqual(native.AsSpan(0x2ed, 173)), "complete KI/EI/action banks");
            Check(restored.ThinkInterval == Float(native, 0x204) && restored.ThinkElapsed == Float(native, 0x208) && restored.KiMode == Short(native, 0xfc) && restored.EiMode == Short(native, 0x150), "AI scheduler/action selectors");
            Check(restored.Flags2B7 == Short(native, 0x2b7) && restored.EquipmentSelector == Short(native, 0x25f) && restored.RandomTail == Short(native, 0x574), "creature words");
            Check(restored.AnimationSpeedMultiplier == Float(native, 0x2c9) && restored.Word210 == Word(native, 0x210), "saved runtime multipliers");
            Check(restored.ScriptGroup == Word(native, 0x249) && restored.DialogIndex == Word(native, 0x245), "script selectors");
            Check(restored.SpellParameter == Short(native, 0x261) && restored.SpellSelector == native[0x2b6], "spell selectors");
            Check(restored.ScriptInitialPosition == new GoldObjectPosition(unchecked((int)Word(native, 0x2bd)), unchecked((int)Word(native, 0x2c1)), native[0x2c5], Short(native, 0x2b9)), "saved original script pose");
            Check(state.PendingEquipmentReferences.Select((reference, index) => reference == Word(native, 0x1a4 + index * 4)).All(value => value) && restored.EquipmentReferences.All(reference => reference == 0), "file identities retained pending actual saved object restoration");
            Check(ReferenceEquals(initial.Node, restored.Node) && initial.RestoredCreature is null && initial.Component.ResultValid == 0, "same root; no source actor mutation");
            if (checks < 400)
                foreach (var unsupported in new[] { context with { LocalReference = 2 }, context with { LearnedCombatArtCount = 0 }, context with { ActiveEffectCount = 1 }, context with { Mounted = true } })
                {
                    try { assets.Calculate(values, template, unsupported, []); throw new Exception("Unsupported hero context accepted."); }
                    catch (NotSupportedException) { checks++; }
                }
        }
        return checks;
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostHeroValueImport: " + reason); checks++; }
    }

    private static uint Word(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ushort Short(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static float Float(byte[] bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));
    private static WorldCreatureInitialActor Initial(byte[] native)
    {
        var actor = new WorldCreatureInitialActor(new(Word(native, 12), Word(native, 16), Word(native, 20), Short(native, 24),
            unchecked((int)Word(native, 28)), unchecked((int)Word(native, 32)), native[36], Word(native, 40), Word(native, 52), native[56], native[57], Word(native, 60)),
            new(native.AsSpan(0x3b0, 238).ToArray(), native.AsSpan(0x4d0, 24).ToArray(), 6, 0),
            Word(native, 0x1f0), Word(native, 0x1f4), Short(native, 0x25f), Short(native, 0x574), Float(native, 0x208), 0, []);
        new WorldObjectActivationQueue().ActivateInitial(actor.Node, 0);
        return actor;
    }
}
