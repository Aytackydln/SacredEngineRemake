using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>All seven script selectors share the original model/triangle-strip constructor.</summary>
internal static class NativeElementStoneFxReader
{
    public static SacredParticleDefinition Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x788ED0) return entry;
        var machine = new X86Machine(code, new PresetMemory(image, 0, quality));
        var creation = machine.ResolveDispatch(SacredGoldExecutableProfile.CreationDispatchAddress, entry.TypeId);
        var preset = NativePresetReader.ReadSelector(code, creation);
        machine.Registers.Set(Register.ESI, machine.Memory.BaseAddress);
        var branch = machine.ResolveDispatch(0x7897F0, (uint)preset);
        if (code.At(branch).Mnemonic != Mnemonic.Push) throw new NotSupportedException("Unmapped Element Stone selector.");
        var modelTexture = image.String(code.At(branch).Immediate32, 128);
        uint? modelType = null;
        for (var ip = branch; ip < branch + 0x40;)
        {
            var instruction = code.At(ip); ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind == OpKind.Immediate32)
                modelType = instruction.Immediate32;
            if (instruction.Mnemonic == Mnemonic.Jmp) break;
        }
        if (modelType is null || modelType > ushort.MaxValue)
            throw new NotSupportedException("Unmapped Element Stone TypeManager model lookup.");
        var memory = new PresetMemory(image, 0x21C0, quality);
        var constructor = new X86Machine(code, memory);
        constructor.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        constructor.Registers.Set(Register.EBX, memory.BaseAddress);
        constructor.Run(0x788F08, 0x789219, token);
        IReadOnlyList<SacredFxSurfaceVertexLayout> Vertices(int offset) => Array.AsReadOnly(
            MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(offset, 0x80)).ToArray());
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle(code.At(address).Immediate32);
        var red = preset == 7; // The original renderer's selector-seven branch changes material and palette.
        var glowSelector = (uint)code.At(0x78972E).GetImmediate(0);
        var coreSelector = (uint)code.At(0x789788).GetImmediate(0);
        var mesh = new SacredElementStoneFxDefinition(modelType.Value,
            image.String(code.At(red ? 0x7893A8u : 0x7893D2u).Immediate32, 128),
            modelTexture, image.String(code.At(0x789212).Immediate32, 128),
            Vertices(0x2130), Vertices(0x20B0),
            NativeTextureReader.ReadFlareTexture(code, glowSelector), NativeTextureReader.ReadFlareTexture(code, coreSelector))
        {
            FullTurn = Operand(0x7892DA), ModelScale = Immediate(0x789323), ModelHeight = Operand(0x78938D),
            BeamHeight = Operand(0x78951C), SecondBeamAngle = Operand(0x78954C), ThirdBeamAngle = Operand(0x7895B8),
            RandomScale = Operand(0x789667), FlickerUWidth = Operand(0x789679),
            FlickerEdgeV = Immediate(0x789646), GlowHalfSize = Immediate(0x789730),
            GlowHeight = Immediate(0x78975E), CoreHeight = Immediate(0x7897AF),
            GlowColor = code.At(red ? 0x7893BBu : 0x7893E5u).Immediate32,
            CoreColor = code.At(red ? 0x7893C3u : 0x7893EDu).Immediate32,
            FlickerBottomColor = red ? code.At(0x7893CB).Immediate32 : (uint)code.At(0x7893F5).GetImmediate(1),
            CoreSizeAddend = Operand(0x789778),
            GlowSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, glowSelector),
            CoreSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, coreSelector),
            ModelSourceColorOnly = code.At(0x789435).GetImmediate(0) != 0,
            SurfaceSourceColorOnly = code.At(0x7894CB).GetImmediate(0) != 0
        };
        return entry with { NativeClass = "Element Stone", Preset = preset, Status = SacredParticleDefinitionStatus.Decoded,
            Diagnostic = null, DisplayName = "Native Element Stone model, three beams, flicker plane and two flares",
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress), ElementStone = mesh,
            TextureBindings = Array.AsReadOnly<SacredParticleTextureBinding>([
                new(0x21B8, mesh.FlickerTexture), new(0x21BC, mesh.BeamTexture)]) };
    }
}
