# Sacred world BMP renderer

This headless tool reads the original Sacred game files and writes three deterministic debug images:

- `map.bmp` — the authored 2048×2048 Ancaria world map with a position marker.
- `minimap.bmp` — the in-game minimap texture lattice centered on the same position.
- `world-day.bmp` — daytime isometric terrain, liquid/floor layers, and static world sprites.

Run it from the repository root:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project Sacred.World.Renderer -- `
  'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --output '.\world-debug-images' `
  --world-x 3360 --world-y 2464
```

Use `--help` for viewport size and zoom options. If coordinates are omitted, the center of the world archive's start sector is used. The renderer is software-only and does not open a window or require Direct3D.
