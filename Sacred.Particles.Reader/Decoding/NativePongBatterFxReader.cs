using System.Numerics;
using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativePongBatterFxReader
{
    public static SacredParticleDefinition Read(SacredExecutableImage image, NativeCode code,
        SacredParticleDefinition entry, SacredParticleQuality quality, CancellationToken token)
    {
        if (entry.NativeConstructorAddress != 0x7BBD90) return entry;
        var memory = new PresetMemory(image, 0x2590, quality);
        var machine = new X86Machine(code, memory);
        var creation = machine.ResolveDispatch(SacredGoldExecutableProfile.CreationDispatchAddress, entry.TypeId);
        var selector = NativePresetReader.ReadSelector(code, creation);
        var obj = memory.BaseAddress;
        var stack = obj + 0x1F000;
        machine.Registers.Set(Register.ESP, stack);
        machine.Registers.Set(Register.EBX, obj);
        machine.Registers.Set(Register.ESI, obj + 0x20B0);
        machine.Registers.Set(Register.EBP, 0);
        machine.Run(0x7BBDF4, 0x7BCB99, token);
        var vertices = MemoryMarshal.Cast<byte, SacredFxSurfaceVertexLayout>(memory.ObjectBytes(0x2110, 36 * 32)).ToArray();
        var edges = ReadEdges(code, memory, token);
        float Operand(uint address) => image.Single((uint)code.At(address).MemoryDisplacement64);
        float Immediate(uint address) => BitConverter.UInt32BitsToSingle((uint)code.At(address).GetImmediate(0));
        var definition = new SacredPongBatterFxDefinition(image.String(code.At(0x7BBDCA).Immediate32, 128),
            image.String(code.At(0x401341).Immediate32, 128), Array.AsReadOnly(vertices), edges)
        {
            ComputerControlled = selector != 0,
            TextureScale = Immediate(0x7BCDB3), UScrollRate = Operand(0x7BCDC8), VScrollRate = Operand(0x7BCDFD),
            EdgeSizeOrigin = Operand(0x7BCC8A), EdgeSizeWave = Operand(0x7BCC76), EdgeSizeHit = Operand(0x7BCC82),
            EdgeDensityOrigin = Operand(0x7BCCA0), EdgeDensityHit = Operand(0x7BCC9A),
            SurfaceColorOrigin = Operand(0x7BCCB6), SurfaceColorWave = Operand(0x7BCCB0),
            HitFlashReset = BitConverter.UInt32BitsToSingle((uint)code.At(0x7BCC3E).GetImmediate(1)),
            EdgeColor = code.At(0x7BCEB0).Immediate32
        };
        return entry with { NativeClass = "Pong Batter", Preset = selector, Status = SacredParticleDefinitionStatus.Decoded,
            Diagnostic = null, PongBatter = definition, Capacity = NativeParticleCapacityReader.Read(code, entry.FactoryAddress),
            DisplayName = "Native Pong paddle surface, scrolling texture and twelve glow lines",
            TextureBindings = Array.AsReadOnly<SacredParticleTextureBinding>([new(0x20AC, definition.SurfaceTexture)]) };
    }

    private static IReadOnlyList<SacredFxBeamSegment> ReadEdges(NativeCode code, PresetMemory memory, CancellationToken token)
    {
        // Execute the original endpoint assembly. Resource/device calls are boundaries;
        // the unchanged vector-add helper and stack copies supply each actual beam argument.
        var machine = new X86Machine(code, memory);
        var stack = memory.BaseAddress + 0x1F000;
        machine.Registers.Set(Register.ESP, stack);
        machine.Registers.Set(Register.EBP, memory.BaseAddress);
        machine.Registers.Set(Register.EBX, memory.BaseAddress + 8);
        var start = 0x7BCE6Bu;
        var edges = new List<SacredFxBeamSegment>();
        for (var ip = start; ip < 0x7BD2B0;)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic != Mnemonic.Call || instruction.NearBranch32 != 0x401660) continue;
            machine.Run(start, (uint)instruction.IP, token);
            machine.Registers.Set(Register.EAX, 0); // Singleton return is only consumed by the intercepted draw.
            var helper = code.At(ip);
            machine.Run(ip, (uint)helper.NextIP, token); // mov ecx,eax
            var draw = code.At((uint)helper.NextIP);
            if (draw.Mnemonic != Mnemonic.Call || draw.NearBranch32 != 0x40DA80)
                throw new NotSupportedException("Unmapped Pong edge submission.");
            var arguments = machine.Registers.Get(Register.ESP);
            var first = MemoryMarshal.Read<Vector3>(memory.Read(arguments + 19 * 4, 12));
            var second = MemoryMarshal.Read<Vector3>(memory.Read(arguments + 22 * 4, 12));
            edges.Add(new(first, second));
            machine.Registers.Set(Register.ESP, arguments + 0x6C);
            start = (uint)draw.NextIP;
        }
        if (edges.Count != 12) throw new NotSupportedException("Incomplete Pong edge geometry.");
        return edges.AsReadOnly();
    }
}
