using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public partial class SacredItemDataTable
{
    private void StartInventoryConsole(SacredItemDataModel[] items)
    {
        Console.WriteLine("[Inventory] Ready. Cheats: item <id>, screenshot <path.png>.");
        _ = Task.Run(async () =>
        {
            while (Console.ReadLine() is { } line)
            {
                Console.WriteLine($"[Inventory] Input: {line}");
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2) continue;
                try
                {
                    if (parts[0].Equals("item", StringComparison.OrdinalIgnoreCase) && uint.TryParse(parts[1], out var id))
                    {
                        var matches = items.Where(item => item.ItemId == id).ToArray();
                        if (matches.Length != 1) throw new InvalidOperationException($"Unknown equipment {id}.");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ResetModelRotationSliders();
                            return LoadModel(matches[0], ItemPreviewRotationMode.RawXyz, ItemPreviewPivotMode.ModelOrigin);
                        });
                        Console.WriteLine($"[Inventory] Item {id} ready.");
                    }
                    else if (parts[0].Equals("screenshot", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = Path.GetFullPath(parts[1].Trim('"'));
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await Dispatcher.UIThread.InvokeAsync(() => _modelViewer.SaveScreenshot(path));
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[Inventory] Cheat failed: {exception.Message}");
                }
            }
        });
    }
}
