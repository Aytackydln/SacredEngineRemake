using Iced.Intel;
using Sacred.Particles.Reader.Executable;

namespace Sacred.Particles.Reader.Decoding;

/// <summary>Groups FX by the constructor actually called by the factory, rather than their names.</summary>
internal static class NativeFxFamilyReader
{
    public static uint? ReadConstructor(NativeCode code, uint factory)
    {
        var allocated = false;
        var visited = new HashSet<uint>();
        for (var ip = factory; visited.Count < 128 && visited.Add(ip);)
        {
            var instruction = code.At(ip);
            ip = (uint)instruction.NextIP;
            if (instruction.Mnemonic == Mnemonic.Ret) return null;
            if (instruction.Mnemonic == Mnemonic.Jmp)
            {
                if (instruction.Op0Kind != OpKind.NearBranch32) return null;
                ip = instruction.NearBranch32;
            }
            if (instruction.Mnemonic != Mnemonic.Call || instruction.Op0Kind != OpKind.NearBranch32) continue;
            var target = instruction.NearBranch32;
            if (target == 0x849842) { allocated = true; continue; }
            return allocated ? target : null;
        }
        return null;
    }
}
