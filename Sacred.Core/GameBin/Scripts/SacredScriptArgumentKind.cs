namespace Sacred.Core.GameBin.Scripts;

/// <summary>Recovered operand tags, not an exhaustive list. Unknown tags cannot be skipped
/// without decoding their individual variable-length representation.</summary>
public enum SacredScriptArgumentKind : byte
{
    NullTerminatedString = 0x01,
    TypeId = 0x02,
    Facing = 0x03,
    TilePosition = 0x04,
    /// <summary>Tagged Int32 literal (including function indices and map icon coordinates).</summary>
    Integer = 0x0B,
    /// <summary>First inclusive SetBaseTrigger corner: X, Y, surface level.</summary>
    TriggerFirstCorner = 0x0C,
    /// <summary>Second inclusive SetBaseTrigger corner; omitted for a singleton cell.</summary>
    TriggerLastCorner = 0x0D,
    WorldPosition = 0x20,
    /// <summary>Null-terminated object reference used by CreateObj instructions.</summary>
    ObjectReference = 0x29,
    HeightOffset = 0x7E
}
