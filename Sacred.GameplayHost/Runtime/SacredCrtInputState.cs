namespace Sacred.GameplayHost.Runtime;

/// <summary>Established original descriptor +4 flags / +5 saved character.
/// Disk, pipe and device bits differ; saved LF means no pending character.</summary>
public sealed class SacredCrtInputState(byte flags)
{
    public byte Flags { get; set; } = flags;
    public byte Lookahead { get; set; } = 10;
}
