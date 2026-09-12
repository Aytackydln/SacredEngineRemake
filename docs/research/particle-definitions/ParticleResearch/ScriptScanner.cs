using System.Security.Cryptography;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace ParticleResearch;

internal sealed record ScriptEffect(string ScriptSha256, int Offset, string TypeName,
    SacredScriptCreateObject Creation, string RawHex);
internal sealed record ScriptSummary(string Path, string Sha256, int ByteLength, int CommandCount,
    int CreateObjectCount, int LiteralCreateObjectCount, int EffectCount, Dictionary<string, int> UnsupportedCreates);
internal sealed record ScriptScan(List<ScriptSummary> Scripts, List<ScriptEffect> Effects);

internal static class ScriptScanner
{
    public static ScriptScan Scan(string game, IReadOnlyDictionary<uint, string> types)
    {
        var result = new ScriptScan([], []);
        var unique = new Dictionary<string, ScriptSummary>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(game, "bin"), "*", SearchOption.AllDirectories)
                     .Where(IsScript).Order(StringComparer.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var relative = Path.GetRelativePath(game, path).Replace('\\', '/');
            if (unique.TryGetValue(hash, out var previous))
            {
                result.Scripts.Add(previous with { Path = relative });
                continue;
            }

            int commands = 0, creates = 0, literal = 0, effects = 0;
            var unsupported = new Dictionary<string, int>();
            foreach (var command in SacredCompiledScriptReader.Read(bytes))
            {
                commands++;
                if (command.Opcode != SacredScriptCommandHeaderLayout.CreateObjectOpcode) continue;
                creates++;
                if (!SacredScriptCreateObjectReader.TryRead(command, out var creation, out var diagnostic))
                {
                    unsupported[diagnostic!] = unsupported.GetValueOrDefault(diagnostic!) + 1;
                    continue;
                }
                literal++;
                if (!types.TryGetValue(creation.TypeId, out var name) || !name.StartsWith("TYPE_FX_", StringComparison.Ordinal)) continue;
                effects++;
                result.Effects.Add(new ScriptEffect(hash, command.FileOffset, name, creation,
                    Convert.ToHexStringLower(command.Bytes.Span)));
            }
            var summary = new ScriptSummary(relative, hash, bytes.Length, commands, creates, literal, effects, unsupported);
            unique.Add(hash, summary);
            result.Scripts.Add(summary);
            Console.WriteLine($"Scanned {relative}: {commands:N0} commands, {effects:N0} literal FX creations, {creates - literal:N0} unsupported creations.");
        }
        return result;
    }

    private static bool IsScript(string path) => Path.GetFileName(path).ToLowerInvariant() is
        "funkcode.bin" or "startcode.bin" or "sgf.bin";
}
