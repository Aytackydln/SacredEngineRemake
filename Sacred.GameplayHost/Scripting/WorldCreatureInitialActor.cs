using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Non-hero creature state from construction through initial script preparation.
/// Native pointers, allocator metadata and untouched backing bytes are excluded.
/// Unplaced serialization can zero-canonicalize its discarded bone-vector bytes;
/// ordinary placed NPCs also ignore that cache in their position consumer.
/// Other placed resource families require their own provenance. This does not publish a creature
/// or grant startup readiness.</summary>
public sealed record WorldCreatureInitialActor(GoldObjectBaseState Base,
    WorldCreatureInitialComponent Component, uint RelationClass, uint BehaviorFlags,
    ushort EquipmentSelector, ushort RandomTail, float ThinkElapsed, uint LastUpdateSecond,
    byte[] Scratch)
{
    public WorldObjectNode Node { get; } = new();
    public GoldPhysicalVector Direction { get; init; }
    public GoldObjectPosition ScriptInitialPosition { get; init; }
    /// <summary>Native +0x570 reciprocal Horse/owner partner (separate from +0x251).</summary>
    public uint PartnerReference { get; init; }
    /// <summary>Native +0x249 group assigned by the script caller.</summary>
    public uint ScriptGroup { get; init; }
    /// <summary>Native +0x245 dialog selector; initial service commands can replace constructor zero.</summary>
    public uint DialogIndex { get; init; }
    /// <summary>Native +0x30 next object in the tile's dynamic-object chain.</summary>
    public uint NextOnTile { get; init; }
    /// <summary>Native +0x2C static floor link (0x4498F0 with flag argument zero).
    /// Separate from dynamic equipment attachment and Horse partner links.</summary>
    public uint StaticSurfaceReference { get; init; }
    public float ThinkInterval { get; init; } = 5;
    public float AnimationSpeedMultiplier => 1;
    public uint PaletteMask => 0;
    public ushort KiMode { get; init; }
    /// <summary>Native +0x2B6, set by CreateNPC's ECS selector operand.</summary>
    public byte SpellSelector { get; init; }
    /// <summary>Native +0x261; high word of the packed ECS selector.</summary>
    public ushort SpellParameter { get; init; }
    public ushort EiMode => 0;
    public uint RuntimeFlags { get; init; }
    public ushort Flags2B7 { get; init; }
    public uint Word210 => BitConverter.SingleToUInt32Bits(1);
    public uint Word564 => BitConverter.SingleToUInt32Bits(1);
    public uint[] EquipmentReferences { get; init; } = new uint[GoldCreatureStateLayout.EquipmentCount];
    public byte[] KiState { get; init; } = new byte[84];
    public byte[] EiState { get; } = new byte[84];
    public byte[] ActionState { get; } = new byte[GoldCreatureStateLayout.Block2EDSize];
}
