using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Sacred.Core.Loot;
using Sacred.Core.Pak.Items;

namespace AssetViewer.AssetViewer;

/// <summary>One console reader for the whole application; commands never emulate desktop input.</summary>
internal static class AssetViewerConsole
{
    public static void Start(MainWindow main)
    {
        Console.WriteLine("[Assets] Inventory cheats: inventory, equipment-layout, inventory-slot <index>, inventory-item <id>, item-description, screenshot-item <path.png>, inventory-clear, stats, allocation, stats-view <stats|skills|bonuses|allocate>, level <level>, attribute <name> <allocated points>, skill <id> <rank>, combat-art <code> <rank>.");
        Console.WriteLine("[Assets] FX cheats: open effects, fx <Items.pak row>, fx-implemented <true|false>, fx-search <text>, fx-variant <script|event subtype>, fx-strength <unsigned value>, fx-distance <native units, 0..2000>, fx-defaults, fx-restart, fx-actor, fx-play <true|false>, fx-time <seconds>.");
        Console.WriteLine("[Assets] FX controller cheats: fx-release (Forge compression/fade), fx-extend <timer units> (Mana duration), fx-cell <0..15> (Trap01 atlas), fx-hit (hit/toggle response), fx-step <seconds> (advance paused FX in 60 Hz steps).");
        Console.WriteLine("[Assets] Models/Effects camera cheat: zoom <wheel steps>; positive zooms in, negative zooms out.");
        Console.WriteLine("[Drops] Cheats: open drops, drop <difficulty 0..4> <hero-level> <category|random|weighted|reward|armor|jewelry> <class 0..9> <seed> [rarity random|0..14], drop-replay, drop-select <history index>, drop-values, drop-copy-equipment, drop-clear. Screenshot cheats work without desktop input.");
        Console.WriteLine("[Drops] Rarity-stage cheat: drop-rarity-stage <Creature|Container> <difficulty> <hero-level> <source-level> <equipment-find-bonus> <survival-seconds> <resolved-creature-bonus> <progression-mask> <seed>. This is not a full kill/open/reward event.");
        Console.WriteLine("[Drops] Authored name preview: drop-authored <unique/set resource>. Uses original values and the native resource name without adding a random drop.");
        Console.WriteLine("[Assets] Cheats: open <models|equipment|textures|characters|drops>, model <row>, item <id>, texture <archive.pak> <row>, character <row>, class <name|npc>, animations, animation <bind|default|slot|GRN name>, animation-play <true|false>, animation-time <seconds>, rotate <horizontal degrees>, equipment-rotation <yaw> <pitch> <roll>, effects <true|false>, equip <slot-index> <id>, unequip <slot-index>, screenshot <path.png>, screenshot-ui <path.png>, location <directory>, close <view>, gc, quit.");
        _ = Task.Run(async () =>
        {
            while (Console.ReadLine() is { } input)
            {
                Console.WriteLine($"[Assets] Input: {input}");
                var parts = input.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                try
                {
                    await main.Loading;
                    await Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(main, parts[0].ToLowerInvariant(), parts.Length > 1 ? parts[1] : string.Empty));
                    await Dispatcher.UIThread.InvokeAsync(() => main.ActiveViewer?.Ready ?? Task.CompletedTask);
                    Console.WriteLine($"[Assets] Command complete: {input}");
                }
                catch (Exception error) { Console.WriteLine($"[Assets] Cheat failed: {error.Message}"); }
            }
        });
    }

    private static async Task ExecuteAsync(MainWindow main, string command, string argument)
    {
        switch (command)
        {
            case "open": await main.OpenViewerAsync(argument); break;
            case "drop":
                var drop = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (drop.Length is not (5 or 6)) throw new ArgumentException("Usage: drop <difficulty 0..4> <hero-level> <category|random|weighted|reward|armor|jewelry> <class 0..9> <seed> [rarity random|0..14]");
                var choice = ItemDropCategoryChoice.Parse(drop[2]);
                await ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).RollAsync(new(ushort.Parse(drop[1], CultureInfo.InvariantCulture),
                    uint.Parse(drop[0], CultureInfo.InvariantCulture), uint.Parse(drop[4], CultureInfo.InvariantCulture),
                    choice.Category, uint.Parse(drop[3], CultureInfo.InvariantCulture), choice.Mode,
                    drop.Length == 5 || drop[5].Equals("random", StringComparison.OrdinalIgnoreCase) ? null : uint.Parse(drop[5], CultureInfo.InvariantCulture)));
                break;
            case "drop-replay": await ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).ReplayAsync(); break;
            case "drop-rarity-stage":
                var stage = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (stage.Length != 9) throw new ArgumentException("Usage: drop-rarity-stage <Creature|Container> <difficulty> <hero-level> <source-level> <equipment-find-bonus> <survival-seconds> <creature-bonus> <unlocks> <seed>");
                await ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).RollAsync(new(
                    ushort.Parse(stage[2], CultureInfo.InvariantCulture), uint.Parse(stage[1], CultureInfo.InvariantCulture),
                    uint.Parse(stage[8], CultureInfo.InvariantCulture), Category: SacredItemCategory.Weapon, Quality: null)
                {
                    RarityContext = new(Enum.Parse<SacredLootRarityProfile>(stage[0], true),
                        float.Parse(stage[4], CultureInfo.InvariantCulture), uint.Parse(stage[5], CultureInfo.InvariantCulture),
                        ushort.Parse(stage[3], CultureInfo.InvariantCulture), ushort.Parse(stage[6], CultureInfo.InvariantCulture), byte.Parse(stage[7], CultureInfo.InvariantCulture))
                });
                break;
            case "drop-select": ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).SelectHistory(int.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "drop-values": ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).PrintValues(); break;
            case "drop-clear": ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).ClearDrops(); break;
            case "drop-copy-equipment": ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).CopyEquipmentFind(); break;
            case "drop-authored": await ((ItemDropSimulatorWindow)await main.OpenViewerAsync("drops")).InspectAuthoredAsync(uint.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "model": ((ModelsWindow)await main.OpenViewerAsync("models")).Select(ushort.Parse(argument)); break;
            case "fx": ((EffectsWindow)await main.OpenViewerAsync("effects")).Select(ushort.Parse(argument)); break;
            case "fx-variant": ((EffectsWindow)await main.OpenViewerAsync("effects")).SelectVariant(argument.Equals("script", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(argument)); break;
            case "fx-restart": ((EffectsWindow)await main.OpenViewerAsync("effects")).Restart(); break;
            case "fx-cell": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetAtlasCell(int.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-hit": ((EffectsWindow)await main.OpenViewerAsync("effects")).RequestHit(); break;
            case "fx-extend": ((EffectsWindow)await main.OpenViewerAsync("effects")).ExtendDuration(uint.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-release": ((EffectsWindow)await main.OpenViewerAsync("effects")).RequestRelease(); break;
            case "fx-step": ((EffectsWindow)await main.OpenViewerAsync("effects")).Step(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-actor": ((EffectsWindow)await main.OpenViewerAsync("effects")).RandomizeActor(); break;
            case "fx-implemented": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetImplementedFilter(bool.Parse(argument)); break;
            case "fx-search": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetSearchText(argument); break;
            case "fx-play": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetPlaying(bool.Parse(argument)); break;
            case "fx-time": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetTime(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-strength": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetStrength(uint.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-distance": ((EffectsWindow)await main.OpenViewerAsync("effects")).SetDistance(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "fx-defaults": ((EffectsWindow)await main.OpenViewerAsync("effects")).ResetInputs(); break;
            case "item": ((EquipmentWindow)await main.OpenViewerAsync("equipment")).Select(uint.Parse(argument)); break;
            case "character": ((CharactersWindow)await main.OpenViewerAsync("characters")).Select(ushort.Parse(argument)); break;
            case "texture":
                var texture = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                ((TexturesWindow)await main.OpenViewerAsync("textures")).Select(texture[0], uint.Parse(texture[1])); break;
            case "effects": ((EquipmentWindow)await main.OpenViewerAsync("equipment")).SetEffectsEnabled(bool.Parse(argument)); break;
            case "class": ((CharactersWindow)await main.OpenViewerAsync("characters")).SetClass(argument); break;
            case "stats": ((CharactersWindow)await main.OpenViewerAsync("characters")).PrintStats(); break;
            case "stats-view": ((CharactersWindow)await main.OpenViewerAsync("characters")).SelectStatsView(argument); break;
            case "inventory": ((CharactersWindow)await main.OpenViewerAsync("characters")).PrintInventory(); break;
            case "equipment-layout": ((CharactersWindow)await main.OpenViewerAsync("characters")).PrintEquipmentLayout(); break;
            case "inventory-slot": ((CharactersWindow)await main.OpenViewerAsync("characters")).SelectInventorySlot(int.Parse(argument)); break;
            case "inventory-item": ((CharactersWindow)await main.OpenViewerAsync("characters")).SelectInventoryItem(uint.Parse(argument)); break;
            case "item-description": ((CharactersWindow)await main.OpenViewerAsync("characters")).PrintSelectedDescription(); break;
            case "screenshot-item":
                var itemViewer = (CharactersWindow)await main.OpenViewerAsync("characters");
                await itemViewer.Ready;
                var itemPath = Path.GetFullPath(argument.Trim('"'));
                Directory.CreateDirectory(Path.GetDirectoryName(itemPath)!);
                itemViewer.SaveItemScreenshot(itemPath); break;
            case "allocation": ((CharactersWindow)await main.OpenViewerAsync("characters")).PrintAllocation(); break;
            case "inventory-clear": ((CharactersWindow)await main.OpenViewerAsync("characters")).ClearInventory(); break;
            case "level": ((CharactersWindow)await main.OpenViewerAsync("characters")).SetLevel(ushort.Parse(argument)); break;
            case "attribute":
                var attribute = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                ((CharactersWindow)await main.OpenViewerAsync("characters")).SetAttribute(attribute[0], int.Parse(attribute[1])); break;
            case "skill":
                var skill = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                ((CharactersWindow)await main.OpenViewerAsync("characters")).SetSkill(byte.Parse(skill[0]), int.Parse(skill[1])); break;
            case "combat-art":
                var art = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                ((CharactersWindow)await main.OpenViewerAsync("characters")).SetCombatArt(ushort.Parse(art[0]), int.Parse(art[1])); break;
            case "animations": ((CharactersWindow)await main.OpenViewerAsync("characters")).ListAnimations(); break;
            case "animation": ((CharactersWindow)await main.OpenViewerAsync("characters")).SelectAnimation(argument); break;
            case "animation-play": ((CharactersWindow)await main.OpenViewerAsync("characters")).SetAnimationPlaying(bool.Parse(argument)); break;
            case "animation-time":
                var characterViewer = (CharactersWindow)await main.OpenViewerAsync("characters");
                await characterViewer.Ready;
                characterViewer.SetAnimationTime(float.Parse(argument, CultureInfo.InvariantCulture)); break;
            case "equip":
                var equip = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                ((CharactersWindow)await main.OpenViewerAsync("characters")).Equip(int.Parse(equip[0]), uint.Parse(equip[1])); break;
            case "unequip": ((CharactersWindow)await main.OpenViewerAsync("characters")).Unequip(int.Parse(argument)); break;
            case "screenshot":
                var viewer = main.ActiveViewer ?? throw new InvalidOperationException("Open a viewer first.");
                await viewer.Ready;
                var path = Path.GetFullPath(argument.Trim('"'));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                viewer.SaveScreenshot(path); break;
            case "rotate":
                var degrees = float.Parse(argument, CultureInfo.InvariantCulture);
                if (!float.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(argument));
                var radians = degrees * (MathF.PI / 180);
                switch (main.ActiveViewer)
                {
                    case ModelsWindow models: models.RotateHorizontally(radians); break;
                    case EffectsWindow fx: fx.RotateHorizontally(radians); break;
                    case CharactersWindow characters: characters.RotateHorizontally(radians); break;
                    case EquipmentWindow equipment: equipment.RotateHorizontally(radians); break;
                    default: throw new InvalidOperationException("Open the models, equipment or characters viewer to rotate its preview.");
                }
                break;
            case "equipment-rotation":
                var axes = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (axes.Length != 3) throw new ArgumentException("Usage: equipment-rotation <yaw> <pitch> <roll> (degrees)");
                if (main.ActiveViewer is not EquipmentWindow rotationWindow)
                    throw new InvalidOperationException("Open equipment first.");
                rotationWindow.SetRotation(float.Parse(axes[0], CultureInfo.InvariantCulture) * MathF.PI / 180,
                    float.Parse(axes[1], CultureInfo.InvariantCulture) * MathF.PI / 180,
                    float.Parse(axes[2], CultureInfo.InvariantCulture) * MathF.PI / 180);
                break;
            case "zoom":
                var steps = double.Parse(argument, CultureInfo.InvariantCulture);
                if (!double.IsFinite(steps)) throw new ArgumentOutOfRangeException(nameof(argument));
                switch (main.ActiveViewer)
                {
                    case ModelsWindow models: models.ZoomBy(steps); break;
                    case EffectsWindow fx: fx.ZoomBy(steps); break;
                    default: throw new InvalidOperationException("Open the models or effects viewer to zoom its preview.");
                }
                Console.WriteLine($"[Assets] Camera zoom: {steps} wheel steps.");
                break;
            case "location": await main.LoadGameAsync(argument.Trim('"')); break;
            case "screenshot-ui":
                var uiPath = Path.GetFullPath(argument.Trim('"'));
                Directory.CreateDirectory(Path.GetDirectoryName(uiPath)!);
                main.SaveWindowScreenshot(uiPath); break;
            case "close": main.OpenViewer(argument).Close(); break;
            case "gc":
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Console.WriteLine("[Assets] Full garbage collection complete."); break;
            case "quit": main.Close(); break;
            default: throw new ArgumentException($"Unknown cheat: {command}");
        }
    }
}
