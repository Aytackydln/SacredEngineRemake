using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.CombatArts;
using Sacred.GameplayHost.World;
using Sacred.Particles;

internal static class VampireTransformationChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); checks++; }
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-vampire-transformation-fx.json")));
        foreach (var sample in evidence.RootElement.GetProperty("samples").EnumerateArray())
        {
            var code = sample.GetProperty("art").GetUInt16();
            var catalogue = SacredParticleCatalogue.LoadEmbedded((SacredParticleQuality)sample.GetProperty("quality").GetInt32());
            Check(catalogue.TryGetEventDefinition(863, 0, out var particle) && particle.VampireTransformation is not null, "Transformation family decoded");
            foreach (var set in sample.GetProperty("sets").EnumerateArray())
            {
                var parameters = particle!.ParameterSets.Single(p => p.Index == set.GetProperty("index").GetInt32());
                Check(MemoryMarshal.AsBytes(new[] { parameters.Emission }.AsSpan()).SequenceEqual(Convert.FromHexString(set.GetProperty("emission").GetString()!)), "Original transformation emission");
                Check(MemoryMarshal.AsBytes(new[] { parameters.Motion }.AsSpan()).SequenceEqual(Convert.FromHexString(set.GetProperty("motion").GetString()!)), "Original transformation motion");
                Check(Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(parameters.Colors.ToArray().AsSpan()))) == set.GetProperty("paletteSha256").GetString(), "Original transformation palette");
            }
            var visuals = SacredCombatVisualCatalogue.Find(SacredCombatArtKind.SpecialMove, code).ToArray();
            var birth = sample.GetProperty("animatedBirth");
            Check(birth.GetProperty("variant").GetInt32() == 2 && birth.GetProperty("position")[2].GetSingle() > 150,
                "Native timed secondary particles use the animated shared source");
            var events = sample.GetProperty("events").EnumerateArray().ToArray();
            Check(visuals.Length == events.Length && visuals.Length == 4, "Native four emitter factory");
            for (var i = 0; i < visuals.Length; i++)
            {
                var bytes = Convert.FromHexString(events[i].GetProperty("event").GetString()!);
                Check(visuals[i].Resource == events[i].GetProperty("resource").GetUInt32(), "Original transformation resource");
                Check(visuals[i].FxSubtype == BitConverter.ToUInt32(bytes, 4), "Original transformation subtype");
                Check(visuals[i].Direction == MemoryMarshal.Read<Vector3>(bytes.AsSpan(0x2C)), "Original transformation source headings");
                Check(visuals[i].ReleaseProgress == 0, "Factory follows animation starter in the same native update");
            }
            Check(GameplayCombatArtMotion.SelectSpecialMove(code, 6, 0) is { Motion: 144, Speed: .7f }, "Both transformation motions supported");
        }
        return checks;
    }
}
