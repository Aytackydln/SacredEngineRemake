using Iced.Intel;

namespace Sacred.Particles.Reader.Evaluation;

internal sealed class X86Registers
{
    private readonly uint[] _values = new uint[8];
    public bool Zero { get; private set; }
    public bool Carry { get; private set; }
    public bool Sign { get; private set; }
    public bool Overflow { get; private set; }

    public uint Get(Register register)
    {
        var (index, width, shift) = Info(register);
        return (_values[index] >> shift) & Mask(width);
    }

    public void Set(Register register, uint value)
    {
        var (index, width, shift) = Info(register);
        var mask = Mask(width) << shift;
        _values[index] = (_values[index] & ~mask) | ((value << shift) & mask);
    }

    public void Compare(uint left, uint right, int width) => Arithmetic(left, right, width, subtract: true);

    public uint Arithmetic(uint left, uint right, int width, bool subtract)
    {
        var mask = Mask(width);
        left &= mask;
        right &= mask;
        var result = (subtract ? left - right : left + right) & mask;
        var signBit = 1u << (width * 8 - 1);
        Zero = result == 0;
        Sign = (result & signBit) != 0;
        Carry = subtract ? left < right : (ulong)left + right > mask;
        Overflow = ((subtract ? left ^ right : ~(left ^ right)) & (left ^ result) & signBit) != 0;
        return result;
    }

    public uint Logical(uint value, int width)
    {
        value &= Mask(width);
        Zero = value == 0;
        Sign = (value & (1u << (width * 8 - 1))) != 0;
        Carry = Overflow = false;
        return value;
    }

    public bool Branch(Mnemonic mnemonic) => mnemonic switch
    {
        Mnemonic.Je => Zero, Mnemonic.Jne => !Zero,
        Mnemonic.Ja => !Carry && !Zero, Mnemonic.Jae => !Carry,
        Mnemonic.Jb => Carry, Mnemonic.Jbe => Carry || Zero,
        Mnemonic.Jg => !Zero && Sign == Overflow, Mnemonic.Jge => Sign == Overflow,
        Mnemonic.Jl => Sign != Overflow, Mnemonic.Jle => Zero || Sign != Overflow,
        _ => throw new NotSupportedException($"Unsupported conditional branch {mnemonic}.")
    };

    public static int Width(Register register) => Info(register).Width;
    private static uint Mask(int width) => width == 4 ? uint.MaxValue : (1u << (width * 8)) - 1;

    private static (int Index, int Width, int Shift) Info(Register register) => register switch
    {
        Register.EAX => (0, 4, 0), Register.AX => (0, 2, 0), Register.AL => (0, 1, 0), Register.AH => (0, 1, 8),
        Register.ECX => (1, 4, 0), Register.CX => (1, 2, 0), Register.CL => (1, 1, 0), Register.CH => (1, 1, 8),
        Register.EDX => (2, 4, 0), Register.DX => (2, 2, 0), Register.DL => (2, 1, 0), Register.DH => (2, 1, 8),
        Register.EBX => (3, 4, 0), Register.BX => (3, 2, 0), Register.BL => (3, 1, 0), Register.BH => (3, 1, 8),
        Register.ESP => (4, 4, 0), Register.SP => (4, 2, 0),
        Register.EBP => (5, 4, 0), Register.BP => (5, 2, 0),
        Register.ESI => (6, 4, 0), Register.SI => (6, 2, 0),
        Register.EDI => (7, 4, 0), Register.DI => (7, 2, 0),
        _ => throw new NotSupportedException($"Unsupported native register {register}.")
    };
}
