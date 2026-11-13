using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class CreaturePlacementSelectionChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-placement-selection.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("constructors").EnumerateArray())
        {
            if (row.GetProperty("runtime_flags").GetUInt32() != 0 || row.GetProperty("file_flags_word").GetUInt16() != 65535)
                throw new Exception("Native KEYX file flags were conflated with sector runtime state.");
            checks += 2;
        }
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var flags = row.GetProperty("runtime_flags").GetUInt32();
            var present = row.GetProperty("present").GetBoolean();
            if (WorldScriptCreaturePlacementBranch.UsesPositionedFactory(present ? flags : null) != row.GetProperty("positioned").GetBoolean() ||
                (flags | WorldScriptCreaturePlacementBranch.TileDataInitializedFlag) != row.GetProperty("loaded_flags").GetUInt32())
                throw new Exception("Original CreateNPC placement branch differs.");
            checks += 2;
        }
        return checks;
    }
}
