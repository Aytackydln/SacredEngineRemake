using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

internal static class CreatureLootEntryChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-entry.json")));
        var json = fixture.RootElement;
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var rules = SacredCreatureDeathLootDefinitions.Read(image);
        var resources = SacredCreatureLootDefinitions.Read(image);
        var balance = LootBalanceBinReader.Read(Convert.FromHexString(json.GetProperty("balance_hex").GetString()!));
        var checks = 0;
        foreach (var row in json.GetProperty("eligibility").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var source = new SacredCreatureDeathLootActor(500, row.GetProperty("resource").GetUInt32(), row.GetProperty("relation").GetUInt32(),
                0, unchecked((int)row.GetProperty("owner").GetUInt32()), row.GetProperty("maximum").GetInt32(),
                row.GetProperty("undead").GetBoolean() ? SacredCreatureClass.Undead : SacredCreatureClass.Monster);
            var killer = new SacredCreatureDeathLootActor(row.GetProperty("killer_reference").GetUInt32(), row.GetProperty("killer_resource").GetUInt32(),
                row.GetProperty("killer_relation").GetUInt32(), row.GetProperty("killer_behavior").GetUInt32(), 0, 100, SacredCreatureClass.Hero);
            var hero = new SacredCreatureDeathLootActor(1, 2, 0, 0, 0, 100, SacredCreatureClass.Hero);
            var observer = new SacredCreatureLootObserver(row.GetProperty("ki").GetUInt16(), row.GetProperty("ei").GetUInt16(), row.GetProperty("hp").GetInt32());
            var detached = 0;
            var actual = SacredCreatureDeathLootEligibility.Evaluate(source, 0x100, row.GetProperty("killer_present").GetBoolean() ? killer : null,
                rules, balance with { DeathEquipmentDetachChance = row.GetProperty("chance").GetInt32() }, random,
                () => row.GetProperty("hero").GetBoolean() ? hero : null,
                () => row.GetProperty("hero").GetBoolean() && observer.CanReceiveLoot(row.GetProperty("hostile").GetBoolean()),
                reference => reference == killer.Reference && row.GetProperty("companion").GetBoolean(),
                () => { detached++; for (var k = 0; k < row.GetProperty("detach_draws").GetInt32(); k++) random.Next(); return true; });
            SacredCreatureDeathLootActor? expectedKiller = row.GetProperty("killer").GetString() switch { "hero" => hero, "killer" => killer, _ => null };
            if (actual.Gate.ToString() != row.GetProperty("gate").GetString() || actual.Killer != expectedKiller ||
                actual.PhysicalFlags != row.GetProperty("flags").GetUInt32() || detached != row.GetProperty("detach_count").GetInt32() ||
                random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native death eligibility differs: {row}; actual {actual}, detach {detached}, RNG {random.State}");
            checks += 5;
        }
        foreach (var row in json.GetProperty("entries").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var lookups = 0;
            var actual = SacredCreatureLootEntry.Resolve(row.GetProperty("forced").GetUInt32(), 295, row.GetProperty("destroyed").GetBoolean(),
                row.GetProperty("source_hero").GetBoolean() ? SacredCreatureClass.Hero : SacredCreatureClass.Monster,
                row.GetProperty("hero").GetBoolean() ? row.GetProperty("rank").GetByte() : (byte)0, resources.ProgressionForcedResources,
                random, _ => { lookups++; return row.GetProperty("authored").GetUInt32(); });
            if (actual.Kind.ToString() != row.GetProperty("kind").GetString() || actual.ForcedResource != row.GetProperty("resolved").GetUInt32() ||
                lookups != row.GetProperty("authored_calls").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native drop entry differs: {row}; actual {actual}, RNG {random.State}");
            checks += 4;
        }
        foreach (var row in json.GetProperty("authored").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var table = row.GetProperty("rules").EnumerateArray().Select(r => new SacredAuthoredLootRule(r[0].GetUInt32(), r[1].GetUInt32(), r[2].GetUInt32())).ToArray();
            var actual = SacredAuthoredLootSelection.Select(table, 295, random, () => row.GetProperty("time_seed").GetUInt32(), _ => row.GetProperty("eligible").GetBoolean());
            if (actual != row.GetProperty("resource").GetUInt32() || random.State != row.GetProperty("final_seed").GetUInt32())
                throw new Exception($"Native authored drop/RNG differs: {row}; actual {actual}, RNG {random.State}");
            checks += 2;
        }
        foreach (var row in json.GetProperty("validations").EnumerateArray())
        {
            var state = new SacredCreatureLootCandidateState { RejectedSharedRecords = row.GetProperty("rejected").GetInt32() };
            var modifierCalls = 0;
            var sharedCalls = 0;
            var actual = SacredCreatureLootCandidateValidation.Validate(row.GetProperty("resource").GetUInt32(), row.GetProperty("category").GetUInt32() != 0,
                resources.MaximumAcceptedResource, resources.FallbackResource, state,
                _ => { modifierCalls++; return row.GetProperty("found").GetBoolean() ? row.GetProperty("modifier").GetInt32() : null; },
                _ => { sharedCalls++; return row.GetProperty("shared").GetBoolean() ? row.GetProperty("flags").GetUInt32() : null; });
            if (actual.Disposition.ToString() != row.GetProperty("disposition").GetString() || state.RejectedSharedRecords != row.GetProperty("final_rejected").GetInt32() ||
                modifierCalls != row.GetProperty("modifier_calls").GetInt32() || sharedCalls != row.GetProperty("shared_calls").GetInt32() ||
                actual.Resource != (actual.Disposition == SacredCreatureLootCandidateDisposition.Fallback ? resources.FallbackResource : row.GetProperty("resource").GetUInt32()))
                throw new Exception($"Native source candidate validation differs: {row}; actual {actual}");
            checks += 5;
        }
        return checks;
    }
}
