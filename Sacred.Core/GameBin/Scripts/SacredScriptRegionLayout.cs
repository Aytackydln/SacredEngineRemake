namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x48F330 SetRG/UnSetRG. Four Int32 operands are authored
/// endpoint tile coordinates, followed by a name/resource operand. The runtime
/// rectangle is sorted only after both sector lookups.</summary>
public static class SacredScriptRegionLayout
{
    public const ushort SetOpcode = 37;
    public const ushort RemoveOpcode = 38;
    public const int CoordinateCount = 4;
    public const int NameCapacity = 256;
    /// <summary>Original resource one selects region type one with resource zero.</summary>
    public const uint SpecialRegionResource = 1;
}
