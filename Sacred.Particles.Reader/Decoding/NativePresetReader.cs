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
        var memory = Evaluate(image, code, family, preset, quality, cancellationToken);
        return ReadParameters(memory, family);
    }

    internal static PresetMemory Evaluate(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, int preset, SacredParticleQuality quality, CancellationToken cancellationToken)
    {
        var memory = new PresetMemory(image, family.ObjectSize, quality);
        uint? environment = null;
        if (family.SampleDefaultEnvironment)
        {
            environment = memory.BaseAddress + 0x18000;
            memory.Write(environment.Value + 0x68, 4, image.UInt32(0x418F8A));
            memory.Write(environment.Value + 0x6C, 4, 0);
            memory.Write(environment.Value + 0x70, 4, 0);
            memory.Write(environment.Value + 0x74, 4, image.UInt32(0x418F83));
        }
        var machine = new X86Machine(code, memory) { EnvironmentAddress = environment, RandomValue = family.SampleRandomValue };
        var obj = memory.BaseAddress;
        var argument = obj + 0x10000;
        var stack = obj + 0x1F000;
        var stop = obj + 0x1FF00;
        // Most presets use an empty vector. Some read its allocated count as their burst size.
        memory.Write(obj + 0x68, 4, obj + 0x8000);
        memory.Write(obj + 0x6C, 4, obj + 0x8000 + checked((uint)family.InitialVectorCount * 0x40));
        if (family.InitialFlags is { } flags) memory.Write(obj + 0x7C, 4, flags);
        memory.Write(argument + (uint)family.SelectorOffset, 4, (uint)preset);
        if (family.EventColor is { } color) memory.Write(argument + 0x38, 4, color);
        if (family.EventArguments is { } arguments)
            foreach (var (offset, value) in arguments) memory.Write(argument + (uint)offset, 4, value);
        memory.Write(stack, 4, stop);
        memory.Write(stack + 4, 4, argument);
        machine.Registers.Set(Register.ESP, stack);
        machine.Registers.Set(Register.ECX, obj);
        if (family.ActorLookup is { } lookup)
        {
            // This omitted region resolves identity/geometry/audio metadata without parameter writes.
            // Playback supplies the real mesh. Evaluation stops before any stack epilogue
            // that would consume arguments discarded with a native lookup call.
            machine.Run(family.Initializer, lookup.Start, cancellationToken);
            if (family.UnitActorRadius) machine.Registers.Set(Register.EAX, 1);
            machine.Run(lookup.End, family.InitializerEnd ?? stop, cancellationToken);
        }
        else machine.Run(family.Initializer, family.InitializerEnd ?? stop, cancellationToken);
        if (memory.ReadUninitializedObject)
            throw new NotSupportedException("Initializer depends on unrecovered constructor state.");
        return memory;
    }

    internal static IReadOnlyList<SacredParticleParameterSet> ReadParameters(PresetMemory memory, NativeParticleFamily family)
    {
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
        var offset = family.ColorOffset is { } colorOffset ? colorOffset + index * family.ColorSlotStride : family.MotionOffset -
                     (family.ParameterSlotCount - index) * SacredParticleColorTableLayout.SerializedSize;
        var size = memory.IsWritten(offset, SacredParticleColorTableLayout.SerializedSize)
            ? SacredParticleColorTableLayout.SerializedSize
            : memory.IsWritten(offset, 16) ? 16 : 0;
        return Array.AsReadOnly(MemoryMarshal.Cast<byte, uint>(memory.ObjectBytes(offset, size)).ToArray());
    }
}
