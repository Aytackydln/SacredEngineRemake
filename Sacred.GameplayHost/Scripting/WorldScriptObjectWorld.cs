using Sacred.Assets.GameBin;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Startup construction of container objects from original templates and granted IDs.
/// Unsupported families/effects fail before allocation; callbacks are retained in native use/take order.</summary>
public sealed class WorldScriptObjectWorld : IWorldScriptObjectWorld
{
    private readonly SacredObjectTemplateArchive _templates;
    private readonly GoldObjectReferencePool _references;
    private readonly SacredSectorGrid _sectors;
    private readonly WorldScriptTriggerRegistry _triggers;
    private readonly byte _owner;
    private readonly WorldObjectAllocationCounter _allocationCounter;
    private readonly bool _hasActiveHero;
    private readonly List<WorldScriptCreatedObject> _objects = [];
    private readonly List<GoldObjectScriptCallback> _callbacks = [];
    public WorldScriptObjectWorld(SacredObjectTemplateArchive templates, GoldObjectReferencePool references,
        SacredSectorGrid sectors, WorldScriptTriggerRegistry triggers, byte owner, byte initialAllocationCounter, bool hasActiveHero)
        : this(templates, references, sectors, triggers, owner, new WorldObjectAllocationCounter(initialAllocationCounter), hasActiveHero) { }

