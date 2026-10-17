namespace Sacred.Core.GameBin.Scripts;

/// <summary>Recovered operand tags, not an exhaustive list. Unknown tags cannot be skipped
/// without decoding their individual variable-length representation.</summary>
public enum SacredScriptArgumentKind : byte
{
    NullTerminatedString = 0x01,
    TypeId = 0x02,
    Facing = 0x03,
    TilePosition = 0x04,
    /// <summary>CreateNPC: selects aggression input 1 (Gold 0x482895). This tag has no payload.</summary>
    CreatureAggressive = 0x08,
    /// <summary>Tagged Int32 literal (including function indices and map icon coordinates).</summary>
    Integer = 0x0B,
    /// <summary>First inclusive SetBaseTrigger corner: X, Y, surface level.</summary>
    TriggerFirstCorner = 0x0C,
    /// <summary>Second inclusive SetBaseTrigger corner; omitted for a singleton cell.</summary>
    TriggerLastCorner = 0x0D,
    /// <summary>CreateNPC: selects the non-aggressive construction path (Gold 0x4829BC).</summary>
    CreatureNonAggressive = 0x0E,
    /// <summary>CreateNPC: Int32 group key, retained at creature +0x249 and registered in the interpreter group map.</summary>
    CreatureGroup = 0x11,
    /// <summary>CreateNPC: replace the low behavior word with 1; preserve high flags (Gold 0x4829E8). No payload.</summary>
    CreatureBehavior1 = 0x12,
    /// <summary>CreateNPC: replace the low behavior selector with 2; preserves high flags (Gold 0x482A09).</summary>
    CreatureBehavior2 = 0x13,
    /// <summary>CreateNPC: clear the low behavior word and preserve high flags (Gold 0x4829FC). No payload.</summary>
    CreatureBehavior0 = 0x14,
    /// <summary>CreateNPC: signed Int32 level input, optionally a native expression.
    /// Gold passes it through level initialization, difficulty/region limits and value recalculation.</summary>
    CreatureLevel = 0x1F,
    /// <summary>Three Int32 world coordinates. CreateNPC negates X/Y into its placement scratch
    /// words and retains the previous surface input, ignoring this operand's Z (Gold 0x48279A).</summary>
    WorldPosition = 0x20,
    /// <summary>SetTrigger: initialize/update state mask with bit 0 set.</summary>
    TriggerStateOn = 0x26,
    /// <summary>SetTrigger: initialize/update state mask with bit 0 clear.</summary>
    TriggerStateOff = 0x27,
    /// <summary>One byte; value 1 selects native trigger flags 2, other values select flags 1.</summary>
    TriggerScope = 0x28,
    /// <summary>NUL-terminated linked trigger name (OC_UseTrigger), separate from the object name.</summary>
    ObjectReference = 0x29,
    /// <summary>TriggerPatch cell: three Int32 values (tile X, Y, surface).</summary>
    TriggerPatchPosition = 0x2A,
    /// <summary>SetTrigger: clear bit 0 and set state bit 0x8000.</summary>
    TriggerState8000On = 0x2C,
    /// <summary>SetTrigger: clear state bit 0x8000.</summary>
    TriggerState8000Off = 0x2D,
    /// <summary>CreateObj NUL-terminated take callback suffix; native registration prepends "Take:".</summary>
    ObjectTakeScript = 0x41,
    /// <summary>CreateNPC: low behavior selector 0x4000; preserves high flags (Gold 0x4828BA).</summary>
    CreatureBehavior4000 = 0x46,
    /// <summary>Compiled "npc" selector (Gold 0x45810A); SetNPCState replaces the low behavior word with 0x8000. No payload.</summary>
    CreatureNpcBehavior = 0x4C,
    /// <summary>CreateNPC UInt16 selector (Gold 0x4827DB): zero enables regional
    /// type substitution and clears creature +0x2B7 mask 0x0008; positive sets it.
    /// Omitted input defaults to the separate signed runtime sentinel -1.</summary>
    CreatureFlag2B7Mask08 = 0x6B,
    /// <summary>Compiled "still" selector (Gold 0x458252); SetNPCState sets behavior flag 0x08000000. No payload.</summary>
    CreatureStationary = 0x74,
    /// <summary>CreateObj NUL-terminated use callback suffix; native registration prepends "Use:".</summary>
    ObjectUseScript = 0x83,
    HeightOffset = 0x7E
}
