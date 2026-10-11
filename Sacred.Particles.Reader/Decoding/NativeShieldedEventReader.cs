using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeShieldedEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x770C70) yield break;
        var family = new NativeParticleFamily("Shielded", entry.FactoryAddress, 0x770C70, 0x770E7B,
            0x7712E0, 0x2158, 0, 0, 0, 0) { ConstructorRegister = Register.EBX };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.EBX, memory.BaseAddress);
        machine.Run(0x770CA8, 0x770DB5, token);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x20B8, 128)).ToArray();
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Value(uint address) => (uint)code.At(address).GetImmediate(1);
        var selector = (uint)code.At(0x7710D0).GetImmediate(0);
        var textures = Enumerable.Range(0, 8).Select(i => bindings.Single(b => b.NativeHandleOffset == 0x2138 + i * 4).TextureName).ToArray();
        for (var variant = 0; variant < 2; variant++)
        {
            var color = Value(variant == 0 ? 0x771307u : 0x771313u);
            var definition = new SacredStrikeSymbolFxDefinition(Array.AsReadOnly(textures), Array.AsReadOnly(vertices),
                NativeTextureReader.ReadFlareTexture(code, selector), null)
            {
                UsesElapsedRate = true, InitialElapsedRate = 1, UsesSavedColor = true, InitialColor = color,
                Lifetime = Operand(0x770EB9), FrameInterval = Operand(0x770EEB),
                FlareHalfSize = BitConverter.UInt32BitsToSingle((uint)code.At(0x7710D2).GetImmediate(0)),
                FlareOffset = new(0, 0, BitConverter.UInt32BitsToSingle(Value(0x7710FD))), FlareColor = color | 0xFF000000,
                FlareSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector),
                StartNotification = Value(0x771332), LightStrength = checked((int)Value(0x77133B)),
                FinishNotification = Value(0x770ED7), FinishArgument = Value(0x770ECD)
            };
            yield return entry with { NativeClass = family.Name, Preset = variant, IsEventPreset = true, OneTime = true,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, StrikeSymbol = definition, TextureBindings = bindings,
                Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
                DisplayName = variant == 0 ? "White strike and glow; timer rate 1" : "Pink strike and glow; timer rate 1" };
        }
    }
}
