using Iced.Intel;

namespace Sacred.Particles.Reader.Evaluation;

internal sealed class X86Operands(X86Registers registers, PresetMemory memory)
{
    public uint Address(Instruction instruction)
    {
        if (instruction.SegmentPrefix is Register.FS or Register.GS)
            throw new NotSupportedException("Native thread-local memory is not available to the preset evaluator.");
        var address = (uint)instruction.MemoryDisplacement64;
        if (instruction.MemoryBase != Register.None) address += registers.Get(instruction.MemoryBase);
        if (instruction.MemoryIndex != Register.None)
            address += registers.Get(instruction.MemoryIndex) * (uint)instruction.MemoryIndexScale;
        return address;
    }

    public uint Read(Instruction instruction, int operand) => instruction.GetOpKind(operand) switch
    {
        OpKind.Register => registers.Get(instruction.GetOpRegister(operand)),
        OpKind.Memory => (uint)memory.Integer(Address(instruction), instruction.MemorySize.GetSize()),
        OpKind.NearBranch32 => instruction.NearBranch32,
        _ => (uint)instruction.GetImmediate(operand)
    };

    public int Width(Instruction instruction, int operand) => instruction.GetOpKind(operand) == OpKind.Register
        ? X86Registers.Width(instruction.GetOpRegister(operand)) : instruction.MemorySize.GetSize();

    public void Write(Instruction instruction, int operand, uint value)
    {
        if (instruction.GetOpKind(operand) == OpKind.Register) registers.Set(instruction.GetOpRegister(operand), value);
        else if (instruction.GetOpKind(operand) == OpKind.Memory)
            memory.Write(Address(instruction), instruction.MemorySize.GetSize(), value);
        else throw new NotSupportedException("Unsupported native destination operand.");
    }
}
