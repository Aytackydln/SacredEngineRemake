using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Original file/image inputs for the compared scalar calculation.
/// Loading these definitions does not initialize or publish a runtime actor.</summary>
public sealed class WorldCreatureScalarResultAssets
{
    private readonly Lazy<SacredMovementBalance> _movement;
    private readonly Lazy<SacredCreatureHealthFactorDefinitions> _healthFactors;
    private readonly SacredCreatureResultDefinitions _initialization;
    private readonly SacredCreatureCombatDefinitions _combat;
    private readonly SacredCreatureResultFinalizationDefinitions _finalization;
    private readonly SacredCreatureResultBalanceLayout _results;
    private readonly SacredCreatureSkillBalanceLayout _skills;
    private readonly SacredCreatureEquipmentLevelBalanceLayout _equipmentLevels;
    private readonly SacredCreatureBalance _health;

    private WorldCreatureScalarResultAssets(byte[] image, byte[] balance)
    {
        _healthFactors = new(() => SacredCreatureHealthFactorDefinitions.Read(image));
        _movement = new(() => BalanceBinArchive.Parse(balance));
        StartupPrecision = SacredStartupFloatingPointDefinitions.Read(image).Precision;
        _initialization = SacredCreatureResultDefinitions.Read(image);
        _combat = SacredCreatureCombatDefinitions.Read(image);
        _finalization = SacredCreatureResultFinalizationDefinitions.Read(image);
        _results = CreatureResultBalanceBinReader.Read(balance);
        _skills = CreatureSkillBalanceBinReader.Read(balance);
        _equipmentLevels = CreatureEquipmentLevelBalanceBinReader.Read(balance);
        _health = CreatureBalanceBinReader.Read(balance);
    }

    public Sacred.Core.Runtime.SacredX87Precision StartupPrecision { get; }

    public WorldCreatureScalarResult Calculate(byte[] inputs, SacredCreatureTemplate? template,
        WorldCreatureScalarResultContext context, IReadOnlyList<WorldCreatureScalarEquipment> equipment)
    {
        if (context.Precision != StartupPrecision)
            throw new NotSupportedException("Requested scalar mode differs from original startup precision.");
        return WorldCreatureScalarResults.Calculate(inputs, template, context, equipment,
            _initialization, _combat, _finalization, _results, _skills, _equipmentLevels, template?.Class == SacredCreatureClass.Horse ? _movement.Value : null);
    }

    public static WorldCreatureScalarResultAssets Load(string installation)
    {
        var balance = File.ReadAllBytes(Path.Combine(installation, "bin/Balance.bin"));
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(installation, filename);
            if (!File.Exists(path)) continue;
            try { return new(File.ReadAllBytes(path), balance); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Original NPC scalar-result definitions unavailable.", failure);
    }

    public static WorldCreatureScalarResultAssets Read(byte[] image, byte[] balance) => new(image, balance);

    public void CalculateHealth(byte[] values, byte[] health, WorldCreatureHealthContext context)
    {
        if (context.Precision != StartupPrecision)
            throw new NotSupportedException("Requested health mode differs from original startup precision.");
        if (context.Owner == context.LocalReference &&
            ((context.BehaviorFlags & 0x80000) != 0 || (context.StatusFlags & 0x4000) != 0))
        {
            if (StartupPrecision != Sacred.Core.Runtime.SacredX87Precision.Single ||
                context.ObjectType is not { } type || context.CreatureClass is not { } creatureClass)
                throw new NotSupportedException("Special health requires compared startup precision and original creature identity/class.");
            var factor = (context.BehaviorFlags & 0x80000) != 0 ? _health.BehaviorHealthFactor : _health.StatusHealthFactor;
            if (factor is null) throw new InvalidDataException("Original special-health Balance factor unavailable.");
            // Gold 0x426B70 reads the resolved Creature.pak class byte and compares Dragon.
            context = context with { SpecialFactor = _healthFactors.Value.FactorStartup(factor.Value,
                type, context.Difficulty, creatureClass == SacredCreatureClass.Dragon) };
        }
        WorldCreatureHealthResults.Apply(values, health, context, _health);
    }
}
