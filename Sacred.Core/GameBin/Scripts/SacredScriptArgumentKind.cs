namespace Sacred.Core.GameBin.Scripts;

/// <summary>Recovered operand tags, not an exhaustive list. Unknown tags cannot be skipped
/// without decoding their individual variable-length representation.</summary>
public enum SacredScriptArgumentKind : byte
{
    NullTerminatedString = 0x01,
    TypeId = 0x02,
    Facing = 0x03,
    TilePosition = 0x04,
    /// <summary>CreateNPC NUL-terminated callback name. Gold 0x4827F4 copies it
    /// to caller +0x4E8; 0x483846 registers it with the new object in interpreter +0x358.
    /// Registration/dispatch lifecycle is separate from decoding.</summary>
    CreatureCallbackName = 0x05,
    /// <summary>CreateNPC NUL-terminated OnSee name. Gold 0x482835 prefixes
    /// See:, then 0x483917 appends it after the death callback and sets behavior bit 0x400000.</summary>
    CreatureSeeCallbackName = 0x60,
    /// <summary>CreateNPC: selects aggression input 1 (Gold 0x482895). This tag has no payload.</summary>
    CreatureAggressive = 0x08,
    /// <summary>Opcode 106: NUL-terminated name searched in the 80-byte dialog-registration table (Gold 0x4652F8).</summary>
    DialogRegistrationName = 0x09,
    /// <summary>Opcode 106: UInt16 registration number, matched against the table record's first word (Gold 0x4653B3).</summary>
    DialogRegistrationNumber = 0x0A,
    /// <summary>Opcode 94: record mode 2, no payload (Gold 0x4A6A86).</summary>
    MouseoverMode2 = 0x0F,
    /// <summary>Opcode 94: record mode 1, no payload (Gold 0x4A6A7C).</summary>
    MouseoverMode1 = 0x10,
    /// <summary>NUL-terminated symbolic resource, shared decoder at Gold 0x4751C4.</summary>
    ScriptResourceName = 0x1E,
    /// <summary>Opcode 94: range word 1 instead of 16000, no payload (Gold 0x4A6A78).</summary>
    MouseoverShortRange = 0x66,
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
    /// <summary>CreateNPC/SetNPCState: later requested Int32 level, optionally a native expression.
    /// Gold applies normal multiplayer/difficulty adjustment. Separate from the
    /// early, raise-only assigned level operand 0x90.</summary>
    CreatureLevel = 0x1F,
    /// <summary>Gold operand 0x1D: UInt32 script-resource identity; the native operand
    /// reader also resolves its optional dialogue/logging text.</summary>
    ScriptResourceId = 0x1D,
    /// <summary>Three Int32 world coordinates. CreateNPC negates X/Y into its placement scratch
    /// words and retains the previous surface input, ignoring this operand's Z (Gold 0x48279A).</summary>
    WorldPosition = 0x20,
    /// <summary>CreateNPC: trader service, construction-mode mask 4; marks explicit behavior.</summary>
    CreatureTrader = 0x24,
    /// <summary>CreateNPC smith service, construction-mode mask 0x10 (Gold 0x482CE0).
    /// Marks explicit behavior; initial service adds runtime flag 0x1000.</summary>
    CreatureSmith = 0x23,
    /// <summary>CreateNPC: combo service, construction-mode mask 8; marks explicit behavior.</summary>
    CreatureComboMaster = 0x25,
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
    /// <summary>CreateNPC: low behavior selector 8, aggression zero; no explicit marker.</summary>
    CreatureBehavior8 = 0x2B,
    /// <summary>SetTrigger: clear bit 0 and set state bit 0x8000.</summary>
    TriggerState8000On = 0x2C,
    /// <summary>SetTrigger: clear state bit 0x8000.</summary>
    TriggerState8000Off = 0x2D,
    /// <summary>CreateNPC, Gold 0x48292F: replace the low behavior word with 0x10
    /// and set aggression input zero. This selector does not mark explicit behavior.</summary>
    CreatureBehavior10 = 0x2E,
    /// <summary>CreateNPC, Gold 0x482949: low behavior selector 0x40; preserve high flags.</summary>
    CreatureBehavior40 = 0x2F,
    CreatureBehavior20 = 0x30,
    CreatureBehavior40Alias = 0x31,
    CreatureBehavior80 = 0x32,
    /// <summary>FillRegion: minimum and maximum levels (two Int32 values).</summary>
    PopulationLevels = 0x33,
    /// <summary>FillRegion: sector grid X/Y (two Int32 values).</summary>
    PopulationSector = 0x34,
    /// <summary>FillRegion: minimum count, maximum count and area divisor (three Int32 values).</summary>
    PopulationDensity = 0x35,
    /// <summary>FillRegion: signed value copied to each generated creature +0x255.</summary>
    PopulationCreatureValue = 0x36,
    /// <summary>FillRegion: resource type, signed weight and group (three Int32 values).</summary>
    PopulationWeightedType = 0x19,
    /// <summary>CreateObj NUL-terminated take callback suffix; native registration prepends "Take:".</summary>
    ObjectTakeScript = 0x41,
    /// <summary>CreateNPC: low behavior selector 0x400, aggression zero; no explicit marker.</summary>
    CreatureBehavior400 = 0x42,
    /// <summary>CreateNPC: low behavior selector 0x4000; preserves high flags (Gold 0x4828BA).</summary>
    CreatureBehavior4000 = 0x46,
    /// <summary>DoNotClean selector; CreateNPC Gold 0x4828EA ORs behavior mask 0x2000 without
    /// clearing prior selectors, and mark explicit behavior. No payload.</summary>
    CreatureBehavior2000 = 0x45,
    /// <summary>Compiled "npc" selector (Gold 0x45810A); SetNPCState replaces the low behavior word with 0x8000. No payload.</summary>
    CreatureNpcBehavior = 0x4C,
    /// <summary>SetNPCState owner name, NUL-terminated (Gold 0x48BD65).
    /// The native definition retains its resource at +0x0C and name at +0x70.</summary>
    CreatureOwner = 0x63,
    /// <summary>Compiled HorseMerchant (Gold 0x4583F8/0x45841D). No payload.
    /// CreateNPC ORs construction mode 0x20 and marks explicit behavior
    /// (0x482CEC/0x482D55); its later service/dialog lifecycle is separate.</summary>
    CreatureHorseMerchant = 0x64,
    /// <summary>CreateNPC Int32 assigned level (Gold 0x48272C). This marks
    /// explicit behavior, raises level before behavior/services, then retains
    /// the low input byte at values +80. Separate from requested level 0x1F.</summary>
    CreatureAssignedLevel = 0x90,
    /// <summary>NUL-terminated ECS_ selector, decoded by Gold 0x472BC0.
    /// CreateNPC copies its low byte to +0x2B6 and high word to +0x261;
    /// FFFFFFFF instead clears +0x25F. Also used by spawn-equipment rules.</summary>
    CreatureSpellSelector = 0x67,
    /// <summary>CreateNPC UInt16 selector (Gold 0x4827DB): zero enables regional
    /// type substitution and clears creature +0x2B7 mask 0x0008; positive sets it.
    /// Omitted input defaults to the separate signed runtime sentinel -1.</summary>
    // SetNPCState uses the same UInt16 selector at Gold 0x461F83..0x461FA2.
    CreatureFlag2B7Mask08 = 0x6B,
    /// <summary>Compiled "still" selector (Gold 0x458252); SetNPCState sets behavior flag 0x08000000. No payload.</summary>
    CreatureStationary = 0x74,
    /// <summary>CreateObj NUL-terminated use callback suffix; native registration prepends "Use:".</summary>
    ObjectUseScript = 0x83,
    /// <summary>Opcode 115: two signed Int32 values, equipment-pool index and percent comparison threshold.</summary>
    SpawnEquipmentFirstPool = 0x87,
    SpawnEquipmentSecondPool = 0x88,
    SpawnEquipmentThirdPool = 0x89,
    /// <summary>Opcode 115: a single unsigned byte, retained as the equipment generator's return selector.</summary>
    SpawnEquipmentSelector = 0x8B,
    HeightOffset = 0x7E
}
