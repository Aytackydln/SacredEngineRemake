using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Particles.Particles;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativePresetReader
{
    public static int ReadSelector(NativeCode code, uint address)
    {
        var first = code.At(address);
        var second = code.At((uint)first.NextIP);
        var third = code.At((uint)second.NextIP);
        var fourth = code.At((uint)third.NextIP);
        if (first.Mnemonic != Mnemonic.Push || first.Op0Kind == OpKind.Register || first.GetImmediate(0) != 0x2C0 ||
            second.Mnemonic != Mnemonic.Push || second.Op0Register != Register.EBX ||
            third.Mnemonic != Mnemonic.Push || third.Op0Register != Register.EBX || fourth.Mnemonic != Mnemonic.Push)
            throw new NotSupportedException($"Unmapped creation argument sequence at 0x{address:X8}.");
        return fourth.Op0Kind == OpKind.Register
            ? fourth.Op0Register == Register.EBX ? 0 : throw new NotSupportedException("Creation selector is not literal.")
            : checked((int)fourth.GetImmediate(0));
    }

    public static IReadOnlyList<SacredParticleParameterSet> Read(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, int preset, SacredParticleQuality quality, CancellationToken cancellationToken)
    {
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        var machine = new X86Machine(code, memory);
        var obj = memory.BaseAddress;
        var argument = obj + 0x10000;
        var stack = obj + 0x1F000;
        var stop = obj + 0x1FF00;
        // Explicit empty live-particle vector; initialization resets this vector after setting parameters.
        memory.Write(obj + 0x68, 4, obj + 0x8000);
        memory.Write(obj + 0x6C, 4, obj + 0x8000);
        memory.Write(argument + 0x38, 4, (uint)preset);
        memory.Write(stack, 4, stop);
        memory.Write(stack + 4, 4, argument);
        machine.Registers.Set(Register.ESP, stack);
        machine.Registers.Set(Register.ECX, obj);
        machine.Run(family.Initializer, stop, cancellationToken);
        if (memory.ReadUninitializedObject)
            throw new NotSupportedException("Initializer depends on unrecovered constructor state.");

        var sets = new List<SacredParticleParameterSet>();
        for (var i = 0; i < family.ParameterSlotCount; i++)
        {
            var emission = family.EmissionOffset + i * SacredParticleEmissionLayout.SerializedSize;
            var motion = family.MotionOffset + i * SacredParticleMotionLayout.SerializedSize;
            if (!memory.IsWritten(emission, SacredParticleEmissionLayout.SerializedSize) ||
                !memory.IsWritten(motion, SacredParticleMotionLayout.SerializedSize)) continue;
            sets.Add(new SacredParticleParameterSet(i,
                MemoryMarshal.Read<SacredParticleEmissionLayout>(memory.ObjectBytes(emission, SacredParticleEmissionLayout.SerializedSize)),
                MemoryMarshal.Read<SacredParticleMotionLayout>(memory.ObjectBytes(motion, SacredParticleMotionLayout.SerializedSize)))
            {
                Colors = ReadColors(memory, family, i)
            });
        }
        if (sets.Count == 0) throw new NotSupportedException("Initializer wrote no complete mapped parameter sets.");
        return sets.AsReadOnly();
    }

    private static IReadOnlyList<uint> ReadColors(PresetMemory memory, NativeParticleFamily family, int index)
    {
        var offset = family.MotionOffset -
                     (family.ParameterSlotCount - index) * SacredParticleColorTableLayout.SerializedSize;
        var size = memory.IsWritten(offset, SacredParticleColorTableLayout.SerializedSize)
            ? SacredParticleColorTableLayout.SerializedSize
            : memory.IsWritten(offset, 16) ? 16 : 0;
        return Array.AsReadOnly(MemoryMarshal.Cast<byte, uint>(memory.ObjectBytes(offset, size)).ToArray());
    }
}
