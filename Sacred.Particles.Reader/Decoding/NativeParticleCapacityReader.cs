using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeParticleCapacityReader
{
    public static int Read(NativeCode code, uint factory)
    {
        // The final literal push before the class constructor is its particle-vector capacity.
        int? capacity = null;
        var calls = 0;
        for (var ip = factory; ip < factory + 0x50;)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind is not (OpKind.Register or OpKind.Memory))
                capacity = checked((int)instruction.GetImmediate(0));
            if (instruction.Mnemonic == Mnemonic.Call && ++calls == 2)
                return capacity is > 0 and <= 4000 ? capacity.Value :
                    throw new NotSupportedException("Native particle capacity is not literal.");
        }
        throw new NotSupportedException("Native particle factory constructor was not found.");
    }
}
