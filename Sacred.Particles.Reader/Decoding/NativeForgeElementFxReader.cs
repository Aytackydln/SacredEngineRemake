using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeForgeElementFxReader
{
    public static SacredParticleDefinition Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7A72E0) return entry;
        var dispatcher = new X86Machine(code, new PresetMemory(image, 0, quality));
        var creation = dispatcher.ResolveDispatch(SacredGoldExecutableProfile.CreationDispatchAddress, entry.TypeId);
        var preset = NativePresetReader.ReadSelector(code, creation);
        var models = new List<string>(); var materials = new List<string>();
        for (var ip = 0x7A7314u; ip < 0x7A74CE;)
        {
            var instruction = code.At(ip); ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic != Mnemonic.Push || instruction.Op0Kind != OpKind.Immediate32 || instruction.Immediate32 < SacredGoldExecutableProfile.ImageBase) continue;
            var name = image.String(instruction.Immediate32, 128);
            if (name.EndsWith(".grn", StringComparison.OrdinalIgnoreCase)) models.Add(name);
            if (name.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) materials.Add(name);
        }
        if (models.Count != 5 || materials.Count != 8 || (uint)preset >= models.Count)
            throw new NotSupportedException("Unmapped Forge Element resource arrays.");
        var memory = new PresetMemory(image, 0x28C0, quality);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.ESI, memory.BaseAddress);
        machine.Run(0x7A7368, 0x7A73DE, token);
        machine.Registers.Set(Register.ECX, memory.BaseAddress);
        var argument = memory.BaseAddress + 0x10000;
        memory.Write(memory.BaseAddress + 0x1F004, 4, argument);
        memory.Write(argument + 0x38, 4, (uint)preset);
        machine.Run(0x7A7C30, 0x7A7C77, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle(code.At(address).Immediate32);
        var ringVertexCount = checked((int)code.At(0x7A7385).GetImmediate(1) * 2);
        var definition = new SacredForgeElementFxDefinition(models[preset], materials[preset + 3], materials[0], materials[1], materials[2],
            Array.AsReadOnly(MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x20B8, ringVertexCount * SacredFxSurfaceVertexLayout.SerializedSize)).ToArray()),
            MemoryMarshal.Read<SacredForgeElementFxStateLayout>(memory.ObjectBytes(0x20A0, 0x18)))
        {
            GrowthRate = Operand(0x7A7548), MaximumGrowthFactor = Immediate(0x7A756B), CompressionRate = Operand(0x7A7585), MinimumCompressionFactor = Immediate(0x7A75A8),
            FadeRate = Operand(0x7A75C2), ScrollRate = Operand(0x7A76AB), ScrollWrap = Operand(0x7A76CA),
            FlareHalfSize = Operand(0x7A77EF), FlareHeight = Immediate(0x7A77DE), FullTurn = Operand(0x7A77A5),
            HalfTurn = Operand(0x7A77B3), FlareAngleBias = Operand(0x7A77C1),
            ModelBobRate = Operand(0x7A78C7), ModelBobAmplitude = Operand(0x7A78DA), ModelHeight = Operand(0x7A78E3),
            RingHeight = Operand(0x7A7A44), TrailStartBrightness = (int)code.At(0x7A774D).Immediate32,
            TrailBrightnessStep = (int)code.At(0x7A7773).GetImmediate(1), ChildDelay = Operand(0x7A7615),
            ChildFrameRate = Operand(0x7A762E), ChildLimit = Operand(0x7A767C), ChildTypeId = (uint)code.At(0x7A7689).GetImmediate(1),
            ModelSourceColorOnly = code.At(0x7A790D).GetImmediate(0) != 0
        };
        return entry with { NativeClass = "Forge Element", Preset = preset, Status = SacredParticleDefinitionStatus.Decoded,
            Diagnostic = null, ForgeElement = definition, DisplayName = "Native Forge Element model, two ground flares and cylindrical strip",
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress), TextureBindings =
                Array.AsReadOnly(materials.Select((texture, i) => new SacredParticleTextureBinding((uint)(0x2878 + i * 4), texture)).ToArray()) };
    }
}
