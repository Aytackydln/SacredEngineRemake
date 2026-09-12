using Iced.Intel;

namespace Sacred.Particles.Reader.Executable;

internal sealed class NativeCode(SacredExecutableImage image)
{
    private readonly Dictionary<uint, Instruction> _instructions = [];

    public Instruction At(uint address)
    {
        if (address < SacredGoldExecutableProfile.CodeHashAddress ||
            address >= SacredGoldExecutableProfile.CodeHashAddress + SacredGoldExecutableProfile.CodeHashLength)
            throw new NotSupportedException($"Native call/branch leaves the verified game code at 0x{address:X8}; external routines are not evaluated.");
        if (_instructions.TryGetValue(address, out var result)) return result;
        var decoder = Decoder.Create(32, new ByteArrayCodeReader(image.Slice(address, 15).ToArray()));
        decoder.IP = address;
        result = decoder.Decode();
        if (result.IsInvalid) throw new InvalidDataException($"Invalid native instruction at 0x{address:X8}.");
        _instructions.Add(address, result);
        return result;
    }
}
