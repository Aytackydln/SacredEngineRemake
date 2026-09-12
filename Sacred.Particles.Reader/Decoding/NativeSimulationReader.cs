using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeSimulationReader
{
    public static int ReadMode(SacredExecutableImage image, NativeCode code, NativeParticleFamily family, int preset)
    {
        if (family.ParameterSlotCount == 1) return 1; // 0x78A369.
        var branchIndex = image.Slice(0x76E5F0 + (uint)(preset - 1), 1)[0];
        var branch = image.UInt32(0x76E5E0 + branchIndex * 4u);
        for (var ip = branch; ip < branch + 64;)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind != OpKind.Register &&
                instruction.GetImmediate(0) is >= 1 and <= 3)
                return (int)instruction.GetImmediate(0);
        }
        throw new NotSupportedException("Native emission selection mode is not mapped.");
    }
}
