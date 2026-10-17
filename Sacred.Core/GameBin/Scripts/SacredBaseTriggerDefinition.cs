using System.Text;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Decoded SetBaseTrigger declaration. Reading it does not execute its script or callbacks.</summary>
public sealed class SacredBaseTriggerDefinition
{
    private readonly byte[] _name;
    public SacredBaseTriggerDefinition(ReadOnlySpan<byte> name, SacredScriptPosition first, SacredScriptPosition last)
    {
        if (name.Contains((byte)0)) throw new ArgumentException("Supply trigger name bytes without a terminator.", nameof(name));
        _name = name.ToArray();
        First = first;
        Last = last;
    }
    public ReadOnlySpan<byte> NameBytes => _name;
    public string Name => Encoding.Latin1.GetString(_name);
    public SacredScriptPosition First { get; }
    public SacredScriptPosition Last { get; }
    public int SourceOffset { get; init; }
    /// <summary>Gold 0x481A38 / 0x481AD7: operand 0x0C supplies the surface; 0x0D's third value is ignored.</summary>
    public int Surface => First.Z is >= 0 and <= 16 ? First.Z : 0;
}
