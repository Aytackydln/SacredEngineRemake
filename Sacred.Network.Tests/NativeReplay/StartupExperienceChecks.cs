using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

internal static class StartupExperienceChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-startup-experience.json")));
        var argument = fixture.RootElement.GetProperty("first_argument").GetInt32();
        foreach (var expected in fixture.RootElement.GetProperty("values").EnumerateArray())
        {
            if (argument > 100)
            {
                try { SacredStartupExperience.AtLevel((ushort)argument); throw new Exception("Uncompared legacy power accepted."); }
                catch (NotSupportedException) { argument++; continue; }
            }
            var actual = SacredStartupExperience.AtLevel((ushort)argument);
            if (actual != expected.GetUInt32()) throw new Exception($"Original PC=24 experience argument {argument}: {actual} != {expected}.");
            argument++;
        }
        return argument - 1;
    }
}
