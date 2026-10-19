namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold opcode 115 (0x4AB940) builds a 160-byte spawn-equipment rule.
/// The original 0x47F8E0 selects the first rule containing the creature type,
/// then tests rand()%100 against each pool's signed chance, in stored order.</summary>
public static class SacredScriptSpawnEquipmentLayout
{
    public const ushort Opcode = 115;
    public const int Size = 160;
    public const int TypesOffset = 0;
    public const int TypeCapacity = 20;
    public const int TypeCountOffset = 80;
    public const int AdditionalCountOffset = 84;
    public const int FirstPoolOffset = 88;
    public const int SecondPoolOffset = 96;
    public const int ThirdPoolOffset = 104;
    public const int PoolChanceOffset = 4;
    /// <summary>Operand 0x8B, zero-extended from one byte; defaults to zero.
    /// The equipment generator returns its low word alongside selected equipment.</summary>
    public const int SelectorOffset = 112;
    /// <summary>Operand 0x16/0x1D/0x1E's resolved resource value. Literal 0x1D retains its UInt32 identity.</summary>
    public const int ResolvedObjectOffset = 116;
    /// <summary>Up to ten decoded ECS_ spell selectors from operand 0x67.</summary>
    public const int AdditionalValuesOffset = 120;
    public const int AdditionalCapacity = 10;
}
