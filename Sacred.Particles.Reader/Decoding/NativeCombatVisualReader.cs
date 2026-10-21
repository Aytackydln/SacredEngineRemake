using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Iced.Intel;
using Sacred.Core.CombatArts;
using Sacred.Particles.Reader.Evaluation;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Runs bounded native CA factory branches; lookup/event submission are explicit boundaries.
/// Unsupported branches produce no invented asset mapping.</summary>
internal static class NativeCombatVisualReader
{
    public static IReadOnlyList<SacredCombatVisualDefinition> Read(SacredExecutableImage image)
    {
        var code = new NativeCode(image);
        var output = new List<SacredCombatVisualDefinition>();
        for (uint spell = 1; spell <= 72; spell++)
        {
            // These branches share the fully inspected destination/self FX factory path.
            // Other spell stages remain explicit extension points, not inferred by names.
            if (spell is not (2 or 4 or 7 or 20 or 21 or 22 or 31 or 32)) continue;
            var factory = image.UInt32(0x52947C + (spell - 1) * 4);
            var definition = MagicDefinition(spell);
            var releaseBranch = image.UInt32(0x521F04 + (spell - 1) * 4);
            uint ip = releaseBranch;
            double tail = 0;
            for (var i = 0; i < 12; i++)
            {
                var instruction = code.At(ip);
                if (instruction.Mnemonic == Mnemonic.Fcomp && instruction.Op0Kind == OpKind.Memory)
                { tail = BinaryPrimitives.ReadDoubleLittleEndian(image.Slice((uint)instruction.MemoryDisplacement64, 8)); break; }
                ip = (uint)instruction.NextIP;
            }
            if (tail is <= 0 or >= 1) throw new NotSupportedException("Unmapped spell release fraction.");
            foreach (var e in Factory(factory, 0x5293E0, spell, false))
                output.Add(e with
                {
                    ReleaseProgress = (float)(1 - tail),
                    DurationBase = definition.ParameterZero.Base,
                    DurationPerLevel = definition.ParameterZero.PerLevel,
                    DurationCap = definition.ParameterZeroCap
                });
        }
        // Vampiress post-animation factory: both transition arts use the same native case.
        foreach (var art in new uint[] { 1022, 1023 })
        {
            var index = image.Byte(0x514758 + art - 1022);
            var start = image.UInt32(0x514748 + index * 4u);
            foreach (var e in Factory(start, 0x5145FC, art, true))
                output.Add(e with { Diagnostic = "Native form-transition particle family is pending; received model replacement remains authoritative." });
        }
        return output.AsReadOnly();

        ushort U16(uint address) => BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(address, 2));
        SacredMagicDefinitionLayout MagicDefinition(uint spell)
        {
            for (uint address = 0x952ACC; address < 0x955344; address += SacredMagicDefinitionLayout.Size)
                if (U16(address) == spell)
                    return MemoryMarshal.Read<SacredMagicDefinitionLayout>(image.Slice(address, SacredMagicDefinitionLayout.Size));
            throw new InvalidDataException($"Missing native magic definition {spell}.");
        }

