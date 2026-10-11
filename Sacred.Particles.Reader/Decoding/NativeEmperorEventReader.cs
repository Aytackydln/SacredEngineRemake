using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeEmperorEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7BD9B0) yield break;
        var family = new NativeParticleFamily("Emperor", entry.FactoryAddress, 0x7BD9B0, 0x7BDA3A,
            0x7BDC50, 0x253C, 0x20B0, 0x20D0, 1, 0x7BDB50)
            { InitializerEnd = 0x7BDD91, ColorOffset = 0x2134, EmitBeforeMovement = false };
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Immediate(uint address) => (uint)code.At(address).GetImmediate(1);
        var selector = code.At(0x7BDBF7).Immediate32;
        var emperor = new SacredEmperorFxDefinition(NativeTextureReader.ReadFlareTexture(code, selector))
        {
            InitialState = MemoryMarshal.Read<SacredEmperorFxStateLayout>(memory.ObjectBytes(0x20A0, 0x494)),
            RandomScale = Operand(0x7BDC18), FlareSizeWidth = Operand(0x7BDC1E), FlareSizeMinimum = Operand(0x7BDC24),
            FlareColor = code.At(0x7BDBEE).Immediate32, FlareSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector),
            RadiusRate = Operand(0x7BDAB9), InitialRadius = Immediate(0x7BDD63), InitialNotification = Immediate(0x7BDD5C),
            HitFlag = (ushort)Immediate(0x7BDAD0), HitExtension = Operand(0x7BDAFB),
            HitFlareMultiplier = BitConverter.UInt32BitsToSingle(Immediate(0x7BDB01)), HitSizeMultiplier = Operand(0x7BDB18),
            HitNotification = Immediate(0x7BDAF4), SpecialHitNotification = Immediate(0x7BDAEB)
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, Emperor = emperor,
            EmitBeforeMovement = false, SingleBirthInitialization = true,
            EmissionDurationSeconds = emperor.InitialState.EmissionEnd,
            EmissionClock = SacredParticleEmissionClock.PreviousTimeExclusive,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single(b => b.NativeHandleOffset == 0x2534)),
            DisplayName = "Native Emperor particles, random flare and hit extension"
        };
    }
}
