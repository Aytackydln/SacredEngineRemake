using Sacred.GameplayHost.Scripting;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Network.Gameplay;
using Sacred.Core.Pak.Items;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Private initial item state through generation and installation. The
/// ordinary item/base/physical constructors copy resolved original values and
/// initialize the item scheduler period independently of creature timing.</summary>
public sealed class WorldEquipmentInitialItem(WorldEquipmentObject physical, byte[] values)
{
    public WorldEquipmentObject Physical { get; } = physical;
    /// <summary>Resolved original descriptor category retained by the actual factory.
    /// Null when an external producer has not established that source.</summary>
    public SacredItemCategory? ResourceCategory { get; internal init; }
    public byte[] Values { get; internal set; } = values;
    public bool Generated { get; internal set; }
    internal bool OriginalScriptValuesPrepared { get; set; }
    /// <summary>Compared initial NPC own values, generated or retained from the
    /// original Shield template. Does not grant serialization/network readiness.</summary>
    public bool InitialValuesPrepared => Generated || OriginalScriptValuesPrepared;
    public uint? Quality { get; internal set; }
    public uint? LegacyGenerationSelector { get; internal set; }
    public SacredEquipmentNameResult? Name { get; internal set; }
    /// <summary>Native +0x30 dynamic tile-chain successor, independent of the attachment/floor link.</summary>
    public uint NextOnTile { get; internal set; }
    public WorldObjectNode Node { get; } = new() { Flags = 2, PeriodMilliseconds = 200000 };
    public WorldEquipmentSavedState? SavedState { get; internal set; }
    public bool SavedPostLoadCompleted { get; internal set; }
    public short QuestNumber => SavedState?.QuestNumber ?? -1;
    public ushort QuestEffect => SavedState?.QuestEffect ?? 0;
    public uint InventoryId { get; internal set; }
    /// <summary>Native item +0x1DC constructor float; purpose is unresolved.</summary>
    public float RuntimeFloat1DC => 0.1f;
    /// <summary>Prepared stream retained within its initial NPC publication graph.</summary>
    public GoldItemState? SerializedState { get; internal set; }
    public bool PacketReady => SerializedState is not null;
}

