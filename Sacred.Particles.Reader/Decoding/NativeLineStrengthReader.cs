using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

internal static class NativeLineStrengthReader
{
    public static SacredParticleStrengthDefinition Read(SacredExecutableImage image, NativeCode code, uint initializer)
    {
        var multiply = new List<float>(); var add = new List<float>(); var doubled = false;
        var objectRegister = Register.None;
        for (var ip = initializer; ip < initializer + 0xC0;)
        {
            var i = code.At(ip); ip = (uint)i.NextIP;
            if (i.Mnemonic == Mnemonic.Mov && i.Op0Kind == OpKind.Register && i.Op1Register == Register.ECX)
                objectRegister = i.Op0Register;
            if (i.Mnemonic is Mnemonic.Fmul or Mnemonic.Fadd && i.Op0Kind == OpKind.Memory && i.MemoryBase == Register.None)
            {
                var value = image.Single((uint)i.MemoryDisplacement64);
                (i.Mnemonic == Mnemonic.Fmul ? multiply : add).Add(value);
            }
            if (i.Mnemonic == Mnemonic.Fadd && i.Op0Register == Register.ST0 && i.Op1Register == Register.ST0) doubled = true;
            if (i.Mnemonic != Mnemonic.Fstp || objectRegister == Register.None || i.MemoryBase != objectRegister || i.MemoryDisplacement64 != 0x20E4) continue;
            if (multiply.Count != 2 || add.Count != 2 || !doubled)
                throw new NotSupportedException("Unmapped native event-strength expression.");
            return new(multiply[0], add[1], 2, multiply[1], add[0], initializer);
        }
        throw new NotSupportedException("Native event-strength size store was not found.");
    }
}
