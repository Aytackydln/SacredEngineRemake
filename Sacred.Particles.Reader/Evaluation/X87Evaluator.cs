using Iced.Intel;

namespace Sacred.Particles.Reader.Evaluation;

/// <summary>Floating-point operations reached by the verified preset initializers.
/// Uses extended-precision intermediates and rounds float32 stores explicitly. This is a bounded
/// catalogue evaluator, not a general x87 emulator or the particle simulation loop.</summary>
internal sealed class X87Evaluator(PresetMemory memory, X86Operands operands)
{
    private readonly List<X87Number> _stack = new(8);
    private ushort _controlWord = 0x037F;

    public bool Execute(Instruction instruction)
    {
        switch (instruction.Mnemonic)
        {
            case Mnemonic.Fld: Push(Read(instruction, 0)); break;
            case Mnemonic.Fild:
                var size = instruction.MemorySize.GetSize();
                var value = memory.Integer(operands.Address(instruction), size);
                Push(size switch { 2 => (short)value, 4 => (int)value, 8 => (long)value,
                    _ => throw new NotSupportedException("Unsupported FILD width.") });
                break;
            case Mnemonic.Fst:
            case Mnemonic.Fstp:
                Store(instruction, Top);
                if (instruction.Mnemonic == Mnemonic.Fstp) Pop();
                break;
            case Mnemonic.Fadd: Binary(instruction, static (a, b) => a + b); break;
            case Mnemonic.Fsub: Binary(instruction, static (a, b) => a - b); break;
            case Mnemonic.Fmul: Binary(instruction, static (a, b) => a * b); break;
            case Mnemonic.Fdiv: Binary(instruction, static (a, b) => a / b); break;
            case Mnemonic.Fnstcw: memory.Write(operands.Address(instruction), 2, _controlWord); break;
            case Mnemonic.Fldcw: _controlWord = (ushort)memory.Integer(operands.Address(instruction), 2); break;
            case Mnemonic.Fistp:
                var rounded = Top.ToInteger((_controlWord >> 10) & 3);
                if (rounded < long.MinValue || rounded > long.MaxValue)
                    throw new NotSupportedException("Native floating-point conversion is outside the recovered range.");
                memory.Write(operands.Address(instruction), instruction.MemorySize.GetSize(), unchecked((ulong)(long)rounded));
                Pop();
                break;
            case Mnemonic.Wait: break;
            default: return false;
        }
        return true;
    }

    private X87Number Top => _stack.Count > 0 ? _stack[0] : throw new InvalidDataException("Native floating-point stack underflow.");
    private void Push(X87Number value)
    {
        if (_stack.Count == 8) throw new InvalidDataException("Native floating-point stack overflow.");
        _stack.Insert(0, value);
    }
    private void Pop() { _ = Top; _stack.RemoveAt(0); }

    private X87Number Read(Instruction instruction, int operand)
    {
        if (instruction.GetOpKind(operand) == OpKind.Register)
            return _stack[StackIndex(instruction.GetOpRegister(operand))];
        var address = operands.Address(instruction);
        return instruction.MemorySize.GetSize() switch
        {
            4 => BitConverter.UInt32BitsToSingle((uint)memory.Integer(address, 4)),
            8 => BitConverter.UInt64BitsToDouble(memory.Integer(address, 8)),
            _ => throw new NotSupportedException("Unsupported native floating-point width.")
        };
    }

    private void Store(Instruction instruction, X87Number value)
    {
        if (instruction.Op0Kind == OpKind.Register) _stack[StackIndex(instruction.Op0Register)] = value;
        else if (instruction.MemorySize.GetSize() == 4)
            memory.Write(operands.Address(instruction), 4, BitConverter.SingleToUInt32Bits((float)value.ToDouble()));
        else if (instruction.MemorySize.GetSize() == 8)
            memory.Write(operands.Address(instruction), 8, BitConverter.DoubleToUInt64Bits(value.ToDouble()));
        else throw new NotSupportedException("Unsupported native floating-point store.");
    }

    private void Binary(Instruction instruction, Func<X87Number, X87Number, X87Number> operation)
    {
        var destination = instruction.OpCount == 2 ? StackIndex(instruction.Op0Register) : 0;
        var right = Read(instruction, instruction.OpCount == 2 ? 1 : 0);
        _stack[destination] = operation(_stack[destination], right);
    }

    private static int StackIndex(Register register)
    {
        var index = (int)register - (int)Register.ST0;
        return index is >= 0 and < 8 ? index : throw new NotSupportedException($"Unsupported x87 register {register}.");
    }
}