    public WorldScriptObjectWorld(SacredObjectTemplateArchive templates, GoldObjectReferencePool references,
        SacredSectorGrid sectors, WorldScriptTriggerRegistry triggers, byte owner, WorldObjectAllocationCounter allocationCounter, bool hasActiveHero)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _references = references ?? throw new ArgumentNullException(nameof(references));
        _sectors = sectors ?? throw new ArgumentNullException(nameof(sectors));
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
        if (owner is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(owner));
        _owner = owner;
        _allocationCounter = allocationCounter ?? throw new ArgumentNullException(nameof(allocationCounter));
        _hasActiveHero = hasActiveHero;
        Objects = _objects.AsReadOnly();
        Callbacks = _callbacks.AsReadOnly();
    }
    public bool HasActiveHero => _hasActiveHero;
    public IReadOnlyList<WorldScriptCreatedObject> Objects { get; }
    public IReadOnlyList<GoldObjectScriptCallback> Callbacks { get; }
    /// <summary>Share this same session counter with NPC/equipment factories.</summary>
    public WorldObjectAllocationCounter AllocationCounter => _allocationCounter;
    public WorldScriptCreatureWorld? Creatures { get; set; }

    public void Create(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (context.IsEnteringSector)
        {
            PrepareEntered(context, command, context.CurrentSector ?? throw new InvalidOperationException("Entered CreateObj needs its current sector."));
            Creatures!.Effects!.CreateEntered(context, command, Creatures);
            return;
        }
        if (!context.IsExecutingStartup) throw new InvalidDataException("Live CreateObj requires sector/ownership dispatch.");
        var creation = SacredScriptObjectCreationReader.Read(command,
            name => context.Definitions.ResolvePosition(name, context.Random, context.PositionAllowed));
        if (creation.SecondaryName is not null && (context.ObjectActionFlags & 1) != 0)
            throw new InvalidDataException("CreateObj secondary-name association requires its native target lookup/deferred lifecycle before construction.");
        if(context.DeferredObjectStates.FindForCreation(creation.Name) is not null)
            throw new NotSupportedException($"CreateObj '{creation.Name}' requires queued open/lock target effects before construction.");
        // Native returns without creating anything if no type/placement was supplied.
        if (creation.TypeId == 0 || creation.TilePosition is null && creation.WorldPosition is null) return;
        var item = _templates.GetItem(creation.TypeId);
        if (item.ModelDesc.Category is SacredItemCategory.QuestItem or SacredItemCategory.Book or SacredItemCategory.Weapon or SacredItemCategory.Shield)
        {
            if (_hasActiveHero && (item.ModelDesc.Category is not (SacredItemCategory.QuestItem or SacredItemCategory.Book or SacredItemCategory.Weapon) || Creatures?.Items.HasCurrentValueSource != true))
                throw new InvalidDataException("CreateObj item needs its compared current active-hero value source.");
            if (_hasActiveHero && item.ModelDesc.Category == SacredItemCategory.Weapon && context.CurrentSector is not null)
                throw new NotSupportedException("CreateObj weapon needs its current sector-callback environment lifecycle.");
            if (_hasActiveHero && (item.ModelDesc.Category is SacredItemCategory.QuestItem or SacredItemCategory.Book) &&
                creation.TilePosition is { } questTile && Creatures!.Terrain?.Tiles.IsLoaded(_sectors.GetSector(questTile.X, questTile.Y)) == true)
                throw new NotSupportedException("CreateObj item in a loaded sector requires its actual creation/placement lifecycle.");
            if ((item.ModelDesc.Category is SacredItemCategory.Weapon or SacredItemCategory.Shield) &&
                creation.TilePosition is { X: >= 0, Y: >= 0 } equipmentTile &&
                Creatures?.Terrain?.Tiles.IsLoaded(_sectors.GetSector(equipmentTile.X, equipmentTile.Y)) == true)
                throw new InvalidDataException("CreateObj ground equipment in a loaded sector requires its compared placement lifecycle.");
            var items = Creatures?.Items ?? throw new InvalidDataException("CreateObj item needs the shared initial item factory.");
            var itemCreation = items.Create(command, creation, item, Creatures!.ItemResources);
            foreach (var callback in itemCreation.Callbacks) AppendCallback(context, callback);
            return;
        }
        if (item.ModelDesc.Category == SacredItemCategory.Effect)
        {
            var effects = Creatures?.Effects ?? throw new InvalidDataException("CreateObj Effect requires original definitions and an explicitly compared projection context.");
            if (creation.TilePosition is { X: >= 0, Y: >= 0 } effectTile &&
                Creatures?.Terrain?.Tiles.IsLoaded(_sectors.GetSector(effectTile.X, effectTile.Y)) == true)
                throw new InvalidDataException("CreateObj Effect in a loaded sector requires its compared tile-placement lifecycle.");
            effects.Create(command,creation,item,Creatures!.ItemResources,context.ObjectActionFlags,key=>Creatures.FindObject(key,context));
            return;
        }
        if (creation.SecondaryName is not null)
            throw new InvalidDataException("CreateObj secondary-name association requires its compared category caller.");
        if (item.ModelDesc.Category != SacredItemCategory.Container)
            throw new InvalidDataException($"CreateObj type {creation.TypeId} requires the native {item.ModelDesc.Category} factory.");
        if (creation.WorldPosition is not null || creation.HeightOffset is not null || creation.Name is not null)
            throw new InvalidDataException("CreateObj requires native world-offset, height or named-object association semantics.");
        var instance = _templates.GetInstance(creation.TypeId);
        var position = creation.TilePosition!.Value;
        if (position.X < 0 || position.Y < 0)
            throw new InvalidDataException("Negative CreateObj coordinates require native placement fallback.");
        var sector = checked((ushort)_sectors.GetSector(position.X, position.Y));
        var nativePosition = new GoldObjectPosition(ToNative(position.X), ToNative(position.Y),
            unchecked((byte)Math.Max(0, position.Z)), sector);
        var terrain = Creatures?.Terrain;
        WorldCreatureLoadedPlacementPlan? loadedPlacement = null;
        if (terrain?.Tiles.IsLoaded(sector) == true)
            loadedPlacement = terrain.PrepareContainerPlacement(context, nativePosition);
        var use = Callback(creation.UseScript, SacredScriptObjectCallbackLayout.UsePrefix);
        var take = Callback(creation.TakeScript, SacredScriptObjectCallbackLayout.TakePrefix);
        // Validate storage before consuming a reservation. Callback IDs are assigned after allocation.
        foreach (var name in new[] { use, take }) if (name is not null) _ = GoldObjectScriptCallback.Create(GoldReferenceCodec.FirstObjectReference, name);
        if (_hasActiveHero) _ = _references.PeekAvailable(1);
        var values = _hasActiveHero
            ? (Creatures ?? throw new InvalidDataException("CreateObj needs its shared creature/hero services.")).PrepareObjectValues(creation.TypeId, instance)
            : instance;
        if (!_references.TryAllocate(out var reference))
            throw new InvalidDataException("CreateObj needs a 0x153 object-reference reservation.");
        _allocationCounter.BeginAllocation();
        // Actual unpublished multiplayer 5FB530 sets 0x10000. The base writer
        // clears it on export; retained runtime state must not drop it early.
        var flags = 2u | (context.Multiplayer ? 0x10000u : 0);
        if (((ushort)item.ModelDesc.GraphicFlags & 0x0100) != 0) flags |= 0x800;
        if (use is not null || take is not null) flags |= 1;
        var trigger = string.IsNullOrEmpty(creation.TriggerReference) ? 0 : _triggers.AttachObject(creation.TriggerReference, reference);
        var animate = trigger != 0 && _triggers.PrepareContainerAnimation(trigger);
        if (animate) flags |= 0x2000000;
        var state = new GoldObjectBaseState(reference, creation.TypeId, flags,
            sector, nativePosition.X, nativePosition.Y, checked((byte)nativePosition.Surface),
            BitConverter.SingleToUInt32Bits(creation.FacingDegrees is > 0 ? creation.FacingDegrees.Value : item.ModelDesc.Angle3D),
            trigger, _allocationCounter.Value, _owner, 0);
        var created = new WorldScriptCreatedObject(command.FileOffset, creation, state, instance)
            { CurrentValues = values, PendingAnimationEvent = animate ? (ushort)0xAB : null };
        _objects.Add(created);
        if (Creatures is { } creatures)
        {
            creatures.Factory.Activation.ActivateInitial(created.Node, creatures.SchedulerClock);
            if (loadedPlacement is { } plan)
            {
                WorldScriptContainerPlacement.PlaceOnFloor(created, plan);
                terrain!.RegisterCreatedContainer(created);
            }
            else creatures.Factory.Activation.DeactivateInitial(created.Node);
        }
        // Actual caller 48750C clears unplaced state and marks the script object
        // after positioned/deferred factory completion; no model is loaded here.
        created.BaseState = created.BaseState with { Flags = (created.BaseState.Flags & ~2u) | 0x200u,
            Word34 = string.IsNullOrEmpty(creation.TriggerReference) ? created.BaseState.TriggerReference : trigger };
        if (use is not null) AppendCallback(context, GoldObjectScriptCallback.Create(reference, use));
        if (take is not null) AppendCallback(context, GoldObjectScriptCallback.Create(reference, take));
    }

    internal void PrepareEntered(WorldScriptExecutionContext context, SacredScriptCommand command, WorldScriptSectorEnvironment environment)
    {
        var creation = WorldScriptEnteredFixedEffect.Read(command);
        if (_templates.GetItem(creation.TypeId).ModelDesc.Category != SacredItemCategory.Effect ||
            Creatures is not { Effects: { } effects } world)
            throw new NotSupportedException("Entered CreateObj requires its compared fixed Effect factory.");
        _ = effects.PrepareEntered(context, command, environment, world);
    }

    private void AppendCallback(WorldScriptExecutionContext context, GoldObjectScriptCallback callback)
    {
        _callbacks.Add(callback);
        context.ObjectCallbacks.Append(callback);
    }
    private static string? Callback(string? value, string prefix) => value is null ? null : prefix + value;
    // Gold uses x87 extended intermediates before truncation, rather than a rounded float product.
    private static int ToNative(int tile) => checked((int)((tile + 0.5d) * SacredNetworkPosition.NativeUnitsPerTile));
}
