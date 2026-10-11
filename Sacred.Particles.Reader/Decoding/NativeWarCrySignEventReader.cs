using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeWarCrySignEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7B8740) yield break;
        var size = (int)code.At(entry.FactoryAddress).GetImmediate(0);
        var constructorMemory = new PresetMemory(image, size, quality);
        var machine = new X86Machine(code, constructorMemory);
        machine.Registers.Set(Register.ESI, constructorMemory.BaseAddress);
        machine.Registers.Set(Register.EBP, constructorMemory.BaseAddress + 0x20C8);
        machine.Registers.Set(Register.EBX, 0);
        machine.Run(0x7B87BF, 0x7B88A4, token);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(constructorMemory.ObjectBytes(0x20C8, 6 * 32)).ToArray();
        uint ConstructorWord(int offset) => (uint)constructorMemory.Integer(constructorMemory.BaseAddress + (uint)offset, 4);
        var family = new NativeParticleFamily("War Cry Sign", entry.FactoryAddress, 0x7B8740, 0x7B88BE, 0x7B8E10,
            size, 0, 0, 0, 0)
        {
            EventArguments = new Dictionary<int, uint> { [0x48] = BitConverter.SingleToUInt32Bits(10) },
            InitialState = new Dictionary<int, uint> { [0x20B4] = ConstructorWord(0x20B4), [0x20B8] = ConstructorWord(0x20B8) }
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        uint Immediate(uint address, int operand = 0) => (uint)code.At(address).GetImmediate(operand);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float State(int offset) => BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + (uint)offset, 4));
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var definition = new SacredWarCrySignFxDefinition(Texture(0x20C0), Array.AsReadOnly(vertices),
            new(Texture(0x20C4), Operand(0x7B89E6), Immediate(0x7B8A01), default, 0,
                Immediate(0x7B89F2) == 0, Immediate(0x7B89F0) != 0))
        {
            InitialLifetime = State(0x20A8), MinimumReach = State(0x20B4), MaximumReach = State(0x20B8),
            WeightRate = Operand(0x7B8988), SweepRate = Operand(0x7B88F4), SweepPeriod = Operand(0x7B8913),
            SectorCount = (int)Immediate(0x7B8DD9, 1), SectorStep = Operand(0x7B8AC5), SweepWidth = Operand(0x7B8AE2),
            ReachFalloff = Operand(0x7B8B46), FullAngle = Operand(0x7B8BA0), OppositeAngle = Operand(0x7B8CB9),
            StripColor = Immediate(0x7B8BFB, 1), HighlightThreshold = Operand(0x7B8C2E), HighlightAlphaScale = Operand(0x7B8C45),
            HighlightPassCount = 3, // Both native branches submit the same highlight strip three times.
            InitialNotification = (uint)memory.Integer(memory.BaseAddress + 0x7C, 4)
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, WarCrySign = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = "Native swept fins and ground glow; explicit ten-second event duration"
        };
    }
}
