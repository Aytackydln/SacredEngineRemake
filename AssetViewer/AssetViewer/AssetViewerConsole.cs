using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace AssetViewer.AssetViewer;

/// <summary>One console reader for the whole application; commands never emulate desktop input.</summary>
internal static class AssetViewerConsole
{
    public static void Start(MainWindow main)
    {
        Console.WriteLine("[Assets] Inventory cheats: inventory, inventory-slot <index>, inventory-item <id>, item-description, screenshot-item <path.png>, inventory-clear, stats, allocation, stats-view <stats|skills|bonuses|allocate>, level <level>, attribute <name> <allocated points>, skill <id> <rank>, combat-art <code> <rank>.");
        Console.WriteLine("[Assets] Cheats: open <models|equipment|textures|characters>, model <row>, item <id>, texture <archive.pak> <row>, character <row>, class <name|npc>, animations, animation <bind|default|slot|GRN name>, animation-play <true|false>, animation-time <seconds>, rotate <horizontal degrees>, effects <true|false>, equip <slot-index> <id>, unequip <slot-index>, screenshot <path.png>, screenshot-ui <path.png>, location <directory>, close <view>, gc, quit.");
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
            case "model": ((ModelsWindow)await main.OpenViewerAsync("models")).Select(ushort.Parse(argument)); break;
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
                    case CharactersWindow characters: characters.RotateHorizontally(radians); break;
                    case EquipmentWindow equipment: equipment.RotateHorizontally(radians); break;
                    default: throw new InvalidOperationException("Open the models, equipment or characters viewer to rotate its preview.");
                }
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
