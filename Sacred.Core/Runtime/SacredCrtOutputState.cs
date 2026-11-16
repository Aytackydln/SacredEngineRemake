namespace Sacred.Core.Runtime;

public sealed class SacredCrtOutputState(byte flags)
{
    public byte Flags { get; set; } = flags;
}
