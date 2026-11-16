using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;
using Sacred.Core.Pak.Creature;
using Sacred.GameplayHost.Runtime;

namespace Sacred.GameplayHost.Scripting;

public readonly record struct WorldCreatureHealthContext(byte Owner, uint LocalReference, uint Difficulty,
    bool NetworkActive, uint Players, uint BehaviorFlags, ushort StatusFlags, uint ComponentFlags, bool Mounted, bool Hero,
    SacredX87Precision Precision)
{
    public uint? ObjectType { get; init; }
    public SacredCreatureClass? CreatureClass { get; init; }
    public float? SpecialFactor { get; init; }
}

/// <summary>Gold 0x5658F0 owner gate, ordinary health and TP mutations. Special factors must be supplied from the original identity/class definitions;
/// mounted health still requires its native runtime queries.
/// Runtime Basis/current physical-regeneration words are used exactly as native;
/// their positions differ from an archive-order reconstruction.</summary>
public static class WorldCreatureHealthResults
{
    public static void Apply(byte[] values, byte[] health, WorldCreatureHealthContext context, SacredCreatureBalance balance)
    {
        if (values.Length != SacredActorValuesLayout.Size || health.Length != GoldCreatureValuesLayout.Block128Size)
            throw new ArgumentException("Expected 238-byte actor values and 24-byte TP state.");
        if (context.Owner != context.LocalReference) return;
        if (context.Precision is not (SacredX87Precision.Single or SacredX87Precision.Double or SacredX87Precision.Extended))
            throw new NotSupportedException("Health calculation requires a compared x87 precision mode.");
        if ((((context.BehaviorFlags & 0x80000) != 0 || (context.StatusFlags & 0x4000) != 0) && context.SpecialFactor is null) || context.Mounted)
            throw new NotSupportedException("Boss factors and mounted health require their native runtime queries.");
        if (context.SpecialFactor is not null && context.Precision != SacredX87Precision.Single)
            throw new NotSupportedException("Special health factors require compared startup precision.");
        byte vitality = 0;
        for (var slot = 0; slot < 8; slot++)
            if (values[28 + slot] == 10) { vitality = (byte)Math.Min(255, values[36 + slot] + values[44 + slot]); break; }
        var maximum = SacredCreatureHealth.Calculate(context.Precision, Word(78), Word(66), Word(72), Word(8), Word(14), vitality,
            balance, context.Difficulty, context.Hero, context.NetworkActive ? context.Players : 0, context.SpecialFactor ?? 1, (context.ComponentFlags & 0x80000) != 0);
        BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(4), maximum);
        var current = BinaryPrimitives.ReadInt32LittleEndian(health);
        if (current > maximum || current == 100 && BinaryPrimitives.ReadInt32LittleEndian(health.AsSpan(8)) == 100)
        {
            BinaryPrimitives.WriteInt32LittleEndian(health, maximum);
            BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(8), maximum);
        }
        ushort Word(int at) => BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(at));
    }
}
