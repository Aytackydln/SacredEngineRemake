using System.Runtime.InteropServices;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeExpandingBurstEventReader
{
    // An explicit standalone event input, rather than a combat caller default.
    private const uint PreviewRadiusRate = 150;
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var kamikaze = entry.NativeConstructorAddress == 0x7C16D0;
        if (!kamikaze && entry.NativeConstructorAddress != 0x7AF490) yield break;
        var capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress);
        var family = new NativeParticleFamily(kamikaze ? "Kamikaze" : "Ring of Poison", entry.FactoryAddress,
            kamikaze ? 0x7C16D0u : 0x7AF490u, kamikaze ? 0x7C176Fu : 0x7AF52Fu,
            kamikaze ? 0x7C1AB0u : 0x7AF840u, kamikaze ? 0x253C : 0x2538,
            0x24A8, 0x24C8, 1, kamikaze ? 0x7C1A79u : 0x7AF802u)
        {
            InitialVectorCount = capacity,
            EventArguments = kamikaze ? new Dictionary<int, uint> { [0x44] = PreviewRadiusRate } : null
        };
        var memory = NativePresetReader.Evaluate(image, code, family, 0, quality, token);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        uint Immediate(uint address, int operand = 0) => (uint)code.At(address).GetImmediate(operand);
        var colorScale = Operand(kamikaze ? 0x7C192Au : 0x7AF6B6u);
        var otherScale = Operand(kamikaze ? 0x7C1953u : 0x7AF6DBu);
        var surfaces = new SacredExpandingBurstFxDefinition(Texture(0x252C), Texture(0x2534), NativeFxRingReader.Read(image, code, quality, token, false))
        {
            InitialState = MemoryMarshal.Read<SacredExpandingBurstFxStateLayout>(memory.ObjectBytes(0x20A0, SacredExpandingBurstFxStateLayout.SerializedSize)),
            Lifetime = Operand(kamikaze ? 0x7C185Eu : 0x7AF61Eu),
            RingDuration = Operand(kamikaze ? 0x7C18BEu : 0x7AF65Eu),
            RingFadeStart = Operand(kamikaze ? 0x7C18FBu : 0x7AF687u),
            RingFadeRate = Operand(kamikaze ? 0x7C191Cu : 0x7AF6A8u),
            RadiusRate = kamikaze ? 0 : Operand(0x7AF677), EventRadiusRate = kamikaze ? PreviewRadiusRate : null,
            RingWidth = Operand(kamikaze ? 0x7C196Du : 0x7AF6F5u),
            RingColorScale = kamikaze ? new(colorScale, colorScale, otherScale) : new(colorScale, otherScale, colorScale),
            SegmentCount = (int)Immediate(kamikaze ? 0x7C1951u : 0x7AF6D9u),
            GroundTimeRate = Operand(kamikaze ? 0x7C19BCu : 0x7AF73Du),
            GroundSizes = new(Operand(kamikaze ? 0x7C19FEu : 0x7AF786u), Operand(kamikaze ? 0x7C1A33u : 0x7AF7BEu)),
            GroundRotations = new(Operand(kamikaze ? 0x7C19F1u : 0x7AF779u), Operand(kamikaze ? 0x7C1A1Cu : 0x7AF7A4u)),
            GroundColor = Immediate(kamikaze ? 0x7C1A04u : 0x7AF78Cu),
            RenderRadiusDecay = kamikaze ? BitConverter.ToDouble(image.Slice(0x891088, 8)) : 1,
            InitialNotification = BitConverter.ToUInt32(memory.ObjectBytes(0x7C, 4)), InitialLightStrength = BitConverter.ToUInt32(memory.ObjectBytes(0x9C, 4))
        };
        yield return entry with
        {
            NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            BurstOnFirstUpdate = true, EmitBeforeMovement = true, Capacity = capacity,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, ExpandingBurst = surfaces,
            TextureBindings = bindings, ParameterSets = NativePresetReader.ReadParameters(memory, family),
            Draw = NativeTextureReader.ReadDraw(image, code, family, 0, bindings, bindings.Single(b => b.NativeHandleOffset == 0x2530)),
            EmissionDurationSeconds = surfaces.Lifetime,
            DisplayName = kamikaze ? "Particle burst, expanding ring and two ground flares; standalone event +0x44 = 150" : "Particle burst, expanding poison ring and two ground flares"
        };
    }
}
