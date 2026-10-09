using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Verified standard-generator event families. Addresses describe native implementations;
/// catalogue membership, capacities, palettes, textures and parameters come from Sacred.exe.</summary>
internal static class NativeStandardEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var profile = Find(entry);
        if (profile is null) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = profile.Family with { InitialVectorCount = capacity };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var selectors = profile.SelectorInstruction is { } selector
            ? Enumerable.Range(code.At(selector).Mnemonic == Mnemonic.Dec ? 0 : (int)code.At(selector).GetImmediate(1),
                profile.SelectorCountOperand is { } count ? checked((int)code.At(count).GetImmediate(1) + 2) : 3) : [0];
        foreach (var preset in selectors)
        {
            var memory = NativePresetReader.Evaluate(image, code, family, preset, quality, token);
            var binding = profile.TextureSelector ? bindings[preset] : null;
            yield return entry with
            {
                NativeClass = family.Name, Preset = preset, IsEventPreset = true,
                DisplayName = profile.DisplayName,
                OneTime = !profile.Continuous, BurstOnFirstUpdate = profile.Burst,
                SkipFirstMovement = profile.SkipFirstMovement,
                EmitBeforeMovement = family.EmitBeforeMovement,
                EmissionClock = profile.Clock,
                EmissionDurationSeconds = profile.DurationOperand is { } operand
                    ? image.Single((uint)code.At(operand).MemoryDisplacement64)
                    : profile.DurationStateOffset is { } stateOffset
                        ? BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + (uint)stateOffset, 4))
                    : family.EventArguments?.TryGetValue(0x48, out var duration) == true
                        ? BitConverter.UInt32BitsToSingle(duration) : null,
                SingleBirthInitialization = !profile.Burst,
                RequiresActorContext = (memory.Integer(memory.BaseAddress + 0x7C, 4) & 0x1200) != 0,
                Capacity = capacity, Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null,
                TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
                Draw = NativeTextureReader.ReadDraw(image, code, family, preset, bindings,
                    binding ?? (bindings.Count == 1 ? bindings[0] : null)) with
                    { HalfSizeMultiplier = profile.DrawScaleOperand is { } scale
                        ? BitConverter.UInt32BitsToSingle((uint)code.At(scale).GetImmediate(0)) : 1 },
                OrbitEmission = family.Constructor == 0x7AB500 ? new(
                    BitConverter.UInt32BitsToSingle((uint)code.At(0x7AB5D5).GetImmediate(1)),
                    image.Single((uint)code.At(0x7AB666).MemoryDisplacement64),
                    image.Single((uint)code.At(0x7AB5AE).MemoryDisplacement64), 0x7AB590) : null,
                Halo = ReadHalo(image, code, family, bindings)
            };
        }
    }

    private static Profile? Find(SacredParticleDefinition entry)
    {
        var factory = entry.FactoryAddress;
        return entry.NativeConstructorAddress switch
        {
            0x76B430 => new(new("cParticleSystem_fireball", factory, 0x76B430, 0x76B4A0,
                0x76B5F0, 0x20A0 + SacredFireballParticleStateLayout.SerializedSize + 4, 0x24B0, 0x24D0, 1, 0x76B540)
                { ColorOffset = 0x20A4, SelectorOffset = 0x3C, EmitBeforeMovement = false })
                { Continuous = true, SelectorInstruction = 0x76B600, SelectorCountOperand = 0x76B601,
                    DisplayName = "Native fireball event palette; actor attachment" },
            0x78E4C0 => new(new("cParticleSystem_aracattack", factory, 0x78E4C0, 0x78E530,
                0x78E630, 0x20A0 + SacredStandardEmitterParticleStateLayout.SerializedSize + 4, 0x24A8, 0x24C8, 1, 0x78E5C0)
                { ColorOffset = 0x20A8, DrawRegister = Register.EBP }) { Burst = true },
            0x7A6510 => new(new("cParticleSystem_laserbeam_impact", factory, 0x7A6510, 0x7A65D2,
                0x7A68A0, 0x20A0 + SacredStandardEmitterParticleStateLayout.SerializedSize + 4, 0x24A8, 0x24C8, 1, 0x7A6660)
                { ColorOffset = 0x20A8 }) { Burst = true, SkipFirstMovement = true },
            0x7B9020 => new(new("Dwarf Shoot Impact", factory, 0x7B9020, 0x7B9090,
                0x7B9180, 0x20A0 + SacredDwarfShootImpactParticleStateLayout.SerializedSize + 4, 0x24A8, 0x24C8, 1, 0x7B9110)
                { ColorOffset = 0x20A8, SelectorOffset = 0x3C, EmitBeforeMovement = false,
                    DrawRegister = Register.EBP })
                { Burst = true, SelectorInstruction = 0x7B92C7, DisplayName = "Native impact palette; zero incoming velocity" },
            0x791010 => new(new("cParticleSystem_elve_sm_hit", factory, 0x791010, 0x791097,
                0x791410, 0x20A0 + SacredElveSmHitParticleStateLayout.SerializedSize + 8, 0x24B0, 0x24D0, 1, 0x791163)
                { ColorOffset = 0x20B0, SelectorOffset = 0x3C })
                { Burst = true, DisplayName = "Native standard hit event (subtype zero)" },
            0x7AAFB0 => new(new("cParticleSystem_dragon_groundfire", factory, 0x7AAFB0, 0x7AB04E,
                0x7AB1A0, 0x20A0 + SacredDragonGroundFireParticleStateLayout.SerializedSize + 12, 0x24A8, 0x24C8, 1, 0x7AB15D)
                { ColorOffset = 0x20A8, EmitBeforeMovement = false })
                { DurationOperand = 0x7AB094, Clock = SacredParticleEmissionClock.PreviousTimeExclusive,
                    TextureSelector = true, SelectorInstruction = 0x7AB0ED },
            0x7AEAF0 => Wall(factory),
            0x7AA980 => new(new("cParticleSystem_shaddar_magic", factory, 0x7AA980, 0x7AAA42,
                0x7AAC20, 0x20A0 + SacredShaddarMagicParticleStateLayout.SerializedSize + 4, 0x24B0, 0x24D0, 1, 0x7AAB90)
                { ColorOffset = 0x20B0, EmitBeforeMovement = false })
                { DurationStateOffset = 0x20A4, DisplayName = "Native timed projectile (normal release)" },
            0x7A5380 => new(new("cParticleSystem_batsignal", factory, 0x7A5380, 0x7A53F0,
                0x7A54D0, 0x20A0 + SacredStandardEmitterParticleStateLayout.SerializedSize + 4, 0x24A8, 0x24C8, 1, 0x7A5460)
                { ColorOffset = 0x20A8, SelectorOffset = 0x3C, EmitBeforeMovement = false })
                { Continuous = true, SelectorInstruction = 0x7A54DE, DisplayName = "Native combat-art palette; actor attachment" },
            0x774F00 => new(new("cParticleSystem_torchsmoke", factory, 0x774F00, 0x774FC2,
                0x7752B0, 0x20A0 + SacredTorchSmokeParticleStateLayout.SerializedSize + 4, 0x20BC, 0x20DC, 1, 0x77506D)
                { ColorOffset = 0x20AC, EmitBeforeMovement = false, InitializerEnd = 0x7753C4,
                    ActorLookup = (0x77538D, 0x775398) })
                { Continuous = true, DisplayName = "Native torch particles and fixed flare; actor attachment" },
            0x7A13C0 => new(new("cParticleSystem_changeling_cast", factory, 0x7A13C0, 0x7A1430,
                0x7A1750, 0x2530, 0x24A8, 0x24C8, 1, 0x7A1565)
                { ColorOffset = 0x20A8, EmitBeforeMovement = false })
                { DurationOperand = 0x7A1483, Clock = SacredParticleEmissionClock.CurrentTimeExclusive },
            0x7AB500 => new(new("cParticleSystem_reanimate", factory, 0x7AB500, 0x7AB590,
                0x7AB6F0, 0x20A0 + SacredReanimateParticleStateLayout.SerializedSize + 4,
                0x24AC, 0x24CC, 1, 0x7AB680)
                { ColorOffset = 0x20AC, EmitBeforeMovement = false, DrawRegister = Register.EBP })
                { DrawScaleOperand = 0x7AB6B5, Clock = SacredParticleEmissionClock.PreviousTimeExclusive,
                    DisplayName = "Native reanimate orbit and fade palette" },
            _ => null
        };
    }

    private static SacredParticleHaloDefinition? ReadHalo(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        if (family.Constructor == 0x76B430)
        {
            if (code.At(0x76B4E4).Mnemonic != Mnemonic.Fsin)
                throw new NotSupportedException("Unmapped fireball flare pulse.");
            // stdFlare selector zero, opaque white, attachment zero. The size is read
            // from the updater's rounded pulse before the renderer's multiply/add.
            return new(NativeTextureReader.ReadFlareTexture(code, 0),
                image.Single((uint)code.At(0x76B5BE).MemoryDisplacement64), (uint)code.At(0x76B5C4).GetImmediate(0), 0x76B5DF)
            {
                SourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, 0),
                AttachmentIndex = checked((int)code.At(0x76B5BC).GetImmediate(0)),
                PulseAngularFrequency = 2,
                PulseAmplitude = image.Single((uint)code.At(0x76B5B0).MemoryDisplacement64)
            };
        }
        if (family.Constructor != 0x7A13C0) return NativeTextureReader.ReadHalo(code, family, bindings);
        // Native lensflare fades after one second and is no longer drawn at 1.5 seconds.
        return new(bindings.Single().TextureName, BitConverter.UInt32BitsToSingle(code.At(0x7A1557).Immediate32),
            code.At(0x7A151A).Immediate32, 0x7A1560)
        {
            FadeStartSeconds = image.Single((uint)code.At(0x7A150D).MemoryDisplacement64),
            FadeEndSeconds = image.Single((uint)code.At(0x7A14FA).MemoryDisplacement64),
            FadeAlphaScale = image.Single((uint)code.At(0x7A1535).MemoryDisplacement64)
        };
    }

    private static Profile Wall(uint factory) => new(new("Wall of Fire Line", factory,
        0x7AEAF0, 0x7AEB60, 0x7AEC60, 0x20A0 + SacredWallOfFireLineParticleStateLayout.SerializedSize + 4, 0x24AC, 0x24CC, 1, 0x7AEBF0)
        { ColorOffset = 0x20AC, EmitBeforeMovement = false, DrawRegister = Register.EBP,
            EventArguments = new Dictionary<int, uint> { [0x48] = 0x3F800000 } })
        { Clock = SacredParticleEmissionClock.PreviousTimeExclusive, DisplayName = "Native duration event (one-second preview)" };

    private sealed record Profile(NativeParticleFamily Family)
    {
        public bool Burst { get; init; }
        public bool Continuous { get; init; }
        public bool SkipFirstMovement { get; init; }
        public bool TextureSelector { get; init; }
        public uint? SelectorInstruction { get; init; }
        public uint? SelectorCountOperand { get; init; }
        public uint? DurationOperand { get; init; }
        public int? DurationStateOffset { get; init; }
        public uint? DrawScaleOperand { get; init; }
        public SacredParticleEmissionClock Clock { get; init; }
        public string? DisplayName { get; init; }
    }
}
