using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Native blood/combat bursts and timed fog/ground emitters. Constructor addresses
/// identify implementations; Items.pak membership and all visual parameters remain native data.</summary>
internal static class NativeSustainedAndBurstEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var profile = Find(entry);
        if (profile is null) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = profile.Family with { InitialVectorCount = capacity };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        foreach (var preset in profile.Presets)
        {
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings,
                profile.TextureOffsets is { } offsets ? bindings.Single(b => b.NativeHandleOffset == offsets[preset]) : bindings.Single());
            if (profile.GroundMatrixCall is { } matrix && code.At(matrix).NearBranch32 != 0x653710)
                throw new NotSupportedException("Unmapped ground emitter draw matrix.");
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                DisplayName = profile.Description, Capacity = capacity,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings, Draw = draw with { GroundAligned = profile.GroundMatrixCall is not null },
                ParameterSets = NativePresetReader.ReadParameters(memory, family),
                BurstOnFirstUpdate = profile.Burst, EmitBeforeMovement = family.EmitBeforeMovement,
                SingleBirthInitialization = !profile.Burst, UsesWind = profile.Wind,
                UsesEventCornerColor = profile.EventColor,
                EventCornerColorMask = profile.EventColor ? 0xFFFFFFu : uint.MaxValue,
                EmissionClock = profile.Retire ? SacredParticleEmissionClock.CrossingUpdate : SacredParticleEmissionClock.CurrentTimeExclusive,
                EmissionDurationSeconds = profile.DurationOffset is { } duration
                    ? BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + duration, 4)) : null,
                RetireAfterDuration = profile.Retire,
                BirthSizeRamp = profile.Ramp ? new(
                    BitConverter.UInt32BitsToSingle(code.At(0x79AB9A).Immediate32),
                    image.Single((uint)code.At(0x79A86C).MemoryDisplacement64),
                    image.Single((uint)code.At(0x79A87E).MemoryDisplacement64),
                    image.Single((uint)code.At(preset == 0 ? 0x79A8ADu : 0x79A8A5u).MemoryDisplacement64), 0x79A868) : null
            };
        }
    }

    private static Profile? Find(SacredParticleDefinition entry) => entry.NativeConstructorAddress switch
    {
        0x76AE60 => new(new("cParticleSystem_bleed", entry.FactoryAddress, 0x76AE60, 0x76AED0,
            0x76B060, 0x20A0 + SacredBleedParticleStateLayout.SerializedSize + 4, 0x20BC, 0x20DC, 1, 0x76AFF0)
            { ColorOffset = 0x20A8, SelectorOffset = 0x3C, EventColor = 0x800000, SampleRandomValue = 32767 })
            { Burst = true, EventColor = true, Description = "Native blood burst; explicit RGB 800000, initialization rand 32767" },
        0x770670 => new(new("cParticleSystem_battlefog", entry.FactoryAddress, 0x770670, 0x7706E0,
            0x7709F0, 0x20A0 + SacredBattleFogParticleStateLayout.SerializedSize + 4, 0x24AC, 0x24CC, 1, 0x7707D0)
            { ColorOffset = 0x20AC, SelectorOffset = 0x3C, EmitBeforeMovement = false,
                SampleCombatArtDurationBase = 100, InitializerEnd = 0x770C2D,
                InitialState = new Dictionary<int, uint> { [0xAC] = 0 },
                EventArguments = new Dictionary<int, uint> { [0x70] = BitConverter.SingleToUInt32Bits(.01f) } })
            { Presets = [0, 1, 2], DurationOffset = 0x20A4, Wind = true,
                Description = "Native fog palette; explicit one-second combat-art duration" },
        0x78E910 => new(new("cParticleSystem_glad_sm_hit", entry.FactoryAddress, 0x78E910, 0x78EA4F,
            0x78F180, 0x20A0 + SacredGladiatorHitParticleStateLayout.SerializedSize + 0x28, 0x24B4, 0x24D4, 1, 0x78ED98)
            { ColorOffset = 0x20B4, SelectorOffset = 0x3C,
                InitialState = new Dictionary<int, uint> { [0x20A0] = 0 } })
            { Burst = true, TextureOffsets = [0x2560], Description = "Native ordinary combat-art hit; special branches pending" },
        0x79A750 => new(new("cParticleSystem_highspeed", entry.FactoryAddress, 0x79A750, 0x79A829,
            0x79AB70, 0x20A0 + SacredHighSpeedParticleStateLayout.SerializedSize + 8, 0x24A8, 0x24C8, 1, 0x79A96D)
            { ColorOffset = 0x20A8, SelectorOffset = 0x38, EmitBeforeMovement = false,
                SampleCombatArtDurationBase = 200, InitializerEnd = 0x79AF22,
                EventArguments = new Dictionary<int, uint> { [0x70] = BitConverter.SingleToUInt32Bits(.01f) } })
            { Presets = [0, 1], TextureOffsets = [0x2538, 0x253C], GroundMatrixCall = 0x79A988,
                DurationOffset = 0x2530, Retire = true, Ramp = true,
                Description = "Native expanding ground particles; explicit two-second combat-art duration" },
        _ => null
    };

    private sealed record Profile(NativeParticleFamily Family)
    {
        public int[] Presets { get; init; } = [0];
        public uint[]? TextureOffsets { get; init; }
        public bool Burst { get; init; }
        public bool Wind { get; init; }
        public bool EventColor { get; init; }
        public bool Retire { get; init; }
        public bool Ramp { get; init; }
        public uint? DurationOffset { get; init; }
        public uint? GroundMatrixCall { get; init; }
        public string? Description { get; init; }
    }
}
