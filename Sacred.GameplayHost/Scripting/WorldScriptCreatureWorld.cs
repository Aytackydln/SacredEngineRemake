using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.Network.Gameplay;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Creature;
using Sacred.GameplayHost.Equipment;
using System.Buffers.Binary;
using Sacred.GameplayHost.Runtime;
using Sacred.GameplayHost.Networking;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial CreateNPC stages in a fresh KEYX-only startup world. Native
/// sector runtime flags begin at zero; randomized sampling can demand-load tiles.
/// Loaded sectors use the compared positioned factory and original indoor grids.
/// Supported ordinary commands retain complete publication buffers;
/// unresolved special branches still fail without granting complete campaign upload.</summary>
public sealed class WorldScriptCreatureWorld : IWorldScriptCreatureWorld, IWorldScriptEquipmentWorld, IWorldScriptCreatureStateWorld, IWorldScriptObjectStateWorld, IWorldScriptCreatureAttributeWorld, IWorldScriptCreatureHealthWorld
{
    private readonly WorldScriptCreatureAssets _assets;
    private readonly WorldScriptCreatureRuntime _runtime;
    private readonly GoldObjectReferencePool _references;
    private readonly SacredSectorGrid _grid;
    private readonly Action<string> _log;
    private readonly WorldHostActiveHeroBinding? _activeHero;
    private readonly List<WorldScriptCreaturePreparation> _prepared = [];
    public WorldScriptCreatureWorld(WorldScriptCreatureAssets assets, GoldObjectReferencePool references,
        WorldObjectAllocationCounter counter, WorldScriptCreatureRuntime runtime, SacredRandomSequence random, Action<string> log)
        : this(assets, references, counter, runtime, random, log, null) { }

