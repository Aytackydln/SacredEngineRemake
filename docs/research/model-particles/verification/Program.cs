using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Weapon;
using System.Collections.Frozen;

var simulationOnly = args.Length == 2 && args[0] == "--simulation-only";
if ((!simulationOnly && args.Length != 1) || (simulationOnly && args.Length != 2))
    throw new ArgumentException("Usage: ModelParticleVerification [--simulation-only] <Sacred Gold/pak>");
var pak = args[^1];
if (simulationOnly)
{
    var simulationItems = ItemsPakArchive.Load(Path.Combine(pak, "Items.pak"))
        .ToFrozenDictionary(x => x.ItemIndex);
    using var simulationModels = ModelsPakArchive.Load(
        Path.Combine(pak, "models.pak"), Path.Combine(pak, "Models.tmp"));
    await SimulationVerification.Run(simulationModels, simulationItems);
    return;
}
NativeChainReferenceVerification.Run(Path.Combine(AppContext.BaseDirectory, "chain-reference.json"));
DemoLayoutVerification.Run(Path.Combine(AppContext.BaseDirectory, "types.json"));
ElementalWeaponEffectVerification.Run();
var items = ItemsPakArchive.Load(Path.Combine(pak, "Items.pak")).ToFrozenDictionary(x => x.ItemIndex);
var equipment = WeaponPakParser.Parse(Path.Combine(pak, "Weapon.pak"), items).ToArray();
EquipmentSelectionVerification.Run(pak, items, equipment);
using var models = ModelsPakArchive.Load(Path.Combine(pak, "models.pak"), Path.Combine(pak, "Models.tmp"));
await SimulationVerification.Run(models, items);
await EquipmentSelectionVerification.CheckInheritedModels(models, items[1].ModelName, equipment);
await EquipmentSelectionVerification.CheckRequestedEffects(models, items[1].ModelName, equipment);
