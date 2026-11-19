using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Creature logical state from construction through initial script preparation.
/// Native pointers, allocator metadata and untouched backing bytes are excluded.
/// Unplaced serialization can zero-canonicalize its discarded bone-vector bytes;
/// ordinary placed NPCs also ignore that cache in their position consumer.
/// Other placed resource families require their own provenance. This does not publish a creature
/// or grant startup readiness.</summary>
public sealed record WorldCreatureInitialActor(GoldObjectBaseState Base,
    WorldCreatureInitialComponent Component, uint RelationClass, uint BehaviorFlags,
    ushort EquipmentSelector, ushort RandomTail, float ThinkElapsed, uint LastUpdateSecond,
    // Native +0x4F4 count/+0x4F8 bytes: constructor clears 100 bytes for
    // argument <16. Index 28 gates automatic NPC level progress at 48468C.
    // Other byte meanings remain unassigned; this is not a saved-file mapping.
    byte[] Scratch)
{
    public WorldObjectNode Node { get; } = new();
    public WorldCreatureRestoredPhysical? RestoredPhysical { get; init; }
    public WorldCreatureRestoredState? RestoredCreature { get; init; }
    public bool HeroPostLoadCompleted { get; init; }
    public bool SavedEquipmentReferencesBound { get; init; }
    public bool ModelReactivated { get; init; }
    /// <summary>Private final equipment/inventory/heading stage. The earlier
    /// saved-tile/surface prelude and campaign entry are independent requirements.</summary>
    public bool SavedLoaderTailPrepared { get; init; }
    public bool SavedWorldPreludeCompleted { get; init; }
    public bool CampaignEntryPlaced { get; init; }
    /// <summary>Native +0x1FC; distinct from RuntimeFlags at +0x200.</summary>
    public ushort Flags1FC { get; init; }
    /// <summary>Native +0x1F8 locomotion/action selector, reset by C0 action 0x48.</summary>
    public uint LocomotionAction { get; init; }
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
    public float AnimationSpeedMultiplier => RestoredCreature?.AnimationSpeedMultiplier ?? 1;
    public uint PaletteMask => RestoredPhysical?.PaletteMask ?? 0;
    public ushort KiMode { get; init; }
    /// <summary>Native +0x2B6, set by CreateNPC's ECS selector operand.</summary>
    public byte SpellSelector { get; init; }
    /// <summary>Native +0x261; high word of the packed ECS selector.</summary>
    public ushort SpellParameter { get; init; }
    public ushort EiMode { get; init; }
    public uint RuntimeFlags { get; init; }
    public ushort Flags2B7 { get; init; }
    public uint Word210 => RestoredCreature?.Word210 ?? BitConverter.SingleToUInt32Bits(1);
    public uint Word564 => BitConverter.SingleToUInt32Bits(1);
    public uint[] EquipmentReferences { get; init; } = new uint[GoldCreatureStateLayout.EquipmentCount];
    public byte[] KiState { get; init; } = new byte[84];
    public byte[] EiState { get; init; } = new byte[84];
    public byte[] ActionState { get; init; } = new byte[GoldCreatureStateLayout.Block2EDSize];
}
