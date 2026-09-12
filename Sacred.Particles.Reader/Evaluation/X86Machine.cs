using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Evaluation;

internal sealed class X86Machine
{
    private readonly NativeCode _code;
    private readonly X86Operands _operands;
    private readonly X87Evaluator _floatingPoint;
    public X86Registers Registers { get; } = new();
    public PresetMemory Memory { get; }
    public uint InstructionPointer { get; set; }

    public X86Machine(NativeCode code, PresetMemory memory)
    {
        _code = code;
        Memory = memory;
        _operands = new X86Operands(Registers, memory);
        _floatingPoint = new X87Evaluator(memory, _operands);
    }

    public void Run(uint start, uint stop, CancellationToken cancellationToken)
    {
        InstructionPointer = start;
        for (var count = 0; count < 200_000; count++)
        {
            if (InstructionPointer == stop) return;
            if ((count & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            Step();
        }
        throw new NotSupportedException("Native initializer exceeded the instruction budget.");
    }

    public uint ResolveDispatch(uint start, uint typeId, Func<Instruction, bool>? stopAt = null)
    {
        InstructionPointer = start;
        Registers.Set(Register.EAX, typeId);
        for (var count = 0; count < 128; count++)
        {
            var instruction = _code.At(InstructionPointer);
            if (stopAt?.Invoke(instruction) ?? (instruction.Mnemonic is Mnemonic.Push or Mnemonic.Pop or Mnemonic.Ret))
                return InstructionPointer;
            Step();
        }
        throw new NotSupportedException("Native type dispatch exceeded the instruction budget.");
    }

    public void Push(uint value)
    {
        var pointer = Registers.Get(Register.ESP) - 4;
        Memory.Write(pointer, 4, value);
        Registers.Set(Register.ESP, pointer);
    }

    private uint Pop()
    {
        var pointer = Registers.Get(Register.ESP);
        var value = (uint)Memory.Integer(pointer, 4);
        Registers.Set(Register.ESP, pointer + 4);
        return value;
    }

    private void Step()
    {
        var instruction = _code.At(InstructionPointer);
        InstructionPointer = (uint)instruction.NextIP;
        uint Read(int index) => _operands.Read(instruction, index);
        void Write(uint value) => _operands.Write(instruction, 0, value);
        int Width() => _operands.Width(instruction, 0);
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Mov: Write(Read(1)); break;
            case Mnemonic.Lea: Write(_operands.Address(instruction)); break;
            case Mnemonic.Cmp: Registers.Compare(Read(0), Read(1), Width()); break;
            case Mnemonic.Test: Registers.Logical(Read(0) & Read(1), Width()); break;
            case Mnemonic.Add: Write(Registers.Arithmetic(Read(0), Read(1), Width(), false)); break;
            case Mnemonic.Sub: Write(Registers.Arithmetic(Read(0), Read(1), Width(), true)); break;
            case Mnemonic.Inc: Write(Registers.Arithmetic(Read(0), 1, Width(), false)); break;
            case Mnemonic.Dec: Write(Registers.Arithmetic(Read(0), 1, Width(), true)); break;
            case Mnemonic.Xor: Write(Registers.Logical(Read(0) ^ Read(1), Width())); break;
            case Mnemonic.And: Write(Registers.Logical(Read(0) & Read(1), Width())); break;
            case Mnemonic.Or: Write(Registers.Logical(Read(0) | Read(1), Width())); break;
            case Mnemonic.Shl: Write(Registers.Logical(Read(0) << ((int)Read(1) & 31), Width())); break;
            case Mnemonic.Shr: Write(Registers.Logical(Read(0) >> ((int)Read(1) & 31), Width())); break;
            case Mnemonic.Sar: Write(Registers.Logical((uint)((int)Read(0) >> ((int)Read(1) & 31)), Width())); break;
            case Mnemonic.Push: Push(Read(0)); break;
            case Mnemonic.Pop: Write(Pop()); break;
            case Mnemonic.Call:
                var target = Read(0);
                Push(InstructionPointer);
                InstructionPointer = target;
                break;
            case Mnemonic.Jmp: InstructionPointer = Read(0); break;
            case Mnemonic.Je: case Mnemonic.Jne: case Mnemonic.Ja: case Mnemonic.Jae:
            case Mnemonic.Jb: case Mnemonic.Jbe: case Mnemonic.Jg: case Mnemonic.Jge:
            case Mnemonic.Jl: case Mnemonic.Jle:
                if (Registers.Branch(instruction.Mnemonic)) InstructionPointer = instruction.NearBranch32;
                break;
            case Mnemonic.Ret:
                InstructionPointer = Pop();
                if (instruction.OpCount != 0) Registers.Set(Register.ESP, Registers.Get(Register.ESP) + Read(0));
                break;
            case Mnemonic.Leave:
                Registers.Set(Register.ESP, Registers.Get(Register.EBP));
                Registers.Set(Register.EBP, Pop());
                break;
            case Mnemonic.Stosd:
                var count = instruction.HasRepPrefix ? Registers.Get(Register.ECX) : 1;
                if (count > 0x8000) throw new NotSupportedException("Native store count exceeds scratch capacity.");
                for (var i = 0u; i < count; i++)
                {
                    var destination = Registers.Get(Register.EDI);
                    Memory.Write(destination, 4, Registers.Get(Register.EAX));
                    Registers.Set(Register.EDI, destination + 4);
                }
                if (instruction.HasRepPrefix) Registers.Set(Register.ECX, 0);
                break;
            case Mnemonic.Nop: break;
            default:
                if (!_floatingPoint.Execute(instruction))
                    throw new NotSupportedException($"Unsupported initializer instruction {instruction.Mnemonic} at 0x{instruction.IP:X8}.");
                break;
        }
    }
}
