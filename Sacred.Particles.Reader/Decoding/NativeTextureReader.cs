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
            if (instruction.Mnemonic == Mnemonic.Call)
            {
                if (pendingName != null)
                {
                    if (++calls == 2)
                    {
                        resolvedName = pendingName;
                        pendingName = null;
                    }
                }
                else resolvedName = null;
            }
            // The compiler can push the next texture name before storing the previous handle.
            if (resolvedName != null && instruction.Mnemonic == Mnemonic.Mov && instruction.Op0Kind == OpKind.Memory &&
                instruction.MemoryBase == Register.ESI && instruction.Op1Register == Register.EAX)
            {
                bindings.Add(new SacredParticleTextureBinding((uint)instruction.MemoryDisplacement64, resolvedName));
                // Multiple native texture slots can share this lookup result.
            }
        }
        if (bindings.Count == 0) throw new NotSupportedException("No verified constructor texture lookup was found.");
        return bindings.AsReadOnly();
    }

    public static SacredParticleDrawDefinition ReadDraw(SacredExecutableImage image, NativeCode code,
        NativeParticleFamily family, int preset, IReadOnlyList<SacredParticleTextureBinding> bindings,
        SacredParticleTextureBinding? selectedBinding = null)
    {
        var address = family.DrawAddress;
        if (family.ParameterSlotCount > 1)
        {
            var machine = new X86Machine(code, new PresetMemory(image, 0, SacredParticleQuality.High));
            address = machine.ResolveDispatch(address, (uint)preset, instruction =>
                IsTextureRead(instruction, bindings, family.DrawRegister) || instruction.Mnemonic is Mnemonic.Pop or Mnemonic.Ret);
        }
        string? texture = selectedBinding?.TextureName;
        uint? flags = null;
        uint? atlasSide = null;
        var ip = address;
        for (var i = 0; i < 120; i++)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (IsTextureRead(instruction, bindings, family.DrawRegister))
                texture = bindings.Single(b => b.NativeHandleOffset == instruction.MemoryDisplacement64).TextureName;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind != OpKind.Register && instruction.Op0Kind != OpKind.Memory)
            {
                atlasSide = flags;
                flags = (uint)instruction.GetImmediate(0);
            }
            if (instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Ret ||
                instruction.Mnemonic == Mnemonic.Call && instruction.Op0Kind == OpKind.NearBranch32 &&
                instruction.NearBranch32 == 0x761B00) break;
        }
        if (texture == null || flags == null) throw new NotSupportedException("Native texture/draw arguments remain unmapped.");
        if (atlasSide is not (1 or 2 or 4 or 8)) throw new NotSupportedException("Unmapped native particle atlas dimensions.");
        return new SacredParticleDrawDefinition(texture, flags.Value, address) { AtlasSide = (int)atlasSide.Value };
    }

    public static SacredParticleHaloDefinition? ReadHalo(NativeCode code, NativeParticleFamily family,
        IReadOnlyList<SacredParticleTextureBinding> bindings)
    {
        string? texture = null;
        var arguments = new List<uint?>();
        for (var ip = family.DrawAddress; ip < family.DrawAddress + 0x100;)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (IsTextureRead(instruction, bindings, family.DrawRegister))
                texture = bindings.Single(b => b.NativeHandleOffset == instruction.MemoryDisplacement64).TextureName;
            if (instruction.Mnemonic == Mnemonic.Push)
                arguments.Add(instruction.Op0Kind == OpKind.Immediate32 || instruction.Op0Kind == OpKind.Immediate8to32
                    ? (uint)instruction.GetImmediate(0) : null);
            if (instruction.Mnemonic == Mnemonic.Call)
            {
                if (instruction.Op0Kind == OpKind.NearBranch32 && instruction.NearBranch32 == 0x762E10)
                {
                    var args = arguments.AsEnumerable().Reverse().Take(8).ToArray();
                    if (args.Length != 8 || args[1] is not { } size || args[2] is not { } selector ||
                        args[3] is not { } color || !args.Skip(4).SequenceEqual(new uint?[] { uint.MaxValue, 0, 1, 0 }))
                        throw new NotSupportedException("Unmapped stdFlare arguments.");
                    return new(ReadFlareTexture(code, selector), BitConverter.UInt32BitsToSingle(size), color,
                        (uint)instruction.IP) { SourceColorOnly = ReadFlareSourceBlend(code, selector), AttachmentIndex = -1 };
                }
                if (instruction.Op0Kind == OpKind.NearBranch32 && instruction.NearBranch32 == 0x7631E0)
                {
                    // Device, texture, half-size, color, rotation, offset, and five render arguments.
                    var args = arguments.AsEnumerable().Reverse().Take(11).ToArray();
                    if (texture is null || args.Length != 11 || args[2] is null || args[3] is null ||
                        !args.Skip(4).SequenceEqual(new uint?[] { 0, 0, 1, 1, 1, 0, 1 }))
                        throw new NotSupportedException("Unmapped stdLensflare arguments.");
                    return new(texture, BitConverter.UInt32BitsToSingle(args[2]!.Value), args[3]!.Value,
                        (uint)instruction.IP) { SourceColorOnly = args[7] == 0 };
                }
                arguments.Clear();
            }
            if (instruction.Mnemonic is Mnemonic.Jmp or Mnemonic.Ret) break;
        }
        return null;
    }

    internal static string ReadFlareTexture(NativeCode code, uint selector)
    {
        if (selector > 9) throw new NotSupportedException("Unmapped stdFlare texture override.");
        // The verified helper's jump table resolves native selectors to texture lookup strings.
        var dispatch = code.At(0x762EEA);
        var lookup = code.ImageUInt32((uint)dispatch.MemoryDisplacement64 + selector * 4);
        var pointer = code.At((uint)code.At(lookup).NextIP);
        if (pointer.Mnemonic != Mnemonic.Push || pointer.Op0Kind != OpKind.Immediate32)
            throw new NotSupportedException("Unmapped stdFlare texture lookup.");
        return code.ImageString(pointer.Immediate32, 128);
    }

    internal static bool ReadFlareSourceBlend(NativeCode code, uint selector)
    {
        // stdFlare clears EBP in its prologue; follow the selected texture path to state 8.
        uint? previous = null, last = null;
        var ip = code.ImageUInt32((uint)code.At(0x762EEA).MemoryDisplacement64 + selector * 4);
        for (var count = 0; count < 64; count++)
        {
            var instruction = code.At(ip); ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Jmp && instruction.Op0Kind == OpKind.NearBranch32)
                ip = instruction.NearBranch32;
            if (instruction.Mnemonic == Mnemonic.Push)
            {
                previous = last;
                last = instruction.Op0Kind == OpKind.Register
                    ? instruction.Op0Register == Register.EBP ? 0u : null
                    : (uint)instruction.GetImmediate(0);
            }
            if (instruction.Mnemonic == Mnemonic.Call && instruction.NearBranch32 == 0x643430 &&
                last == 8 && previous is 0 or 1) return previous == 1;
        }
        throw new NotSupportedException("Unmapped stdFlare source blend.");
    }

    private static bool IsTextureRead(Instruction instruction, IReadOnlyList<SacredParticleTextureBinding> bindings, Register owner) =>
        instruction.Mnemonic == Mnemonic.Mov && instruction.Op1Kind == OpKind.Memory &&
        instruction.MemoryBase == owner && bindings.Any(b => b.NativeHandleOffset == instruction.MemoryDisplacement64);
}
