using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeControlledFlareEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var constructor = entry.NativeConstructorAddress;
        if (constructor is not (0x79E180 or 0x7AFB80 or 0x7A18C0)) yield break;
        var revival = constructor == 0x79E180; var death = constructor == 0x7AFB80;
        var kind = revival ? SacredControlledFlareKind.Revival : death ? SacredControlledFlareKind.DeathCall : SacredControlledFlareKind.ToggleTrap;
        var family = new NativeParticleFamily(revival ? "Live Undead" : death ? "Call of Death" : "Trap01",
            entry.FactoryAddress, constructor.Value, revival ? 0x79E238u : death ? 0x7AFBF3u : 0x7A1933u,
            revival ? 0x79E5D0u : death ? 0x7AFD70u : 0x7A1C40u, 0x20C4, 0, 0, 0, 0)
        {
            InitializerEnd = revival ? 0x79E617u : death ? 0x7AFDC5u : 0x7A1C79u,
            SampleRandomValue = 0,
            InitialState = new Dictionary<int, uint> { [0xAC] = 0 },
            // Explicit standalone event radius; callers supply this field at runtime.
            EventArguments = death ? new Dictionary<int, uint> { [0x2C] = BitConverter.SingleToUInt32Bits(30f) } : null
        };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        float State(int offset) => BitConverter.UInt32BitsToSingle((uint)memory.Integer(memory.BaseAddress + (uint)offset, 4));
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var layers = new List<SacredFxFlareLayer>();
        if (revival)
        {
            layers.Add(new(Texture(0x20B4), Immediate(0x79E350), (uint)code.At(0x79E346).GetImmediate(1), default, 1, false, false));
            layers.Add(new(Texture(0x20BC), Immediate(0x79E391), (uint)code.At(0x79E346).GetImmediate(1), default, Operand(0x79E378), false, false));
            layers.Add(new(Texture(0x20B8), Immediate(0x79E3D1), 0, default, 1, true, true));
            layers.Add(new(Texture(0x20C0), Immediate(0x79E412), 0, default, Operand(0x79E3F9), true, true));
        }
        else layers.Add(new(Texture(death ? 0x20B0u : 0x20B4u), death ? 0 : Operand(0x7A1A85),
            death ? (uint)code.At(0x7AFD38).GetImmediate(1) : 0u, default, 0, !death, true));
        var definition = new SacredControlledFlareFxDefinition(kind, layers.AsReadOnly())
        {
            RiseRate = revival ? Operand(0x79E29F) : 0,
            FallRate = Operand(revival ? 0x79E2D1u : death ? 0x7AFC24u : 0x7A1979u),
            RotationRate = revival ? 2 : death ? 0 : Operand(0x7A1A5E),
            FullAngle = revival ? Operand(0x79E26C) : 0,
            AlphaScale = Operand(revival ? 0x79E30Cu : death ? 0x7AFD13u : 0x7A1A25u),
            InitialWeight = death ? State(0x20A8) : 0,
            InitialRadius = death ? State(0x20AC) : 0,
            RadiusScale = death ? Operand(0x7AFCD3) : 0,
            LayerTimeIncrement = death ? Operand(0x7AFCEC) : 0,
            RotationNumerator = death ? Operand(0x7AFCB3) : 0,
            RadiusAmplitude = revival || death ? 0 : Operand(0x7A1A7B),
            AtlasSide = revival || death ? 1 : (int)code.At(0x7A1A56).GetImmediate(0),
            InitialAtlasCell = revival || death ? 0 : (int)memory.Integer(memory.BaseAddress + 0x20A8, 4),
            ToggleFlag = revival || death ? (ushort)0 : (ushort)code.At(0x7A19A2).GetImmediate(1),
            InitialNotification = revival ? (uint)code.At(0x79E623).GetImmediate(1) : 0,
            ToggleNotification = revival || death ? 0 : (uint)code.At(0x7A19C4).GetImmediate(1)
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = kind != SacredControlledFlareKind.ToggleTrap,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, ControlledFlare = definition,
            TextureBindings = bindings, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = revival ? "Four native revival layers" : death ? "Native layered death flare; standalone radius 30" : "Native toggle trap; sampled atlas cell zero"
        };
    }
}
