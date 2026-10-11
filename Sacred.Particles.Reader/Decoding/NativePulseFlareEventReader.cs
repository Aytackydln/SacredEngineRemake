using System.Numerics;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativePulseFlareEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x7AD3E0 or 0x7BD4C0 or 0x7C2E50)) yield break;
        var transdim = entry.NativeConstructorAddress == 0x7AD3E0; var ball = entry.NativeConstructorAddress == 0x7BD4C0;
        var kind = transdim ? SacredPulseFlareKind.Transdim : ball ? SacredPulseFlareKind.PongBall : SacredPulseFlareKind.Zhadum;
        var family = new NativeParticleFamily(kind.ToString(), entry.FactoryAddress, entry.NativeConstructorAddress.Value,
            transdim ? 0x7AD48Eu : ball ? 0x7BD548u : 0x7C2EC1u,
            transdim ? 0x7AD810u : ball ? 0x7BD7A0u : 0x7C3000u,
            (int)code.At(entry.FactoryAddress).GetImmediate(0), 0, 0, 0, 0)
        {
            SampleCombatArtDurationBase = transdim ? (ushort)10 : null,
            EventArguments = transdim ? new Dictionary<int, uint> { [0x40] = 1, [0x70] = BitConverter.SingleToUInt32Bits(1f) } : null,
            // Constructor zeroes the dynamic offsets/angle/cycle. This handle is copied to a command, not used by evaluation.
            InitialState = transdim ? new Dictionary<int, uint> { [0x20C8] = 0 } : null,
            ConstructorState = transdim ? (0x7AD45Bu, 0x7AD47Fu) : null
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Immediate(uint address, int operand = 0) => (uint)code.At(address).GetImmediate(operand);
        float FloatImmediate(uint address) => BitConverter.UInt32BitsToSingle(Immediate(address));
        uint State(uint offset) => (uint)memory.Integer(memory.BaseAddress + offset, 4);
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var layers = new List<SacredFxFlareLayer>();
        if (transdim) layers.Add(new(Texture(0x20C4), FloatImmediate(0x7AD642), Immediate(0x7AD63A, 1), default, 0,
            Immediate(0x7AD631) == 0, Immediate(0x7AD62F) != 0));
        else if (ball)
        {
            layers.Add(new(Texture(0x20B0), Operand(0x7BD68C), Immediate(0x7BD680), default, Operand(0x7BD668), Immediate(0x7BD674) == 0, Immediate(0x7BD672) != 0));
            layers.Add(new(Texture(0x20B0), Operand(0x7BD6CC), Immediate(0x7BD6C0), default, Operand(0x7BD6AA), Immediate(0x7BD6B6) == 0, Immediate(0x7BD6B4) != 0));
            layers.Add(new(Texture(0x20B4), 2, Immediate(0x7BD730), default, 0, Immediate(0x7BD728) == 0, Immediate(0x7BD726) != 0));
            var selector = Immediate(0x7BD755);
            layers.Add(new(NativeTextureReader.ReadFlareTexture(code, selector), 1, Immediate(0x7BD753),
                new Vector3(0, 0, BitConverter.UInt32BitsToSingle(Immediate(0x7BD77E, 1))), 0,
                NativeTextureReader.ReadFlareSourceBlend(code, selector), true));
        }
        else layers.Add(new(NativeTextureReader.ReadFlareTexture(code, Immediate(0x7C2FDA)), FloatImmediate(0x7C2FDC), 0, default, 0, NativeTextureReader.ReadFlareSourceBlend(code, Immediate(0x7C2FDA)), true));
        var definition = new SacredPulseFlareFxDefinition(kind, layers.AsReadOnly())
        {
            Lifetime = transdim ? BitConverter.UInt32BitsToSingle(State(0x20C0)) : 0,
            ResetInterval = transdim ? Operand(0x7AD4D8) : 0, RandomScale = transdim ? Operand(0x7AD500) : 0,
            PositionRange = transdim ? Operand(0x7AD506) : 0, PositionBias = transdim ? Operand(0x7AD50C) : 0,
            FullAngle = transdim ? Operand(0x7AD562) : 0, PlaneCount = transdim ? (int)Immediate(0x7AD651, 1) : 0,
            PlaneStep = transdim ? Operand(0x7AD5BF) : 0, SpreadScale = transdim ? Operand(0x7AD5AF) : 0,
            AlphaBias = transdim ? BitConverter.ToDouble(image.Slice((uint)code.At(0x7AD5F1).MemoryDisplacement64, 8)) : 0,
            AlphaScale = transdim ? BitConverter.ToDouble(image.Slice((uint)code.At(0x7AD5F7).MemoryDisplacement64, 8)) : Operand(0x7C2FB6),
            FlashAlpha = transdim ? Immediate(0x7AD61C, 1) >> 24 : 0,
            FadeRate = ball ? Operand(0x7BD5F4) : transdim ? 0 : Operand(0x7C2F63),
            InitialTimer = ball ? BitConverter.UInt32BitsToSingle(State(0x20A8)) : transdim ? 0 : BitConverter.UInt32BitsToSingle(Immediate(0x7C2EEE, 1)),
            ResetTimer = ball ? BitConverter.UInt32BitsToSingle(Immediate(0x7BD593, 1)) : 0,
            HitTimer = ball ? BitConverter.UInt32BitsToSingle(Immediate(0x7BD5D7, 1)) : 0,
            InitialSpeed = ball ? State(0x98) : 0, SpeedIncrement = ball ? Immediate(0x7BD59D, 1) : 0,
            SpeedLimit = ball ? Immediate(0x7BD5A0, 1) : 0, ResumeNotification = ball ? Immediate(0x7BD619, 1) : 0,
            InitialNotification = State(0x7C), HitFlag = transdim ? (ushort)0 : (ushort)Immediate(ball ? 0x7BD5BDu : 0x7C2EF3u, 1),
            RadiusFrequency = ball ? Operand(0x7BD6E4) : 0, RadiusAmplitude = ball ? Operand(0x7BD6FB) : 0,
            RadiusBias = ball ? Operand(0x7BD701) : 0
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = transdim,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, PulseFlare = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = transdim ? "All fifteen native ground passes; explicit ten-second duration" : ball ? "Complete native hit flares and ball glow" : "Native triggered hold and grayscale fade"
        };
    }
}
