using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Particles;
using Sacred.Particles.Reader;

namespace ParticleReaderChecks;

internal static class CatalogueChecks
{
    public static void Run(string referencePath, string? executable)
    {
        var checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new Exception($"FAILED: {description}");
            checks++;
        }
        using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
        foreach (var reference in document.RootElement.GetProperty("qualities").EnumerateArray())
        {
            var quality = (SacredParticleQuality)reference.GetProperty("quality").GetByte();
            var embedded = SacredParticleCatalogue.LoadEmbedded(quality);
            Check(ReferenceEquals(embedded, SacredParticleCatalogue.LoadEmbedded(quality)), "embedded catalogue is cached");
            Check(embedded.Definitions.Count == 210 && embedded.DecodedCount == 14, $"catalogue coverage ({quality})");
            Check(embedded.NativeCodeSha256 == document.RootElement.GetProperty("text_sha256").GetString(), "reference code identity");
            Check(!embedded.TryGetDefinition(uint.MaxValue, out _), "unknown ID absent");
            Check(embedded.Definitions.Where(e => e.Status != SacredParticleDefinitionStatus.Decoded).All(e => e.ParameterSets.Count == 0),
                "unsupported definitions do not invent parameters");
            var freshlyRead = executable == null ? null : SacredParticleCatalogueReader.Load(executable, new() { Quality = quality, Log = Console.WriteLine });
            foreach (var expected in reference.GetProperty("entries").EnumerateArray())
            {
                var id = expected.GetProperty("type_id").GetUInt32();
                Check(embedded.TryGetDefinition(id, out var definition), "native type present");
                Check(definition!.TypeName == expected.GetProperty("type_name").GetString(), "stored type ID/name mapping");
                Check(definition.Preset == expected.GetProperty("preset").GetInt32(), "native creation dispatch");
                Check(definition.Draw?.TextureName == expected.GetProperty("texture").GetString(), "native draw texture binding");
                Check(definition.Draw?.RawFlags == expected.GetProperty("raw_renderer_flags").GetUInt32(), "native draw flags");
                var complete = expected.GetProperty("status").GetString() == "complete";
                Check((definition.Status == SacredParticleDefinitionStatus.Decoded) == complete, "initializer support status");
                if (!complete) continue;
                var slots = expected.GetProperty("slots").EnumerateArray().ToArray();
                Check(slots.Length == definition.ParameterSets.Count, "written parameter slot count");
                foreach (var slot in slots)
                {
                    var set = definition.ParameterSets.Single(s => s.Index == slot.GetProperty("index").GetInt32());
                    Check(Hex(set.Emission) == slot.GetProperty("emission").GetString(), $"native emission bytes ({quality}, {id}, {set.Index})");
                    Check(Hex(set.Motion) == slot.GetProperty("motion").GetString(), $"native motion bytes ({quality}, {id}, {set.Index})");
                    Check(Convert.ToHexStringLower(MemoryMarshal.AsBytes(set.Colors.ToArray().AsSpan())) ==
                          slot.GetProperty("colors").GetString(), $"native RGBA table ({quality}, {id}, {set.Index})");
                }
            }
            if (freshlyRead != null)
            {
                Check(embedded.Projection == freshlyRead.Projection, "native projection matches embedded");
                Check(Math.Abs(embedded.Projection.DefaultWind - 0.2f) < 0.000001f &&
                      Math.Abs(embedded.Projection.HorizontalScale - 1024f / 534) < 0.000001f &&
                      Math.Abs(embedded.Projection.VerticalScale - 768f / 400) < 0.000001f,
                    "reference projection and default wind operands");
                foreach (var entry in freshlyRead.Definitions)
                {
                    Check(embedded.TryGetDefinition(entry.TypeId, out var saved), "fresh entry embedded");
                    Check(saved!.TypeName == entry.TypeName && saved.Preset == entry.Preset && saved.Status == entry.Status &&
                          saved.Diagnostic == entry.Diagnostic && saved.FactoryAddress == entry.FactoryAddress &&
                          saved.TypeRecordAddress == entry.TypeRecordAddress && saved.NativeClass == entry.NativeClass &&
                          saved.Draw == entry.Draw && saved.EmissionMode == entry.EmissionMode && saved.UsesWind == entry.UsesWind &&
                          saved.TextureBindings.SequenceEqual(entry.TextureBindings), "fresh metadata matches embedded");
                    Check(saved.ParameterSets.Count == entry.ParameterSets.Count, "fresh slot count matches embedded");
                    foreach (var set in entry.ParameterSets)
                    {
                        var savedSet = saved.ParameterSets.Single(s => s.Index == set.Index);
                        Check(Hex(savedSet.Emission) == Hex(set.Emission) && Hex(savedSet.Motion) == Hex(set.Motion), "fresh bytes match embedded");
                        Check(savedSet.Colors.SequenceEqual(set.Colors), "fresh colors match embedded");
                    }
                }
            }
        }
        var invalidQuality = false;
        try { SacredParticleCatalogue.LoadEmbedded((SacredParticleQuality)255); }
        catch (ArgumentOutOfRangeException) { invalidQuality = true; }
        Check(invalidQuality, "invalid embedded quality rejected");
        if (executable != null)
        {
            var cancelled = false;
            try { SacredParticleCatalogueReader.Load(executable, cancellationToken: new CancellationToken(true)); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "preprocessing cancellation");
            VerifyMalformedExecutables(executable, Check);
            VerifySampleCommands(executable, Path.GetDirectoryName(referencePath)!, Check);
        }
        Console.WriteLine($"Passed {checks} checks, including 81 native parameter-set comparisons across all three quality settings.");
    }

    private static void VerifySampleCommands(string executable, string reportDirectory, Action<bool, string> check)
    {
        using var matches = JsonDocument.Parse(File.ReadAllText(Path.Combine(reportDirectory, "sample-matches.json")));
        var game = Path.GetDirectoryName(executable)!;
        var scriptPath = matches.RootElement.GetProperty("script").GetProperty("path").GetString()!;
        var commands = SacredCompiledScriptReader.Read(File.ReadAllBytes(Path.Combine(game, scriptPath)))
            .ToDictionary(c => c.FileOffset);
        var catalogue = SacredParticleCatalogue.LoadEmbedded();
        foreach (var sample in matches.RootElement.GetProperty("matches").EnumerateArray())
        {
            var effect = sample.GetProperty("candidates")[0].GetProperty("effect");
            var command = commands[effect.GetProperty("offset").GetInt32()];
            check(SacredScriptCreateObjectReader.TryRead(command, out var creation, out _), "sample command decoded");
            check(catalogue.TryGetDefinition(creation!.TypeId, out var definition) && definition.Status == SacredParticleDefinitionStatus.Decoded,
                "sample script ID resolves to an embedded decoded preset");
        }
    }

    private static void VerifyMalformedExecutables(string executable, Action<bool, string> check)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"SacredParticleReader-{Guid.NewGuid():N}.exe");
        var bytes = File.ReadAllBytes(executable);
        try
        {
            foreach (var kind in new[] { "truncated PE", "changed native code", "invalid section range", "unterminated type name" })
            {
                var altered = (byte[])bytes.Clone();
                switch (kind)
                {
                    case "truncated PE": Array.Resize(ref altered, 64); break;
                    case "changed native code": altered[0x1000] ^= 1; break;
                    case "invalid section range":
                        var pe = BinaryPrimitives.ReadInt32LittleEndian(altered.AsSpan(0x3C));
                        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(altered.AsSpan(pe + 20));
                        BinaryPrimitives.WriteInt32LittleEndian(altered.AsSpan(pe + 24 + optionalSize + 20), bytes.Length + 1);
                        break;
                    case "unterminated type name": altered.AsSpan(0x4EC32C, 64).Fill(65); break;
                }
                File.WriteAllBytes(temporary, altered);
                var rejected = false;
                try { SacredParticleCatalogueReader.Load(temporary); }
                catch (Exception exception) when (exception is InvalidDataException or BadImageFormatException or NotSupportedException) { rejected = true; }
                check(rejected, kind + " rejected");
            }
        }
        finally { File.Delete(temporary); }
    }

    private static string Hex<T>(T value) where T : unmanaged =>
        Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));
}
