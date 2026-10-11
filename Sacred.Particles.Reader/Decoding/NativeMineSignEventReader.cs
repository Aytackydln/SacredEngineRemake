using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeMineSignEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7BA580) yield break;
        var scratch = (image.EndAddress + 0xFFFF) & ~0xFFFFu;
        var family = new NativeParticleFamily("Dwarf Mine Sign", entry.FactoryAddress, 0x7BA580, 0x7BA5F3,
            0x7BA8D0, 0x253C, 0x24B4, 0x24D4, 1, 0x7BA830)
        {
            ColorOffset = 0x20B4, EmitBeforeMovement = false,
            // Explicit standalone combat-art sample: parameter 0 = 3, parameter 1 = 1,
            // no level increments, multiplier 1. Execute the real ratio helper.
            InitialState = new Dictionary<int, uint> { [0x17076] = 3, [0x1707A] = 1 },
            EventArguments = new Dictionary<int, uint>
                { [0x40] = 1, [0x70] = BitConverter.SingleToUInt32Bits(1), [0x74] = scratch + 0x17000 }
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            MineSign = new()
            {
                InitialState = MemoryMarshal.Read<SacredMineSignFxStateLayout>(memory.ObjectBytes(0x20A0, 0x498)),
                BirthRate = Operand(0x7BA660), MaximumCarry = Operand(0x7BA676), BirthUnit = Operand(0x7BA68F),
                RingRadius = BitConverter.UInt32BitsToSingle(code.At(0x7BA6E6).Immediate32),
                FullAngle = Operand(0x7BA6E0), RandomScale = Operand(0x7BA6CC),
                RotationWidth = Operand(0x7BA768), RotationHalfWidth = Operand(0x7BA76E),
                RotationBase = Operand(0x7BA778), AudioPitchMinimum = Operand(0x7BA7FA),
                MineUsedFlag = (ushort)code.At(0x7BA7B1).Immediate8
            },
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            EmitBeforeMovement = false, TextureBindings = bindings,
            ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single()) with
                { GroundAligned = true, HalfSizeMultiplier = BitConverter.UInt32BitsToSingle(code.At(0x7BA878).Immediate32) },
            DisplayName = "Native mine ring; standalone count 3, mine-use notification consumes one"
        };
    }
}
