using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Gold's day/night Time Control move branches share the integer path walk at 50F6AD.
/// It relocates the actor, then creates a generic event burst on every thirtieth step.
/// The named time-control particle class is a separate visual layer.</summary>
internal static class NativeTimeControlCastReader
{
    public static SacredParticleCastTrailDefinition Read(NativeCode code)
    {
        var clear = code.At(0x50E6E9);
        var subtype = code.At(0x50F87C);
        if (clear.Mnemonic != Mnemonic.Xor || clear.Op0Register != Register.EBX || clear.Op1Register != Register.EBX ||
            subtype.Mnemonic != Mnemonic.Push || subtype.Op0Register != Register.EBX)
            throw new NotSupportedException("The cast caller's zero subtype register is not recognized.");
        return new((uint)code.At(0x50F857).GetImmediate(0), 0,
            (uint)code.At(0x50F87D).GetImmediate(0),
            (int)code.At(0x50F78B).GetImmediate(1), 0x50F6AD);
    }
}
