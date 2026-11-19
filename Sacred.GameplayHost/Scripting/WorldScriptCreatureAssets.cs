using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.World;
using Sacred.Assets.Equipment;
using Sacred.Assets.Paks.Models;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Original-file dependencies for the initial script creature stages.</summary>
public sealed record WorldScriptCreatureAssets(SacredObjectTemplateArchive Objects, CreaturePakArchive Creatures,
    WorldCreatureScalarResultAssets Results, SacredCreatureConstructionDefinitions Construction,
    SacredCreatureLevelBalance Levels, SacredCreatureBalance Progression, SacredCreatureLevelDefinitions LevelDefinitions,
    SacredSectorIndex Sectors)
{
    public SacredEquipmentGenerationData? Equipment { get; init; }
    public SacredEquipmentSlotDefinitions? EquipmentSlots { get; init; }
    public SacredScriptObjectValueDefinitions? ObjectValues { get; init; }
    public ModelsMotionAvailability? Motions { get; init; }
    public SacredFixedEffectDefinitions? FixedEffects { get; init; }
    public SacredMagicPrisonDefinitions? MagicPrison { get; init; }
    public SacredBubblePrisonDefinitions? BubblePrison { get; init; }
    public SacredHorseOwnerDefinitions? HorseOwner { get; init; }
    public SacredHorseValueBalance? HorseValues { get; init; }
    public SacredCreatureScriptServiceDefinitions? Services { get; init; }
    public SacredCreatureAutomaticLevelDefinitions? AutomaticLevelDefinitions { get; init; }
    public Func<uint, KeyxSectorRecord, byte[]>? ReadOutdoorTiles { get; init; }
    public Func<uint, KeyxSectorRecord, Sacred.World.WldxSectorPayload>? ReadSectorTiles { get; init; }
    public Func<uint, StaticObjectRecord?>? ReadStaticObject { get; init; }
    public static WorldScriptCreatureAssets Load(string installation, SacredObjectTemplateArchive objects, SacredSectorIndex sectors)
    {
        var balance = File.ReadAllBytes(Path.Combine(installation, "bin/Balance.bin"));
        return new(objects, CreaturePakArchive.Load(Path.Combine(installation, "pak/Creature.pak")),
            WorldCreatureScalarResultAssets.Load(installation), SacredCreatureConstructionDefinitions.Load(installation),
            CreatureLevelBalanceBinReader.Read(balance), CreatureBalanceBinReader.Read(balance),
            SacredCreatureLevelDefinitions.Load(installation), sectors)
            { Equipment = SacredEquipmentGenerationData.Load(installation), EquipmentSlots = SacredEquipmentSlotDefinitions.Load(installation),
                ObjectValues = SacredScriptObjectValueDefinitions.Load(installation),
                Motions = ModelsMotionAvailability.Load(installation), FixedEffects = SacredFixedEffectDefinitions.Load(installation),
                MagicPrison = SacredMagicPrisonDefinitions.Load(installation), BubblePrison = SacredBubblePrisonDefinitions.Load(installation),
                HorseOwner = SacredHorseOwnerDefinitions.Load(installation), HorseValues = HorseValueBalanceBinReader.Read(balance),
                Services = SacredCreatureScriptServiceDefinitions.Load(installation),
                AutomaticLevelDefinitions=SacredCreatureAutomaticLevelDefinitions.Load(installation), ReadOutdoorTiles = (id,entry) =>
                {
                    using var loader = new Sacred.World.WldxLoader(new FileStream(Path.Combine(installation,"World/sectors.wldx"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite));
                    return loader.LoadSector(id,entry).OutdoorTiles;
                }, ReadSectorTiles=(id,entry)=>
                {
                    using var loader = new Sacred.World.WldxLoader(new FileStream(Path.Combine(installation,"World/sectors.wldx"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite));
                    return loader.LoadSector(id,entry);
                }, ReadStaticObject=id=>
                {
                    using var archive=Sacred.Assets.World.Static.StaticPakArchive.Load(Path.Combine(installation,"World/Static.pak"));
                    return archive.Get(id);
                } };
    }
}
