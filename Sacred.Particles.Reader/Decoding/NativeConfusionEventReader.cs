using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeConfusionEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x778E80) yield break;
        var family = new NativeParticleFamily("Confusion", entry.FactoryAddress, 0x778E80, 0x778F27,
            0x7792C0, 0x20F0, 0, 0, 0, 0);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var obj = memory.BaseAddress; var stack = obj + 0x1F000;
        var machine = new X86Machine(code, memory) { RandomValue = 0 };
        // The base constructor authors UVs and unused FVF fields for both 50-pair strip buffers.
        machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(Register.EBP, obj);
        machine.Registers.Set(Register.EBX, 0);
        machine.Run(0x7617AF, 0x7619B3, token);
        var pairCount = checked((int)code.At(0x7619A4).GetImmediate(1));
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x140, pairCount * 64)).ToArray()
            .Concat(MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0xDC0, pairCount * 64)).ToArray()).ToArray();
        var argument = obj + 0x10000; var combat = obj + 0x17000;
        memory.Write(argument + 0x40, 2, 1); memory.Write(argument + 0x70, 4, BitConverter.SingleToUInt32Bits(.01f));
        memory.Write(argument + 0x74, 4, combat); memory.Write(combat + 0x76, 2, 1000);
        memory.Write(stack, 4, obj + 0x1FF00); memory.Write(stack + 4, 4, argument);
        machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(Register.ECX, obj);
        machine.Run(0x7792C0, 0x7793B4, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var d = new SacredConfusionFxDefinition(Array.AsReadOnly<SacredFxFlareLayer>([
                new(Texture(0x20E4), Immediate(0x779067), (uint)code.At(0x779065).GetImmediate(0), default, 1,
                    code.At(0x77905E).GetImmediate(0) == 0, true),
                new(Texture(0x20E8), Immediate(0x7790C3), (uint)code.At(0x7790C1).GetImmediate(0), default, Operand(0x7790A4),
                    code.At(0x7790B0).GetImmediate(0) == 0, true)]),
            Texture(0x20EC), Array.AsReadOnly(vertices), MemoryMarshal.Read<SacredConfusionFxStateLayout>(memory.ObjectBytes(0x20A0, 0x44)))
        {
            PhaseRate = Operand(0x778F6C), PhaseWrap = Operand(0x778F78), AlphaAngleScale = Operand(0x778F8B),
            AlphaScale = Operand(0x778F93), RandomScale = Operand(0x778FD0), RandomAngleScale = Operand(0x778FD6),
            RibbonTimeScale = Operand(0x7790FC), MaximumGroundHeight = Operand(0x778FFE),
            ResetGroundHeight = BitConverter.UInt32BitsToSingle((uint)code.At(0x77900B).GetImmediate(1)),
            StartNotification = (uint)code.At(0x7793A7).GetImmediate(1)
        };
        yield return entry with { NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Confusion = d, TextureBindings = bindings,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = "Two ground symbols and both helical ribbons; explicit ten-second duration" };
    }
}
