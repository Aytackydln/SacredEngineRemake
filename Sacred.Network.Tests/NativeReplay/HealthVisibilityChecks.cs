using System.Text.Json;
using Sacred.Core.Runtime;

internal static class HealthVisibilityChecks
{
    public static int Run(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-health-visibility.json")));
        var checks = 0;
        void Check(bool result, string message)
        { if (!result) throw new Exception("Health visibility: " + message); checks++; }
        foreach (var row in doc.RootElement.GetProperty("hover").EnumerateArray())
            Check(SacredCreatureHealthDisplay.Hover(row.GetProperty("timer").GetSingle()) == row.GetProperty("result").GetSingle(), "native hover clock");
        foreach (var row in doc.RootElement.GetProperty("aging").EnumerateArray())
            Check(SacredCreatureHealthDisplay.Advance(row.GetProperty("timer").GetSingle(), row.GetProperty("seconds").GetSingle()) == row.GetProperty("result").GetSingle(), "native timer aging");
        foreach (var row in doc.RootElement.GetProperty("alpha").EnumerateArray())
            Check(SacredCreatureHealthDisplay.Alpha(row.GetProperty("timer").GetSingle()) == row.GetProperty("result").GetUInt32(), "native fade alpha");
        foreach (var row in doc.RootElement.GetProperty("relations").EnumerateArray())
        {
            var first = row.GetProperty("first").GetUInt32(); var second = row.GetProperty("second").GetUInt32();
            var hostile = row.GetProperty("hostile").GetBoolean();
            Check(SacredCreatureRelations.AreDefaultEnemies(first, second, 0, 0) == hostile, "native default class matrix");
        }
        foreach (var row in doc.RootElement.GetProperty("inversion").EnumerateArray())
        {
            var first = row.GetProperty("enemy").GetBoolean() ? 2u : 3u;
            Check(SacredCreatureRelations.AreDefaultEnemies(first, 1, row.GetProperty("first_behavior").GetUInt32(),
                row.GetProperty("second_behavior").GetUInt32()) == row.GetProperty("result").GetBoolean(), "native hostility inversion");
        }
        return checks + CreatureRelationshipChecks.Run(doc.RootElement.GetProperty("full_relations"));
    }
}