    internal WorldScriptCreatureWorld(WorldScriptCreatureAssets assets, GoldObjectReferencePool references,
        WorldObjectAllocationCounter counter, WorldScriptCreatureRuntime runtime, SacredRandomSequence random, Action<string> log,
        WorldHostActiveHeroBinding? activeHero)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(runtime.Clock);
        ArgumentNullException.ThrowIfNull(log);
        (_assets, _runtime, _references, Random, _log) = (assets, runtime, references, random, log);
        _activeHero = activeHero;
        _grid = assets.Sectors.CreateGrid();
        ScriptRegions = new(_grid,assets.Equipment?.Naming?.Resources,runtime.Owner);
        MapIcons = new(_grid, assets.Equipment?.Naming?.Resources);
        Factory = activeHero?.Factory ?? new(assets.Results, assets.Construction, references, counter, random, runtime.Owner, runtime.Difficulty);
        EquipmentFactory = activeHero?.EquipmentFactory ?? new(references, counter, Factory.Activation, runtime.Owner);
        StateCommands = new(this, assets, runtime, log);
        ObjectStateCommands = new(this,log);
        EquipmentCommands = new(this, assets, runtime, log);
        if (activeHero is not null) Terrain = activeHero.Terrain.BeginStartup(this, assets, log);
        else if (assets.ReadOutdoorTiles is { } readTiles && !runtime.Owned && runtime.Players == 0)
            Terrain = new(this,assets,readTiles,log);
        if (Terrain is { } terrain && assets.Equipment?.Naming?.Resources is { } resources)
        {
            Mouseovers = new(name => WorldScriptResourceReference.Resolve("Res:" + name, resources), terrain.MarkMouseover);
            Hideouts = new(name => WorldScriptResourceReference.Resolve(name,resources),terrain.GetInitialSectorFlags);
        }
        ItemResources = new(Resources, assets.Equipment?.Naming?.Resources);
        Items = new(assets.Objects, EquipmentFactory, runtime, _grid, log)
            { PrepareCurrentValues = activeHero is null ? null : PrepareObjectValues,
                PrepareWeaponValues = activeHero is null ? null : PrepareInitialWeapon };
        if (assets.FixedEffects is { } effects && runtime.EffectProjection is { } projection)
            Effects = new(effects,projection,references,counter,Factory.Activation,random,runtime,_grid,log) { MagicPrison = assets.MagicPrison, BubblePrison = assets.BubblePrison };
        Prepared = _prepared.AsReadOnly();
    }
    public WorldScriptRegions ScriptRegions { get; }
    public WorldScriptObjectStateCommands ObjectStateCommands { get; }
    public void SetObjectState(WorldScriptExecutionContext context,SacredScriptCommand command)=>ObjectStateCommands.SetObjectState(context,command);
    public WorldScriptCreatureStateWorld StateCommands { get; }
    public WorldScriptStartupTerrain? Terrain { get; }
    public void SetState(WorldScriptExecutionContext context, SacredScriptCommand command) => StateCommands.SetState(context,command);
    public void SetBaseAttribute(WorldScriptExecutionContext context, SacredScriptCommand command) =>
        WorldScriptCreatureAttributeCommand.Apply(context, command, this, _assets, _runtime, _log);
    public void SetHitPoints(WorldScriptExecutionContext context, SacredScriptCommand command) =>
        WorldScriptCreatureHealthCommand.Apply(context, command, this, _assets, _runtime, _log);
    internal void UpdateScriptActor(WorldCreatureInitialActor actor)
    {
        if (_activeHero is not null && actor.Base.ObjectReference == _runtime.Owner)
        {
            _activeHero.UpdateActor(actor);
            return;
        }
        Factory.Update(actor);
        var prepared = _prepared.Single(value => value.Actor?.Base.ObjectReference == actor.Base.ObjectReference);
        prepared.Actor = actor;
        prepared.CreatureState = WorldCreatureInitialSerialization.Write(actor,prepared.Blueprint.Visuals,EquipmentFactory.Items,_assets.Objects.GetItem);
    }
    public Sacred.Core.GameRes.GameResStore? ResourceText => _assets.Equipment?.Naming?.Resources;
    public byte LocalPlayerSlot => _runtime.Owner;
    internal byte[] PrepareObjectValues(uint resource, byte[] original)
    {
        var hero = _activeHero?.Actor ?? throw new NotSupportedException("CreateObj values require the actual entered local hero binding.");
        return WorldScriptObjectValues.Prepare(resource, original, hero.Base.ObjectType,
            _assets.Equipment ?? throw new InvalidDataException("CreateObj requires original equipment data."),
            _assets.ObjectValues ?? throw new InvalidDataException("CreateObj requires original item-value exclusion definitions."), Random);
    }
    public SacredRandomSequence Random { get; }
    internal bool IsEnteredHeroReference(uint reference) => _activeHero is { } hero &&
        reference == hero.Actor.Base.ObjectReference && Factory.Actors.TryGetValue(reference, out var actor) && ReferenceEquals(actor, hero.Actor);
    internal void EnterHeroPopup(WorldScriptExecutionContext context, int selected)
    {
        if (!context.IsExecutingStartup || !context.Multiplayer)
            throw new NotSupportedException("Entered hero POPUP requires its current multiplayer startup scope.");
        var hero = _activeHero ?? throw new NotSupportedException("POPUP requires the actual entered hero binding.");
        hero.RequireCurrent();
        var result = WorldScriptEnteredPopup.Prepare(hero.Actor, context.Registrations, selected);
        var state = WorldCreatureHostSerialization.Write(result.Dialog, EquipmentFactory.Items, _assets.Objects.GetItem, 0);
        StateCommands.RetainPublication(GoldCreatureUpdateCodec.CreateState(_runtime.Owner,
            result.Dialog.Base.ObjectReference, result.Dialog.Base.ObjectType, GoldCreatureUpdateState.FromCreature(state)));
        hero.UpdateActor(result.Pending);
    }
    internal bool? MatchEnteredHero(string name)
    {
        if (_activeHero is not { } hero || !name.StartsWith("TYPE_", StringComparison.OrdinalIgnoreCase)) return null;
        var lookup = _assets.Equipment?.ResourceName ?? throw new NotSupportedException("IsHero needs the original symbolic type table.");
        var resolved = _assets.Objects.Items.Keys.FirstOrDefault(type => string.Equals(lookup(type), name, StringComparison.OrdinalIgnoreCase));
        return WorldScriptHeroPredicates.Matches(hero.Actor.Base.ObjectType, resolved);
    }
    internal void PrepareEnteredQuestHero(int number)
    {
        var hero = _activeHero ?? throw new NotSupportedException("Present-hero quest entry requires the actual entered host binding.");
        hero.RequireCurrent();
        var flags = _runtime.SessionGameFlags ?? throw new NotSupportedException("Quest entry requires received session game flags.");
        hero.UpdateActor(WorldScriptEnteredQuestHero.Prepare(hero.Actor, number, flags, connected: true));
    }
    private void PrepareInitialWeapon(Sacred.GameplayHost.Equipment.WorldEquipmentInitialItem item, GoldObjectPosition position)
    {
        var hero = _activeHero ?? throw new NotSupportedException("CreateObj weapon needs its actual entered hero.");
        hero.RequireCurrent();
        if (_assets.Sectors.TryGetSector(position.Sector, out var sector) && sector.Environment.MonsterMaxLevel != 0)
            throw new NotSupportedException("CreateObj weapon with nonzero sector maximum requires current level-bound/addition inputs.");
        // 487C1B/487C2D skips every sector adjustment and the runtime addition
        // when the actual current sector has no maximum. Startup has not run
        // sector callbacks; their mutable environment lifecycle remains separate.
        WorldScriptInitialWeaponValues.Apply(item, hero.Actor.Base.ObjectType, hero.Level, _runtime.Difficulty,
            _assets.Equipment ?? throw new InvalidDataException("CreateObj weapon needs original generation data."), Random);
    }
    internal double SchedulerClock => _runtime.Clock() / 1000d;
    public WorldCreatureInitialFactory Factory { get; }
    public WorldEquipmentInitialFactory EquipmentFactory { get; }
    public WorldScriptEquipmentWorld EquipmentCommands { get; }
    public void Equip(WorldScriptExecutionContext context, SacredScriptCommand command) => EquipmentCommands.Equip(context, command);
    public WorldScriptItemResources ItemResources { get; }
    public WorldScriptItemWorld Items { get; }
    public IReadOnlyList<WorldScriptCreaturePreparation> Prepared { get; }
    public WorldScriptCreaturePreparation? Pending { get; private set; }
    public int SkippedEntryCreatures { get; private set; }
    internal WorldHostAmbientCreatureFactory AmbientFactory(WorldScriptExecutionContext context) => new(this, _assets, _runtime, context);
    public GoldScriptResourceReplica Resources { get; } = new();
    public WorldScriptMapIcons MapIcons { get; }
    public WorldScriptMouseovers? Mouseovers { get; }
    public WorldScriptHideouts? Hideouts { get; }
    public WorldScriptEffectWorld? Effects { get; }

    private WorldCreatureLoadedPlacementPlan PrepareEnteredCreature(WorldScriptExecutionContext context,
        WorldScriptCreatureBlueprint blueprint, WorldScriptSectorEnvironment environment)
    {
        var creation = blueprint.Creation;
        if (!context.Multiplayer || context.HasActiveHero != true || _activeHero is null ||
            context.CurrentSectorRuntimeFlags is not { } flags || (flags & GoldSectorEntryRuntime.EnteredFlag) == 0 ||
            _runtime.EnteredAutomaticLevelInputs is null || _runtime.EnteredPlayerCount?.Invoke() is null ||
            _runtime.Difficulty != 1 || _assets.Results.StartupPrecision != SacredX87Precision.Single)
            throw new NotSupportedException("Entered NPC requires the actual initial multiplayer hero, statistics, player count and entered-sector runtime.");
        _activeHero.RequireCurrent();
        if (!creation.HasExplicitBehavior || creation.Behavior != 0x4000 || creation.Aggression != -1 ||
            creation.ConstructionFlags != 0 || creation.Flag2B7Mask08Input != -1 || creation.Group != 0 ||
            creation.AssignedLevelInput != 0 || creation.LevelInput != 0 || creation.PackedSpell != 0 ||
            creation.DialogSelection is not null || creation.CallbackName is not null || creation.SeeCallbackName is not null ||
            blueprint.Template.Class is SacredCreatureClass.Hero or SacredCreatureClass.Horse ||
            WorldScriptCreatureResourceNames.Resolve(creation.Name, Resources, ResourceText) != GoldScriptResourceLayout.UnnamedResource)
            throw new NotSupportedException("Entered NPC requires its compared unnamed ordinary mercenary construction profile.");
        if (_assets.Equipment is null || _assets.EquipmentSlots is null || _assets.AutomaticLevelDefinitions is null ||
            blueprint.Equipment.Count != 2 || blueprint.Equipment[0].ModelDesc.Category != SacredItemCategory.Shield ||
            blueprint.Equipment[1].ModelDesc.Category != SacredItemCategory.Weapon)
            throw new NotSupportedException("Entered NPC requires its original Shield and Weapon hand equipment definitions.");
        foreach (var descriptor in blueprint.Equipment)
            RequireEquipment(blueprint, descriptor);
        var position = WorldScriptCreaturePlacement.Resolve(creation, _grid, _assets.Results.StartupPrecision);
        if (position is not { } resolved || resolved.Sector != environment.Sector ||
            Terrain is not { } terrain || !terrain.Tiles.IsLoaded(resolved.Sector))
            throw new NotSupportedException("Entered NPC requires its original position on the same loaded callback sector.");
        var plan = terrain.PreparePlacement(context, resolved);
        if (plan.Position.Surface != 0 || plan.StaticSurfaceReference != 0 || plan.TriggerReference != 0)
            throw new NotSupportedException("Entered NPC requires its compared outdoor placement without a floor trigger.");
        _ = _references.PeekAvailable(1 + blueprint.Equipment.Count);
        return plan;
    }

    internal void PrepareEnteredCreature(WorldScriptExecutionContext context, SacredScriptCommand command,
        WorldScriptSectorEnvironment environment)
    {
        // Entry preflight uses the same literal-only decoder as the native skip
        // check. It must not sample a DefPos or advance the shared RNG.
        var blueprint = WorldScriptCreatureBlueprint.Read(context, command, _assets.Objects, _assets.Creatures);
        _ = PrepareEnteredCreature(context, blueprint, environment);
    }

    public void Create(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        var entered = context.IsEnteringSector;
        if (!entered)
        {
            if (context.HasActiveHero == true) _ = _runtime.RequirePlayerCount();
            if (!context.IsExecutingStartup && !(context.IsInitializingRegion && context.Multiplayer && context.HasActiveHero == false))
                throw new InvalidDataException("CreateNPC requires its compared startup or absent-hero region callback scope.");
        }
        // The native facing draw precedes decoding, even for an early return
        // or an explicit heading.
        var randomFacing = (ushort)(Random.Next() % 361);
        if (entered)
        {
            if (context.Multiplayer && context.LocalNetworkSpeedSettings is { } setting &&
                WorldScriptEnteredCreatureGate.CanSkip(context, command, setting, Resources, ResourceText))
            { SkippedEntryCreatures++; return; }
        }
        var blueprint = WorldScriptCreatureBlueprint.Read(context, command, _assets.Objects, _assets.Creatures);
        var enteredPlan = entered ? PrepareEnteredCreature(context, blueprint, context.CurrentSector ??
            throw new InvalidOperationException("Entered NPC needs its executing sector.")) : null;
        var runtime = entered ? _runtime with { Players = _runtime.EnteredPlayerCount!() } : _runtime;
        if (entered) _ = runtime.RequirePlayerCount();
        var creation = blueprint.Creation;
        if (!context.IsExecutingStartup && (creation.ConstructionFlags & 0x20) != 0)
            throw new NotSupportedException("Region HorseMerchant construction requires its additional dialog publication lifecycle.");
        WorldCreatureAutomaticScriptLevel.RequireInputs(context,creation,_runtime,_assets);
        // Gold 0x482E06 gates implicit behavior only for an owned caller.
        // This initial no-owned profile rejects that caller below; no class exception is needed.
        var pending = new WorldScriptCreaturePreparation(blueprint, creation.FacingDegrees ?? randomFacing);
        Pending = pending;
        var resource = WorldScriptCreatureResourceNames.Resolve(creation.Name, Resources, _assets.Equipment?.Naming?.Resources);
        if (resource is not (0 or uint.MaxValue) && FindObject(resource, context) is { } existing)
            throw new InvalidDataException($"CreateNPC named resource {resource} already resolves to object {existing}; native duplicate reuse/teleport requires its lifecycle.");
        // Both associated-creature branches are excluded by this supported mask,
        // independently of caller identity and quest-object table contents.
        if ((creation.ConstructionFlags & ~SacredScriptCreatureAssociationFlags.StartupServicesMask) != 0 || (creation.ConstructionFlags != 0 && _assets.Services is null) || creation.Flag2B7Mask08Input == 0)
            throw new InvalidDataException($"CreateNPC requires native named-resource, special-mode or regional replacement state before initial construction (name '{creation.Name ?? ""}', construction 0x{creation.ConstructionFlags:X}, regional input {creation.Flag2B7Mask08Input}, explicit behavior {creation.HasExplicitBehavior}, behavior 0x{creation.Behavior:X}).");
        if (_runtime.Difficulty is not (1 or 2))
            throw new InvalidDataException("CreateNPC requires higher-difficulty actor predicate services.");
        if (_runtime.Owned && !entered)
            throw new InvalidDataException("CreateNPC owned caller requires its later ownership/group/physical flag effects.");
        if (!string.IsNullOrEmpty(creation.CallbackName))
            _ = GoldObjectScriptCallback.Create(GoldReferenceCodec.FirstObjectReference, creation.CallbackName);
        if (creation.SeeCallbackName is { } seeCallback)
            _ = GoldObjectScriptCallback.Create(GoldReferenceCodec.FirstObjectReference, SacredScriptObjectCallbackLayout.SeePrefix + seeCallback);
        var position = WorldScriptCreaturePlacement.Resolve(creation,_grid,_assets.Results.StartupPrecision);
        if (position is null) { Pending = null; return; }
        ushort sectorMaximumLevel;
        if (_assets.Sectors.TryGetSector(position.Value.Sector,out var sector))
            sectorMaximumLevel=sector.Environment.MonsterMaxLevel;
        else if (position.Value.Sector==0 && creation.AssignedLevelInput==0 && creation.LevelInput==0)
            sectorMaximumLevel=0; // Zero assigned/absent-hero levels do not use it; automatic requests use the current snapshot's cap.
        else
            throw new NotSupportedException($"CreateNPC needs its original sector-zero level context or missing KEYX entry: {position.Value.X},{position.Value.Y}, surface {position.Value.Surface}, sector {position.Value.Sector}; source {creation.PlacementInput}.");
        var loadedPlacement=enteredPlan ?? (Terrain?.Tiles.IsLoaded(position.Value.Sector)==true
            ? Terrain.PreparePlacement(context,position.Value) : null);
        // 0x62373A initializes fresh sector runtime +30 to zero. KEYX +1CC
        // is copied into +2E and cannot establish tile-data readiness.
        var clock = _runtime.Clock();
        var actor = Factory.Create(blueprint.Template, blueprint.Visuals, clock, clock / 1000d, context.Multiplayer, entered || _runtime.Owned);
        pending.Actor = actor; pending.Stage = "constructed";
        _log($"Startup NPC constructed: source 0x{command.FileOffset:X}, ref {actor.Base.ObjectReference}, type {actor.Base.ObjectType}, counter {actor.Base.Byte38}.");
        if (enteredPlan is not null)
        {
            actor = WorldCreatureAmbientPlacement.Place(actor, enteredPlan);
            Terrain!.Membership.RegisterAmbient(actor, context.CurrentSectorRuntimeFlags!.Value);
        }
        else actor = loadedPlacement is null ? WorldCreatureScriptPlacement.PlaceDormant(actor,position.Value,Factory.Activation)
            : Terrain!.Place(actor,loadedPlacement);
        actor = WorldCreatureScriptPose.Apply(actor, pending.FacingDegrees);
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "placed";
        actor = WorldCreatureScriptLevel.Apply(actor, blueprint.Template, creation.AssignedLevelInput, _assets.Results,
            _assets.Levels, _assets.Progression, _assets.LevelDefinitions, _runtime.Difficulty, _runtime.Owner,
            context.Multiplayer, sectorMaximumLevel, runtime.RequirePlayerCount());
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "level initialized";
        context.ObjectCallbacks.RegisterCreature(actor.Base.ObjectReference, creation.CallbackName);
        var behavior = creation.Behavior;
        if (creation.SeeCallbackName is { } seen)
        {
            context.ObjectCallbacks.RegisterCreatureSee(actor.Base.ObjectReference, seen, creation.CallbackName);
            behavior |= 0x400000;
        }
        actor = WorldCreatureScriptControl.Apply(actor, behavior, creation.Aggression, creation.Flag2B7Mask08Input);
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "behavior initialized";
        if (resource is not (0 or uint.MaxValue))
        {
            actor = actor with { Base = WorldScriptCreatureResourceNames.Bind(actor.Base, resource, Resources,
                key => FindObject(key, context)) };
            Factory.Update(actor); pending.Actor = actor; pending.Stage = "resource bound";
        }
        actor = context.Groups.AssignInitial(actor, creation.Group);
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "group initialized";
        var selectedDialog = WorldCreatureScriptDialog.Resolve(context.Registrations, creation.DialogSelection);
        actor = WorldCreatureScriptDialog.ApplyInitial(actor, context, creation.DialogSelection);
        var dialogPublication = !context.IsExecutingStartup && selectedDialog > 0
            ? GoldCreatureUpdateCodec.CreateState(_runtime.Owner, actor.Base.ObjectReference, actor.Base.ObjectType,
                GoldCreatureUpdateState.FromCreature(WorldCreatureInitialSerialization.Write(actor, blueprint.Visuals,
                    EquipmentFactory.Items, _assets.Objects.GetItem))) : null;
        actor = WorldCreatureScriptServices.ApplyInitial(actor, creation.ConstructionFlags, _assets.Services);
        var spell = new SacredCreatureScriptSpellState(actor.KiMode, actor.SpellSelector, actor.SpellParameter).Apply(creation.PackedSpell);
        actor = actor with { KiMode = spell.Mode, SpellSelector = spell.Selector, SpellParameter = spell.Parameter };
        actor = WorldCreatureAutomaticScriptLevel.Apply(actor,blueprint,context,runtime,_assets,sectorMaximumLevel,Random,_log);
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "service initialized";
        _log($"Startup NPC level/behavior/group initialized: ref {actor.Base.ObjectReference}, level {BinaryPrimitives.ReadUInt16LittleEndian(actor.Component.Values.AsSpan(78))} " +
            $"(requested {creation.LevelInput}, assigned {creation.AssignedLevelInput}), dialog {actor.DialogIndex}, sector {actor.Base.Sector}, position {actor.Base.X},{actor.Base.Y},{actor.Base.Surface}, " +
            $"facing {pending.FacingDegrees}, relation {actor.RelationClass}, group {actor.ScriptGroup}; equipment and placed serialization pending.");
        foreach (var descriptor in blueprint.Equipment)
        {
            RequireEquipment(blueprint, descriptor);
            // For assigned NPCs both supported categories have an eligible hand
            // slot (0x54B0E0/0x54ACC0); no player model-slot query is required.
            var equipment = EquipmentFactory.Create(descriptor, _assets.Objects.GetInstance(descriptor.ItemIndex),
                _runtime.Clock() / 1000d, context.Multiplayer);
            pending.AddEquipment(equipment); pending.Stage = "equipment constructed";
            _log($"Startup NPC equipment constructed: actor {actor.Base.ObjectReference}, item ref {equipment.Physical.Base.ObjectReference}, type {equipment.Physical.Base.ObjectType}, counter {equipment.Physical.Base.Byte38}, node period {equipment.Node.PeriodMilliseconds}; generation/installation pending.");
            WorldScriptCreatureEquipmentValues.Apply(equipment, BinaryPrimitives.ReadUInt16LittleEndian(actor.Component.Values.AsSpan(78)),
                _runtime.Difficulty, _assets.Equipment ?? throw new InvalidDataException("CreateNPC needs original equipment generation/naming definitions."),
                Random, _assets.Results.StartupPrecision);
            pending.Stage = "equipment values prepared";
            _log($"Startup NPC equipment values prepared: ref {equipment.Physical.Base.ObjectReference}, generated {equipment.Generated}, quality {equipment.Quality}, name '{equipment.Name?.Text}'; installation pending.");
            actor = WorldCreatureInitialEquipmentInstallation.Apply(actor, equipment, descriptor.ItemIndex, _assets.Equipment,
                _assets.EquipmentSlots ?? throw new InvalidDataException("CreateNPC needs original equipment slot definitions."), EquipmentFactory.Items, context.Multiplayer);
            Factory.Update(actor); pending.Actor = actor; pending.Stage = "equipment installed";
            _log($"Startup NPC equipment installed: actor {actor.Base.ObjectReference}, item ref {equipment.Physical.Base.ObjectReference}, parent {equipment.Physical.AttachmentParent}; actor calculation and placed serialization pending.");
        }
        // 0x485022 sets the owned caller's high bit before the equipped result
        // and serialization. Explicit behavior skips the subsequent name gate.
        if (entered) actor = actor with { Base = actor.Base with { Flags = actor.Base.Flags | 0x80000000u } };
        actor = WorldCreatureEquippedResults.Apply(actor, blueprint.Template, _assets.Results,
            _assets.Equipment ?? throw new InvalidDataException("CreateNPC needs original equipped-result definitions."),
            EquipmentFactory.Items, _runtime.Difficulty, _runtime.Owner, context.Multiplayer, runtime.RequirePlayerCount());
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "results initialized";
        _log($"Startup NPC equipped calculation complete: ref {actor.Base.ObjectReference}, result valid {actor.Component.ResultValid}; placed serialization pending.");
        pending.CreatureState = WorldCreatureInitialSerialization.Write(actor, blueprint.Visuals, EquipmentFactory.Items, _assets.Objects.GetItem);
        // 0x449D71 clears the original pending bit while writing the base block.
        actor = actor with { Base = actor.Base with { Flags = actor.Base.Flags & ~0x10000u } };
        Factory.Update(actor); pending.Actor = actor; pending.Stage = "creature serialized";
        _log($"Startup NPC serialization complete: ref {actor.Base.ObjectReference}, {pending.CreatureState.ToPayload().Length} bytes; equipment streams/publication pending.");
        var publications = WorldCreatureInitialPublication.Prepare(actor, pending.CreatureState, EquipmentFactory.Items, _assets.Objects.GetItem).ToList();
        if (dialogPublication is not null) publications.Insert(0, dialogPublication);
        foreach (var reference in actor.EquipmentReferences.Take(18).Where(value => value != 0))
        {
            var physical = EquipmentFactory.Items[reference].Physical;
            physical.Base = physical.Base with { Flags = physical.Base.Flags & ~0x10000u };
        }
        pending.RandomStateAfter = Random.State;
        pending.Complete(publications); _prepared.Add(pending); Pending = null;
        _log($"{(entered ? "Entered" : "Startup")} NPC preparation complete: ref {actor.Base.ObjectReference}, {publications.Count} ordered messages queued.");
    }

    private void RequireEquipment(WorldScriptCreatureBlueprint blueprint, ItemsPakEntry descriptor)
    {
        if (descriptor.ModelDesc.Category is not (SacredItemCategory.Weapon or SacredItemCategory.Shield) ||
            !_assets.Objects.Equipment.TryGetValue(descriptor.ItemIndex, out var shared))
            throw new InvalidDataException("CreateNPC requires its equipment category constructor and resolved original shared definition.");
        // 0x427E80/0x427ED0 choose ranged-resource replacement before construction.
        if (!WorldCreatureRangedEquipment.CanRetain(shared.UsageIdentifier,
            motion => _assets.Motions?.HasMotion(blueprint.Visuals.ModelName, motion) ?? false))
            throw new InvalidDataException("CreateNPC missing authored ranged motion requires native actor-resource replacement before construction.");
    }

    internal uint? FindObject(uint resource, WorldScriptExecutionContext context)
    {
        // Native ascending registry scan includes inactive objects and uses the
        // original descriptor resource when instance +0x3C is zero.
        var objects = Factory.Actors.Values.Select(value => value.Base)
            .Concat(EquipmentFactory.Items.Values.Select(value => value.Physical.Base));
        if (context.ObjectWorld is WorldScriptObjectWorld world)
            objects = objects.Concat(world.Objects.Select(value => value.BaseState));
        if (Effects is not null) objects = objects.Concat(Effects.Created.Select(value => value.BaseState));
        return Resources.FindObject(resource, objects.ToDictionary(value => value.ObjectReference,
            value => new GoldReplicatedObject(value, null, null)), type => _assets.Objects.GetItem(type).ModelDesc.ResourceId);
    }
}
