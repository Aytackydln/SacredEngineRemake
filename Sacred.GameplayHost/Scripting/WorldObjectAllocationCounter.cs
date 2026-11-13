namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold global allocation byte 0xAD5C50. Incremented before each
/// constructor attempt, with byte wrap, and read again after construction.
/// One instance belongs to the session and is shared by object families.</summary>
public sealed class WorldObjectAllocationCounter(byte initialValue = 0)
{
    public byte Value { get; private set; } = initialValue;
    public void BeginAllocation() => Value = unchecked((byte)(Value + 1));
}
