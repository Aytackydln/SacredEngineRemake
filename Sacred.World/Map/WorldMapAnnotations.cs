using System.Numerics;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;

namespace Sacred.World.Map;

/// <summary>Authored map labels from the selected startup script and service markers from merc.bin.</summary>
public sealed record WorldMapAnnotations(
    IReadOnlyList<WorldMapLabel> Labels,
    IReadOnlyList<SacredRegionNpcIconLayout> Npcs)
{
    public static WorldMapAnnotations Empty { get; } = new([], []);

    public static WorldMapAnnotations Load(string gameDirectory, WorldCampaignScripts scripts, GameResStore resources)
    {
        var labels = new List<WorldMapLabel>();
        var depth = 0;
        foreach (var command in SacredCompiledScriptReader.Read(scripts.StartupCode))
        {
            if (command.Opcode == SacredScriptPortalOpcodes.If) { depth++; continue; }
            if (command.Opcode == SacredScriptPortalOpcodes.EndBlock) { depth = Math.Max(0, depth - 1); continue; }
            if (depth != 0 || !SacredMapAnnotationReader.TryReadLabel(command, out var label)) continue;
            var key = label.Resource;
            var text = key.StartsWith("res:", StringComparison.OrdinalIgnoreCase)
                ? ResolveResource(key[4..], resources) : key;
            if (!string.IsNullOrWhiteSpace(text))
                // Gold's font states 4/5 receive signed-byte curvature and angle, not pixel offsets.
                labels.Add(new(new(label.TileX, label.TileY),
                    unchecked((sbyte)label.Curvature), unchecked((sbyte)label.AngleDegrees), text));
        }
        var npcs = SacredMapAnnotationReader.ReadNpcs(File.ReadAllBytes(Path.Combine(gameDirectory, "bin", "merc.bin")));
        Console.WriteLine($"World map annotations loaded: {labels.Count} labels, {npcs.Count} service markers ({scripts.Files.Name}).");
        return new(labels.Distinct().ToArray(), npcs);
    }

    private static string ResolveResource(string key, GameResStore resources) =>
        uint.TryParse(key, out var id) ? resources.Strings.GetValueOrDefault(id, string.Empty)
            : resources.GetString(key, string.Empty);
}

public readonly record struct WorldMapLabel(Vector2 WorldPosition, sbyte Curvature, sbyte AngleDegrees, string Text);
