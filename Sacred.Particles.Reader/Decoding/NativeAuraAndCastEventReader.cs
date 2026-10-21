using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Native auras, clouds and casting effects with explicit standalone event inputs.</summary>
internal static class NativeAuraAndCastEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var profile = Find(entry);
        if (profile is null) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        foreach (var preset in profile.Presets)
        {
            var family = profile.Family with { InitialVectorCount = capacity };
            if (profile.OwnerSelector)
                family = family with { InitialState = new Dictionary<int, uint> { [0xA9] = (uint)preset } };
            if (family.Constructor == 0x78F640)
                family = family with { DrawAddress = preset == 1002 ? 0x78FABFu : preset == 1012 ? 0x78F941u : 0x78F8D4u };
            var bindings = NativeTextureReader.ReadBindings(image, code, family);
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var parameters = NativePresetReader.ReadParameters(memory, family);
            SacredParticleDrawDefinition draw;
            try { draw = NativeTextureReader.ReadDraw(image, code, family with { ParameterSlotCount = 1 }, preset, bindings,
                profile.TextureOffset is { } texture ? bindings.Single(b => b.NativeHandleOffset == texture) : null); }
            catch (NotSupportedException e) { throw new NotSupportedException($"{family.Name}/{preset}: {e.Message}", e); }
            float StateFloat(uint offset) => BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + offset, 4));
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true, OneTime = true,
                DisplayName = profile.Description, Capacity = capacity,
                Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings, ParameterSets = parameters,
                Draw = profile.OwnerSelector && preset != 0 ? draw with { TextureName = bindings.Single(b => b.NativeHandleOffset == 0x2550).TextureName } : draw,
                AdditionalHalos = NativeAuraHaloReader.Read(image, code, family, preset, memory),
                SimulationStartSeconds = family.Constructor == 0x7905E0 ? StateFloat(0x20A4) : family.Constructor == 0x78F640 && preset == 1015
                    ? StateFloat(0x20A4) : family.Constructor == 0x7AFFB0 ? image.Single(0x89066C) : 0,
                RandomBurst = family.Constructor == 0x77F5C0 ? new(image.Single(0x890C08), image.Single(0x897E80), StateFloat(0x20AC), image.Single(0x89076C), 0x77F72F) : null,
                MaximumSimulationStep = family.Constructor == 0x77F5C0 ? image.Single(0x89066C) : null,
                EmitBeforeMovement = family.Constructor == 0x77E1C0,
                SingleBirthInitialization = !profile.Burst && !(family.Constructor == 0x78F640 && preset == 1015) && family.Constructor != 0x77F5C0,
                BurstOnFirstUpdate = profile.Burst || family.Constructor == 0x77F5C0 || family.Constructor == 0x78F640 && preset == 1015,
                EmissionDurationSeconds = profile.DurationOffset is { } duration ?
                    BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + duration, 4)) : null,
                EmissionClock = profile.Clock, RetireAfterDuration = profile.Retire,
                EmissionMode = family.ParameterSlotCount == 2 ? 2 : 1,
                UsesEventCornerColor = family.Constructor == 0x783EE0
            };
        }
    }

    private static Profile? Find(SacredParticleDefinition entry) => entry.NativeConstructorAddress switch
    {
        0x7905E0 => new(new("Seraphim cast", entry.FactoryAddress, 0x7905E0, 0x79065A,
            0x790CE0, 0x20A0 + SacredSeraphimCastParticleStateLayout.SerializedSize + 8, 0x24B0, 0x24D0, 1, 0x790A7A)
            { ColorOffset = 0x20B0, SelectorOffset = 0x3C,
                InitialState = new Dictionary<int, uint> { [0xAC] = 0 },
                ActorLookup = (0x790F62, 0x790F6E), OmittedCallStackBytes = 36 })
            { Presets = [1018], TextureOffset = 0x2574, DurationOffset = 0x20A8,
                Clock = SacredParticleEmissionClock.PreviousTimeInclusive,
                Description = "Native BeeEffGee cast: growing finger flare, delayed spark emission and draining lifetime" },
        0x783EE0 => new(new("cParticleSystem_cloud", entry.FactoryAddress, 0x783EE0, 0x783F51,
            0x784220, 0x20A0 + SacredCloudParticleStateLayout.SerializedSize + 4, 0x20BC, 0x20DC, 1, 0x784000)
            { ColorOffset = 0x20AC, EventColor = 0xFFFFFFFF, InitialState = new Dictionary<int, uint> { [0x20A9] = 1 } })
            { DurationOffset = 0x20A4, Description = "Native one-second cloud; explicit white event colour" },
        0x78B9D0 => new(new("cParticleSystem_fireskin", entry.FactoryAddress, 0x78B9D0, 0x78BAEB,
            0x78BE10, 0x20A0 + SacredFireSkinParticleStateLayout.SerializedSize + 12, 0x24C4, 0x24E4, 1, 0x78BC26)
            { ColorOffset = 0x20C0, SampleCombatArtDurationBase = 200,
                ActorLookup = (0x78BEAB, 0x78BEEC), InitializerEnd = 0x78BF8D,
                ConstructorState = (0x78BA05, 0x78BA94),
                EventArguments = DurationArguments() })
            { Presets = [0, 1], OwnerSelector = true, TextureOffset = 0x254C,
                DurationOffset = 0x20A4, Clock = SacredParticleEmissionClock.CrossingUpdate,
                Description = "Native Fire Skin; both owner visual modes, explicit two-second duration" },
        0x778760 => new(new("cParticleSystem_groundPoison", entry.FactoryAddress, 0x778760, 0x7788D8,
            0x778D10, 0x20A0 + SacredGroundPoisonParticleStateLayout.SerializedSize + 12, 0x24AC, 0x24CC, 1, 0x778A4C)
            { ColorOffset = 0x20A8, SampleCombatArtDurationBase = 200, InitializerEnd = 0x778E2D,
                ConstructorState = (0x778795, 0x778831),
                InitialState = new Dictionary<int, uint> { [0xAC] = 0 }, EventArguments = DurationArguments() })
            { DurationOffset = 0x20A4, Clock = SacredParticleEmissionClock.CrossingUpdate, Retire = true,
                Description = "Native Ground Poison smoke and both circles; explicit two-second duration" },
        0x77E1C0 => new(new("cParticleSystem_freeze_impact", entry.FactoryAddress, 0x77E1C0, 0x77E283,
            0x77E630, 0x20A0 + SacredFreezeImpactParticleStateLayout.SerializedSize + 4, 0x20AC, 0x20CC, 1, 0x77E3CE) { ColorOffset = 0x2130 })
            { Presets = [1], Burst = true, Description = "Native freeze impact; terminal chain input one" },
        0x78F640 => new(new("cParticleSystem_glad_sm_cast", entry.FactoryAddress, 0x78F640, 0x78F6C8,
            0x78FD20, 0x20A0 + SacredGladiatorCastParticleStateLayout.SerializedSize + 8, 0x24B0, 0x24D0, 1, 0x78FABF)
            { ColorOffset = 0x20B0, SelectorOffset = 0x3C, DrawRegister = Register.EBP })
            { Presets = [1002, 1012, 1015], DurationOffset = 0x20A8,
                Clock = SacredParticleEmissionClock.PreviousTimeInclusive,
                Description = "Native Gladiator cast; explicit combat-art branch" },
        0x76A6D0 => new(new("cParticleSystem_soulcatcher", entry.FactoryAddress, 0x76A6D0, 0x76A846,
            0x76AA40, 0x20A0 + SacredSoulcatcherParticleStateLayout.SerializedSize + 4, 0x28AC, 0x28EC, 2, 0x76A960)
            { ColorOffset = 0x20A4, ColorSlotStride = 0x400, SampleCombatArtDurationBase = 200,
                ConstructorState = (0x76A706, 0x76A81C), ActorLookup = (0x76AA99, 0x76AAC9),
                InitializerEnd = 0x76AC5F, InitialState = new Dictionary<int, uint> { [0xAC] = 0 },
                EventArguments = DurationArguments() })
            { Retire = true, DurationOffset = 0x28A8, Clock = SacredParticleEmissionClock.PreviousTimeExclusive,
                Description = "Native Soulcatcher dual emission; explicit two-second duration, no external hit flags" },
        0x77F5C0 => new(new("cParticleSystem_burninglight", entry.FactoryAddress, 0x77F5C0, 0x77F691,
            0x77FA60, 0x20A0 + SacredBurningLightParticleStateLayout.SerializedSize + 4, 0x24B0, 0x24D0, 1, 0x77F840)
            { ColorOffset = 0x20B0, ConstructorState = (0x77F621, 0x77F683), ActorLookup = (0x77FA6E, 0x77FAB2), InitializerEnd = 0x77FBB3,
                InitialState = new Dictionary<int, uint> { [0x20AC] = BitConverter.SingleToUInt32Bits(20), [0x20A8] = 0 },
                SampleCombatArtDurationBase = 200, EventArguments = DurationArguments() })
            { DurationOffset = 0x2534, Description = "Native random Burning Light bursts; explicit radius 20 and duration two seconds" },
        0x7AFFB0 => new(new("Songs from Hell", entry.FactoryAddress, 0x7AFFB0, 0x7B0021,
            0x7B0200, 0x20A0 + SacredSongsFromHellParticleStateLayout.SerializedSize + 4, 0x20C4, 0x20E4, 1, 0x7B01C2)
            { ColorOffset = 0x20B4, SampleCombatArtDurationBase = 300,
                ActorLookup = (0x7B028C, 0x7B0309), InitializerEnd = 0x7B0410,
                EventArguments = new Dictionary<int, uint> { [0x38] = 120, [0x3C] = 0, [0x70] = BitConverter.SingleToUInt32Bits(.01f) } })
            { TextureOffset = 0x2148, DurationOffset = 0x20A4, Description = "Native Songs from Hell; explicit radius input 120, duration three seconds" },
        _ => null
    };

    private static Dictionary<int, uint> DurationArguments() => new() { [0x70] = BitConverter.SingleToUInt32Bits(.01f) };

    private sealed record Profile(NativeParticleFamily Family)
    {
        public int[] Presets { get; init; } = [0];
        public bool Burst { get; init; }
        public bool OwnerSelector { get; init; }
        public bool Retire { get; init; }
        public uint? DurationOffset { get; init; }
        public uint? TextureOffset { get; init; }
        public SacredParticleEmissionClock Clock { get; init; } = SacredParticleEmissionClock.CurrentTimeExclusive;
        public string? Description { get; init; }
    }
}
