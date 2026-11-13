using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldScriptCreatedItem(int SourceOffset, WorldEquipmentInitialItem Item,
    IReadOnlyList<GoldObjectScriptCallback> Callbacks, GoldMessage Publication)
{
    public GoldObjectBaseState BaseState => Item.Physical.Base;
    public bool PacketReady => Item.PacketReady;
}
