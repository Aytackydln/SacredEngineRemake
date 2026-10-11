using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeRingShieldEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x785860 or 0x78A8A0)) yield break;
        var holy = entry.NativeConstructorAddress == 0x785860;
        var family = new NativeParticleFamily(holy ? "Holy Shield" : "Magic Shield", entry.FactoryAddress,
            entry.NativeConstructorAddress!.Value, holy ? 0x785943u : 0x78AADAu,
            holy ? 0x785DE0u : 0x78B040u, 0x2904, 0, 0, 0, 0)
            { ConstructorRegister = holy ? Register.ESI : Register.EBX };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var obj = memory.BaseAddress; var stack = obj + 0x1F000;
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(holy ? Register.ESI : Register.EBX, obj);
        machine.Run(holy ? 0x7858D1u : 0x78A92Cu, holy ? 0x785943u : 0x78AAC8u, token);
        var ringCount = checked((int)code.At(holy ? 0x7858EEu : 0x78A955u).GetImmediate(1) * 2);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(holy ? 0x20B4 : 0x20B8, ringCount * 32)).ToArray();
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        uint Value(uint address) => (uint)code.At(address).GetImmediate(1);
        string Texture(uint offset) => bindings.Single(b => b.NativeHandleOffset == offset).TextureName;
        // Explicit standalone combat-art parameter zero base 1000, level one, zero increments/cap, multiplier .01.
        var argument = obj + 0x10000; var combat = obj + 0x17000;
        memory.Write(argument + 0x40, 2, 1); memory.Write(argument + 0x70, 4, BitConverter.SingleToUInt32Bits(.01f));
        memory.Write(argument + 0x74, 4, combat); memory.Write(combat + 0x76, 2, 1000);
        memory.Write(stack, 4, obj + 0x1FF00); memory.Write(stack + 4, 4, argument);
        machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(Register.ECX, obj);
        machine.Run(family.Initializer, holy ? 0x785E51u : 0x78B0A4u, token);
        var d = new SacredRingShieldFxDefinition(Array.AsReadOnly(vertices), Texture(holy ? 0x2874u : 0x28F8u), Texture(holy ? 0x2878u : 0x28FCu))
        {
            AngleStep = Operand(holy ? 0x7858F7u : 0x78A95Eu), Radius = Operand(holy ? 0x785901u : 0x78A968u),
            InitialLifetime = BitConverter.UInt32BitsToSingle((uint)memory.Integer(obj + (holy ? 0x20B0u : 0x20B4u), 4)),
            StartNotification = Value(holy ? 0x785EBDu : 0x78B12Cu), LightStrength = checked((int)Value(holy ? 0x785EC4u : 0x78B146u)),
            Holy = holy ? new(Operand(0x78599B), Operand(0x7859BA), checked((int)Value(0x785A08)), Operand(0x7859D9),
                Operand(0x785AB0), checked((int)Value(0x785ABE)), checked((int)Value(0x785B10)),
                Immediate(0x785B69), Operand(0x785B3C), Operand(0x785B4A), Operand(0x785B5C), code.At(0x785B48).GetImmediate(0) == 0) : null,
            Magic = holy ? null : new(Operand(0x78AB9C), Operand(0x78ABAE), BitConverter.UInt32BitsToSingle(Value(0x78ABEC)), Operand(0x78AB82),
                Operand(0x78ACCE), Operand(0x78ACC8), Operand(0x78AD9C), Operand(0x78AD90), Operand(0x78AD6E), Operand(0x78AE15)),
            MarkerTexture = holy ? null : Texture(0x2900), MarkerVertices = holy ? [] :
                Array.AsReadOnly(MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x2878, 128)).ToArray())
        };
        yield return entry with { NativeClass = family.Name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, RingShield = d, TextureBindings = bindings,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = holy ? "Rotating shield trail and ground symbol; hit flash" : "Growing rune shield, ground markers and two pulsing layers; hit flash" };
    }
}
