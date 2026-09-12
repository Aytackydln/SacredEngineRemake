using Iced.Intel;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeTextureReader
{
    public static IReadOnlyList<SacredParticleTextureBinding> ReadBindings(
        SacredExecutableImage image, NativeCode code, NativeParticleFamily family)
    {
        var bindings = new List<SacredParticleTextureBinding>();
        string? pendingName = null;
        string? resolvedName = null;
        var calls = 0;
        for (var ip = family.Constructor; ip < family.ConstructorEnd;)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind == OpKind.Immediate32)
            {
                var pointer = instruction.Immediate32;
                if (pointer >= SacredGoldExecutableProfile.ImageBase && pointer + 128 < image.EndAddress)
                {
                    var bytes = image.Slice(pointer, 128);
                    var terminator = bytes.IndexOf((byte)0);
                    if (terminator > 0)
                    {
                        var name = image.String(pointer, 128);
                        if (name.EndsWith(".TGA", StringComparison.OrdinalIgnoreCase))
                        {
                            pendingName = name;
                            calls = 0;
                        }
                    }
                }
            }
            if (pendingName != null && instruction.Mnemonic == Mnemonic.Call && ++calls == 2)
            {
                resolvedName = pendingName;
                pendingName = null;
            }
            // The compiler can push the next texture name before storing the previous handle.
            if (resolvedName != null && instruction.Mnemonic == Mnemonic.Mov && instruction.Op0Kind == OpKind.Memory &&
                instruction.MemoryBase == Register.ESI && instruction.Op1Register == Register.EAX)
            {
                bindings.Add(new SacredParticleTextureBinding((uint)instruction.MemoryDisplacement64, resolvedName));
                resolvedName = null;
            }
        }
        if (bindings.Count == 0) throw new NotSupportedException("No verified constructor texture lookup was found.");
        return bindings.AsReadOnly();
    }

    public static SacredParticleDrawDefinition ReadDraw(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, int preset, IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        var address = family.DrawAddress;
        if (family.ParameterSlotCount > 1)
        {
            var machine = new X86Machine(code, new PresetMemory(image, 0, SacredParticleQuality.High));
            address = machine.ResolveDispatch(address, (uint)preset, instruction =>
                IsTextureRead(instruction, bindings) || instruction.Mnemonic is Mnemonic.Pop or Mnemonic.Ret);
        }
        string? texture = null;
        uint? flags = null;
        uint? atlasSide = null;
        var ip = address;
        for (var i = 0; i < 120; i++)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (IsTextureRead(instruction, bindings))
                texture = bindings.Single(b => b.NativeHandleOffset == instruction.MemoryDisplacement64).TextureName;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind != OpKind.Register && instruction.Op0Kind != OpKind.Memory)
            {
                atlasSide = flags;
                flags = (uint)instruction.GetImmediate(0);
            }
            if (instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Ret) break;
        }
        if (texture == null || flags == null) throw new NotSupportedException("Native texture/draw arguments remain unmapped.");
        if (atlasSide is not (1 or 2)) throw new NotSupportedException("Unmapped native particle atlas dimensions.");
        return new SacredParticleDrawDefinition(texture, flags.Value, address) { AtlasSide = (int)atlasSide.Value };
    }

    private static bool IsTextureRead(Instruction instruction, IReadOnlyList<SacredParticleTextureBinding> bindings) =>
        instruction.Mnemonic == Mnemonic.Mov && instruction.Op1Kind == OpKind.Memory &&
        instruction.MemoryBase == Register.EBX && bindings.Any(b => b.NativeHandleOffset == instruction.MemoryDisplacement64);
}
