using System.Buffers.Binary;
using Sacred.Assets.Equipment;
using Sacred.Assets.Executable;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold SetCV non-hero startup mutation. Recalculates the current level
/// without difficulty adjustment, preserves the authored level marker and restores
/// current health with the original two integer divisions before final results.</summary>
public static class WorldCreatureScriptBaseAttribute
{
    public static WorldCreatureInitialActor Apply(WorldCreatureInitialActor actor, int index, int value,
        SacredCreatureTemplate template, WorldCreatureScalarResultAssets results,
        SacredCreatureLevelBalance levels, SacredCreatureBalance progression,
        SacredCreatureLevelDefinitions definitions, SacredEquipmentGenerationData equipment,
        uint difficulty, uint localReference, bool networking, uint players)
    {
        var values = actor.Component.Values.ToArray();
        var health = actor.Component.Health;
        var maximum = BinaryPrimitives.ReadInt32LittleEndian(health.AsSpan(SacredCreatureHealthLayout.MaximumHitPointsOffset));
        var current = BinaryPrimitives.ReadInt32LittleEndian(health.AsSpan(SacredCreatureHealthLayout.CurrentHitPointsOffset));
        var level = BinaryPrimitives.ReadUInt16LittleEndian(values.AsSpan(78));
        if (maximum <= 0 || level == 0 || actor.Flags2B7 != 0)
            throw new NotSupportedException("SetCV requires compared positive health/level and initial creature status.");
        if ((uint)index < SacredActorValuesLayout.BaseAttributeCount)
            BinaryPrimitives.WriteUInt16LittleEndian(values.AsSpan(SacredActorValuesLayout.BaseAttributesOffset + index * 2), (ushort)Math.Clamp(value, 0, 100));
        var marker = values[80];
        var next = WorldCreatureScriptLevel.Apply(actor with { Component = actor.Component with { Values = values } },
            template, level, results, levels, progression, definitions, difficulty, localReference, networking, 0, players);
        next.Component.Values[80] = marker;
        var nextMaximum = BinaryPrimitives.ReadInt32LittleEndian(next.Component.Health.AsSpan(SacredCreatureHealthLayout.MaximumHitPointsOffset));
        var restored = unchecked(nextMaximum * (unchecked(current * 100) / maximum)) / 100;
        BinaryPrimitives.WriteInt32LittleEndian(next.Component.Health.AsSpan(SacredCreatureHealthLayout.CurrentHitPointsOffset), restored);
        next = WorldCreatureEquippedResults.Apply(next, template, results, equipment,
            new Dictionary<uint, WorldEquipmentInitialItem>(), difficulty, localReference, networking, players);
        return next with { Component = next.Component with { ResultValid = 1 } };
    }
}
