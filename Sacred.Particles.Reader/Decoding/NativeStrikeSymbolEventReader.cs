using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeStrikeSymbolEventReader
{
    public static IEnumerable<SacredParticleDefinition> Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress is not (0x7851B0 or 0x788180)) yield break;
        var speed = entry.NativeConstructorAddress == 0x788180;
        var name = speed ? "Speed Up" : "Crusader Strength";
        var family = new NativeParticleFamily(name, entry.FactoryAddress, entry.NativeConstructorAddress!.Value,
            speed ? 0x7883AAu : 0x7853C3u, speed ? 0x7887E0u : 0x785800u, 0x2160, 0, 0, 0, 0)
            { ConstructorRegister = Register.EBX };
        var bindings = NativeTextureReader.ReadBindings(image, code, family);
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var machine = new X86Machine(code, memory);
        machine.Registers.Set(Register.ESP, memory.BaseAddress + 0x1F000);
        machine.Registers.Set(Register.EBX, memory.BaseAddress);
        machine.Run(speed ? 0x7881B7u : 0x7851E7u, speed ? 0x7882CDu : 0x7852FDu, token);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(speed ? 0x20B4 : 0x20B0, 128)).ToArray();
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        uint Value(uint address) => (uint)code.At(address).GetImmediate(1);
        var baseOffset = speed ? 0x2134u : 0x2130u;
        var textures = Enumerable.Range(0, 8).Select(i => bindings.Single(b => b.NativeHandleOffset == baseOffset + i * 4).TextureName).ToArray();
        var selector = (uint)code.At(speed ? 0x7885D2u : 0x7855EFu).GetImmediate(0);
        var d = new SacredStrikeSymbolFxDefinition(Array.AsReadOnly(textures), Array.AsReadOnly(vertices),
            NativeTextureReader.ReadFlareTexture(code, selector), speed ? bindings.Single(b => b.NativeHandleOffset == 0x2154).TextureName
                : image.String(code.At(0x785400).Immediate32, 128))
        {
            UsesElapsedRate = speed, InitialElapsedRate = 1, // Explicit standalone event +0x20 for Speed Up.
            Lifetime = Operand(speed ? 0x7883D9u : 0x7853F3u), FrameInterval = Operand(speed ? 0x788417u : 0x78543Cu),
            FlareHalfSize = Immediate(speed ? 0x7885D4u : 0x7855F1u), FlareColor = (uint)code.At(speed ? 0x7885CDu : 0x7855EDu).GetImmediate(0),
            FlareOffset = new(0, 0, BitConverter.UInt32BitsToSingle(Value(speed ? 0x7885FFu : 0x78561Cu))),
            FlareSourceColorOnly = NativeTextureReader.ReadFlareSourceBlend(code, selector),
            StartNotification = Value(speed ? 0x78881Du : 0x785830u), LightStrength = checked((int)Value(speed ? 0x788824u : 0x785837u)),
            FinishNotification = Value(speed ? 0x7883FCu : 0x785428u), FinishArgument = Value(speed ? 0x7883ECu : 0x78540Cu)
        };
        yield return entry with { NativeClass = name, Preset = 0, IsEventPreset = true, OneTime = true,
            Status = SacredParticleDefinitionStatus.Decoded, Diagnostic = null, StrikeSymbol = d, TextureBindings = bindings,
            Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = speed ? "Native eight-state strike sprite and flare; timer rate 1"
                : "Native eight-state strike sprite and flare" };
    }
}
