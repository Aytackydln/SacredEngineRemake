namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold CreateNPC 0x483CF8..0x483D1A. Mode is native +0x25F,
/// selector +0x2B6 and parameter +0x261. This does not cast or initialize an action.</summary>
public readonly record struct SacredCreatureScriptSpellState(ushort Mode, byte Selector, ushort Parameter)
{
    public SacredCreatureScriptSpellState Apply(uint packed) => packed == uint.MaxValue
        ? this with { Mode = 0 }
        : this with { Selector = unchecked((byte)packed), Parameter = (ushort)(packed >> 16) };
}
