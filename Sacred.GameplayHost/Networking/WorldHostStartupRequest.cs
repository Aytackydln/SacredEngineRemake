namespace Sacred.GameplayHost.Networking;

/// <summary>Original cCommand_armaPlay request and window dispatcher mode.
/// A fresh world resets both inputs, independently of its selected saved hero.
/// This describes the requested operation, not inferred admission flag bits.</summary>
public sealed record WorldHostStartupRequest(ushort CommandMode, uint Value)
{
    public static WorldHostStartupRequest FreshWorld => new(0, 0);
    public uint EngineMode => Value | (CommandMode == 0 ? 0x80000000u : CommandMode == 4 ? 0xc0000000u : 0);
    public bool LoadsStartCode => EngineMode == uint.MaxValue || (EngineMode & 0xc0000000u) == 0x80000000u;
    internal void RequireFreshWorld()
    {
        if (CommandMode != 0 || Value != 0 || !LoadsStartCode)
            throw new NotSupportedException("Selected first-host startup requires the explicit native fresh-world request; restored-world modes remain separate.");
    }
}
