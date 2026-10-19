namespace Sacred.Core.Runtime;

/// <summary>Gold CRT rand (0x849B40): one session/thread's unsigned seed and 15-bit output.</summary>
public sealed class SacredRandomSequence(uint seed)
{
    public uint State { get; private set; } = seed;
    public void Reset(uint seed) => State = seed;
    public int Next()
    {
        State = unchecked(State * 0x343FD + 0x269EC3);
        return (int)(State >> 16) & 0x7FFF;
    }
}
