using Sacred.Core.Pak.Items;

namespace Sacred.Core.Loot;

public enum SacredCreatureLootChoiceKind { Resource, Potion, Equipment }

/// <summary>Decision before native resource lookup/preparation/construction.
/// Potion and equipment choices require their real selectors to run next.</summary>
public readonly record struct SacredCreatureLootChoice(SacredCreatureLootChoiceKind Kind,
    uint Selector, uint Resource = 0, SacredItemCategory Category = SacredItemCategory.Unspecified,
    uint HeroPreference = 0, byte PreferenceFlags = 0, byte Quality = 0, bool EmitRuneEffect = false);

public readonly record struct SacredCreatureLootSource(uint Reference, uint Resource,
    uint Category, ushort Level, uint HeroResource, uint Selector, byte Quality);

/// <summary>Gold's session-wide last-source references (0xAD3FFC / 0xAD4000).
/// These are shared across events, not reset for each item or source.</summary>
public sealed class SacredCreatureLootSelectionState
{
    public uint LastRuneSource { get; set; }
    public uint LastJewelrySource { get; set; }
    public bool RuneDropsDisabled { get; set; }
}
