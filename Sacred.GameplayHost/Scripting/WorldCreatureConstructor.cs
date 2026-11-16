using System.Buffers.Binary;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

public readonly record struct WorldCreatureConstructorContext(uint Argument, uint Clock, uint Difficulty, uint LocalReference);
public enum WorldCreatureConstructionStep { PhysicalLevelEvent, ComponentInitialized, CreatureLevelEvent, LevelSet, ResultsCalculated, HealthCalculated }
public sealed record WorldCreatureConstructionObservation(WorldCreatureConstructionStep Step, uint Behavior,
    byte[] Values, byte[] Health, byte CombatProfile, byte ResultValid);

/// <summary>Gold 0x52DE40 creature logical constructor, including the empty hero scalar path. Real component
/// initialization precedes actor defaults; SetLevel and the explicit final pass
/// calculate again after the template overlay. Equipment/model creation,
/// registration, placement and publication are later factory obligations.</summary>
public static class WorldCreatureConstructor
{
    public static WorldCreatureInitialActor Create(SacredCreatureTemplate template,
        WorldCreatureScalarResultAssets results, SacredCreatureConstructionDefinitions definitions,
        SacredRandomSequence random, WorldCreatureConstructorContext context,
        Action<WorldCreatureConstructionObservation>? observe = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(random);
        if (context.LocalReference is 0 or > 16)
            throw new NotSupportedException("Construction requires an initialized local controller.");
        var scalarContext = new WorldCreatureScalarResultContext(template.ItemType, 0, context.Difficulty, false,
            0, 0, 0, 0, 0, 0, false, default, context.LocalReference, results.StartupPrecision);
        uint behavior = 0;
        var component = WorldCreatureComponentInitialization.Create(template, scalarContext, results, (values, health) =>
        {
            ResultBehavior();
            _ = WorldCreatureConstructionEvents.LevelChanged(false, 0, 0);
            Observe(WorldCreatureConstructionStep.PhysicalLevelEvent, values, health, 0, 0);
        });
        ObserveComponent(WorldCreatureConstructionStep.ComponentInitialized);
        var tail = (ushort)(random.Next() % 6);
        var elapsed = (random.Next() % 100 - 49) * 0.1f; // Startup PC=24 multiplication, then float store.
        behavior = definitions.EarlyFlags(template.ItemType).BehaviorFlags;
        scalarContext = scalarContext with { CombatProfile = component.CombatProfile };

        // Unassigned reference zero skips level adjustment, experience and skill
        // updates. The seeded level is one; SetLevel still recalculates/refills
        // and dispatches the canonical event through the now-creature vtable.
        Recalculate();
        var maximum = BinaryPrimitives.ReadInt32LittleEndian(component.Health.AsSpan(4));
        if (maximum == 0) maximum = 100;
        for (var at = 0; at < 12; at += 4) BinaryPrimitives.WriteInt32LittleEndian(component.Health.AsSpan(at), maximum);
        _ = WorldCreatureConstructionEvents.LevelChanged(true, 0, 0);
        ObserveComponent(WorldCreatureConstructionStep.CreatureLevelEvent);
        ObserveComponent(WorldCreatureConstructionStep.LevelSet);
        Recalculate();
        ObserveComponent(WorldCreatureConstructionStep.ResultsCalculated);
        Health();
        ObserveComponent(WorldCreatureConstructionStep.HealthCalculated);

        var flags = definitions.Flags(template.ItemType);
        behavior |= flags.BehaviorFlags;
        if ((template.Flags & 0x20) != 0) behavior |= 0x01000000;
        var second = context.Clock / 1000;
        return new(new GoldObjectBaseState(0, template.ItemType, 2 | flags.ObjectFlags, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            component, (uint)template.Class, behavior, definitions.EquipmentSelector(template.ItemType), tail,
            elapsed, second > 1 ? second - 1 : 0, context.Argument < 16 ? new byte[100] : [])
        {
            RuntimeFlags = template.Class == SacredCreatureClass.Hero && context.Argument is > 0 and <= 16 ? 0x80040u : 0,
            ThinkInterval = template.Class == SacredCreatureClass.Hero && context.Argument is > 0 and <= 16 ? 0.3f : 5
        };

        void Recalculate()
        {
            ResultBehavior();
            var calculated = results.Calculate(component.Values, template,
                scalarContext with { Regeneration = BinaryPrimitives.ReadUInt32LittleEndian(component.Health.AsSpan(16)) }, []);
            calculated.Values.CopyTo(component.Values, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(component.Health.AsSpan(16), calculated.Regeneration);
            Health();
        }
        // Gold 0x5797D2..0x579821, before the scalar input reset: clear
        // behavior bit 31; a resolved template's flag 0x40 adds bit 27.
        void ResultBehavior() => behavior = (behavior & 0x7FFFFFFF) | ((template.Flags & 0x40) != 0 ? 0x08000000u : 0);
        void Health() => results.CalculateHealth(component.Values, component.Health,
            new(0, context.LocalReference, context.Difficulty, true, 0, behavior, 0, 0, false, false, results.StartupPrecision));
        void ObserveComponent(WorldCreatureConstructionStep step) => Observe(step, component.Values, component.Health, component.CombatProfile, component.ResultValid);
        void Observe(WorldCreatureConstructionStep step, byte[] values, byte[] health, byte profile, byte valid) =>
            observe?.Invoke(new(step, behavior, (byte[])values.Clone(), (byte[])health.Clone(), profile, valid));
    }
}
