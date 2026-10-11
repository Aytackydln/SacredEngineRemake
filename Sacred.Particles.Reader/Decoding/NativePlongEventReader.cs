using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativePlongEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x779400) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = new NativeParticleFamily("Plong", entry.FactoryAddress, 0x779400, 0x779473,
            0x7798F0, 0x20C0, 0, 0, 0, 0x7796E0)
            { InitialVectorCount = capacity, DrawRegister = Register.EBP };
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        var texture = image.String(code.At(0x779435).Immediate32, 128);
        var bindings = Array.AsReadOnly<SacredParticleTextureBinding>([new(0x20BC, texture)]);
        var definition = new SacredPlongFxDefinition
        {
            InitialState = MemoryMarshal.Read<SacredPlongFxStateLayout>(memory.ObjectBytes(0x20A0, 0x1C)),
            RandomScale = Operand(0x7794E5), SpawnWidth = Operand(0x7794EB), SpawnOrigin = Operand(0x7794F1),
            SpawnHeight = Operand(0x779534), VelocityAngle = Operand(0x779553), HorizontalSpeed = Operand(0x77955B),
            VerticalSpeedWidth = Operand(0x7795A2), VerticalSpeedOrigin = Operand(0x7795A8),
            InitialHalfSize = BitConverter.UInt32BitsToSingle((uint)code.At(0x779591).GetImmediate(1)),
            FullTurn = Operand(0x7795CB), Gravity = Operand(0x77965C), BounceFactor = Operand(0x77968C),
            ShrinkRate = Operand(0x779698)
        };
        if (code.At(0x779776).GetImmediate(1) != SacredPlongFxStateLayout.SerializedSize)
            throw new NotSupportedException("Unmapped Plong saved block.");
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Plong = definition,
            Capacity = capacity, TextureBindings = bindings,
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single()),
            DisplayName = "Native Plong burst, independent velocity samples and ground bounce"
        };
    }
}
