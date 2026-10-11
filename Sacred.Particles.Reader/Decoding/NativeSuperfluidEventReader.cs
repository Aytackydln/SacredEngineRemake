using System.Numerics;
using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeSuperfluidEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x773880) yield break;
        var family = new NativeParticleFamily("Superfluid", entry.FactoryAddress, 0x773880, 0x773A86,
            0x773EC0, 0x2154, 0, 0, 0, 0) { ConstructorRegister = Register.EBX };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.EBX, memory.BaseAddress);
        machine.Run(0x7738B8, 0x7739C5, token);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x20B4, 128)).ToArray();
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Value(uint address) => (uint)code.At(address).GetImmediate(1);
        var selector = (uint)code.At(0x773CAF).GetImmediate(0);
        var definition = new SacredStrikeSymbolFxDefinition(Array.AsReadOnly(Enumerable.Range(0, 8)
            .Select(i => bindings.Single(b => b.NativeHandleOffset == 0x2134 + i * 4).TextureName).ToArray()),
            Array.AsReadOnly(vertices), NativeTextureReader.ReadFlareTexture(code, selector), null)
        {
            UsesElapsedRate = true, InitialElapsedRate = 1,
            Lifetime = Operand(0x773AC9), FrameInterval = Operand(0x773AFB),
            FlareHalfSize = BitConverter.UInt32BitsToSingle((uint)code.At(0x773CB1).GetImmediate(0)),
            FlareOffset = new Vector3(0, 0, BitConverter.UInt32BitsToSingle(Value(0x773CDC))),
            FlareColor = (uint)code.At(0x773CAD).GetImmediate(0),
            FlareSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector),
            StartNotification = Value(0x773EFD), LightStrength = checked((int)Value(0x773F04)),
            FinishNotification = Value(0x773AE0), FinishArgument = Value(0x773AD6)
        };
        yield return entry with { NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, StrikeSymbol = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = "Eight-state fluid strike and glow; explicit timer rate 1" };
    }
}
