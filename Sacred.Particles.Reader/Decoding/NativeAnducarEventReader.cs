using Iced.Intel;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeAnducarEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7C0020) yield break;
        var names = new List<string>();
        for (var ip = 0x7C009A; ip < 0x7C0122;)
        {
            var instruction = code.At((uint)ip); ip = (int)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind == OpKind.Immediate32 && instruction.Immediate32 > 0x400000)
                names.Add(image.String(instruction.Immediate32, 128));
        }
        if ((ulong)names.Count != code.At(0x7C0615).GetImmediate(1)) throw new NotSupportedException("Unmapped Anducar model selection.");
        var memory = new PresetMemory(image, 0x225C, quality);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ECX, memory.BaseAddress);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Run(0x7C04B0, 0x7C04E4, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        if (code.At(0x7C01C1).Mnemonic != Mnemonic.Fadd || code.At(0x7C021E).Mnemonic != Mnemonic.Fadd)
            throw new NotSupportedException("Unmapped Anducar envelope rate.");
        var d = new SacredAnducarFxDefinition(names.AsReadOnly(), image.String(code.At(0x7C0053).Immediate32, 128),
            image.String(code.At(0x7C0083).Immediate32, 128))
        {
            Count = (byte)memory.Integer(memory.BaseAddress + 0x2200, 1),
            HasGlowPass = (byte)quality == code.At(0x7C041D).GetImmediate(1),
            RandomScale = Operand(0x7C053E), ScaleOrigin = Operand(0x7C054A), ScaleWidth = Operand(0x7C0544),
            FullTurn = Operand(0x7C0566), OffsetWidth = Operand(0x7C0585), OffsetOrigin = Operand(0x7C058B),
            DelayPerUnit = Operand(0x7C0606), HoldSeconds = BitConverter.UInt32BitsToSingle((uint)code.At(0x7C0522).GetImmediate(1)),
            EnvelopeRate = 2, HorizontalScaleFactor = Operand(0x7C0312), GlowColor = (uint)code.At(0x7C0439).GetImmediate(0),
            GlowSourceColorOnly = code.At(0x7C0437).GetImmediate(0) != 0, StartNotification = (uint)code.At(0x7C01A9).GetImmediate(1)
        };
        yield return entry with { NativeClass = "Anducar Spikes", Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Anducar = d,
            DisplayName = "Native randomized Anducar spikes; explicit endpoint distance",
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            TextureBindings = Array.AsReadOnly<SacredParticleTextureBinding>([new(0x2254, d.SurfaceTexture), new(0x2258, d.GlowTexture)]) };
    }
}
