using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class MagicPrisonInitializationChecks
{
    public static int Run(string root)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-magic-prison-snapshots.json")));
        var segments = source.RootElement.GetProperty("definition_code").EnumerateArray().ToArray();
        var image = new byte[segments.Max(row => row.GetProperty("offset").GetInt32() + row.GetProperty("hex").GetString()!.Length / 2)];
        foreach (var segment in segments)
            Convert.FromHexString(segment.GetProperty("hex").GetString()!).CopyTo(image, segment.GetProperty("offset").GetInt32());
        var definitions = SacredMagicPrisonDefinitions.Read(image);
        var checks = 0;
        Check(definitions.Resource == 854 && definitions.DefaultDetailLevel == 2, "constructor resource and configuration default read through original code linkage");
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (row.GetProperty("mode").GetString() != "creation") continue; // Factory alone has not called Start.
            var native = Convert.FromHexString(row.GetProperty("particle_hex").GetString()!);
            var palette = definitions.CreatePalette();
            Check(palette.Length == 256, "original color-table length");
            for (var index = 0; index < palette.Length; index++)
                Check(palette[index] == Word(0x20a8 + index * 4), $"PC=24 packed palette entry {index}: {palette[index]:X8} vs {Word(0x20a8 + index * 4):X8}");
            Check(definitions.RateBits(row.GetProperty("detail").GetByte()) == Word(0x2520), "detail-dependent Start rate");
            foreach (var (offset, value) in definitions.InitialWords)
                Check(value == Word(0x20a0 + offset), "executable parameter word at " + offset);
            var bytes = Convert.FromHexString(row.GetProperty("publications")[0].GetProperty("payload_hex").GetString()!);
            var state = GoldObjectFxState.FromPayload(bytes);
            var particle = GoldParticleSnapshot.FromPayload(state.ParticleState.Span);
            var generated = WorldMagicPrisonEffectSerialization.Create(definitions, state.BaseState,
                particle.Position, row.GetProperty("detail").GetByte()).ToPayload();
            // Actual update/full-render comparisons prove invariance for these
            // six backing spans at all three detail inputs; writer controls read each.
            foreach (var (offset, length) in new[] { (71,12), (115,24), (236,3), (1419,72), (1525,12), (1545,8) })
                bytes.AsSpan(offset, length).Clear();
            Check(generated.AsSpan().SequenceEqual(bytes), "complete original FX/particle stream after proven backing canonicalization");
            uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(native.AsSpan(offset));
        }
        // A changed executable coefficient must affect generated state. This
        // prevents a fixture-shaped recipe from replacing file-derived inputs.
        var original = definitions.CreatePalette();
        var changed = image.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(changed.AsSpan(0x89208C - 0x400000), 1);
        Check(!original.SequenceEqual(SacredMagicPrisonDefinitions.Read(changed).CreatePalette()), "changed executable coefficient changes the generated palette");
        try { definitions.RateBits(3); throw new Exception("Uncompared detail admitted"); }
        catch (NotSupportedException) { checks++; }
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new Exception("MAGICPRISON initialization: " + reason); checks++; }
    }
}
