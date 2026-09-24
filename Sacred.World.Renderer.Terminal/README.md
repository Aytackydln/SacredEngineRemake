# Sacred world BMP renderer

This headless tool reads the original Sacred game files and writes four deterministic debug images:

- `map.bmp` — the authored 2048×2048 Ancaria world map with a position marker.
- `minimap.bmp` — the in-game minimap texture lattice centered on the same position.
- `world-day.bmp` — daytime isometric terrain, liquid/floor layers, and static world sprites.
- `world-models.bmp` - the daytime view with authored GRN world models for placement checks.

Run it from the repository root:

```powershell
& 'C:\Users\Aytac\.dotnet\dotnet.exe' run --project Sacred.World.Renderer.Terminal -- `
  'E:\SteamLibrary\steamapps\common\Sacred Gold' `
  --output '.\world-debug-images' `
  --world-x 3360 --world-y 2464
```

Use `--help` for viewport size and zoom options. If coordinates are omitted, the center of the world archive's start sector is used. The renderer is software-only and does not open a window or require Direct3D.

Use `--open-doors` to hold each door's authored activation sequence at its final pose.
Both states use Sacred.World's shared placement and animation logic. Models use their
Items.pak materials and a software depth buffer; static sprites provide a painter-depth
occlusion mask. Lighting, filtering and transparency are not identical to Direct3D.
See [door research and registered comparisons](../docs/_research/doors/README.md).
