namespace Sacred.Network.Gameplay;

public sealed record GoldBowProjectile(uint Reference, long Generation, GoldCreatureAttackEvent Attack,
    long AttackerInstance, long AttackVersion, int Index, int Count, GoldBowActionState Action)
{
    public uint CreatureFlags { get; init; }
    public ushort FxNotification { get; init; }
    public IReadOnlyList<uint> EquipmentReferences { get; init; } = Array.Empty<uint>();
}
