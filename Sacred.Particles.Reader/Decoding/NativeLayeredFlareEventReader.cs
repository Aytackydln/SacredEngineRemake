using Iced.Intel;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Three independent native classes sharing the timed layered ground-flare implementation.</summary>
internal static class NativeLayeredFlareEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        var constructor = entry.NativeConstructorAddress;
        if (constructor is not (0x78B160 or 0x78B5B0 or 0x784DD0)) yield break;
        var mana = constructor == 0x78B160;
        var light = constructor == 0x784DD0;
        var name = light ? "MagLite" : mana ? "Mana Regeneration" : "HP Regeneration";
        var end = light ? 0x784E40u : mana ? 0x78B1EDu : 0x78B637u;
        var init = light ? 0x7850D0u : mana ? 0x78B4B0u : 0x78B900u;
        var stop = light ? 0x785132u : mana ? 0x78B550u : 0x78B9B1u;
        var family = new NativeParticleFamily(name, entry.FactoryAddress, constructor!.Value, end, init,
            0x2138, 0, 0, 0, 0);
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var obj = memory.BaseAddress;
        var argument = obj + 0x10000;
        var combat = obj + 0x17000;
        var stack = obj + 0x1F000;
        // Explicit standalone inputs: duration units/base 1000; parameter one base 200;
        // combat-art level 1 with zero increments/caps. Native runtime callers remain separate.
        memory.Write(argument + 0x3C, 4, 1000);
        memory.Write(argument + 0x40, 2, 1);
        memory.Write(argument + 0x70, 4, BitConverter.SingleToUInt32Bits(Operand(0x78B4FA)));
        memory.Write(argument + 0x74, 4, combat);
        memory.Write(combat + 0x76, 2, 1000); memory.Write(combat + 0x7A, 2, 200);
        memory.Write(stack, 4, obj + 0x1FF00); memory.Write(stack + 4, 4, argument);
        for (uint offset = 0; offset < 16; offset += 4) memory.Write(obj + 0x20A0 + offset, 4, 0);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ECX, obj); machine.Registers.Set(Register.ESP, stack);
        machine.Run(init, stop, token);
        if (light) memory.Write(obj + 0x20AC, 4, machine.Registers.Get(Register.EAX));
        var layers = new List<SacredFxFlareLayer>();
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        if (light)
        {
            var count = checked((int)code.At(0x784F12).GetImmediate(1));
            var origin = unchecked((int)(uint)code.At(0x784ECD).MemoryDisplacement64);
            for (var index = 0; index < count; index++)
            {
                var height = (index + origin) * (double)Operand(0x784EE6);
                layers.Add(new(Texture(0x2130), (float)Math.Sqrt(Operand(0x784EFF) - height * height),
                    (uint)code.At(0x784EEF).GetImmediate(0), new(0, 0, (float)height), 0,
                    code.At(0x784EDE).GetImmediate(0) == 0, code.At(0x784EDC).GetImmediate(0) != 0));
            }
        }
        else
        {
            layers.Add(new(Texture(0x2134), Immediate(mana ? 0x78B271u : 0x78B6C1u),
                (uint)code.At(mana ? 0x78B26Fu : 0x78B6BFu).GetImmediate(0), default,
                Operand(mana ? 0x78B253u : 0x78B6A3u),
                code.At(mana ? 0x78B25Fu : 0x78B6AFu).GetImmediate(0) == 0,
                code.At(mana ? 0x78B25Du : 0x78B6ADu).GetImmediate(0) != 0));
            var count = checked((int)code.At(mana ? 0x78B2F1u : 0x78B741u).GetImmediate(1));
            for (var index = 0; index < count; index++)
                layers.Add(new(Texture(0x2130), (float)(Operand(mana ? 0x78B2E0u : 0x78B730u) - index * (double)Operand(mana ? 0x78B2D8u : 0x78B728u)),
                    (uint)code.At(mana ? 0x78B2D2u : 0x78B722u).GetImmediate(0),
                    new(0, 0, (float)(index * (double)Operand(mana ? 0x78B2BFu : 0x78B70Fu))), 0,
                    code.At(mana ? 0x78B2BBu : 0x78B70Bu).GetImmediate(0) == 0,
                    code.At(mana ? 0x78B2B9u : 0x78B709u).GetImmediate(0) != 0));
        }
        var definition = new SacredLayeredFlareFxDefinition(layers.AsReadOnly())
        {
            InitialLifetime = BitConverter.UInt32BitsToSingle((uint)memory.Integer(obj + 0x20A8, 4)),
            InitialAuxiliary = (uint)memory.Integer(obj + 0x20AC, 4), DurationScale = Operand(0x78B4FA),
            StartNotification = (uint)code.At(light ? 0x78518Fu : mana ? 0x78B563u : 0x78B9BDu).GetImmediate(1),
            CanExtendDuration = mana
        };
        yield return entry with { NativeClass = name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, LayeredFlare = definition,
            TextureBindings = bindings, DisplayName = "Native timed ground-flare layers; explicit standalone duration inputs",
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress) };
    }
}
