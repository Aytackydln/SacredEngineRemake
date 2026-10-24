using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Original holylight/holywall geometry, textures and controllers, without combat calculations.</summary>
internal static class NativeSeraphimLightEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x780C70 or 0x784380)) yield break;
        var column = entry.NativeConstructorAddress == 0x780C70;
        var family = new NativeParticleFamily(column ? "Holy Light" : "Holy Wall", entry.FactoryAddress,
            entry.NativeConstructorAddress!.Value, column ? 0x781005u : 0x784620u,
            column ? 0x7816A0u : 0x784C90u, column ? 0x2598 : 0x21C4,
            0x20C4, 0x20E4, 1, 0) { ConstructorRegister = Register.EBX, ColorOffset = 0x20B4 };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var obj = memory.BaseAddress;
        var stack = obj + 0x1F000;
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, stack);
        machine.Registers.Set(Register.EBX, obj); machine.Registers.Set(Register.EBP, 0);
        machine.Run(column ? 0x780D4Bu : 0x7843B8u, column ? 0x781005u : 0x7844CBu, token);
        IReadOnlyList<SacredFxSurfaceVertexLayout> Vertices(int offset, int count) =>
            Array.AsReadOnly(MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(offset, count * 32)).ToArray());
        float F(uint address) => image.Single(address);
        uint Immediate(uint address, int index = 0) => (uint)code.At(address).GetImmediate(index);
        float FloatImmediate(uint address, int index = 0) => BitConverter.UInt32BitsToSingle(Immediate(address, index));
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        var result = entry with { NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, TextureBindings = bindings,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress) };
        if (!column)
        {
            result = result with { RotatingBlades = new(Vertices(0x20B8, 4), Texture(0x21B8), Texture(0x21BC), Texture(0x21C0),
                F(0x890754), F(0x890744), F(0x897FBC), F(0x890998), F(0x897988), F(0x890774), F(0x897FC0), F(0x89073C),
                F(0x890798), F(0x890948), F(0x890718) * F(0x89076C))
                { GroundAdditive = Immediate(0x7846EF) != 0, GroundSourceColorOnly = Immediate(0x7846F1) == 0,
                  TrailSourceColorOnly = Immediate(0x784753) != 0, DefaultActorRadius = Immediate(0x428CFA, 1), RingSamples = checked((int)Immediate(0x784816, 1)), HelixSamples = checked((int)Immediate(0x7848BA, 1)), GroundColor = Immediate(0x7846FC) },
                DisplayName = "Ground ring, twin rising helices and two orbiting spark tips; actor radius and CA duration supplied by caller" };
        }
        else
        {
            var ring = Vertices(0x214C, 26); var edges = Vertices(0x248C, 8);
            var argument = obj + 0x10000; var combat = obj + 0x17000;
            memory.Write(argument + 0x40, 2, 1); memory.Write(argument + 0x70, 4, BitConverter.SingleToUInt32Bits(1));
            memory.Write(argument + 0x74, 4, combat); memory.Write(combat + 0x76, 2, 10);
            memory.Write(stack, 4, obj + 0x1FF00); memory.Write(stack + 4, 4, argument);
            machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(Register.ECX, obj);
            machine.Run(family.Initializer, 0x7817DF, token);
            // The palette is authored after the audio arguments, before the external sound call.
            for (uint offset = 0x20B4; offset < 0x20C4; offset += 4) memory.Write(obj + offset, 4, uint.MaxValue);
            var parameters = NativePresetReader.ReadParameters(memory, family);
            result = result with { CelestialLight = new(ring, edges, Texture(0x258C), Texture(0x2590),
                F(0x89098C), F(0x8907DC), F(0x890820), F(0x890C08), F(0x897EF4), F(0x890D2C),
                FloatImmediate(0x7812EC), FloatImmediate(0x78133C), F(0x890670),
                Immediate(0x7812CE, 1) & 0xFFFFFF, Immediate(0x781332, 1) & 0xFFFFFF, BitConverter.UInt32BitsToSingle((uint)memory.Integer(obj + 0x2148, 4)))
                { FlareAdditive = Immediate(0x7812CC) != 0, FlareSourceColorOnly = Immediate(0x7812C7) == 0 },
                Draw = new(Texture(0x258C), 0x31, 0x7813A6) { GroundAligned = true },
                ParameterSets = parameters, EmitBeforeMovement = false, SingleBirthInitialization = true,
                DisplayName = "Fading untextured light column, edge strips, rotating ground symbol and falling particles" };
        }
        yield return result;
    }
}

