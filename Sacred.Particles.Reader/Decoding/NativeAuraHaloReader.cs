using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Additional original flare/lensflare calls for the decoded aura and cast families.</summary>
internal static class NativeAuraHaloReader
{
    public static IReadOnlyList<SacredParticleHaloDefinition> Read(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, int preset, PresetMemory memory)
    {
        float State(uint offset) => BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + offset, 4));
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle(code.At(address).Immediate32);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        string Texture(uint offset) => NativeTextureReader.ReadBindings(image, code, family).Single(b => b.NativeHandleOffset == offset).TextureName;
        SacredParticleHaloDefinition Flare(uint selector, float size, uint color, uint address) => new(
            NativeTextureReader.ReadFlareTexture(code, selector), size, color, address)
            { SourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector), AttachmentIndex = -1, PersistAfterEmission = true };
        return family.Constructor switch
        {
            0x778760 =>
            [
                new(Texture(0x253C), Immediate(0x7789F8), (uint)code.At(0x7789F3).GetImmediate(0), 0x7789FF)
                { GroundAligned = true, SourceColorOnly = true, RotationRate = Operand(0x7789DB), PersistAfterEmission = true },
                new(Texture(0x2534), Immediate(0x778A3E), (uint)code.At(0x778A3C).GetImmediate(0), 0x778A47)
                { GroundAligned = true, PersistAfterEmission = true }
            ],
            0x78B9D0 =>
            [
                new(Texture(0x2554), Immediate(0x78BBCF), (uint)memory.Integer(memory.BaseAddress + 0x24C0, 4), 0x78BBE6)
                { GroundAligned = true, SourceColorOnly = true, VisibleUntilSeconds = State(0x20A4), PersistAfterEmission = true }
            ],
            0x77E1C0 =>
            [
                Flare((uint)code.At(0x77E459).GetImmediate(0), Immediate(0x77E45B), 0xFFFFFF, 0x77E473) with
                { Envelope = new(State(0x20A8), 0, 1, Operand(0x77E352), Operand(0x77E363), true) }
            ],
            0x78F640 =>
            [
                Flare(preset == 1015 ? (uint)code.At(0x78F93A).GetImmediate(0) : (uint)code.At(0x78FB25).GetImmediate(0),
                    preset == 1012 ? 0 : Immediate(0x78FB27), (uint)code.At(0x78FB20).GetImmediate(0), 0x78FB3F) with
                {
                    SizeGrowthRate = preset == 1012 ? Operand(0x78F9AE) * State(0x254C) : 0,
                    VisibleFromSeconds = State(0x20A4)
                }
            ],
            0x7AFFB0 => Array.AsReadOnly(Enumerable.Range(0, checked((int)code.At(0x7B01A5).GetImmediate(1)))
                .Select(i => new SacredParticleHaloDefinition(Texture(0x2148),
                    (float)(State(0x20AC) * (Operand(0x7B0188) - i)), (uint)code.At(0x7B017A).GetImmediate(0), 0x7B019F)
                {
                    GroundAligned = true, Offset = new(0, 0, i * Operand(0x7B015F)), PersistAfterEmission = true,
                    SineSizeAmplitude = (float)(Operand(0x7B0188) - i), Envelope = new(0, 1, 1, null, 0, false)
                }).ToArray()),
            _ => []
        };
    }
}
