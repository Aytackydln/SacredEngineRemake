using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Loot;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

internal static class CreatureLootDecisionSessionChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-loot-event.json")));
        using var factory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-factory.json")));
        var json = fixture.RootElement;
        var image = new byte[json.GetProperty("image_size").GetInt32()];
        foreach (var chunk in json.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var data = EquipmentGenerationFixture.Read(root, factory.RootElement) with
        {
            CategoryRules = ItemCategorySelectionChecks.ReadRules(root),
            CreatureLootResources = SacredCreatureLootDefinitions.Read(image)
        };
        var checks = 0;
        foreach (var row in json.GetProperty("composed_cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var context = new WorldCreatureLootDecisionContext(17, 295, row.GetProperty("category").GetUInt32(),
                row.GetProperty("level").GetUInt16(), row.GetProperty("experience").GetUInt16(), row.GetProperty("killer_reference").GetUInt32(),
                row.GetProperty("hero_level").GetUInt16(), 2, row.GetProperty("find").GetUInt16(), row.GetProperty("seconds").GetUInt32(),
                row.GetProperty("skill").GetUInt16(), row.GetProperty("difficulty").GetUInt32(), 0, row.GetProperty("boosted").GetBoolean(),
                row.GetProperty("count").GetUInt32(), row.GetProperty("base").GetUInt32(), 10, 3);
            var session = new WorldCreatureLootDecisionSession(data);
            session.SelectionState.RuneDropsDisabled = row.GetProperty("disabled").GetBoolean();
            var decisions = row.GetProperty("decisions");
            var index = 0;
            var completed = session.Run(context, random, _ => true, actual =>
            {
                if (index >= decisions.GetArrayLength()) throw new Exception($"Extra host source decision: {row}");
                var expected = decisions[index++];
                var choice = expected.GetProperty("choice");
                if (actual.Call != expected.GetProperty("call").GetUInt32() || actual.Attempt.Attempt != expected.GetProperty("attempt").GetInt32() ||
                    actual.Attempt.RemainingBudget != expected.GetProperty("remaining").GetInt32() || actual.Attempt.Bonus != expected.GetProperty("bonus").GetUInt32() ||
                    actual.Choice.Kind.ToString() != choice.GetProperty("kind").GetString() || actual.Choice.Selector != choice.GetProperty("selector").GetUInt32() ||
                    actual.RandomState != expected.GetProperty("seed").GetUInt32())
                    throw new Exception($"Native composed source decision differs: {row}; actual {actual}");
                if (actual.Choice.Kind == SacredCreatureLootChoiceKind.Equipment)
                {
                    var args = choice.GetProperty("arguments");
                    if ((byte)actual.Choice.Category != args[0].GetUInt32() || actual.Choice.HeroPreference != args[4].GetUInt32() ||
                        actual.Choice.PreferenceFlags != args[5].GetUInt32() || actual.Choice.Quality != args[7].GetUInt32())
                        throw new Exception($"Native composed equipment request differs: {row}; actual {actual}");
                    checks += 4;
                }
                else if (actual.Choice.Resource != choice.GetProperty("resource").GetUInt32())
                    throw new Exception($"Native composed resource differs: {row}; actual {actual}");
                checks += 8;
                if (row.GetProperty("unresolved").GetBoolean()) return SacredCreatureLootAttemptOutcome.Unresolved;
                for (var k = 0; k < row.GetProperty("body_draws").GetInt32(); k++) random.Next();
                return actual.Attempt.Attempt <= row.GetProperty("retries").GetInt32()
                    ? SacredCreatureLootAttemptOutcome.RetrySelection : SacredCreatureLootAttemptOutcome.CompletedPass;
            });
            if (completed != (!row.GetProperty("unresolved").GetBoolean() || decisions.GetArrayLength() == 0) ||
                index != decisions.GetArrayLength() || random.State != row.GetProperty("final_seed").GetUInt32() ||
                session.SelectionState.LastRuneSource != row.GetProperty("final_rune").GetUInt32() ||
                session.SelectionState.LastJewelrySource != row.GetProperty("final_jewelry").GetUInt32())
                throw new Exception($"Native source session completion differs: {row}; RNG {random.State}");
            checks += 5;
        }
        return checks;
    }
}