        IReadOnlyList<SacredCombatVisualDefinition> Factory(uint start, uint stop, uint art, bool special)
        {
            var memory = new PresetMemory(image, 0x800, SacredParticleQuality.High);
            var actor = memory.BaseAddress;
            var action = actor + 0x2000;
            var created = actor + 0x3000;
            var vtable = actor + 0x4000;
            var callback = actor + 0x4100;
            var stack = actor + 0x7000;
            uint resource = 0;
            var records = new List<SacredCombatVisualDefinition>();
            memory.Write(actor + 0xC, 4, 1); memory.Write(actor + 0x10, 4, special ? 6u : 1u);
            memory.Write(action, 4, special ? art : 1u); memory.Write(action + 0x20, 4, art);
            memory.Write(created, 4, vtable); memory.Write(vtable + 0x18, 4, callback);
            memory.Write(stack + 0x14, 4, action); memory.Write(stack + 0x84, 2, 1);
            memory.Write(stack + 0xB4, 4, 0x3F800000);
            var machine = new X86Machine(code, memory) { Boundary = Boundary };
            machine.Registers.Set(Register.ESP, stack); machine.Registers.Set(Register.EBP, actor);
            machine.Registers.Set(Register.ESI, action); machine.Registers.Set(Register.EBX, special ? 0u : uint.MaxValue);
            machine.Run(start, stop, CancellationToken.None);
            return records;

            bool Boundary(X86Machine cpu)
            {
                var sp = cpu.Registers.Get(Register.ESP);
                uint Word(uint address) => (uint)memory.Integer(address, 4);
                void Return(uint bytes, uint result)
                { cpu.InstructionPointer = Word(sp); cpu.Registers.Set(Register.ESP, sp + 4 + bytes); cpu.Registers.Set(Register.EAX, result); }
                switch (cpu.InstructionPointer)
                {
                    case 0x5FE000: Return(4, Word(sp + 4) == 0xFE01 ? created : actor); return true;
                    case 0x5FBA40: resource = Word(sp + 4); Return(20, 0xFE01); return true;
                    case 0x41DEE0: Return(0, actor + 0x18); return true;
                    case 0x4B3590:
                        for (uint i = 0; i < 12; i += 4) memory.Write(cpu.Registers.Get(Register.ECX) + i, 4, 0);
                        Return(0, cpu.Registers.Get(Register.ECX)); return true;
                    case 0x4107C0:
                        for (uint i = 0; i < 12; i += 4) memory.Write(cpu.Registers.Get(Register.ECX) + i, 4, Word(Word(sp + 4) + i));
                        Return(4, cpu.Registers.Get(Register.ECX)); return true;
                }
                if (cpu.InstructionPointer != callback) return false;
                var ev = Word(sp + 4);
                var subtype = Word(ev + 4);
                var attachment = Attachment(subtype, ev);
                var bone = attachment.Bone;
                records.Add(new(special ? SacredCombatArtKind.SpecialMove : SacredCombatArtKind.Spell, art, resource,
                    subtype, bone, attachment.Caster, special ? 1 : 0, 0, 0, 0, start)
                {
                    Direction = new Vector3(BitConverter.UInt32BitsToSingle(Word(ev + 0x2C)),
                    BitConverter.UInt32BitsToSingle(Word(ev + 0x30)), BitConverter.UInt32BitsToSingle(Word(ev + 0x34)))
                });
                Return(4, 0);
                // Spell branches are single attached-effect factories. Stop before unrelated stages.
                if (!special) cpu.InstructionPointer = stop;
                return true;
            }

            (string Bone, bool Caster) Attachment(uint subtype, uint ev)
            {
                var state = memory.BaseAddress + 0x14000;
                var argument = memory.BaseAddress + 0x15000;
                var tempStack = memory.BaseAddress + 0x19000;
                var done = memory.BaseAddress + 0x1FF00;
                for (uint i = 0; i < 124; i += 4) memory.Write(argument + i, 4, memory.Integer(ev + i, 4));
                memory.Write(argument + 8, 4, 0x1111); memory.Write(argument + 12, 4, 0x2222);
                memory.Write(state + 0x6E, 2, 1);
                var m = new X86Machine(code, memory)
                {
                    Boundary = cpu =>
                {
                    var at = cpu.InstructionPointer;
                    if (at is 0x5A3BF8 or 0x5A3BFE or 0x5A3C05)
                    { cpu.InstructionPointer = done; return true; }
                    if (at is not (0x7CC040 or 0x7CBB00)) return false;
                    var sp = cpu.Registers.Get(Register.ESP);
                    cpu.InstructionPointer = (uint)memory.Integer(sp, 4);
                    cpu.Registers.Set(Register.ESP, sp + 12); return true;
                }
                };
                m.Registers.Set(Register.EBP, state); m.Registers.Set(Register.EBX, argument);
                m.Registers.Set(Register.ESI, actor); m.Registers.Set(Register.EAX, 15);
                m.Registers.Set(Register.ESP, tempStack);
                m.Run(image.UInt32(0x5A5EC4 + (subtype - 1) * 4), done, CancellationToken.None);
                var bone = (uint)memory.Integer(state + 0x70, 2);
                var wearer = (uint)memory.Integer(state + 0x68, 4);
                if (wearer is not (0x1111 or 0x2222)) throw new NotSupportedException("Native FX has no creature wearer.");
                return (image.String(0x8E7200 + bone * 32, 32), wearer == 0x1111);
            }
        }
    }
}
