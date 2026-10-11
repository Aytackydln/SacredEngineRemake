using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeUnderworldGlyphFxReader
{
    public static SacredParticleDefinition Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7C4A00) return entry;
        var memory = new PresetMemory(image, 0x2C14, quality);
        var machine = new X86Machine(code, memory);
        var creation = machine.ResolveDispatch(SacredGoldExecutableProfile.CreationDispatchAddress, entry.TypeId);
        var preset = NativePresetReader.ReadSelector(code, creation);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.ESI, memory.BaseAddress);
        var modelPush = machine.ResolveDispatch(0x7C5192, (uint)preset, instruction =>
            instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind == OpKind.Immediate32 &&
            instruction.Immediate32 > SacredGoldExecutableProfile.ImageBase);
        var model = image.String(code.At(modelPush).Immediate32, 128);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.EDI, 0);
        machine.Run(0x7C4A79, 0x7C4AB6, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle(code.At(address).Immediate32);
        var glowSelector = (uint)code.At(0x7C50A8).GetImmediate(0);
        var definition = new SacredUnderworldGlyphFxDefinition(model,
            image.String(code.At(0x7C4A38).Immediate32, 128), image.String(code.At(0x7C4A5C).Immediate32, 128),
            Array.AsReadOnly(MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x29D4, 18 * 32)).ToArray()),
            NativeTextureReader.ReadFlareTexture(code, glowSelector))
        {
            Element = (uint)preset, FullTurn = Operand(0x7C4B5B), RandomScale = Operand(0x7C5143),
            WaveRateOrigin = Operand(0x7C514F), WaveRateWidth = Operand(0x7C5149),
            WaveRadiusOrigin = Operand(0x7C5175), WaveRadiusWidth = Operand(0x7C516F),
            PointPhaseStep = Operand(0x7C4BB1), HalfTurn = Operand(0x7C4BB9), WaveRadiusScale = Operand(0x7C4BCD),
            WaveHeightOrigin = unchecked((int)(uint)code.At(0x7C4B8E).MemoryDisplacement64),
            ModelScale = Immediate(0x7C4CD6), ModelHeight = Operand(0x7C4D36), ModelColor = code.At(0x7C4D91).Immediate32,
            ModelSourceColorOnly = code.At(0x7C4D8F).GetImmediate(0) != 0,
            FirstUScrollRate = Operand(0x7C4C47), FirstVScrollRate = Operand(0x7C4C91),
            SecondUScrollRate = Operand(0x7C4CA6), SecondVScrollRate = Operand(0x7C4CBB),
            RibbonHeight = Operand(0x7C4EEF), RibbonSideY = Operand(0x7C4F38), RibbonSideZ = Operand(0x7C4F40),
            RibbonNormalMinimum = Operand(0x7C4F72), RibbonHalfWidth = Operand(0x7C4FD4),
            InitialThickness = Immediate(0x7C5126), RibbonSourceColorOnly = code.At(0x7C4E89).GetImmediate(0) != 0,
            GlowHalfSize = Immediate(0x7C50AA), GlowHeight = Immediate(0x7C50DB), GlowColor = code.At(0x7C50A3).Immediate32,
            GlowSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, glowSelector)
        };
        return entry with { NativeClass = "Underworld Glyph", Preset = preset, Status = SacredParticleDefinitionStatus.Decoded,
            Diagnostic = null, UnderworldGlyph = definition, DisplayName = "Native glyph model with two scrolling passes, twenty ribbons and glow",
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress), TextureBindings =
                Array.AsReadOnly<SacredParticleTextureBinding>([new(0x29CC, definition.ModelTexture), new(0x29D0, definition.RibbonTexture)]) };
    }
}
