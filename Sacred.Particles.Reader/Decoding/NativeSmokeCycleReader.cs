using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeSmokeCycleReader
{
    public static bool HasController(NativeParticleFamily family, int preset) =>
        family.Constructor == 0x76E200 && preset == 7;

    public static SacredParticleEmissionCycle Read(SacredExecutableImage image, NativeCode code,
        SacredParticleQuality quality)
    {
        float Constant(uint ip, Mnemonic expected)
        {
            var instruction = code.At(ip);
            if (instruction.Mnemonic != expected || instruction.MemoryBase != Register.None)
                throw new NotSupportedException($"Unexpected cycle constant at 0x{ip:X8}.");
            return image.Single((uint)instruction.MemoryDisplacement64);
        }
        float Write(uint ip, uint field)
        {
            var instruction = code.At(ip);
            if (instruction.Mnemonic != Mnemonic.Mov || instruction.MemoryBase != Register.ESI ||
                instruction.MemoryDisplacement64 != field || instruction.Op1Kind != OpKind.Immediate32)
                throw new NotSupportedException($"Unexpected cycle parameter write at 0x{ip:X8}.");
            return BitConverter.UInt32BitsToSingle(instruction.Immediate32);
        }
        var factor = Constant(quality switch { SacredParticleQuality.High => 0x76E488,
            SacredParticleQuality.Medium => 0x76E494, _ => 0x76E49C }, Mnemonic.Fld);
        return new(Constant(0x76F61B, Mnemonic.Fadd), Constant(0x76F615, Mnemonic.Fmul),
            Constant(0x76E43D, Mnemonic.Fadd), Constant(0x76E437, Mnemonic.Fmul),
            Constant(0x76F60F, Mnemonic.Fmul), Constant(0x76E44E, Mnemonic.Fcomp),
            (float)(factor * (double)Constant(0x76E5AE, Mnemonic.Fmul)),
            Write(0x76E57C, 0x2D34), Write(0x76E588, 0x2CB0),
            (float)(factor * (double)Constant(0x76E4A2, Mnemonic.Fmul)),
            Write(0x76E470, 0x2D34), Write(0x76E47C, 0x2CB0), 0x76E3E8);
    }
}
