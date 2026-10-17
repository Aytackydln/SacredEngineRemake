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
public sealed class WorldScriptObjectWorld
{
    private readonly SacredObjectTemplateArchive _templates;
    private readonly GoldObjectReferencePool _references;
    private readonly SacredSectorGrid _sectors;
    private readonly WorldScriptTriggerRegistry _triggers;
    private readonly byte _owner;
    private byte _allocationCounter;
    private readonly bool _hasActiveHero;
    private readonly List<WorldScriptCreatedObject> _objects = [];
    private readonly List<GoldObjectScriptCallback> _callbacks = [];
    public WorldScriptObjectWorld(SacredObjectTemplateArchive templates, GoldObjectReferencePool references,
        SacredSectorGrid sectors, WorldScriptTriggerRegistry triggers, byte owner, byte initialAllocationCounter, bool hasActiveHero)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _references = references ?? throw new ArgumentNullException(nameof(references));
        _sectors = sectors ?? throw new ArgumentNullException(nameof(sectors));
        _triggers = triggers ?? throw new ArgumentNullException(nameof(triggers));
        if (owner is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(owner));
        _owner = owner;
        _allocationCounter = initialAllocationCounter;
        _hasActiveHero = hasActiveHero;
        Objects = _objects.AsReadOnly();
        Callbacks = _callbacks.AsReadOnly();
    }
    public IReadOnlyList<WorldScriptCreatedObject> Objects { get; }
    public IReadOnlyList<GoldObjectScriptCallback> Callbacks { get; }

    internal void Create(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new InvalidDataException("Live CreateObj requires sector/ownership dispatch.");
        var creation = SacredScriptObjectCreationReader.Read(command,
            name => context.Definitions.ResolvePosition(name, context.Random, context.PositionAllowed));
        // Native returns without creating anything if no type/placement was supplied.
        if (creation.TypeId == 0 || creation.TilePosition is null && creation.WorldPosition is null) return;
        var item = _templates.GetItem(creation.TypeId);
        if (item.ModelDesc.Category != SacredItemCategory.Container)
            throw new InvalidDataException($"CreateObj type {creation.TypeId} requires the native {item.ModelDesc.Category} factory.");
        if (_hasActiveHero) throw new InvalidDataException("CreateObj item construction requires native active-hero value calculation.");
        if (creation.WorldPosition is not null || creation.HeightOffset is not null || creation.Name is not null)
            throw new InvalidDataException("CreateObj requires native world-offset, height or named-object association semantics.");
        var instance = _templates.GetInstance(creation.TypeId);
        var position = creation.TilePosition!.Value;
        if (position.X < 0 || position.Y < 0)
            throw new InvalidDataException("Negative CreateObj coordinates require native placement fallback.");
        var use = Callback(creation.UseScript, SacredScriptObjectCallbackLayout.UsePrefix);
        var take = Callback(creation.TakeScript, SacredScriptObjectCallbackLayout.TakePrefix);
        // Validate storage before consuming a reservation. Callback IDs are assigned after allocation.
        foreach (var name in new[] { use, take }) if (name is not null) _ = GoldObjectScriptCallback.Create(GoldReferenceCodec.FirstObjectReference, name);
        if (!_references.TryAllocate(out var reference))
            throw new InvalidDataException("CreateObj needs a 0x153 object-reference reservation.");
        var flags = 0x200u; // Native tile-placement branch clears 2 and sets 0x200 (0x48750C).
        if (((ushort)item.ModelDesc.GraphicFlags & 0x0100) != 0) flags |= 0x800;
        if (use is not null || take is not null) flags |= 1;
        var trigger = string.IsNullOrEmpty(creation.TriggerReference) ? 0 : _triggers.AttachObject(creation.TriggerReference, reference);
        var animate = trigger != 0 && _triggers.PrepareContainerAnimation(trigger);
        if (animate) flags |= 0x2000000;
        var state = new GoldObjectBaseState(reference, creation.TypeId, flags,
            unchecked((ushort)_sectors.GetSector(position.X, position.Y)),
            ToNative(position.X), ToNative(position.Y), unchecked((byte)Math.Max(0, position.Z)),
            BitConverter.SingleToUInt32Bits(creation.FacingDegrees is > 0 ? creation.FacingDegrees.Value : item.ModelDesc.Angle3D),
            trigger, unchecked(++_allocationCounter), _owner, 0);
        _objects.Add(new(command.FileOffset, creation, state, instance) { PendingAnimationEvent = animate ? (ushort)0xAB : null });
        if (use is not null) _callbacks.Add(GoldObjectScriptCallback.Create(reference, use));
        if (take is not null) _callbacks.Add(GoldObjectScriptCallback.Create(reference, take));
    }

    private static string? Callback(string? value, string prefix) => value is null ? null : prefix + value;
    // Gold uses x87 extended intermediates before truncation, rather than a rounded float product.
    private static int ToNative(int tile) => checked((int)((tile + 0.5d) * SacredNetworkPosition.NativeUnitsPerTile));
}
