namespace Sacred.GameplayHost.Runtime;

public sealed class SacredCrtOutputState(byte flags)
{
    public byte Flags { get; set; } = flags;
}
