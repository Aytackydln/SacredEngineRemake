using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Native authored radial bursts and actor-vertex bursts.
/// Mesh-dependent definitions require a live posed model at playback.</summary>
internal static class NativeParticleEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.FactoryAddress == 0x5A0ED9)
        {
            var family = new NativeParticleFamily("cParticleSystem_puzzleSolved", entry.FactoryAddress,
                0x76C930, 0x76C9B7, 0x76CD50, 0x20A0 + SacredPuzzleSolvedParticleStateLayout.SerializedSize + 8, 0x20BC, 0x20DC, 1, 0x76CB42)
                { InitializerEnd = 0x76CDFE, InitialFlags = 0, ColorOffset = 0x20AC };
            var bindings = NativeTextureReader.ReadBindings(image, code, family);
            var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
            yield return entry with
            {
                NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
                Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
                ModelBurstCount = checked((int)code.At(0x76CE2B).GetImmediate(0)),
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
                Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single(b => b.NativeHandleOffset == 0x2140)),
                ParameterSets = NativePresetReader.ReadParameters(memory, family)
            };
            yield break;
        }
        if (entry.FactoryAddress != 0x5A0F0F) yield break;
        foreach (var preset in new[] { 0, 1, 2, 3, 4, 5 })
        {
            var family = new NativeParticleFamily("cParticleSystem_puzzleSolved_v", entry.FactoryAddress,
                0x76CE60, 0x76CF11, 0x76D260, 0x253C, 0x24A8, 0x24C8, 1,
                preset >= 4 ? 0x76D052u : preset == 1 ? 0x76CFC4u : 0x76D00Eu)
            {
                SampleDefaultEnvironment = preset >= 4,
                InitializerEnd = preset switch { 0 => 0x76D355u, 1 => 0x76D42Cu, 2 => 0x76D545u, 4 => 0x76D862u, 5 => 0x76D9F1u, _ => null }
            };
            var bindings = NativeTextureReader.ReadBindings(image, code, family);
            var textureBranch = code.At(image.UInt32(0x76D08C + (uint)preset * 4));
            var selectedBinding = bindings.Single(b => b.NativeHandleOffset == textureBranch.MemoryDisplacement64);
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
            var initial = MemoryMarshal.Cast<byte, SacredNativeParticleLayout>(
                memory.ObjectBytes(0x8000, capacity * 0x40)).ToArray()
                .Where(p => p.Energy > 0).ToArray();
            if (preset is 2 or 3 && initial.Length == 0) throw new InvalidDataException("Radial event initializer wrote no live particles.");
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                Capacity = capacity,
                ModelBurstCount = preset is 2 or 3 ? 0 : checked((int)code.At(
                    preset == 0 ? 0x76D382u : preset == 1 ? 0x76D443u : 0x76DA1Au).GetImmediate(0)),
                InitialParticles = Array.AsReadOnly(initial),
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings,
                Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings, selectedBinding),
                ParameterSets = NativePresetReader.ReadParameters(memory, family)
            };
        }
    }
}
