using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Both Items.pak teleport members share this constructor. Decode the
/// manual updater's operands; there are no stdCreation parameter blocks.</summary>
internal static class NativeTeleportEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x771370) yield break;
        var family = new NativeParticleFamily("cParticleSystem_teleport", entry.FactoryAddress,
            0x771370, 0x771400, 0x771E20, 0x20E4, 0, 0, 0, 0x771C10)
        {
            EventArguments = new Dictionary<int, uint> { [4] = 0, [0x2C] = BitConverter.SingleToUInt32Bits(1) }
        };
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var memory = NativePresetReader.Evaluate(image, code, family with { InitialVectorCount = capacity }, 0, quality, token);
        var state = MemoryMarshal.Read<SacredTeleportParticleStateLayout>(memory.ObjectBytes(0x20A0, 0x40));
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(1));
        int Integer(uint address) => checked((int)code.At(address).GetImmediate(1));
        if (code.At(0x771CB9).GetImmediate(0) != SacredTeleportParticleStateLayout.SerializedSize ||
            code.At(0x771B53).GetImmediate(1) != 4 || code.At(0x771BD1).GetImmediate(1) != 6 ||
            code.At(0x77189F).Mnemonic != Mnemonic.Fadd || code.At(0x771598).GetImmediate(1) != code.At(0x771509).GetImmediate(1))
            throw new NotSupportedException("Unmapped teleport state machine.");
        var teleport = new SacredTeleportParticleDefinition(state,
            Operand(0x77141C), Operand(0x7714B5), Operand(0x7714BB), Operand(0x77154F),
            Immediate(0x7714A5), Immediate(0x7714AD), Operand(0x7714DE), Operand(0x7714D8),
            Immediate(0x771509), Operand(0x7715E2), Operand(0x7719A2), Operand(0x7717D5),
            Integer(0x771665), Operand(0x77173D), Operand(0x7716E1), Operand(0x77182A), Operand(0x77184D),
            Integer(0x77185D), Integer(0x77186E), Integer(0x771879), Immediate(0x771B5D), 0x771410);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true,
            DisplayName = "Native teleport stages; ownerless event, cast speed one",
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
            Capacity = capacity, OneTime = true, TextureBindings = bindings,
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single()),
            ParameterSets = [], Teleport = teleport
        };
    }
}
