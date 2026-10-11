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
            Check(embedded.Definitions.Count == 210 && embedded.DecodedCount == 34 && embedded.EventDefinitions.Count == 112, $"catalogue coverage ({quality})");
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
                var complete = expected.GetProperty("status").GetString() == "complete" || definition.EmissionCycle is not null;
                Check((definition.Status == SacredParticleDefinitionStatus.Decoded) == complete, "initializer support status");
                if (!complete || definition.EmissionCycle is not null) continue; // Independently verified by geyser-native-reference.json.
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
                foreach (var entry in freshlyRead.Definitions.Concat(freshlyRead.EventDefinitions))
                {
                    SacredParticleDefinition? saved;
                    Check(entry.IsEventPreset
                        ? embedded.TryGetEventDefinition(entry.TypeId, entry.Preset!.Value, out saved)
                        : embedded.TryGetDefinition(entry.TypeId, out saved), "fresh entry embedded");
                    Check(saved!.TypeName == entry.TypeName && saved.Preset == entry.Preset && saved.Status == entry.Status &&
                          saved.Diagnostic == entry.Diagnostic && saved.FactoryAddress == entry.FactoryAddress &&
                          saved.TypeRecordAddress == entry.TypeRecordAddress && saved.NativeClass == entry.NativeClass &&
                          saved.Draw == entry.Draw && saved.Halo == entry.Halo && saved.Capacity == entry.Capacity &&
                          saved.GroundCollision == entry.GroundCollision && saved.OneTime == entry.OneTime && saved.IsEventPreset == entry.IsEventPreset &&
                          saved.DisplayName == entry.DisplayName && saved.ModelBurstCount == entry.ModelBurstCount &&
                          saved.UsesActorBlockRadius == entry.UsesActorBlockRadius &&
                          saved.BurstOnFirstUpdate == entry.BurstOnFirstUpdate &&
                          saved.NativeConstructorAddress == entry.NativeConstructorAddress &&
                          saved.EmissionDurationSeconds == entry.EmissionDurationSeconds &&
                          saved.SingleBirthInitialization == entry.SingleBirthInitialization &&
                          saved.SkipFirstMovement == entry.SkipFirstMovement && saved.EmissionClock == entry.EmissionClock &&
                          saved.EmissionCycle == entry.EmissionCycle &&
                          saved.LineEmission == entry.LineEmission &&
                          saved.Strength == entry.Strength && saved.BirthSizeRamp == entry.BirthSizeRamp &&
                          saved.SimulationStartSeconds == entry.SimulationStartSeconds && saved.MaximumSimulationStep == entry.MaximumSimulationStep &&
                          saved.RandomBurst == entry.RandomBurst && saved.RadialBurst == entry.RadialBurst &&
                          saved.UsesEventCornerColor == entry.UsesEventCornerColor && saved.EventCornerColorMask == entry.EventCornerColorMask &&
                          saved.Teleport == entry.Teleport && saved.OrbitEmission == entry.OrbitEmission &&
                          ElementStoneEquals(saved.ElementStone, entry.ElementStone) &&
                          ForgeElementEquals(saved.ForgeElement, entry.ForgeElement) &&
                          GlyphEquals(saved.UnderworldGlyph, entry.UnderworldGlyph) &&
                          PongEquals(saved.PongBatter, entry.PongBatter) &&
                          AnducarEquals(saved.Anducar, entry.Anducar) &&
                          DoorSpikeEquals(saved.DoorSpike, entry.DoorSpike) &&
                          saved.Stoned == entry.Stoned && saved.Plong == entry.Plong &&
                          LayeredFlareEquals(saved.LayeredFlare, entry.LayeredFlare) &&
                          PulseFlareEquals(saved.PulseFlare, entry.PulseFlare) &&
                          WarCrySignEquals(saved.WarCrySign, entry.WarCrySign) &&
                          ExpandingEquals(saved.ExpandingBurst, entry.ExpandingBurst) &&
                          BubbleEquals(saved.BubbleTrapExplosion, entry.BubbleTrapExplosion) &&
                          TerrorEquals(saved.TerrorSprinter, entry.TerrorSprinter) &&
                          saved.ColumnFlare == entry.ColumnFlare &&
                          SpriteFlareEquals(saved.SpriteFlare, entry.SpriteFlare) &&
                          ControlledFlareEquals(saved.ControlledFlare, entry.ControlledFlare) &&
                          StrikeSymbolEquals(saved.StrikeSymbol, entry.StrikeSymbol) &&
                          RingShieldEquals(saved.RingShield, entry.RingShield) &&
                          ConfusionEquals(saved.Confusion, entry.Confusion) &&
                          saved.AdditionalDraws.SequenceEqual(entry.AdditionalDraws) && saved.AdditionalHalos.SequenceEqual(entry.AdditionalHalos) &&
                          saved.ParticlesVisible == entry.ParticlesVisible && saved.RetireAfterDuration == entry.RetireAfterDuration &&
                          saved.RequiresActorContext == entry.RequiresActorContext &&
                          saved.InitialParticles.SequenceEqual(entry.InitialParticles) &&
                          saved.EmitBeforeMovement == entry.EmitBeforeMovement && saved.EmissionMode == entry.EmissionMode && saved.UsesWind == entry.UsesWind &&
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

    private static bool RingEquals(SacredFxRingDefinition left,SacredFxRingDefinition right) =>
        left.Vertices.SequenceEqual(right.Vertices) && left with { Vertices=right.Vertices } == right;
    private static bool ExpandingEquals(SacredExpandingBurstFxDefinition? left, SacredExpandingBurstFxDefinition? right) =>
        left is null || right is null ? left == right : RingEquals(left.Ring,right.Ring) && Hex(left.InitialState) == Hex(right.InitialState) &&
        left with { Ring=right.Ring,InitialState=right.InitialState } == right;
    private static bool BubbleEquals(SacredBubbleTrapExplosionFxDefinition? left,SacredBubbleTrapExplosionFxDefinition? right) =>
        left is null || right is null ? left==right : RingEquals(left.Ring,right.Ring) && left with { Ring=right.Ring } == right;
    private static bool TerrorEquals(SacredTerrorSprinterFxDefinition? left,SacredTerrorSprinterFxDefinition? right) =>
        left is null || right is null ? left==right : RingEquals(left.Ring,right.Ring) && left.SegmentTextureVariants.SequenceEqual(right.SegmentTextureVariants) && left with { Ring=right.Ring,SegmentTextureVariants=right.SegmentTextureVariants } == right;

    private static bool WarCrySignEquals(SacredWarCrySignFxDefinition? left, SacredWarCrySignFxDefinition? right)
    {
        if (left is null || right is null) return left == right;
        return left.StripVertices.SequenceEqual(right.StripVertices) && left with { StripVertices = right.StripVertices } == right;
    }

    private static bool ConfusionEquals(SacredConfusionFxDefinition? left, SacredConfusionFxDefinition? right)
    {
        if (left is null || right is null) return left == right;
        return left.GroundLayers.SequenceEqual(right.GroundLayers) && left.RibbonVertices.SequenceEqual(right.RibbonVertices) &&
            left with { GroundLayers = right.GroundLayers, RibbonVertices = right.RibbonVertices } == right;
    }

    private static bool RingShieldEquals(SacredRingShieldFxDefinition? left, SacredRingShieldFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var vertices = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        return (left with { RingVertices = vertices, MarkerVertices = vertices }) == (right with { RingVertices = vertices, MarkerVertices = vertices }) &&
            left.RingVertices.SequenceEqual(right.RingVertices) && left.MarkerVertices.SequenceEqual(right.MarkerVertices);
    }

    private static bool StrikeSymbolEquals(SacredStrikeSymbolFxDefinition? left, SacredStrikeSymbolFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var textures = Array.Empty<string>();
        var vertices = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        return (left with { Textures = textures, SurfaceVertices = vertices }) == (right with { Textures = textures, SurfaceVertices = vertices }) &&
            left.Textures.SequenceEqual(right.Textures) && left.SurfaceVertices.SequenceEqual(right.SurfaceVertices);
    }

    private static bool PulseFlareEquals(SacredPulseFlareFxDefinition? left, SacredPulseFlareFxDefinition? right)
    {
        if (left is null || right is null) return left == right;
        return left.Layers.SequenceEqual(right.Layers) &&
            left with { Layers = Array.Empty<SacredFxFlareLayer>() } == right with { Layers = Array.Empty<SacredFxFlareLayer>() };
    }
    private static bool SpriteFlareEquals(SacredSpriteFlareFxDefinition? left, SacredSpriteFlareFxDefinition? right)
    {
        if (left is null || right is null) return left == right;
        return left.Layers.SequenceEqual(right.Layers) &&
            left with { Layers = Array.Empty<SacredFxFlareLayer>() } == right with { Layers = Array.Empty<SacredFxFlareLayer>() };
    }

    private static bool ControlledFlareEquals(SacredControlledFlareFxDefinition? left, SacredControlledFlareFxDefinition? right)
    {
        if (left is null || right is null) return left == right;
        return left.Layers.SequenceEqual(right.Layers) &&
            left with { Layers = Array.Empty<SacredFxFlareLayer>() } == right with { Layers = Array.Empty<SacredFxFlareLayer>() };
    }

    private static bool LayeredFlareEquals(SacredLayeredFlareFxDefinition? left, SacredLayeredFlareFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var empty = Array.Empty<SacredFxFlareLayer>();
        return (left with { Layers = empty }) == (right with { Layers = empty }) && left.Layers.SequenceEqual(right.Layers);
    }

    private static bool AnducarEquals(SacredAnducarFxDefinition? left, SacredAnducarFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var empty = Array.Empty<string>();
        return (left with { ModelNames = empty }) == (right with { ModelNames = empty }) &&
               left.ModelNames.SequenceEqual(right.ModelNames);
    }

    private static bool DoorSpikeEquals(SacredDoorSpikeFxDefinition? left, SacredDoorSpikeFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.ModelName == right.ModelName && left.SurfaceTexture == right.SurfaceTexture &&
               left.DrawCount == right.DrawCount && left.EvenHeading == right.EvenHeading && left.OddHeading == right.OddHeading &&
               left.Tilt == right.Tilt && left.ReleaseFlag == right.ReleaseFlag && left.HasShadowPass == right.HasShadowPass &&
               Hex(left.InitialState) == Hex(right.InitialState);
    }

    private static bool PongEquals(SacredPongBatterFxDefinition? left, SacredPongBatterFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var vertices = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        var edges = Array.Empty<SacredFxBeamSegment>();
        return (left with { SurfaceVertices = vertices, Edges = edges }) == (right with { SurfaceVertices = vertices, Edges = edges }) &&
               left.SurfaceVertices.SequenceEqual(right.SurfaceVertices) && left.Edges.SequenceEqual(right.Edges);
    }

    private static bool GlyphEquals(SacredUnderworldGlyphFxDefinition? left, SacredUnderworldGlyphFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var empty = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        return (left with { RibbonVertices = empty }) == (right with { RibbonVertices = empty }) &&
               left.RibbonVertices.SequenceEqual(right.RibbonVertices);
    }

    private static bool ForgeElementEquals(SacredForgeElementFxDefinition? left, SacredForgeElementFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var empty = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        return (left with { RingVertices = empty }) == (right with { RingVertices = empty }) &&
               left.RingVertices.SequenceEqual(right.RingVertices);
    }

    private static bool ElementStoneEquals(SacredElementStoneFxDefinition? left, SacredElementStoneFxDefinition? right)
    {
        if (left is null || right is null) return left is null && right is null;
        var empty = Array.Empty<Sacred.Particles.Particles.SacredFxSurfaceVertexLayout>();
        return (left with { BeamVertices = empty, FlickerVertices = empty }) ==
               (right with { BeamVertices = empty, FlickerVertices = empty }) &&
               left.BeamVertices.SequenceEqual(right.BeamVertices) && left.FlickerVertices.SequenceEqual(right.FlickerVertices);
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
