using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeDoorSpikeEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7C4470) yield break;
        var family = new NativeParticleFamily("Door Spike", entry.FactoryAddress, 0x7C4470, 0x7C4507,
            0x7C4780, 0x215C, 0, 0, 0, 0);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        var offsetAngle = Operand(0x7C45F9);
        var definition = new SacredDoorSpikeFxDefinition(image.String(code.At(0x7C44DC).Immediate32, 128),
            image.String(code.At(0x7C44A3).Immediate32, 128))
        {
            InitialState = MemoryMarshal.Read<SacredDoorSpikeFxStateLayout>(memory.ObjectBytes(0x20A0, SacredDoorSpikeFxStateLayout.SerializedSize)),
            DrawCount = (int)code.At(0x7C4739).GetImmediate(1),
            EvenHeading = (float)(Operand(0x7C45F3) - (double)offsetAngle),
            OddHeading = (float)(Operand(0x7C45EB) - (double)offsetAngle),
            Tilt = BitConverter.UInt32BitsToSingle(code.At(0x7C4618).Immediate32),
            ReleaseFlag = (ushort)code.At(0x7C4574).GetImmediate(1),
            HasShadowPass = (byte)quality == code.At(0x7C46C8).GetImmediate(1)
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, DoorSpike = definition,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            TextureBindings = Array.AsReadOnly<SacredParticleTextureBinding>([new(0x2158, definition.SurfaceTexture)]),
            DisplayName = "Six authored Door Spike models; native grow, hold and release"
        };
    }
}
