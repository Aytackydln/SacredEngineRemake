using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldCreatureInitialComponent(byte[] Values, byte[] Health, byte CombatProfile, byte ResultValid)
{
    /// <summary>Native component +0x148, initialized to zero; serialized after
    /// compact/full value lists by 585C60.</summary>
    public uint Flags148 { get; init; }
}

/// <summary>Gold 0x55E1B0/0x586C00 component scalar values. SetLevel calculates
/// with still-zero Basis/skills, refills TP, and delivers actor event 6 BEFORE
/// the template input overlay. The required callback is the outer actor's work;
/// this does not construct that actor, attach models or authorize publication.</summary>
public static class WorldCreatureComponentInitialization
{
    public static WorldCreatureInitialComponent Create(SacredCreatureTemplate template,
        WorldCreatureScalarResultContext context, WorldCreatureScalarResultAssets assets,
        Action<byte[], byte[]> levelChanged)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(levelChanged);
        if (context.Reference != 0 ||
            context.Resource != template.ItemType || context.Regeneration != 0 || context.ComponentFlags != 0 || context.Skills != default)
            throw new NotSupportedException("Component initialization requires original empty unassigned on-foot state.");
        var inputs = new byte[SacredActorValuesLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(inputs, template.ItemType);
        BinaryPrimitives.WriteUInt16LittleEndian(inputs.AsSpan(78), 1);
        var calculated = assets.Calculate(inputs, template, context with { ActorTypeAssigned = false }, []);
        var health = new byte[GoldCreatureValuesLayout.Block128Size];
        // Unassigned owner zero leaves maximum health zero in CalcResults;
        // SetLevel then supplies its native fallback and refills TP[0..2].
        for (var at = 0; at < 12; at += 4) BinaryPrimitives.WriteInt32LittleEndian(health.AsSpan(at), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), calculated.Regeneration);
        levelChanged(calculated.Values, health);
        WorldCreatureComponentSeed.Create(template.ItemType, template).AsSpan(0, 82).CopyTo(calculated.Values);
        if (template.Class == SacredCreatureClass.Hero)
        {
            // Gold 55FE20 -> 580750 calculates after each initial acquired art.
            // In the empty unassigned level-one constructor those entries do not
            // alter the scalar pass, but its seeded inputs must be calculated.
            var seeded = assets.Calculate(calculated.Values, template,
                context with { ActorTypeAssigned = false, CombatProfile = 6 }, []);
            seeded.Values.CopyTo(calculated.Values, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(health.AsSpan(16), seeded.Regeneration);
        }
        return new(calculated.Values, health, 6, 0);
    }
}
