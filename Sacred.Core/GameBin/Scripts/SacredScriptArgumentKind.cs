namespace Sacred.Core.GameBin.Scripts;

/// <summary>Recovered operand tags, not an exhaustive list. Unknown tags cannot be skipped
/// without decoding their individual variable-length representation.</summary>
public enum SacredScriptArgumentKind : byte
{
    NullTerminatedString = 0x01,
    TypeId = 0x02,
    TilePosition = 0x04,
    WorldPosition = 0x20,
    /// <summary>Null-terminated object reference used by CreateObj instructions.</summary>
    ObjectReference = 0x29,
    HeightOffset = 0x7E
}
