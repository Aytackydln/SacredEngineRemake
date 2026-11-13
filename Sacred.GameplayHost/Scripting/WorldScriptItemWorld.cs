using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Core.World;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared fresh initial QuestItem, Book, Weapon and Shield commands. Uses the same reserved
/// item factory and scheduler as NPC equipment; publication remains private.</summary>
public sealed class WorldScriptItemWorld(SacredObjectTemplateArchive templates, WorldEquipmentInitialFactory factory,
    WorldScriptCreatureRuntime runtime, SacredSectorGrid grid, Action<string> log)
{
    private readonly List<WorldScriptCreatedItem> _created = [];
    public IReadOnlyList<WorldScriptCreatedItem> Created => _created.AsReadOnly();

    public WorldScriptCreatedItem Create(SacredScriptCommand command, SacredScriptCreateObject creation, ItemsPakEntry descriptor)
        => Create(command,creation,descriptor,null);

    public WorldScriptCreatedItem Create(SacredScriptCommand command, SacredScriptCreateObject creation, ItemsPakEntry descriptor,
        WorldScriptItemResources? resources)
    {
        if (creation.SecondaryName is not null || descriptor.ModelDesc.Category is not (SacredItemCategory.QuestItem or SacredItemCategory.Book or SacredItemCategory.Weapon or SacredItemCategory.Shield) || runtime.Owned || (creation.Name is not null && resources is null) ||
            creation.WorldPosition is not null || creation.HeightOffset is not null || creation.TriggerReference is not null ||
            creation.TilePosition is not { X: >= 0, Y: >= 0, Z: 0 } tile)
            throw new InvalidDataException("CreateObj item needs its compared fresh ground-position item caller.");
        if ((descriptor.ModelDesc.Category is SacredItemCategory.Weapon or SacredItemCategory.Shield) && creation.Name is not null)
            throw new InvalidDataException("CreateObj named ground equipment requires its compared identity/duplicate lifecycle.");
        var position = new GoldObjectPosition(SacredWorldCoordinates.TileCenterToWorld(tile.X),
            SacredWorldCoordinates.TileCenterToWorld(tile.Y), 0, grid.GetSector(tile.X, tile.Y));
        if (Math.Max(position.X, position.Y) < 1000)
            throw new InvalidDataException("CreateObj near-origin item needs its original placed bone/cache service.");
        var callbackNames = new List<string>();
        if (creation.UseScript is { } use) callbackNames.Add(SacredScriptObjectCallbackLayout.UsePrefix + use);
        if (creation.TakeScript is { } take) callbackNames.Add(SacredScriptObjectCallbackLayout.TakePrefix + take);
        foreach (var name in callbackNames) _ = GoldObjectScriptCallback.Create(GoldReferenceCodec.FirstObjectReference, name);
        var resource = resources?.Resolve(creation.Name) ?? GoldScriptResourceLayout.UnnamedResource;
        var values = templates.GetInstance(creation.TypeId);
        var item = factory.Create(descriptor, values, runtime.Clock() / 1000d, networking: true);
        var physical = item.Physical;
        physical.Base = physical.Base with { Sector = (ushort)position.Sector, X = position.X, Y = position.Y,
            Flags = (physical.Base.Flags & ~2u) | 0x200u | (callbackNames.Count != 0 ? 1u : 0u) };
        if (resources is not null) physical.Base = resources.Bind(physical.Base,resource);
        if (!factory.Activation.DeactivateInitial(item.Node))
            throw new InvalidOperationException("Initial item scheduler registration is missing.");
        // The caller sets explicit facing directly, including Book.
        if (creation.FacingDegrees is > 0)
        {
            var direction = SacredStartupHeading.ToDirection(creation.FacingDegrees.Value);
            physical.Direction = new(direction.X, direction.Y, direction.Z);
        }
        var callbacks = callbackNames.Select(name => GoldObjectScriptCallback.Create(physical.Base.ObjectReference, name)).ToArray();
        item.SerializedState = WorldScriptItemSerialization.Write(item, descriptor);
        physical.Base = physical.Base with { Flags = physical.Base.Flags & ~0x10000u };
        var publication = GoldObjectStateCodec.Create(0x16e, runtime.Owner, item.SerializedState.ToPayload());
        var result = new WorldScriptCreatedItem(command.FileOffset, item, Array.AsReadOnly(callbacks), publication);
        _created.Add(result);
        log($"Startup item prepared: source 0x{command.FileOffset:X}, ref {physical.Base.ObjectReference}, type {creation.TypeId}, counter {physical.Base.Byte38}, position {position.X},{position.Y}, sector {position.Sector}, {callbacks.Length} callbacks, {item.SerializedState.ToPayload().Length} bytes; retained privately.");
        return result;
    }
}
